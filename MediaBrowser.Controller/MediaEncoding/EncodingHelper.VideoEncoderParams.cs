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
        private static bool IsH264BelowLevel51(EncoderParamCodecLevel codecLevel)
        {
            return string.Equals(codecLevel.Codec, "h264", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(codecLevel.Level, CultureInfo.InvariantCulture, out double requestedLevel)
                && requestedLevel < 51;
        }

        private string GetEncoderParam(GetEncoderParamParts parts, EncodingOptions encodingOptions, EncoderParamX265Encoder x265Encoder)
        {
            var param = string.Empty;
            var encoderPreset = parts.Preset ?? parts.DefaultPreset;
            if (IsLibX264OrLibX265(x265Encoder))
            {
                param = AppendX26xPresetAndCrf(new EncoderParamPreset(param, encoderPreset), new EncoderParamCrfOptions(encodingOptions, x265Encoder.IsLibX265));
            }
            else if (string.Equals(x265Encoder.VideoEncoder, "libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                param = AppendSvtAv1Preset(new EncoderParamPreset(param, encoderPreset));
            }
            else if (IsVaapiVideoEncoder(x265Encoder.VideoEncoder))
            {
                param = AppendVaapiCompressionLevel(new EncoderParamPreset(param, encoderPreset));
            }
            else
            {
                param = AppendVideoEncoderHwPresetParam(new EncoderParamPresetEncoder(param, encoderPreset, x265Encoder.VideoEncoder));
            }

            return param;
        }

        private string AppendVideoEncoderHwPresetParam(EncoderParamPresetEncoder presetEncoder)
        {
            var param = presetEncoder.Param;
            if (IsQsvVideoEncoder(presetEncoder.VideoEncoder))
            {
                param = AppendQsvPreset(new EncoderParamPresetEncoder(param, presetEncoder.EncoderPreset, presetEncoder.VideoEncoder));
            }
            else if (IsNvencVideoEncoder(presetEncoder.VideoEncoder))
            {
                param = AppendNvencPreset(new EncoderParamPresetEncoder(param, presetEncoder.EncoderPreset, presetEncoder.VideoEncoder));
            }
            else if (IsAmfVideoEncoder(presetEncoder.VideoEncoder))
            {
                param = AppendAmfQuality(new EncoderParamPresetEncoder(param, presetEncoder.EncoderPreset, presetEncoder.VideoEncoder));
            }
            else if (IsVideoToolboxH26xEncoder(presetEncoder.VideoEncoder))
            {
                param = AppendVideoToolboxPrioSpeed(new EncoderParamPreset(param, presetEncoder.EncoderPreset));
            }

            return param;
        }

        private static bool IsLibX264OrLibX265(EncoderParamX265Encoder x265Encoder)
        {
            return string.Equals(x265Encoder.VideoEncoder, "libx264", StringComparison.OrdinalIgnoreCase) || x265Encoder.IsLibX265;
        }

        private static bool IsVideoToolboxH26xEncoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase) // h264 (h264_videotoolbox)
                        || string.Equals(videoEncoder, "hevc_videotoolbox", StringComparison.OrdinalIgnoreCase); // hevc (hevc_videotoolbox)
        }

        private static string AppendX26xPresetAndCrf(EncoderParamPreset preset, EncoderParamCrfOptions crfOptions)
        {
            var param = preset.Param;
            var presetString = preset.EncoderPreset switch
            {
                EncoderPreset.auto => EncoderPreset.veryfast.ToString().ToLowerInvariant(),
                _ => preset.EncoderPreset.ToString().ToLowerInvariant()
            };

            param += " -preset " + presetString;

            int encodeCrf = crfOptions.EncodingOptions.H264Crf;
            if (crfOptions.IsLibX265)
            {
                encodeCrf = crfOptions.EncodingOptions.H265Crf;
            }

            if (encodeCrf >= 0 && encodeCrf <= 51)
            {
                param += " -crf " + encodeCrf.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                string defaultCrf = "23";
                if (crfOptions.IsLibX265)
                {
                    defaultCrf = "28";
                }

                param += " -crf " + defaultCrf;
            }

            return param;
        }

        private static string AppendSvtAv1Preset(EncoderParamPreset preset)
        {
            var param = preset.Param;
            // Default to use the recommended preset 10.
            // Omit presets < 5, which are too slow for on the fly encoding.
            // https://gitlab.com/AOMediaCodec/SVT-AV1/-/blob/master/Docs/Ffmpeg.md
            param += preset.EncoderPreset switch
            {
                EncoderPreset.veryslow => " -preset 5",
                EncoderPreset.slower => " -preset 6",
                EncoderPreset.slow => " -preset 7",
                EncoderPreset.medium => " -preset 8",
                EncoderPreset.fast => " -preset 9",
                _ => VideoEncoderSvtAv1FasterPreset(preset.EncoderPreset)
            };

            return param;
        }

        private static string VideoEncoderSvtAv1FasterPreset(EncoderPreset encoderPreset)
        {
            return encoderPreset switch
            {
                EncoderPreset.faster => " -preset 10",
                EncoderPreset.veryfast => " -preset 11",
                EncoderPreset.superfast => " -preset 12",
                EncoderPreset.ultrafast => " -preset 13",
                _ => " -preset 10"
            };
        }

        private string AppendVaapiCompressionLevel(EncoderParamPreset preset)
        {
            var param = preset.Param;
            // -compression_level is not reliable on AMD.
            if (_mediaEncoder.IsVaapiDeviceInteliHD)
            {
                param += preset.EncoderPreset switch
                {
                    EncoderPreset.veryslow => " -compression_level 1",
                    EncoderPreset.slower => " -compression_level 2",
                    EncoderPreset.slow => " -compression_level 3",
                    EncoderPreset.medium => " -compression_level 4",
                    _ => VideoEncoderVaapiFasterCompressionLevel(preset.EncoderPreset)
                };
            }

            return param;
        }

        private static string VideoEncoderVaapiFasterCompressionLevel(EncoderPreset encoderPreset)
        {
            return encoderPreset switch
            {
                EncoderPreset.fast => " -compression_level 5",
                EncoderPreset.faster => " -compression_level 6",
                EncoderPreset.veryfast => " -compression_level 7",
                EncoderPreset.superfast => " -compression_level 7",
                EncoderPreset.ultrafast => " -compression_level 7",
                _ => string.Empty
            };
        }

        private static string AppendQsvPreset(EncoderParamPresetEncoder presetEncoder)
        {
            var param = presetEncoder.Param;
            EncoderPreset[] valid_presets = [EncoderPreset.veryslow, EncoderPreset.slower, EncoderPreset.slow, EncoderPreset.medium, EncoderPreset.fast, EncoderPreset.faster, EncoderPreset.veryfast];

            param += " -preset " + (valid_presets.Contains(presetEncoder.EncoderPreset) ? presetEncoder.EncoderPreset : EncoderPreset.veryfast).ToString().ToLowerInvariant();

            // Enable MacroBlock level bitrate control for better subjective visual quality
            if (string.Equals(presetEncoder.VideoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(presetEncoder.VideoEncoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase))
            {
                param += " -mbbrc 1";
            }

            return param;
        }

        private static string AppendNvencPreset(EncoderParamPresetEncoder presetEncoder)
        {
            var param = presetEncoder.Param;
            param += presetEncoder.EncoderPreset switch
            {
                EncoderPreset.veryslow => " -preset p7",
                EncoderPreset.slower => " -preset p6",
                EncoderPreset.slow => " -preset p5",
                _ => VideoEncoderNvencFasterPreset(presetEncoder.EncoderPreset)
            };

            return AppendNvencAdaptiveQuantization(new EncoderParamEncoderArgs(param, presetEncoder.VideoEncoder));
        }

        private static string VideoEncoderNvencFasterPreset(EncoderPreset encoderPreset)
        {
            return encoderPreset switch
            {
                EncoderPreset.medium => " -preset p4",
                EncoderPreset.fast => " -preset p3",
                EncoderPreset.faster => " -preset p2",
                _ => " -preset p1"
            };
        }

        private static string AppendNvencAdaptiveQuantization(EncoderParamEncoderArgs encoderArgs)
        {
            var param = encoderArgs.Param;
            // Enable Spatial Adaptive Quantization for better subjective visual quality
            // H.264 is skipped for now because Maxwell 1 does not support it
            if (!string.Equals(encoderArgs.VideoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase))
            {
                param += " -spatial-aq 1 -aq-strength 15";
            }

            return param;
        }

        private string AppendAmfQuality(EncoderParamPresetEncoder presetEncoder)
        {
            var param = presetEncoder.Param;
            param += presetEncoder.EncoderPreset switch
            {
                EncoderPreset.veryslow => " -quality quality",
                EncoderPreset.slower => " -quality quality",
                EncoderPreset.slow => " -quality quality",
                EncoderPreset.medium => " -quality balanced",
                _ => " -quality speed"
            };

            return AppendAmfQualityOptions(new EncoderParamEncoderArgs(param, presetEncoder.VideoEncoder));
        }

        private string AppendAmfQualityOptions(EncoderParamEncoderArgs encoderArgs)
        {
            var param = encoderArgs.Param;
            // Enable Variance Based/Context Adaptive Quantization for better subjective visual quality
            if (IsAmfH26xEncoder(encoderArgs.VideoEncoder))
            {
                param += " -vbaq 1";
            }
            else if (SupportsAmfAv1Caq(encoderArgs.VideoEncoder))
            {
                param += " -aq_mode caq";
            }

            if (IsAmfHevcOrAv1Encoder(encoderArgs.VideoEncoder))
            {
                param += " -header_insertion_mode gop";
            }

            if (string.Equals(encoderArgs.VideoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase))
            {
                param += " -gops_per_idr 1";
            }

            return param;
        }

        private static bool IsAmfH26xEncoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase);
        }

        private bool SupportsAmfAv1Caq(string videoEncoder)
        {
            return string.Equals(videoEncoder, "av1_amf", StringComparison.OrdinalIgnoreCase)
                && _mediaEncoder.EncoderVersion >= _minFFmpegAmfAv1EncCAQ;
        }

        private static bool IsAmfHevcOrAv1Encoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "av1_amf", StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendVideoToolboxPrioSpeed(EncoderParamPreset preset)
        {
            var param = preset.Param;
            param += preset.EncoderPreset switch
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
                if (IsAv1LevelAboveCap(requestLevel))
                {
                    return "15";
                }
            }
            else
            {
                return NormalizeVideoEncoderHevcOrH264Level(state, new EncoderParamLevelRequest(level, requestLevel));
            }

            return level;
        }

        private static string NormalizeVideoEncoderHevcOrH264Level(EncodingJobInfo state, EncoderParamLevelRequest levelRequest)
        {
            if (IsHevcOrH265OutputCodec(state.ActualOutputVideoCodec))
            {
                // Transcode to level 5.0 and lower for maximum compatibility.
                // Level 5.0 is suitable for up to 4k 30fps hevc encoding, otherwise let the encoder to handle it.
                // https://en.wikipedia.org/wiki/High_Efficiency_Video_Coding_tiers_and_levels
                // MaxLumaSampleRate = 3840*2160*30 = 248832000 < 267386880.
                if (IsHevcLevelAboveCap(levelRequest.RequestLevel))
                {
                    return "150";
                }
            }
            else
            {
                return NormalizeVideoEncoderH264Level(state, levelRequest);
            }

            return levelRequest.Level;
        }

        private static string NormalizeVideoEncoderH264Level(EncodingJobInfo state, EncoderParamLevelRequest levelRequest)
        {
            if (string.Equals(state.ActualOutputVideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
            {
                // Transcode to level 5.1 and lower for maximum compatibility.
                // h264 4k 30fps requires at least level 5.1 otherwise it will break on safari fmp4.
                // https://en.wikipedia.org/wiki/Advanced_Video_Coding#Levels
                if (IsH264LevelAboveCap(levelRequest.RequestLevel))
                {
                    return "51";
                }
            }

            return levelRequest.Level;
        }

        private static bool IsAv1LevelAboveCap(double requestLevel)
        {
            return requestLevel < 0 || requestLevel >= 15;
        }

        private static bool IsHevcLevelAboveCap(double requestLevel)
        {
            return requestLevel < 0 || requestLevel >= 150;
        }

        private static bool IsH264LevelAboveCap(double requestLevel)
        {
            return requestLevel < 0 || requestLevel >= 51;
        }

        private static bool IsHevcOrH265OutputCodec(string codec)
        {
            return string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase);
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
                intelLowPowerHwEncoding = GetVaapiIntelLowPowerHwEncoding(new EncoderParamVaapiLowPower(videoEncoder, encodingOptions, intelLowPowerHwEncoding));
            }
            else if (hardwareAccelerationType == HardwareAccelerationType.qsv)
            {
                if (OperatingSystem.IsLinux())
                {
                    enableWaFori915Hang = GetWaFori915Hang(state, new EncoderParamI915Workaround(encodingOptions, enableWaFori915Hang));
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

            param = AppendIntelLowPowerParams(new EncoderParamLowPowerFlags(param, intelLowPowerHwEncoding, enableWaFori915Hang));

            var isLibX265 = string.Equals(videoEncoder, "libx265", StringComparison.OrdinalIgnoreCase);
            var encodingPreset = encodingOptions.EncoderPreset;

            param += GetEncoderParam(new GetEncoderParamParts(encodingPreset, defaultPreset), encodingOptions, new EncoderParamX265Encoder(videoEncoder, isLibX265));
            param += GetVideoBitrateParam(state, videoEncoder);

            var framerate = GetFramerateParam(state);
            if (framerate.HasValue)
            {
                param += string.Format(CultureInfo.InvariantCulture, " -r {0}", framerate.Value.ToString(CultureInfo.InvariantCulture));
            }

            param = AppendVideoProfileAndLevelParams(new EncoderParamProfileLevelArgs(param, state, videoEncoder));

            return AppendVideoEncoderSpecificParams(new EncoderParamEncoderArgs(param, videoEncoder), encodingOptions);
        }

        private static string AppendIntelLowPowerParams(EncoderParamLowPowerFlags lowPowerFlags)
        {
            var param = lowPowerFlags.Param;
            if (lowPowerFlags.IntelLowPowerHwEncoding)
            {
                param += " -low_power 1";
            }

            if (lowPowerFlags.EnableWaFori915Hang)
            {
                param += " -async_depth 1";
            }

            return param;
        }

        private string AppendVideoProfileAndLevelParams(EncoderParamProfileLevelArgs profileLevelArgs)
        {
            var param = profileLevelArgs.Param;
            var targetVideoCodec = profileLevelArgs.State.ActualOutputVideoCodec;
            if (IsHevcCodecName(targetVideoCodec))
            {
                targetVideoCodec = "hevc";
            }

            var profile = profileLevelArgs.State.GetRequestedProfiles(targetVideoCodec).FirstOrDefault() ?? string.Empty;
            profile = WhiteSpaceRegex().Replace(profile, string.Empty).ToLowerInvariant();

            var videoProfiles = GetVideoProfiles(targetVideoCodec);

            profile = NormalizeVideoProfile(new EncoderParamProfileCandidates(profileLevelArgs.VideoEncoder, profile, videoProfiles));

            if (!string.IsNullOrEmpty(profile))
            {
                param = AppendProfileParam(new EncoderParamProfileArgs(param, profileLevelArgs.VideoEncoder, profile));
            }

            var level = NormalizeTranscodingLevel(profileLevelArgs.State, profileLevelArgs.State.GetRequestedLevel(targetVideoCodec));

            if (!string.IsNullOrEmpty(level))
            {
                param = AppendLevelParam(new EncoderParamLevelArgs(param, profileLevelArgs.VideoEncoder, level));
            }

            return param;
        }

        private string AppendVideoEncoderSpecificParams(EncoderParamEncoderArgs encoderArgs, EncodingOptions encodingOptions)
        {
            var param = encoderArgs.Param;
            if (string.Equals(encoderArgs.VideoEncoder, "libx264", StringComparison.OrdinalIgnoreCase))
            {
                param += " -x264opts:0 subme=0:me_range=16:rc_lookahead=10:me=hex:open_gop=0";
            }

            if (string.Equals(encoderArgs.VideoEncoder, "libx265", StringComparison.OrdinalIgnoreCase))
            {
                param = AppendX265Params(new EncoderParamX265Options(param, encodingOptions));
            }

            if (SupportsSvtAv1Params(encoderArgs.VideoEncoder))
            {
                param += " -svtav1-params:0 rc=1:tune=0:film-grain=0:enable-overlays=1:enable-tf=0";
            }

            /* Access unit too large: 8192 < 20880 error */
            if (SupportsVaapiA53CcSei(encoderArgs.VideoEncoder))
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

        private static string NormalizeVideoProfile(EncoderParamProfileCandidates profileCandidates)
        {
            var profile = profileCandidates.Profile;
            if (!profileCandidates.VideoProfiles.Contains(profile, StringComparison.OrdinalIgnoreCase))
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
            if (IsHigh10ProfileWithoutLibx264(new EncoderParamProfile(profileCandidates.VideoEncoder, profile)))
            {
                profile = "high";
            }

            // We only need Main profile of AV1 encoders.
            if (IsAv1EncoderWithUnsupportedProfile(new EncoderParamProfile(profileCandidates.VideoEncoder, profile)))
            {
                profile = "main";
            }

            return NormalizeH264EncoderProfile(new EncoderParamProfile(profileCandidates.VideoEncoder, profile));
        }

        private static string NormalizeH264EncoderProfile(EncoderParamProfile encoderProfile)
        {
            var profile = encoderProfile.Profile;
            // h264_vaapi does not support Baseline profile, force Constrained Baseline in this case,
            // which is compatible (and ugly).
            if (IsVaapiH264BaselineProfile(new EncoderParamProfile(encoderProfile.VideoEncoder, profile)))
            {
                profile = "constrained_baseline";
            }

            // libx264, h264_{qsv,nvenc,rkmpp} does not support Constrained Baseline profile, force Baseline in this case.
            if (IsBaselineOnlyH264Encoder(new EncoderParamProfile(encoderProfile.VideoEncoder, profile)))
            {
                profile = "baseline";
            }

            // libx264, h264_{qsv,nvenc,vaapi,rkmpp} does not support Constrained High profile, force High in this case.
            if (IsHighOnlyH264Encoder(new EncoderParamProfile(encoderProfile.VideoEncoder, profile)))
            {
                profile = "high";
            }

            if (IsAmfH264BaselineProfile(new EncoderParamProfile(encoderProfile.VideoEncoder, profile)))
            {
                profile = "constrained_baseline";
            }

            if (IsAmfH264ConstrainedHighProfile(new EncoderParamProfile(encoderProfile.VideoEncoder, profile)))
            {
                profile = "constrained_high";
            }

            if (IsVideoToolboxH264ConstrainedBaselineProfile(new EncoderParamProfile(encoderProfile.VideoEncoder, profile)))
            {
                profile = "constrained_baseline";
            }

            if (IsVideoToolboxH264ConstrainedHighProfile(new EncoderParamProfile(encoderProfile.VideoEncoder, profile)))
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

        private static bool IsHigh10ProfileWithoutLibx264(EncoderParamProfile encoderProfile)
        {
            return !string.Equals(encoderProfile.VideoEncoder, "libx264", StringComparison.OrdinalIgnoreCase)
                && encoderProfile.Profile.Contains("high10", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVaapiH264BaselineProfile(EncoderParamProfile encoderProfile)
        {
            return string.Equals(encoderProfile.VideoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                && encoderProfile.Profile.Contains("baseline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAmfH264BaselineProfile(EncoderParamProfile encoderProfile)
        {
            return string.Equals(encoderProfile.VideoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase)
                && encoderProfile.Profile.Contains("baseline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAmfH264ConstrainedHighProfile(EncoderParamProfile encoderProfile)
        {
            return string.Equals(encoderProfile.VideoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase)
                && encoderProfile.Profile.Contains("constrainedhigh", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVideoToolboxH264ConstrainedBaselineProfile(EncoderParamProfile encoderProfile)
        {
            return string.Equals(encoderProfile.VideoEncoder, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase)
                && encoderProfile.Profile.Contains("constrainedbaseline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVideoToolboxH264ConstrainedHighProfile(EncoderParamProfile encoderProfile)
        {
            return string.Equals(encoderProfile.VideoEncoder, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase)
                && encoderProfile.Profile.Contains("constrainedhigh", StringComparison.OrdinalIgnoreCase);
        }

        private bool SupportsSvtAv1Params(string videoEncoder)
        {
            return string.Equals(videoEncoder, "libsvtav1", StringComparison.OrdinalIgnoreCase)
                && _mediaEncoder.EncoderVersion >= _minFFmpegSvtAv1Params;
        }

        private bool GetVaapiIntelLowPowerHwEncoding(EncoderParamVaapiLowPower vaapiLowPower)
        {
            var intelLowPowerHwEncoding = vaapiLowPower.IntelLowPowerHwEncoding;
            var isIntelVaapiDriver = _mediaEncoder.IsVaapiDeviceInteliHD || _mediaEncoder.IsVaapiDeviceInteli965;

            if (string.Equals(vaapiLowPower.VideoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase))
            {
                intelLowPowerHwEncoding = vaapiLowPower.EncodingOptions.EnableIntelLowPowerH264HwEncoder && isIntelVaapiDriver;
            }
            else if (string.Equals(vaapiLowPower.VideoEncoder, "hevc_vaapi", StringComparison.OrdinalIgnoreCase))
            {
                intelLowPowerHwEncoding = vaapiLowPower.EncodingOptions.EnableIntelLowPowerHevcHwEncoder && isIntelVaapiDriver;
            }

            return intelLowPowerHwEncoding;
        }

        private bool GetWaFori915Hang(EncodingJobInfo state, EncoderParamI915Workaround i915Workaround)
        {
            var enableWaFori915Hang = i915Workaround.EnableWaFori915Hang;
            var ver = Environment.OSVersion.Version;
            var isFixedKernel60 = IsFixedKernel60i915(ver);
            var isUnaffectedKernel = IsKernelUnaffectedByI915Hang(ver);

            if (!(isUnaffectedKernel || isFixedKernel60))
            {
                var vidDecoder = GetHardwareVideoDecoder(state, i915Workaround.EncodingOptions) ?? string.Empty;
                var isIntelDecoder = IsIntelHwDecoder(vidDecoder);
                var doOclTonemap = NeedsQsvOclTonemap(state, i915Workaround.EncodingOptions);

                enableWaFori915Hang = isIntelDecoder && doOclTonemap;
            }

            return enableWaFori915Hang;
        }

        private bool IsFixedKernel60i915(Version ver)
        {
            return ver.Major == 6 && ver.Minor == 0 && ver >= _minFixedKernel60i915Hang;
        }

        private bool IsKernelUnaffectedByI915Hang(Version ver)
        {
            return ver < _minKerneli915Hang || ver > _maxKerneli915Hang;
        }

        private static bool IsIntelHwDecoder(string vidDecoder)
        {
            return vidDecoder.Contains("qsv", StringComparison.OrdinalIgnoreCase)
                || vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
        }

        private bool NeedsQsvOclTonemap(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            return _mediaEncoder.SupportsHwaccel("qsv")
                && IsVaapiSupported(state)
                && IsOpenclFullSupported()
                && !IsIntelVppTonemapAvailable(state, encodingOptions)
                && IsHwTonemapAvailable(state, encodingOptions);
        }

        private static string AppendProfileParam(EncoderParamProfileArgs profileArgs)
        {
            var param = profileArgs.Param;
            // Currently there's no profile option in av1_nvenc encoder
            if (!(string.Equals(profileArgs.VideoEncoder, "av1_nvenc", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(profileArgs.VideoEncoder, "h264_v4l2m2m", StringComparison.OrdinalIgnoreCase)))
            {
                param += " -profile:v:0 " + profileArgs.Profile;
            }

            return param;
        }

        private string AppendLevelParam(EncoderParamLevelArgs levelArgs)
        {
            var param = levelArgs.Param;
            // libx264, QSV, AMF can adjust the given level to match the output.
            if (IsH264QsvOrLibX264Encoder(levelArgs.VideoEncoder))
            {
                param += " -level " + levelArgs.Level;
            }
            else if (string.Equals(levelArgs.VideoEncoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase))
            {
                param = AppendHevcQsvLevel(new EncoderParamLevel(param, levelArgs.Level));
            }
            else if (IsAv1QsvOrSvtAv1Encoder(levelArgs.VideoEncoder))
            {
                param = AppendAv1Level(new EncoderParamLevel(param, levelArgs.Level));
            }
            else
            {
                param = AppendVideoEncoderHwLevelParam(new EncoderParamLevelArgs(param, levelArgs.VideoEncoder, levelArgs.Level));
            }

            return param;
        }

        private string AppendVideoEncoderHwLevelParam(EncoderParamLevelArgs levelArgs)
        {
            var param = levelArgs.Param;
            if (IsAmfVideoEncoder(levelArgs.VideoEncoder))
            {
                param += " -level " + levelArgs.Level;
            }
            else if (IsNvencVideoEncoder(levelArgs.VideoEncoder))
            {
                // level option may cause NVENC to fail.
                // NVENC cannot adjust the given level, just throw an error.
            }
            else if (IsVaapiVideoEncoder(levelArgs.VideoEncoder))
            {
                param = AppendVaapiLevel(new EncoderParamLevel(param, levelArgs.Level));
            }
            else if (IsRkmppH26xEncoder(levelArgs.VideoEncoder))
            {
                param += " -level " + levelArgs.Level;
            }
            else if (IsNotLibX265Encoder(levelArgs.VideoEncoder))
            {
                param += " -level " + levelArgs.Level;
            }

            return param;
        }

        private static bool IsAv1QsvOrSvtAv1Encoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "av1_qsv", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(videoEncoder, "libsvtav1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNotLibX265Encoder(string videoEncoder)
        {
            return !string.Equals(videoEncoder, "libx265", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsH264QsvOrLibX264Encoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRkmppH26xEncoder(string videoEncoder)
        {
            return string.Equals(videoEncoder, "h264_rkmpp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoEncoder, "hevc_rkmpp", StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendHevcQsvLevel(EncoderParamLevel paramLevel)
        {
            var param = paramLevel.Param;
            // hevc_qsv use -level 51 instead of -level 153.
            if (double.TryParse(paramLevel.Level, CultureInfo.InvariantCulture, out double hevcLevel))
            {
                param += " -level " + (hevcLevel / 3);
            }

            return param;
        }

        private static string AppendAv1Level(EncoderParamLevel paramLevel)
        {
            var param = paramLevel.Param;
            // libsvtav1 and av1_qsv use -level 60 instead of -level 16
            // https://aomedia.org/av1/specification/annex-a/
            if (int.TryParse(paramLevel.Level, NumberStyles.Any, CultureInfo.InvariantCulture, out int av1Level))
            {
                var x = 2 + (av1Level >> 2);
                var y = av1Level & 3;
                var res = (x * 10) + y;
                param += " -level " + res;
            }

            return param;
        }

        private string AppendVaapiLevel(EncoderParamLevel paramLevel)
        {
            var param = paramLevel.Param;
            // level option may cause corrupted frames on AMD VAAPI.
            if (_mediaEncoder.IsVaapiDeviceInteliHD || _mediaEncoder.IsVaapiDeviceInteli965)
            {
                param += " -level " + paramLevel.Level;
            }

            return param;
        }

        private static string AppendX265Params(EncoderParamX265Options x265Options)
        {
            var param = x265Options.Param;
            // libx265 only accept level option in -x265-params.
            // level option may cause libx265 to fail.
            // libx265 cannot adjust the given level, just throw an error.
            param += " -x265-params:0 no-scenecut=1:no-open-gop=1:no-info=1";

            if (x265Options.EncodingOptions.EncoderPreset < EncoderPreset.ultrafast)
            {
                // The following params are slower than the ultrafast preset, don't use when ultrafast is selected.
                param += ":subme=3:merange=25:rc-lookahead=10:me=star:ctu=32:max-tu-size=32:min-cu-size=16:rskip=2:rskip-edge-threshold=2:no-sao=1:no-strong-intra-smoothing=1";
            }

            return param;
        }

        private static bool IsAv1EncoderWithUnsupportedProfile(EncoderParamProfile encoderProfile)
        {
            return encoderProfile.VideoEncoder.Contains("av1", StringComparison.OrdinalIgnoreCase)
                && (encoderProfile.Profile.Contains("high", StringComparison.OrdinalIgnoreCase)
                    || encoderProfile.Profile.Contains("professional", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsBaselineOnlyH264Encoder(EncoderParamProfile encoderProfile)
        {
            return (string.Equals(encoderProfile.VideoEncoder, "libx264", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(encoderProfile.VideoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(encoderProfile.VideoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(encoderProfile.VideoEncoder, "h264_rkmpp", StringComparison.OrdinalIgnoreCase))
                && encoderProfile.Profile.Contains("baseline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHighOnlyH264Encoder(EncoderParamProfile encoderProfile)
        {
            return (string.Equals(encoderProfile.VideoEncoder, "libx264", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(encoderProfile.VideoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(encoderProfile.VideoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(encoderProfile.VideoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(encoderProfile.VideoEncoder, "h264_rkmpp", StringComparison.OrdinalIgnoreCase))
                && encoderProfile.Profile.Contains("high", StringComparison.OrdinalIgnoreCase);
        }

        private bool SupportsVaapiA53CcSei(string videoEncoder)
        {
            return (string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(videoEncoder, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)) &&
                 _mediaEncoder.EncoderVersion >= _minFFmpegVaapiH26xEncA53CcSei;
        }

        private readonly record struct GetEncoderParamParts(EncoderPreset? Preset, EncoderPreset DefaultPreset);

        private readonly record struct EncoderParamCodecLevel(string Codec, string Level);

        private readonly record struct EncoderParamX265Encoder(string VideoEncoder, bool IsLibX265);

        private readonly record struct EncoderParamPresetEncoder(string Param, EncoderPreset EncoderPreset, string VideoEncoder);

        private readonly record struct EncoderParamPreset(string Param, EncoderPreset EncoderPreset);

        private readonly record struct EncoderParamCrfOptions(EncodingOptions EncodingOptions, bool IsLibX265);

        private readonly record struct EncoderParamEncoderArgs(string Param, string VideoEncoder);

        private readonly record struct EncoderParamLevelRequest(string Level, double RequestLevel);

        private readonly record struct EncoderParamLowPowerFlags(string Param, bool IntelLowPowerHwEncoding, bool EnableWaFori915Hang);

        private readonly record struct EncoderParamProfileLevelArgs(string Param, EncodingJobInfo State, string VideoEncoder);

        private readonly record struct EncoderParamProfileCandidates(string VideoEncoder, string Profile, string[] VideoProfiles);

        private readonly record struct EncoderParamProfile(string VideoEncoder, string Profile);

        private readonly record struct EncoderParamVaapiLowPower(string VideoEncoder, EncodingOptions EncodingOptions, bool IntelLowPowerHwEncoding);

        private readonly record struct EncoderParamI915Workaround(EncodingOptions EncodingOptions, bool EnableWaFori915Hang);

        private readonly record struct EncoderParamProfileArgs(string Param, string VideoEncoder, string Profile);

        private readonly record struct EncoderParamLevelArgs(string Param, string VideoEncoder, string Level);

        private readonly record struct EncoderParamLevel(string Param, string Level);

        private readonly record struct EncoderParamX265Options(string Param, EncodingOptions EncodingOptions);
    }
}
