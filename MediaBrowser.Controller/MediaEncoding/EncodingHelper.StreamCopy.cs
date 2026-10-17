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
        public bool CanStreamCopyVideo(EncodingJobInfo state, MediaStream videoStream)
        {
            var request = state.BaseRequest;

            if (!request.AllowVideoStreamCopy)
            {
                return false;
            }

            if (IsInterlacedToBeDeinterlaced(state, videoStream))
            {
                return false;
            }

            if (videoStream.IsAnamorphic ?? false)
            {
                if (request.RequireNonAnamorphic)
                {
                    return false;
                }
            }

            // Can't stream copy if we're burning in subtitles
            if (IsBurningInSubtitles(state, request))
            {
                return false;
            }

            if (IsNonAvcH264WhenAvcRequired(request, videoStream))
            {
                return false;
            }

            // Source and target codecs must match
            if (IsUnsupportedVideoCodec(state, videoStream))
            {
                return false;
            }

            var requestedProfiles = state.GetRequestedProfiles(videoStream.Codec);

            // If client is requesting a specific video profile, it must match the source
            if (requestedProfiles.Length > 0)
            {
                if (string.IsNullOrEmpty(videoStream.Profile))
                {
                    // return false;
                }

                var requestedProfile = requestedProfiles[0];
                // strip spaces because they may be stripped out on the query string as well
                if (IsProfileNotRequested(videoStream, requestedProfiles))
                {
                    var currentScore = GetVideoProfileScore(videoStream.Codec, videoStream.Profile);
                    var requestedScore = GetVideoProfileScore(videoStream.Codec, requestedProfile);

                    if (currentScore == -1 || currentScore > requestedScore)
                    {
                        return false;
                    }
                }
            }

            var requestedRangeTypes = state.GetRequestedRangeTypes(videoStream.Codec);
            if (requestedRangeTypes.Length > 0)
            {
                if (videoStream.VideoRangeType == VideoRangeType.Unknown)
                {
                    return false;
                }

                // DOVIWithHDR10 should be compatible with HDR10 supporting players. Same goes with HLG and of course SDR. So allow copy of those formats
                var requestHasHDR10 = requestedRangeTypes.Contains(VideoRangeType.HDR10.ToString(), StringComparison.OrdinalIgnoreCase);
                var requestHasHLG = requestedRangeTypes.Contains(VideoRangeType.HLG.ToString(), StringComparison.OrdinalIgnoreCase);
                var requestHasSDR = requestedRangeTypes.Contains(VideoRangeType.SDR.ToString(), StringComparison.OrdinalIgnoreCase);
                var requestHasDOVI = requestedRangeTypes.Contains(VideoRangeType.DOVI.ToString(), StringComparison.OrdinalIgnoreCase);

                // If SDR is the only supported range, we should not copy any of the HDR streams.
                // All the following copy check assumes at least one HDR format is supported.
                if (IsHdrStreamForSdrOnlyRequest(requestedRangeTypes, requestHasSDR, videoStream))
                {
                    return false;
                }

                // If the client does not support DOVI and the video stream is DOVI without fallback, we should not copy it.
                if (IsUnsupportedDoviStream(requestHasDOVI, videoStream))
                {
                    return false;
                }

                if (IsIncompatibleRangeType(requestedRangeTypes, videoStream, requestHasHDR10, requestHasHLG, requestHasSDR))
                {
                    // If the video stream is in HDR10+ or a static HDR format, don't allow copy if the client does not support HDR10 or HLG.
                    if (videoStream.VideoRangeType is VideoRangeType.HDR10Plus or VideoRangeType.HDR10 or VideoRangeType.HLG)
                    {
                        return false;
                    }

                    // Check complicated cases where we need to remove dynamic metadata
                    // Conservatively refuse to copy if the encoder can't remove dynamic metadata,
                    // but a removal is required for compatability reasons.
                    var dynamicHdrMetadataRemovalPlan = ShouldRemoveDynamicHdrMetadata(state);
                    if (!CanEncoderRemoveDynamicHdrMetadata(dynamicHdrMetadataRemovalPlan, videoStream))
                    {
                        return false;
                    }
                }
            }

            var requestedRotations = state.GetRequestedRotations(videoStream.Codec);
            if (requestedRotations.Length > 0)
            {
                var rotation = state.VideoStream?.Rotation ?? 0;
                if (IsUnrequestedRotation(rotation, requestedRotations))
                {
                    return false;
                }
            }

            // Video width must fall within requested value
            if (ExceedsMaxWidth(request, videoStream))
            {
                return false;
            }

            // Video height must fall within requested value
            if (ExceedsMaxHeight(request, videoStream))
            {
                return false;
            }

            // Video framerate must fall within requested value
            var requestedFramerate = request.MaxFramerate ?? request.Framerate;
            if (requestedFramerate.HasValue)
            {
                var videoFrameRate = videoStream.ReferenceFrameRate;

                // Add a little tolerance to the framerate check because some videos might record a framerate
                // that is slightly greater than the intended framerate, but the device can still play it correctly.
                // 0.05 fps tolerance should be safe enough.
                if (!videoFrameRate.HasValue || videoFrameRate.Value > requestedFramerate.Value + 0.05f)
                {
                    return false;
                }
            }

            return CanCopyGivenVideoBitRate(state, request, videoStream);
        }

        private static bool CanCopyGivenVideoBitRate(EncodingJobInfo state, BaseEncodingJobOptions request, MediaStream videoStream)
        {
            // Video bitrate must fall within requested value
            if (ExceedsMaxVideoBitRate(request, videoStream))
            {
                // For LiveTV that has no bitrate, let's try copy if other conditions are met
                if (IsNotBitratelessLiveStream(request, videoStream))
                {
                    return false;
                }
            }

            return CanCopyGivenBitDepthRefFramesLevelAndContainer(state, videoStream);
        }

        private static bool CanCopyGivenBitDepthRefFramesLevelAndContainer(EncodingJobInfo state, MediaStream videoStream)
        {
            var maxBitDepth = state.GetRequestedVideoBitDepth(videoStream.Codec);
            if (maxBitDepth.HasValue)
            {
                if (ExceedsMaxBitDepth(videoStream, maxBitDepth))
                {
                    return false;
                }
            }

            return CanCopyGivenRefFramesLevelAndContainer(state, videoStream);
        }

        private static bool CanCopyGivenRefFramesLevelAndContainer(EncodingJobInfo state, MediaStream videoStream)
        {
            var maxRefFrames = state.GetRequestedMaxRefFrames(videoStream.Codec);
            if (ExceedsMaxRefFrames(maxRefFrames, videoStream))
            {
                return false;
            }

            // If a specific level was requested, the source must match or be less than
            var level = state.GetRequestedLevel(videoStream.Codec);
            if (double.TryParse(level, CultureInfo.InvariantCulture, out var requestLevel))
            {
                if (!videoStream.Level.HasValue)
                {
                    // return false;
                }

                if (ExceedsRequestedLevel(videoStream, requestLevel))
                {
                    return false;
                }
            }

            if (IsNonAvcH264InAvi(state, videoStream))
            {
                // see Coach S01E01 - Kelly and the Professor(0).avi
                return false;
            }

            return true;
        }

        private static bool IsUnsupportedDoviStream(bool requestHasDOVI, MediaStream videoStream)
        {
            return !requestHasDOVI && videoStream.VideoRangeType == VideoRangeType.DOVI;
        }

        private static bool ExceedsMaxBitDepth(MediaStream videoStream, int? maxBitDepth)
        {
            return videoStream.BitDepth.HasValue && videoStream.BitDepth.Value > maxBitDepth.Value;
        }

        private static bool ExceedsRequestedLevel(MediaStream videoStream, double requestLevel)
        {
            return videoStream.Level.HasValue && videoStream.Level.Value > requestLevel;
        }

        private static bool IsInterlacedToBeDeinterlaced(EncodingJobInfo state, MediaStream videoStream)
        {
            return videoStream.IsInterlaced
                && state.DeInterlace(videoStream.Codec, false);
        }

        private static bool IsProfileNotRequested(MediaStream videoStream, string[] requestedProfiles)
        {
            return !string.IsNullOrEmpty(videoStream.Profile)
                && !requestedProfiles.Contains(videoStream.Profile.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNotBitratelessLiveStream(BaseEncodingJobOptions request, MediaStream videoStream)
        {
            return string.IsNullOrWhiteSpace(request.LiveStreamId) || videoStream.BitRate.HasValue;
        }

        private static bool IsBurningInSubtitles(EncodingJobInfo state, BaseEncodingJobOptions request)
        {
            return request.SubtitleStreamIndex.HasValue
                && request.SubtitleStreamIndex.Value >= 0
                && state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Encode;
        }

        private static bool IsNonAvcH264WhenAvcRequired(BaseEncodingJobOptions request, MediaStream videoStream)
        {
            return string.Equals("h264", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                && videoStream.IsAVC.HasValue
                && !videoStream.IsAVC.Value
                && request.RequireAvc;
        }

        private static bool IsUnsupportedVideoCodec(EncodingJobInfo state, MediaStream videoStream)
        {
            return string.IsNullOrEmpty(videoStream.Codec)
                || (state.SupportedVideoCodecs.Length != 0
                    && !state.SupportedVideoCodecs.Contains(videoStream.Codec, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsHdrStreamForSdrOnlyRequest(string[] requestedRangeTypes, bool requestHasSDR, MediaStream videoStream)
        {
            return requestedRangeTypes.Length == 1 && requestHasSDR && videoStream.VideoRangeType != VideoRangeType.SDR;
        }

        private static bool IsIncompatibleRangeType(string[] requestedRangeTypes, MediaStream videoStream, bool requestHasHDR10, bool requestHasHLG, bool requestHasSDR)
        {
            return !requestedRangeTypes.Contains(videoStream.VideoRangeType.ToString(), StringComparison.OrdinalIgnoreCase)
                && !IsRangeTypeCompatibleByFallback(videoStream, requestHasHDR10, requestHasHLG, requestHasSDR);
        }

        private static bool IsRangeTypeCompatibleByFallback(MediaStream videoStream, bool requestHasHDR10, bool requestHasHLG, bool requestHasSDR)
        {
            return (requestHasHDR10 && videoStream.VideoRangeType == VideoRangeType.DOVIWithHDR10)
                || (requestHasHLG && videoStream.VideoRangeType == VideoRangeType.DOVIWithHLG)
                || (requestHasSDR && videoStream.VideoRangeType == VideoRangeType.DOVIWithSDR)
                || (requestHasHDR10 && videoStream.VideoRangeType == VideoRangeType.HDR10Plus);
        }

        private static bool IsUnrequestedRotation(int rotation, string[] requestedRotations)
        {
            return rotation != 0
                && !requestedRotations.Contains(rotation.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        private static bool ExceedsMaxWidth(BaseEncodingJobOptions request, MediaStream videoStream)
        {
            return request.MaxWidth.HasValue
                && (!videoStream.Width.HasValue || videoStream.Width.Value > request.MaxWidth.Value);
        }

        private static bool ExceedsMaxHeight(BaseEncodingJobOptions request, MediaStream videoStream)
        {
            return request.MaxHeight.HasValue
                && (!videoStream.Height.HasValue || videoStream.Height.Value > request.MaxHeight.Value);
        }

        private static bool ExceedsMaxVideoBitRate(BaseEncodingJobOptions request, MediaStream videoStream)
        {
            return request.VideoBitRate.HasValue
                && (!videoStream.BitRate.HasValue || videoStream.BitRate.Value > request.VideoBitRate.Value);
        }

        private static bool ExceedsMaxRefFrames(int? maxRefFrames, MediaStream videoStream)
        {
            return maxRefFrames.HasValue
                && videoStream.RefFrames.HasValue && videoStream.RefFrames.Value > maxRefFrames.Value;
        }

        private static bool IsNonAvcH264InAvi(EncodingJobInfo state, MediaStream videoStream)
        {
            return string.Equals(state.InputContainer, "avi", StringComparison.OrdinalIgnoreCase)
                && string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase)
                && !(videoStream.IsAVC ?? false);
        }

        public bool CanStreamCopyAudio(EncodingJobInfo state, MediaStream audioStream, IEnumerable<string> supportedAudioCodecs)
            => CanStreamCopyAudio(state, audioStream, supportedAudioCodecs, out _);

        /// <summary>
        /// Determines whether the given audio stream can be stream-copied and, regardless of the outcome,
        /// reports the codec/parameter incompatibilities that would force a re-encode via <paramref name="failureReasons"/>.
        /// </summary>
        /// <param name="state">The encoding job state.</param>
        /// <param name="audioStream">The source audio stream.</param>
        /// <param name="supportedAudioCodecs">The audio codecs the target supports.</param>
        /// <param name="failureReasons">The codec/parameter incompatibilities preventing a copy, or <c>0</c> if the stream is copy-compatible.</param>
        /// <returns><c>true</c> if the audio stream can be stream-copied; otherwise, <c>false</c>.</returns>
        public bool CanStreamCopyAudio(EncodingJobInfo state, MediaStream audioStream, IEnumerable<string> supportedAudioCodecs, out TranscodeReason failureReasons)
        {
            var request = state.BaseRequest;

            // Policy-independent compatibility check, so the reasons are reported even when a policy gate is what ultimately prevents the copy.
            failureReasons = GetAudioStreamCopyFailureReasons(state, audioStream, supportedAudioCodecs);

            return request.AllowAudioStreamCopy
                && request.EnableAutoStreamCopy
                && failureReasons == 0;
        }

        private static TranscodeReason GetAudioStreamCopyFailureReasons(EncodingJobInfo state, MediaStream audioStream, IEnumerable<string> supportedAudioCodecs)
        {
            var request = state.BaseRequest;
            TranscodeReason reasons = 0;

            var maxBitDepth = state.GetRequestedAudioBitDepth(audioStream.Codec);
            if (ExceedsMaxAudioBitDepth(maxBitDepth, audioStream))
            {
                reasons |= TranscodeReason.AudioBitDepthNotSupported;
            }

            // Source and target codecs must match
            if (string.IsNullOrEmpty(audioStream.Codec)
                || !supportedAudioCodecs.Contains(audioStream.Codec, StringComparison.OrdinalIgnoreCase))
            {
                reasons |= TranscodeReason.AudioCodecNotSupported;
            }

            // Channels must fall within requested value
            var channels = state.GetRequestedAudioChannels(audioStream.Codec);
            if (ExceedsRequestedAudioChannels(channels, audioStream))
            {
                reasons |= TranscodeReason.AudioChannelsNotSupported;
            }

            // Sample rate must fall within requested value
            if (ExceedsRequestedAudioSampleRate(request, audioStream))
            {
                reasons |= TranscodeReason.AudioSampleRateNotSupported;
            }

            // Audio bitrate must fall within requested value
            if (ExceedsRequestedAudioBitRate(request, audioStream))
            {
                reasons |= TranscodeReason.AudioBitrateNotSupported;
            }

            return reasons;
        }

        private static bool ExceedsMaxAudioBitDepth(int? maxBitDepth, MediaStream audioStream)
        {
            return maxBitDepth.HasValue
                && audioStream.BitDepth.HasValue
                && audioStream.BitDepth.Value > maxBitDepth.Value;
        }

        private static bool ExceedsRequestedAudioChannels(int? channels, MediaStream audioStream)
        {
            return channels.HasValue
                && (!audioStream.Channels.HasValue
                    || audioStream.Channels.Value <= 0
                    || audioStream.Channels.Value > channels.Value);
        }

        private static bool ExceedsRequestedAudioSampleRate(BaseEncodingJobOptions request, MediaStream audioStream)
        {
            return request.AudioSampleRate.HasValue
                && (!audioStream.SampleRate.HasValue
                    || audioStream.SampleRate.Value <= 0
                    || audioStream.SampleRate.Value > request.AudioSampleRate.Value);
        }

        private static bool ExceedsRequestedAudioBitRate(BaseEncodingJobOptions request, MediaStream audioStream)
        {
            return request.AudioBitRate.HasValue
                && audioStream.BitRate.HasValue
                && audioStream.BitRate.Value > request.AudioBitRate.Value;
        }
    }
}
