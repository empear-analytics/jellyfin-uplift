#nullable disable

#pragma warning disable CS1591
// We need lowercase normalized string for ffmpeg
#pragma warning disable CA1308

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
        public string GetH264Encoder(EncodingJobInfo state, EncodingOptions encodingOptions)
            => GetH26xOrAv1Encoder("libx264", "h264", state, encodingOptions);

        public string GetH265Encoder(EncodingJobInfo state, EncodingOptions encodingOptions)
            => GetH26xOrAv1Encoder("libx265", "hevc", state, encodingOptions);

        public string GetAv1Encoder(EncodingJobInfo state, EncodingOptions encodingOptions)
            => GetH26xOrAv1Encoder("libsvtav1", "av1", state, encodingOptions);

        private string GetH26xOrAv1Encoder(string defaultEncoder, string hwEncoder, EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            // Only use alternative encoders for video files.
            // When using concat with folder rips, if the mfx session fails to initialize, ffmpeg will be stuck retrying and will not exit gracefully
            // Since transcoding of folder rips is experimental anyway, it's not worth adding additional variables such as this.
            if (state.VideoType == VideoType.VideoFile)
            {
                var hwType = encodingOptions.HardwareAccelerationType;

                var codecMap = new Dictionary<HardwareAccelerationType, string>()
                {
                    { HardwareAccelerationType.amf,                  hwEncoder + "_amf" },
                    { HardwareAccelerationType.nvenc,                hwEncoder + "_nvenc" },
                    { HardwareAccelerationType.qsv,                  hwEncoder + "_qsv" },
                    { HardwareAccelerationType.vaapi,                hwEncoder + "_vaapi" },
                    { HardwareAccelerationType.videotoolbox,         hwEncoder + "_videotoolbox" },
                    { HardwareAccelerationType.v4l2m2m,              hwEncoder + "_v4l2m2m" },
                    { HardwareAccelerationType.rkmpp,                hwEncoder + "_rkmpp" },
                };

                if (IsHardwareEncodingEnabled(hwType, encodingOptions)
                    && TryGetSupportedEncoder(codecMap, hwType, out var preferredEncoder))
                {
                    return preferredEncoder;
                }
            }

            return defaultEncoder;
        }

        private bool TryGetSupportedEncoder(Dictionary<HardwareAccelerationType, string> codecMap, HardwareAccelerationType hwType, out string preferredEncoder)
        {
            return codecMap.TryGetValue(hwType, out preferredEncoder)
                && _mediaEncoder.SupportsEncoder(preferredEncoder);
        }

        private static bool IsHardwareEncodingEnabled(HardwareAccelerationType hwType, EncodingOptions encodingOptions)
        {
            return hwType != HardwareAccelerationType.none
                && encodingOptions.EnableHardwareEncoding;
        }

        private string GetMjpegEncoder(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            if (state.VideoType == VideoType.VideoFile)
            {
                var hwType = encodingOptions.HardwareAccelerationType;

                // Only enable VA-API MJPEG encoder on Intel iHD driver.
                // Legacy platforms supported ONLY by i965 do not support MJPEG encoder.
                if (IsNonIHDVaapi(hwType))
                {
                    return _defaultMjpegEncoder;
                }

                if (IsHardwareEncodingEnabled(hwType, encodingOptions)
                    && TryGetSupportedEncoder(_mjpegCodecMap, hwType, out var preferredEncoder))
                {
                    return preferredEncoder;
                }
            }

            return _defaultMjpegEncoder;
        }

        /// <summary>
        /// Gets the name of the output video codec.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="encodingOptions">Encoding options.</param>
        /// <returns>Encoder string.</returns>
        public string GetVideoEncoder(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            var codec = state.OutputVideoCodec;

            if (!string.IsNullOrEmpty(codec))
            {
                if (string.Equals(codec, "av1", StringComparison.OrdinalIgnoreCase))
                {
                    return GetAv1Encoder(state, encodingOptions);
                }

                if (string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase))
                {
                    return GetH265Encoder(state, encodingOptions);
                }

                if (string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase))
                {
                    return GetH264Encoder(state, encodingOptions);
                }

                if (string.Equals(codec, "mjpeg", StringComparison.OrdinalIgnoreCase))
                {
                    return GetMjpegEncoder(state, encodingOptions);
                }

                if (ContainerValidationRegex().IsMatch(codec))
                {
                    return codec.ToLowerInvariant();
                }
            }

            return "copy";
        }

        public int GetVideoProfileScore(string videoCodec, string videoProfile)
        {
            // strip spaces because they may be stripped out on the query string
            string profile = videoProfile.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (string.Equals("h264", videoCodec, StringComparison.OrdinalIgnoreCase))
            {
                return Array.FindIndex(_videoProfilesH264, x => string.Equals(x, profile, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals("hevc", videoCodec, StringComparison.OrdinalIgnoreCase))
            {
                return Array.FindIndex(_videoProfilesH265, x => string.Equals(x, profile, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals("av1", videoCodec, StringComparison.OrdinalIgnoreCase))
            {
                return Array.FindIndex(_videoProfilesAv1, x => string.Equals(x, profile, StringComparison.OrdinalIgnoreCase));
            }

            return -1;
        }

        /// <summary>
        /// Gets the audio encoder.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetAudioEncoder(EncodingJobInfo state)
        {
            var codec = state.OutputAudioCodec;

            if (!ContainerValidationRegex().IsMatch(codec))
            {
                codec = "aac";
            }

            if (string.Equals(codec, "aac", StringComparison.OrdinalIgnoreCase))
            {
                // Use Apple's aac encoder if available as it provides best audio quality
                if (_mediaEncoder.SupportsEncoder("aac_at"))
                {
                    return "aac_at";
                }

                // Use libfdk_aac for better audio quality if using custom build of FFmpeg which has fdk_aac support
                if (_mediaEncoder.SupportsEncoder("libfdk_aac"))
                {
                    return "libfdk_aac";
                }

                return "aac";
            }

            return GetNonAacAudioEncoder(codec);
        }

        public void TryStreamCopy(EncodingJobInfo state, EncodingOptions options)
        {
            if (state.VideoStream is not null && CanStreamCopyVideo(state, state.VideoStream))
            {
                state.OutputVideoCodec = "copy";
            }
            else
            {
                ApplyVideoCopyFallback(state);
            }

            var preventHlsAudioCopy = IsHlsAudioCopyPrevented(state, options);

            TranscodeReason audioCopyFailureReasons = 0;
            if (CanCopyAudioStream(state, preventHlsAudioCopy, out audioCopyFailureReasons))
            {
                state.OutputAudioCodec = "copy";
            }
            else
            {
                ApplyAudioCopyFallback(state, audioCopyFailureReasons);
            }
        }

        private static bool IsHlsAudioCopyPrevented(EncodingJobInfo state, EncodingOptions options)
        {
            return state.TranscodingType is TranscodingJobType.Hls
                && state.VideoStream is not null
                && !IsCopyCodec(state.OutputVideoCodec)
                && options.HlsAudioSeekStrategy is HlsAudioSeekStrategy.TranscodeAudio;
        }

        private bool CanCopyAudioStream(EncodingJobInfo state, bool preventHlsAudioCopy, out TranscodeReason audioCopyFailureReasons)
        {
            audioCopyFailureReasons = default;
            return state.AudioStream is not null
                && CanStreamCopyAudio(state, state.AudioStream, state.SupportedAudioCodecs, out audioCopyFailureReasons)
                && !preventHlsAudioCopy;
        }

        private static void ApplyVideoCopyFallback(EncodingJobInfo state)
        {
            var user = state.User;

            // If the user doesn't have access to transcoding, then force stream copy, regardless of whether it will be compatible or not
            if (user is not null && !user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding))
            {
                state.OutputVideoCodec = "copy";
            }
        }

        private static void ApplyAudioCopyFallback(EncodingJobInfo state, TranscodeReason audioCopyFailureReasons)
        {
            var user = state.User;

            // If the user doesn't have access to transcoding, then force stream copy, regardless of whether it will be compatible or not
            if (user is not null && !user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding))
            {
                state.OutputAudioCodec = "copy";
            }
            else if (state.AudioStream is not null && !IsCopyCodec(state.OutputAudioCodec))
            {
                // Audio is actually being re-encoded although the playback determination may have considered the source copyable.
                // Only carry the primary "cannot be passed through" cause - the codec mismatch.
                // Bitrate/channels/sample-rate/bit-depth copy refusals are consequences of the chosen transcode target.
                state.AddTranscodeReason(audioCopyFailureReasons & TranscodeReason.AudioCodecNotSupported);
            }
        }

        private static bool HasSupportedCodecs(BaseEncodingJobOptions request, string[] supportedCodecs)
        {
            return request is not null && supportedCodecs is not null && supportedCodecs.Length > 0;
        }

        private void ShiftAudioCodecsIfNeeded(List<string> audioCodecs, MediaStream audioStream)
        {
            // No need to shift if there is only one supported audio codec.
            if (audioCodecs.Count < 2)
            {
                return;
            }

            var inputChannels = audioStream is null ? 6 : audioStream.Channels ?? 6;
            var shiftAudioCodecs = new List<string>();
            if (inputChannels >= 6)
            {
                // DTS and TrueHD are not supported by HLS
                // Keep them in the supported codecs list, but shift them to the end of the list so that if transcoding happens, another codec is used
                shiftAudioCodecs.Add("dts");
                shiftAudioCodecs.Add("truehd");
            }
            else
            {
                // Transcoding to 2ch ac3 or eac3 almost always causes a playback failure
                // Keep them in the supported codecs list, but shift them to the end of the list so that if transcoding happens, another codec is used
                shiftAudioCodecs.Add("ac3");
                shiftAudioCodecs.Add("eac3");
            }

            if (audioCodecs.All(i => shiftAudioCodecs.Contains(i, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            while (shiftAudioCodecs.Contains(audioCodecs[0], StringComparison.OrdinalIgnoreCase))
            {
                var removed = audioCodecs[0];
                audioCodecs.RemoveAt(0);
                audioCodecs.Add(removed);
            }
        }

        private void ShiftVideoCodecsIfNeeded(List<string> videoCodecs, EncodingOptions encodingOptions)
        {
            // No need to shift if there is only one supported video codec.
            if (videoCodecs.Count < 2)
            {
                return;
            }

            // Shift codecs to the end of list if it's not allowed.
            var shiftVideoCodecs = new List<string>();
            if (!encodingOptions.AllowHevcEncoding)
            {
                shiftVideoCodecs.Add("hevc");
                shiftVideoCodecs.Add("h265");
            }

            if (!encodingOptions.AllowAv1Encoding)
            {
                shiftVideoCodecs.Add("av1");
            }

            if (videoCodecs.All(i => shiftVideoCodecs.Contains(i, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            while (shiftVideoCodecs.Contains(videoCodecs[0], StringComparison.OrdinalIgnoreCase))
            {
                var removed = videoCodecs[0];
                videoCodecs.RemoveAt(0);
                videoCodecs.Add(removed);
            }
        }
    }
}
