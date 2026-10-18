#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Configuration;
using IConfigurationManager = MediaBrowser.Common.Configuration.IConfigurationManager;

namespace MediaBrowser.Controller.MediaEncoding
{
    public partial class EncodingHelper
    {
        /// <summary>
        /// Determines whether the specified stream is H264.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns><c>true</c> if the specified stream is H264; otherwise, <c>false</c>.</returns>
        public static bool IsH264(MediaStream stream)
        {
            var codec = stream.Codec ?? string.Empty;

            return codec.Contains("264", StringComparison.OrdinalIgnoreCase)
                    || codec.Contains("avc", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsH265(MediaStream stream)
        {
            var codec = stream.Codec ?? string.Empty;

            return codec.Contains("265", StringComparison.OrdinalIgnoreCase)
                || codec.Contains("hevc", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAv1(MediaStream stream)
        {
            var codec = stream.Codec ?? string.Empty;

            return codec.Contains("av1", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAAC(MediaStream stream)
        {
            var codec = stream.Codec ?? string.Empty;

            return codec.Contains("aac", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDoviWithHdr10Bl(MediaStream stream)
        {
            var rangeType = stream?.VideoRangeType;

            return rangeType is VideoRangeType.DOVIWithHDR10
                or VideoRangeType.DOVIWithEL
                or VideoRangeType.DOVIWithHDR10Plus
                or VideoRangeType.DOVIWithELHDR10Plus
                || (rangeType == VideoRangeType.DOVIInvalid
                    && string.Equals(stream.ColorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase)); // invalid may be hlg now
        }

        public static bool IsDovi(MediaStream stream)
        {
            var rangeType = stream?.VideoRangeType;

            return IsDoviWithHdr10Bl(stream)
                   || (rangeType is VideoRangeType.DOVI
                       or VideoRangeType.DOVIWithHLG
                       or VideoRangeType.DOVIWithSDR
                       or VideoRangeType.DOVIInvalid);
        }

        public static bool IsHdr10Plus(MediaStream stream)
        {
            var rangeType = stream?.VideoRangeType;

            return rangeType is VideoRangeType.HDR10Plus
                       or VideoRangeType.DOVIWithHDR10Plus
                       or VideoRangeType.DOVIWithELHDR10Plus;
        }

        /// <summary>
        /// Check if dynamic HDR metadata should be removed during stream copy.
        /// Please note this check assumes the range check has already been done
        /// and trivial fallbacks like HDR10+ to HDR10, DOVIWithHDR10 to HDR10 is already checked.
        /// </summary>
        private static DynamicHdrMetadataRemovalPlan ShouldRemoveDynamicHdrMetadata(EncodingJobInfo state)
        {
            var videoStream = state.VideoStream;
            if (IsNonHdrVideoStream(videoStream))
            {
                return DynamicHdrMetadataRemovalPlan.None;
            }

            var requestedRangeTypes = state.GetRequestedRangeTypes(state.VideoStream.Codec);
            if (requestedRangeTypes.Length == 0)
            {
                return DynamicHdrMetadataRemovalPlan.None;
            }

            var requestHasHDR10 = requestedRangeTypes.Contains(VideoRangeType.HDR10.ToString(), StringComparison.OrdinalIgnoreCase);
            var requestHasDOVI = requestedRangeTypes.Contains(VideoRangeType.DOVI.ToString(), StringComparison.OrdinalIgnoreCase);
            var requestHasDOVIwithEL = requestedRangeTypes.Contains(VideoRangeType.DOVIWithEL.ToString(), StringComparison.OrdinalIgnoreCase);
            var requestHasDOVIwithELHDR10plus = requestedRangeTypes.Contains(VideoRangeType.DOVIWithELHDR10Plus.ToString(), StringComparison.OrdinalIgnoreCase);

            var shouldRemoveHdr10Plus = false;
            // Case 1: Client supports HDR10, does not support DOVI with EL but EL presets
            var shouldRemoveDovi = IsUnsupportedDoviWithEL(videoStream, requestHasDOVIwithEL, requestHasHDR10);

            // Case 2: Client supports DOVI, does not support broken DOVI config
            // Client does not report DOVI support should be allowed to copy bad data for remuxing as HDR10 players would not crash
            shouldRemoveDovi = shouldRemoveDovi || IsInvalidDoviForDoviClient(videoStream, requestHasDOVI);

            // Special case: we have a video with both EL and HDR10+
            // If the client supports EL but not in the case of coexistence with HDR10+, remove HDR10+ for compatibility reasons.
            // Otherwise, remove DOVI if the client is not a DOVI player
            if (videoStream.VideoRangeType == VideoRangeType.DOVIWithELHDR10Plus)
            {
                shouldRemoveHdr10Plus = requestHasDOVIwithEL && !requestHasDOVIwithELHDR10plus;
                shouldRemoveDovi = shouldRemoveDovi || !shouldRemoveHdr10Plus;
            }

            if (shouldRemoveDovi)
            {
                return DynamicHdrMetadataRemovalPlan.RemoveDovi;
            }

            // If the client is a Dolby Vision Player, remove the HDR10+ metadata to avoid playback issues
            shouldRemoveHdr10Plus = shouldRemoveHdr10Plus || IsDoviWithHdr10PlusForDoviClient(videoStream, requestHasDOVI);
            return shouldRemoveHdr10Plus ? DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus : DynamicHdrMetadataRemovalPlan.None;
        }

        private static bool IsInvalidDoviForDoviClient(MediaStream videoStream, bool requestHasDOVI)
        {
            return requestHasDOVI && videoStream.VideoRangeType == VideoRangeType.DOVIInvalid;
        }

        private static bool IsDoviWithHdr10PlusForDoviClient(MediaStream videoStream, bool requestHasDOVI)
        {
            return requestHasDOVI && videoStream.VideoRangeType == VideoRangeType.DOVIWithHDR10Plus;
        }

        private static bool IsNonHdrVideoStream(MediaStream videoStream)
        {
            return videoStream.VideoRange is not VideoRange.HDR
                && videoStream.VideoRangeType != VideoRangeType.DOVIInvalid;
        }

        private static bool IsUnsupportedDoviWithEL(MediaStream videoStream, bool requestHasDOVIwithEL, bool requestHasHDR10)
        {
            return (!requestHasDOVIwithEL && requestHasHDR10) && videoStream.VideoRangeType == VideoRangeType.DOVIWithEL;
        }

        private bool CanEncoderRemoveDynamicHdrMetadata(DynamicHdrMetadataRemovalPlan plan, MediaStream videoStream)
        {
            return plan switch
            {
                DynamicHdrMetadataRemovalPlan.RemoveDovi => _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.DoviRpuStrip)
                                                            || (IsH265(videoStream) && _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.HevcMetadataRemoveDovi))
                                                            || (IsAv1(videoStream) && _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.Av1MetadataRemoveDovi)),
                DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus => (IsH265(videoStream) && _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.HevcMetadataRemoveHdr10Plus))
                                                                 || (IsAv1(videoStream) && _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.Av1MetadataRemoveHdr10Plus)),
                _ => true,
            };
        }

        public bool IsDoviRemoved(EncodingJobInfo state)
        {
            return state?.VideoStream is not null && ShouldRemoveDynamicHdrMetadata(state) == DynamicHdrMetadataRemovalPlan.RemoveDovi
                                              && CanEncoderRemoveDynamicHdrMetadata(DynamicHdrMetadataRemovalPlan.RemoveDovi, state.VideoStream);
        }

        public bool IsHdr10PlusRemoved(EncodingJobInfo state)
        {
            return state?.VideoStream is not null && ShouldRemoveDynamicHdrMetadata(state) == DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus
                                                  && CanEncoderRemoveDynamicHdrMetadata(DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus, state.VideoStream);
        }

        public string GetBitStreamArgs(EncodingJobInfo state, MediaStreamType streamType)
        {
            if (state is null)
            {
                return null;
            }

            var stream = GetBitStreamSourceStream(state, streamType);
            // TODO This is auto inserted into the mpegts mux so it might not be needed.
            // https://www.ffmpeg.org/ffmpeg-bitstream-filters.html#h264_005fmp4toannexb
            if (IsH264(stream))
            {
                return "-bsf:v h264_mp4toannexb";
            }

            if (IsAAC(stream))
            {
                // Convert adts header(mpegts) to asc header(mp4).
                return "-bsf:a aac_adtstoasc";
            }

            if (IsH265(stream))
            {
                return GetHevcBitStreamArgs(state);
            }

            if (IsAv1(stream))
            {
                return GetAv1BitStreamArgs(state);
            }

            return null;
        }

        private static MediaStream GetBitStreamSourceStream(EncodingJobInfo state, MediaStreamType streamType)
        {
            return streamType switch
            {
                MediaStreamType.Audio => state.AudioStream,
                MediaStreamType.Video => state.VideoStream,
                _ => state.VideoStream
            };
        }

        private string GetHevcBitStreamArgs(EncodingJobInfo state)
        {
            var filter = "-bsf:v hevc_mp4toannexb";

            // The following checks are not complete because the copy would be rejected
            // if the encoder cannot remove required metadata.
            // And if bsf is used, we must already be using copy codec.
            switch (ShouldRemoveDynamicHdrMetadata(state))
            {
                default:
                case DynamicHdrMetadataRemovalPlan.None:
                    break;
                case DynamicHdrMetadataRemovalPlan.RemoveDovi:
                    filter += _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.HevcMetadataRemoveDovi)
                        ? ",hevc_metadata=remove_dovi=1"
                        : ",dovi_rpu=strip=1";
                    break;
                case DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus:
                    filter += ",hevc_metadata=remove_hdr10plus=1";
                    break;
            }

            return filter;
        }

        private string GetAv1BitStreamArgs(EncodingJobInfo state)
        {
            switch (ShouldRemoveDynamicHdrMetadata(state))
            {
                default:
                case DynamicHdrMetadataRemovalPlan.None:
                    return null;
                case DynamicHdrMetadataRemovalPlan.RemoveDovi:
                    return _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.Av1MetadataRemoveDovi)
                        ? "-bsf:v av1_metadata=remove_dovi=1"
                        : "-bsf:v dovi_rpu=strip=1";
                case DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus:
                    return "-bsf:v av1_metadata=remove_hdr10plus=1";
            }
        }

        public string GetAudioBitStreamArguments(EncodingJobInfo state, string segmentContainer, string mediaSourceContainer)
        {
            var filters = new List<string>();

            var noiseFilter = GetCopiedAudioTrimBsf(state);
            if (!string.IsNullOrEmpty(noiseFilter))
            {
                filters.Add(noiseFilter);
            }

            var segmentFormat = GetSegmentFileExtension(segmentContainer).TrimStart('.');

            // Apply aac_adtstoasc bitstream filter when media source is in mpegts.
            if (NeedsAacAdtsToAsc(state, segmentFormat, mediaSourceContainer))
            {
                filters.Add("aac_adtstoasc");
            }

            return filters.Count == 0
                ? string.Empty
                : " -bsf:a " + string.Join(',', filters);
        }

        private static bool NeedsAacAdtsToAsc(EncodingJobInfo state, string segmentFormat, string mediaSourceContainer)
        {
            return string.Equals(segmentFormat, "mp4", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(mediaSourceContainer, "ts", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mediaSourceContainer, "aac", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mediaSourceContainer, "hls", StringComparison.OrdinalIgnoreCase))
                && IsAAC(state.AudioStream);
        }

        // When video is transcoded, accurate_seek (the default) trims video to the
        // exact seek point via decoder-side frame discard. But stream-copied audio
        // bypasses the decoder, so it starts from the nearest keyframe — potentially
        // seconds before the target. Use the noise bsf to drop copied audio packets
        // before the seek target, achieving the same trim precision without
        // re-encoding. The noise bsf's drop= parameter requires ffmpeg >= 5.0.
        // Important: make sure not to use it with wtv because it breaks seeking
        private string GetCopiedAudioTrimBsf(EncodingJobInfo state)
        {
            if (IsCopiedAudioTrimUnsupported(state))
            {
                return null;
            }

            var startTicks = state.BaseRequest.StartTimeTicks ?? 0;
            if (startTicks <= 0)
            {
                return null;
            }

            var seekSeconds = startTicks / (double)TimeSpan.TicksPerSecond;
            return string.Format(
                CultureInfo.InvariantCulture,
                "noise=drop='lt(pts*tb\\,{0:F3})'",
                seekSeconds);
        }

        private bool IsCopiedAudioTrimUnsupported(EncodingJobInfo state)
        {
            return state.TranscodingType is not TranscodingJobType.Hls
                || !state.IsVideoRequest
                || IsCopyCodec(state.OutputVideoCodec)
                || !IsCopyCodec(state.OutputAudioCodec)
                || string.Equals(state.InputContainer, "wtv", StringComparison.OrdinalIgnoreCase)
                || _mediaEncoder.EncoderVersion < _minFFmpegNoiseBsfDrop;
        }
    }
}
