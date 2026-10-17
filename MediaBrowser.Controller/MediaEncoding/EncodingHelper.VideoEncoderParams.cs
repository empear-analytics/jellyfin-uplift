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
        public static string GetSegmentFileExtension(string segmentContainer)
        {
            if (!string.IsNullOrWhiteSpace(segmentContainer))
            {
                return "." + segmentContainer;
            }

            return ".ts";
        }

        private string GetVideoBitrateParam(EncodingJobInfo state, string videoCodec)
        {
            if (state.OutputVideoBitrate is null)
            {
                return string.Empty;
            }

            int bitrate = state.OutputVideoBitrate.Value;

            // Bit rate under 1000k is not allowed in h264_qsv.
            if (string.Equals(videoCodec, "h264_qsv", StringComparison.OrdinalIgnoreCase))
            {
                bitrate = Math.Max(bitrate, 1000);
            }

            // Currently use the same buffer size for all non-QSV encoders.
            // Use long arithmetic to prevent int32 overflow for very high bitrate values.
            int bufsize = (int)Math.Min((long)bitrate * 2, int.MaxValue);

            if (string.Equals(videoCodec, "libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                return FormattableString.Invariant($" -b:v {bitrate} -bufsize {bufsize}");
            }

            if (string.Equals(videoCodec, "libx264", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "libx265", StringComparison.OrdinalIgnoreCase))
            {
                return FormattableString.Invariant($" -maxrate {bitrate} -bufsize {bufsize}");
            }

            if (IsQsvVideoEncoder(videoCodec))
            {
                // TODO: probe QSV encoders' capabilities and enable more tuning options
                // See also https://github.com/intel/media-delivery/blob/master/doc/quality.rst

                // Some less powerful H.264 HW decoders require strict CPB size
                // So bufsize optimizations should not be applied to them
                int factor = 2;
                var codec = state.ActualOutputVideoCodec;
                var level = state.GetRequestedLevel(codec);
                if (IsH264BelowLevel51(codec, level))
                {
                    factor = 1;
                }

                // Set (maxrate == bitrate + 1) to trigger VBR for better bitrate allocation
                // Set (rc_init_occupancy == 2 * bitrate) and (bufsize == 4 * bitrate) to deal with drastic scene changes
                // Use long arithmetic and clamp to int.MaxValue to prevent int32 overflow
                // (e.g. bitrate * 4 wraps to a negative value for bitrates above ~537 million)
                int qsvMaxrate = (int)Math.Min((long)bitrate + 1, int.MaxValue);
                int qsvInitOcc = (int)Math.Min((long)bitrate * 1 * factor, int.MaxValue);
                int qsvBufsize = (int)Math.Min((long)bitrate * 2 * factor, int.MaxValue);

                return string.Create(CultureInfo.InvariantCulture, $" -b:v {bitrate} -maxrate {qsvMaxrate} -rc_init_occupancy {qsvInitOcc} -bufsize {qsvBufsize}");
            }

            if (string.Equals(videoCodec, "h264_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "hevc_amf", StringComparison.OrdinalIgnoreCase))
            {
                // Override the too high default qmin 18 in transcoding preset in legacy h26x_amf
                return FormattableString.Invariant($" -rc cbr -qmin 0 -qmax 32 -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
            }

            if (IsVaapiVideoEncoder(videoCodec))
            {
                // VBR in i965 driver may result in pixelated output.
                if (_mediaEncoder.IsVaapiDeviceInteli965)
                {
                    return FormattableString.Invariant($" -rc_mode CBR -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
                }

                return FormattableString.Invariant($" -rc_mode VBR -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
            }

            if (string.Equals(videoCodec, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "hevc_videotoolbox", StringComparison.OrdinalIgnoreCase))
            {
                // The `maxrate` and `bufsize` options can potentially lead to performance regression
                // and even encoder hangs, especially when the value is very high.
                return FormattableString.Invariant($" -b:v {bitrate} -qmin -1 -qmax -1");
            }

            return FormattableString.Invariant($" -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
        }

        private static bool IsH264BelowLevel51(string codec, string level)
        {
            return string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(level, CultureInfo.InvariantCulture, out double requestedLevel)
                && requestedLevel < 51;
        }

        private string GetEncoderParam(EncoderPreset? preset, EncoderPreset defaultPreset, EncodingOptions encodingOptions, string videoEncoder, bool isLibX265)
        {
            var param = string.Empty;
            var encoderPreset = preset ?? defaultPreset;
            if (IsLibX264OrLibX265(videoEncoder, isLibX265))
            {
                param = AppendX26xPresetAndCrf(param, encoderPreset, encodingOptions, isLibX265);
            }
            else if (string.Equals(videoEncoder, "libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                param = AppendSvtAv1Preset(param, encoderPreset);
            }
            else if (IsVaapiVideoEncoder(videoEncoder))
            {
                param = AppendVaapiCompressionLevel(param, encoderPreset);
            }
            else if (IsQsvVideoEncoder(videoEncoder))
            {
                param = AppendQsvPreset(param, encoderPreset, videoEncoder);
            }
            else if (IsNvencVideoEncoder(videoEncoder))
            {
                param = AppendNvencPreset(param, encoderPreset, videoEncoder);
            }
            else if (IsAmfVideoEncoder(videoEncoder))
            {
                param = AppendAmfQuality(param, encoderPreset, videoEncoder);
            }
            else if (IsVideoToolboxH26xEncoder(videoEncoder))
            {
                param = AppendVideoToolboxPrioSpeed(param, encoderPreset);
            }

            return param;
        }

        private static bool IsLibX264OrLibX265(string videoEncoder, bool isLibX265)
        {
            return string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase) || isLibX265;
        }

        private static bool IsVideoToolboxH26xEncoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase) // h264 (h264_videotoolbox)
                        || string.Equals(videoEncoder, "hevc_videotoolbox", StringComparison.OrdinalIgnoreCase); // hevc (hevc_videotoolbox)
        }

        private static string AppendX26xPresetAndCrf(string param, EncoderPreset encoderPreset, EncodingOptions encodingOptions, bool isLibX265)
        {
            var presetString = encoderPreset switch
            {
                EncoderPreset.auto => EncoderPreset.veryfast.ToString().ToLowerInvariant(),
                _ => encoderPreset.ToString().ToLowerInvariant()
            };

            param += " -preset " + presetString;

            int encodeCrf = encodingOptions.H264Crf;
            if (isLibX265)
            {
                encodeCrf = encodingOptions.H265Crf;
            }

            if (encodeCrf >= 0 && encodeCrf <= 51)
            {
                param += " -crf " + encodeCrf.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                string defaultCrf = "23";
                if (isLibX265)
                {
                    defaultCrf = "28";
                }

                param += " -crf " + defaultCrf;
            }

            return param;
        }

        private static string AppendSvtAv1Preset(string param, EncoderPreset encoderPreset)
        {
            // Default to use the recommended preset 10.
            // Omit presets < 5, which are too slow for on the fly encoding.
            // https://gitlab.com/AOMediaCodec/SVT-AV1/-/blob/master/Docs/Ffmpeg.md
            param += encoderPreset switch
            {
                EncoderPreset.veryslow => " -preset 5",
                EncoderPreset.slower => " -preset 6",
                EncoderPreset.slow => " -preset 7",
                EncoderPreset.medium => " -preset 8",
                EncoderPreset.fast => " -preset 9",
                EncoderPreset.faster => " -preset 10",
                EncoderPreset.veryfast => " -preset 11",
                EncoderPreset.superfast => " -preset 12",
                EncoderPreset.ultrafast => " -preset 13",
                _ => " -preset 10"
            };

            return param;
        }

        private string AppendVaapiCompressionLevel(string param, EncoderPreset encoderPreset)
        {
            // -compression_level is not reliable on AMD.
            if (_mediaEncoder.IsVaapiDeviceInteliHD)
            {
                param += encoderPreset switch
                {
                    EncoderPreset.veryslow => " -compression_level 1",
                    EncoderPreset.slower => " -compression_level 2",
                    EncoderPreset.slow => " -compression_level 3",
                    EncoderPreset.medium => " -compression_level 4",
                    EncoderPreset.fast => " -compression_level 5",
                    EncoderPreset.faster => " -compression_level 6",
                    EncoderPreset.veryfast => " -compression_level 7",
                    EncoderPreset.superfast => " -compression_level 7",
                    EncoderPreset.ultrafast => " -compression_level 7",
                    _ => string.Empty
                };
            }

            return param;
        }

        private static string AppendQsvPreset(string param, EncoderPreset encoderPreset, string videoEncoder)
        {
            EncoderPreset[] valid_presets = [EncoderPreset.veryslow, EncoderPreset.slower, EncoderPreset.slow, EncoderPreset.medium, EncoderPreset.fast, EncoderPreset.faster, EncoderPreset.veryfast];

            param += " -preset " + (valid_presets.Contains(encoderPreset) ? encoderPreset : EncoderPreset.veryfast).ToString().ToLowerInvariant();

            // Enable MacroBlock level bitrate control for better subjective visual quality
            if (string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase))
            {
                param += " -mbbrc 1";
            }

            return param;
        }

        private static string AppendNvencPreset(string param, EncoderPreset encoderPreset, string videoEncoder)
        {
            param += encoderPreset switch
            {
                EncoderPreset.veryslow => " -preset p7",
                EncoderPreset.slower => " -preset p6",
                EncoderPreset.slow => " -preset p5",
                EncoderPreset.medium => " -preset p4",
                EncoderPreset.fast => " -preset p3",
                EncoderPreset.faster => " -preset p2",
                _ => " -preset p1"
            };

            // Enable Spatial Adaptive Quantization for better subjective visual quality
            // H.264 is skipped for now because Maxwell 1 does not support it
            if (!string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase))
            {
                param += " -spatial-aq 1 -aq-strength 15";
            }

            return param;
        }

        private string AppendAmfQuality(string param, EncoderPreset encoderPreset, string videoEncoder)
        {
            param += encoderPreset switch
            {
                EncoderPreset.veryslow => " -quality quality",
                EncoderPreset.slower => " -quality quality",
                EncoderPreset.slow => " -quality quality",
                EncoderPreset.medium => " -quality balanced",
                _ => " -quality speed"
            };

            // Enable Variance Based/Context Adaptive Quantization for better subjective visual quality
            if (string.Equals(videoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase))
            {
                param += " -vbaq 1";
            }
            else if (string.Equals(videoEncoder, "av1_amf", StringComparison.OrdinalIgnoreCase)
                     && _mediaEncoder.EncoderVersion >= _minFFmpegAmfAv1EncCAQ)
            {
                param += " -aq_mode caq";
            }

            if (string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "av1_amf", StringComparison.OrdinalIgnoreCase))
            {
                param += " -header_insertion_mode gop";
            }

            if (string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase))
            {
                param += " -gops_per_idr 1";
            }

            return param;
        }

        private static string AppendVideoToolboxPrioSpeed(string param, EncoderPreset encoderPreset)
        {
            param += encoderPreset switch
            {
                EncoderPreset.veryslow => " -prio_speed 0",
                EncoderPreset.slower => " -prio_speed 0",
                EncoderPreset.slow => " -prio_speed 0",
                EncoderPreset.medium => " -prio_speed 0",
                _ => " -prio_speed 1"
            };

            return param;
        }

        private static bool IsVaapiVideoEncoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "av1_vaapi", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsQsvVideoEncoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase) // h264 (h264_qsv)
                || string.Equals(videoEncoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase) // hevc (hevc_qsv)
                || string.Equals(videoEncoder, "av1_qsv", StringComparison.OrdinalIgnoreCase); // av1 (av1_qsv)
        }

        private static bool IsNvencVideoEncoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase) // h264 (h264_nvenc)
                || string.Equals(videoEncoder, "hevc_nvenc", StringComparison.OrdinalIgnoreCase) // hevc (hevc_nvenc)
                || string.Equals(videoEncoder, "av1_nvenc", StringComparison.OrdinalIgnoreCase); // av1 (av1_nvenc)
        }

        private static bool IsAmfVideoEncoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase) // h264 (h264_amf)
                || string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase) // hevc (hevc_amf)
                || string.Equals(videoEncoder, "av1_amf", StringComparison.OrdinalIgnoreCase); // av1 (av1_amf)
        }

        public static string NormalizeTranscodingLevel(EncodingJobInfo state, string level)
        {
            if (!double.TryParse(level, CultureInfo.InvariantCulture, out double requestLevel))
            {
                return null;
            }

            if (string.Equals(state.ActualOutputVideoCodec, "av1", StringComparison.OrdinalIgnoreCase))
            {
                // Transcode to level 5.3 (15) and lower for maximum compatibility.
                // https://en.wikipedia.org/wiki/AV1#Levels
                if (requestLevel < 0 || requestLevel >= 15)
                {
                    return "15";
                }
            }
            else if (string.Equals(state.ActualOutputVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(state.ActualOutputVideoCodec, "h265", StringComparison.OrdinalIgnoreCase))
            {
                // Transcode to level 5.0 and lower for maximum compatibility.
                // Level 5.0 is suitable for up to 4k 30fps hevc encoding, otherwise let the encoder to handle it.
                // https://en.wikipedia.org/wiki/High_Efficiency_Video_Coding_tiers_and_levels
                // MaxLumaSampleRate = 3840*2160*30 = 248832000 < 267386880.
                if (requestLevel < 0 || requestLevel >= 150)
                {
                    return "150";
                }
            }
            else if (string.Equals(state.ActualOutputVideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
            {
                // Transcode to level 5.1 and lower for maximum compatibility.
                // h264 4k 30fps requires at least level 5.1 otherwise it will break on safari fmp4.
                // https://en.wikipedia.org/wiki/Advanced_Video_Coding#Levels
                if (requestLevel < 0 || requestLevel >= 51)
                {
                    return "51";
                }
            }

            return level;
        }

        public double? GetFramerateParam(EncodingJobInfo state)
        {
            var request = state.BaseRequest;

            if (request.Framerate.HasValue)
            {
                return request.Framerate.Value;
            }

            var maxrate = request.MaxFramerate;

            if (maxrate.HasValue && state.VideoStream is not null)
            {
                var contentRate = state.VideoStream.ReferenceFrameRate;

                if (contentRate.HasValue && contentRate.Value > maxrate.Value)
                {
                    return maxrate;
                }
            }

            return null;
        }

        public string GetHlsVideoKeyFrameArguments(
            EncodingJobInfo state,
            string codec,
            int segmentLength,
            bool isEventPlaylist,
            int? startNumber)
        {
            var args = string.Empty;
            var gopArg = string.Empty;

            var keyFrameArg = string.Format(
                CultureInfo.InvariantCulture,
                " -force_key_frames:0 \"expr:gte(t,n_forced*{0})\"",
                segmentLength);

            var framerate = state.VideoStream?.RealFrameRate;
            if (framerate.HasValue)
            {
                // This is to make sure keyframe interval is limited to our segment,
                // as forcing keyframes is not enough.
                // Example: we encoded half of desired length, then codec detected
                // scene cut and inserted a keyframe; next forced keyframe would
                // be created outside of segment, which breaks seeking.
                gopArg = string.Format(
                    CultureInfo.InvariantCulture,
                    " -g:v:0 {0} -keyint_min:v:0 {0}",
                    Math.Ceiling(segmentLength * framerate.Value));
            }

            // Unable to force key frames using these encoders, set key frames by GOP.
            if (IsGopOnlyKeyFrameEncoder(codec))
            {
                args += gopArg;
            }
            else if (IsForcedKeyFrameEncoder(codec))
            {
                args += keyFrameArg;

                // prevent the libx264 from post processing to break the set keyframe.
                if (string.Equals(codec, "libx264", StringComparison.OrdinalIgnoreCase))
                {
                    args += " -sc_threshold:v:0 0";
                }
            }
            else
            {
                args += keyFrameArg + gopArg;
            }

            // The in-band Parameter Sets generated by the AMD HEVC VA-API encoder is inconsistent
            // with the extradata generated by ffmpeg, causing decoding failures when using hvc1.
            if (IsAmdHevcVaapiEncoder(codec))
            {
                // Extracting the extradata from the in-band PS to bypass the issue.
                // This can be removed once the issue is resolved in libva or Mesa.
                // Transcoding is unavoidable here, so using BSF will not conflict with BSF in remuxing.
                args += " -flags:v -global_header -bsf:v extract_extradata=remove=0";
            }

            return args;
        }

        private static bool IsGopOnlyKeyFrameEncoder(string codec)
        {
            return string.Equals(codec, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "h264_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "h264_rkmpp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc_nvenc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc_rkmpp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "av1_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "av1_nvenc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "av1_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "libsvtav1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsForcedKeyFrameEncoder(string codec)
        {
            return string.Equals(codec, "libx264", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "libx265", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "av1_vaapi", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsAmdHevcVaapiEncoder(string codec)
        {
            return string.Equals(codec, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)
                && _mediaEncoder.IsVaapiDeviceAmd;
        }

        /// <summary>
        /// Gets the video bitrate to specify on the command line.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="videoEncoder">Video encoder to use.</param>
        /// <param name="encodingOptions">Encoding options.</param>
        /// <param name="defaultPreset">Default present to use for encoding.</param>
        /// <returns>Video bitrate.</returns>
        public string GetVideoQualityParam(EncodingJobInfo state, string videoEncoder, EncodingOptions encodingOptions, EncoderPreset defaultPreset)
        {
            var param = string.Empty;

            // Tutorials: Enable Intel GuC / HuC firmware loading for Low Power Encoding.
            // https://01.org/group/43/downloads/firmware
            // https://wiki.archlinux.org/title/intel_graphics#Enable_GuC_/_HuC_firmware_loading
            // Intel Low Power Encoding can save unnecessary CPU-GPU synchronization,
            // which will reduce overhead in performance intensive tasks such as 4k transcoding and tonemapping.
            var intelLowPowerHwEncoding = false;

            // Workaround for linux 5.18 to 6.1.3 i915 hang at cost of performance.
            // https://github.com/intel/media-driver/issues/1456
            var enableWaFori915Hang = false;

            var hardwareAccelerationType = encodingOptions.HardwareAccelerationType;

            if (hardwareAccelerationType == HardwareAccelerationType.vaapi)
            {
                intelLowPowerHwEncoding = GetVaapiIntelLowPowerHwEncoding(videoEncoder, encodingOptions, intelLowPowerHwEncoding);
            }
            else if (hardwareAccelerationType == HardwareAccelerationType.qsv)
            {
                if (OperatingSystem.IsLinux())
                {
                    enableWaFori915Hang = GetWaFori915Hang(state, encodingOptions, enableWaFori915Hang);
                }

                if (string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase))
                {
                    intelLowPowerHwEncoding = encodingOptions.EnableIntelLowPowerH264HwEncoder;
                }
                else if (string.Equals(videoEncoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase))
                {
                    intelLowPowerHwEncoding = encodingOptions.EnableIntelLowPowerHevcHwEncoder;
                }
                else
                {
                    enableWaFori915Hang = false;
                }
            }

            if (intelLowPowerHwEncoding)
            {
                param += " -low_power 1";
            }

            if (enableWaFori915Hang)
            {
                param += " -async_depth 1";
            }

            var isLibX265 = string.Equals(videoEncoder, "libx265", StringComparison.OrdinalIgnoreCase);
            var encodingPreset = encodingOptions.EncoderPreset;

            param += GetEncoderParam(encodingPreset, defaultPreset, encodingOptions, videoEncoder, isLibX265);
            param += GetVideoBitrateParam(state, videoEncoder);

            var framerate = GetFramerateParam(state);
            if (framerate.HasValue)
            {
                param += string.Format(CultureInfo.InvariantCulture, " -r {0}", framerate.Value.ToString(CultureInfo.InvariantCulture));
            }

            var targetVideoCodec = state.ActualOutputVideoCodec;
            if (IsHevcCodecName(targetVideoCodec))
            {
                targetVideoCodec = "hevc";
            }

            var profile = state.GetRequestedProfiles(targetVideoCodec).FirstOrDefault() ?? string.Empty;
            profile = WhiteSpaceRegex().Replace(profile, string.Empty).ToLowerInvariant();

            var videoProfiles = GetVideoProfiles(targetVideoCodec);

            profile = NormalizeVideoProfile(videoEncoder, profile, videoProfiles);

            if (!string.IsNullOrEmpty(profile))
            {
                param = AppendProfileParam(param, videoEncoder, profile);
            }

            var level = NormalizeTranscodingLevel(state, state.GetRequestedLevel(targetVideoCodec));

            if (!string.IsNullOrEmpty(level))
            {
                param = AppendLevelParam(param, videoEncoder, level);
            }

            if (string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase))
            {
                param += " -x264opts:0 subme=0:me_range=16:rc_lookahead=10:me=hex:open_gop=0";
            }

            if (string.Equals(videoEncoder, "libx265", StringComparison.OrdinalIgnoreCase))
            {
                param = AppendX265Params(param, encodingOptions);
            }

            if (SupportsSvtAv1Params(videoEncoder))
            {
                param += " -svtav1-params:0 rc=1:tune=0:film-grain=0:enable-overlays=1:enable-tf=0";
            }

            /* Access unit too large: 8192 < 20880 error */
            if (SupportsVaapiA53CcSei(videoEncoder))
            {
                param += " -sei -a53_cc";
            }

            return param;
        }

        private static string[] GetVideoProfiles(string targetVideoCodec)
        {
            var videoProfiles = Array.Empty<string>();
            if (string.Equals("h264", targetVideoCodec, StringComparison.OrdinalIgnoreCase))
            {
                videoProfiles = _videoProfilesH264;
            }
            else if (string.Equals("hevc", targetVideoCodec, StringComparison.OrdinalIgnoreCase))
            {
                videoProfiles = _videoProfilesH265;
            }
            else if (string.Equals("av1", targetVideoCodec, StringComparison.OrdinalIgnoreCase))
            {
                videoProfiles = _videoProfilesAv1;
            }

            return videoProfiles;
        }

        private static string NormalizeVideoProfile(string videoEncoder, string profile, string[] videoProfiles)
        {
            if (!videoProfiles.Contains(profile, StringComparison.OrdinalIgnoreCase))
            {
                profile = string.Empty;
            }

            // We only transcode to HEVC 8-bit for now, force Main Profile.
            if (IsMain10OrMainStillProfile(profile))
            {
                profile = "main";
            }

            // Extended Profile is not supported by any known h264 encoders, force Main Profile.
            if (profile.Contains("extended", StringComparison.OrdinalIgnoreCase))
            {
                profile = "main";
            }

            // Only libx264 support encoding H264 High 10 Profile, otherwise force High Profile.
            if (IsHigh10ProfileWithoutLibx264(videoEncoder, profile))
            {
                profile = "high";
            }

            // We only need Main profile of AV1 encoders.
            if (IsAv1EncoderWithUnsupportedProfile(videoEncoder, profile))
            {
                profile = "main";
            }

            // h264_vaapi does not support Baseline profile, force Constrained Baseline in this case,
            // which is compatible (and ugly).
            if (IsVaapiH264BaselineProfile(videoEncoder, profile))
            {
                profile = "constrained_baseline";
            }

            // libx264, h264_{qsv,nvenc,rkmpp} does not support Constrained Baseline profile, force Baseline in this case.
            if (IsBaselineOnlyH264Encoder(videoEncoder, profile))
            {
                profile = "baseline";
            }

            // libx264, h264_{qsv,nvenc,vaapi,rkmpp} does not support Constrained High profile, force High in this case.
            if (IsHighOnlyH264Encoder(videoEncoder, profile))
            {
                profile = "high";
            }

            if (IsAmfH264BaselineProfile(videoEncoder, profile))
            {
                profile = "constrained_baseline";
            }

            if (IsAmfH264ConstrainedHighProfile(videoEncoder, profile))
            {
                profile = "constrained_high";
            }

            if (IsVideoToolboxH264ConstrainedBaselineProfile(videoEncoder, profile))
            {
                profile = "constrained_baseline";
            }

            if (IsVideoToolboxH264ConstrainedHighProfile(videoEncoder, profile))
            {
                profile = "constrained_high";
            }

            return profile;
        }

        private static bool IsHevcCodecName(string targetVideoCodec)
        {
            return string.Equals(targetVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMain10OrMainStillProfile(string profile)
        {
            return profile.Contains("main10", StringComparison.OrdinalIgnoreCase)
                || profile.Contains("mainstill", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHigh10ProfileWithoutLibx264(string videoEncoder, string profile)
        {
            return !string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("high10", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVaapiH264BaselineProfile(string videoEncoder, string profile)
        {
            return string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("baseline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAmfH264BaselineProfile(string videoEncoder, string profile)
        {
            return string.Equals(videoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("baseline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAmfH264ConstrainedHighProfile(string videoEncoder, string profile)
        {
            return string.Equals(videoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("constrainedhigh", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVideoToolboxH264ConstrainedBaselineProfile(string videoEncoder, string profile)
        {
            return string.Equals(videoEncoder, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("constrainedbaseline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVideoToolboxH264ConstrainedHighProfile(string videoEncoder, string profile)
        {
            return string.Equals(videoEncoder, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("constrainedhigh", StringComparison.OrdinalIgnoreCase);
        }

        private bool SupportsSvtAv1Params(string videoEncoder)
        {
            return string.Equals(videoEncoder, "libsvtav1", StringComparison.OrdinalIgnoreCase)
                && _mediaEncoder.EncoderVersion >= _minFFmpegSvtAv1Params;
        }

        private bool GetVaapiIntelLowPowerHwEncoding(string videoEncoder, EncodingOptions encodingOptions, bool intelLowPowerHwEncoding)
        {
            var isIntelVaapiDriver = _mediaEncoder.IsVaapiDeviceInteliHD || _mediaEncoder.IsVaapiDeviceInteli965;

            if (string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase))
            {
                intelLowPowerHwEncoding = encodingOptions.EnableIntelLowPowerH264HwEncoder && isIntelVaapiDriver;
            }
            else if (string.Equals(videoEncoder, "hevc_vaapi", StringComparison.OrdinalIgnoreCase))
            {
                intelLowPowerHwEncoding = encodingOptions.EnableIntelLowPowerHevcHwEncoder && isIntelVaapiDriver;
            }

            return intelLowPowerHwEncoding;
        }

        private bool GetWaFori915Hang(EncodingJobInfo state, EncodingOptions encodingOptions, bool enableWaFori915Hang)
        {
            var ver = Environment.OSVersion.Version;
            var isFixedKernel60 = ver.Major == 6 && ver.Minor == 0 && ver >= _minFixedKernel60i915Hang;
            var isUnaffectedKernel = ver < _minKerneli915Hang || ver > _maxKerneli915Hang;

            if (!(isUnaffectedKernel || isFixedKernel60))
            {
                var vidDecoder = GetHardwareVideoDecoder(state, encodingOptions) ?? string.Empty;
                var isIntelDecoder = vidDecoder.Contains("qsv", StringComparison.OrdinalIgnoreCase)
                                     || vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                var doOclTonemap = _mediaEncoder.SupportsHwaccel("qsv")
                    && IsVaapiSupported(state)
                    && IsOpenclFullSupported()
                    && !IsIntelVppTonemapAvailable(state, encodingOptions)
                    && IsHwTonemapAvailable(state, encodingOptions);

                enableWaFori915Hang = isIntelDecoder && doOclTonemap;
            }

            return enableWaFori915Hang;
        }

        private static string AppendProfileParam(string param, string videoEncoder, string profile)
        {
            // Currently there's no profile option in av1_nvenc encoder
            if (!(string.Equals(videoEncoder, "av1_nvenc", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(videoEncoder, "h264_v4l2m2m", StringComparison.OrdinalIgnoreCase)))
            {
                param += " -profile:v:0 " + profile;
            }

            return param;
        }

        private string AppendLevelParam(string param, string videoEncoder, string level)
        {
            // libx264, QSV, AMF can adjust the given level to match the output.
            if (string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase))
            {
                param += " -level " + level;
            }
            else if (string.Equals(videoEncoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase))
            {
                // hevc_qsv use -level 51 instead of -level 153.
                if (double.TryParse(level, CultureInfo.InvariantCulture, out double hevcLevel))
                {
                    param += " -level " + (hevcLevel / 3);
                }
            }
            else if (string.Equals(videoEncoder, "av1_qsv", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(videoEncoder, "libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                // libsvtav1 and av1_qsv use -level 60 instead of -level 16
                // https://aomedia.org/av1/specification/annex-a/
                if (int.TryParse(level, NumberStyles.Any, CultureInfo.InvariantCulture, out int av1Level))
                {
                    var x = 2 + (av1Level >> 2);
                    var y = av1Level & 3;
                    var res = (x * 10) + y;
                    param += " -level " + res;
                }
            }
            else if (IsAmfVideoEncoder(videoEncoder))
            {
                param += " -level " + level;
            }
            else if (IsNvencVideoEncoder(videoEncoder))
            {
                // level option may cause NVENC to fail.
                // NVENC cannot adjust the given level, just throw an error.
            }
            else if (IsVaapiVideoEncoder(videoEncoder))
            {
                // level option may cause corrupted frames on AMD VAAPI.
                if (_mediaEncoder.IsVaapiDeviceInteliHD || _mediaEncoder.IsVaapiDeviceInteli965)
                {
                    param += " -level " + level;
                }
            }
            else if (string.Equals(videoEncoder, "h264_rkmpp", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(videoEncoder, "hevc_rkmpp", StringComparison.OrdinalIgnoreCase))
            {
                param += " -level " + level;
            }
            else if (!string.Equals(videoEncoder, "libx265", StringComparison.OrdinalIgnoreCase))
            {
                param += " -level " + level;
            }

            return param;
        }

        private static string AppendX265Params(string param, EncodingOptions encodingOptions)
        {
            // libx265 only accept level option in -x265-params.
            // level option may cause libx265 to fail.
            // libx265 cannot adjust the given level, just throw an error.
            param += " -x265-params:0 no-scenecut=1:no-open-gop=1:no-info=1";

            if (encodingOptions.EncoderPreset < EncoderPreset.ultrafast)
            {
                // The following params are slower than the ultrafast preset, don't use when ultrafast is selected.
                param += ":subme=3:merange=25:rc-lookahead=10:me=star:ctu=32:max-tu-size=32:min-cu-size=16:rskip=2:rskip-edge-threshold=2:no-sao=1:no-strong-intra-smoothing=1";
            }

            return param;
        }

        private static bool IsAv1EncoderWithUnsupportedProfile(string videoEncoder, string profile)
        {
            return videoEncoder.Contains("av1", StringComparison.OrdinalIgnoreCase)
                && (profile.Contains("high", StringComparison.OrdinalIgnoreCase)
                    || profile.Contains("professional", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsBaselineOnlyH264Encoder(string videoEncoder, string profile)
        {
            return (string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_rkmpp", StringComparison.OrdinalIgnoreCase))
                && profile.Contains("baseline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHighOnlyH264Encoder(string videoEncoder, string profile)
        {
            return (string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_rkmpp", StringComparison.OrdinalIgnoreCase))
                && profile.Contains("high", StringComparison.OrdinalIgnoreCase);
        }

        private bool SupportsVaapiA53CcSei(string videoEncoder)
        {
            return (string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(videoEncoder, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)) &&
                 _mediaEncoder.EncoderVersion >= _minFFmpegVaapiH26xEncA53CcSei;
        }
    }
}
