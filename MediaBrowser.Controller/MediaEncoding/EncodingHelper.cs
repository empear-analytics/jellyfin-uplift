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
        /// <summary>
        /// The codec validation regex string.
        /// This regular expression matches strings that consist of alphanumeric characters, hyphens,
        /// periods, underscores, commas, and vertical bars, with a length between 0 and 40 characters.
        /// This should matches all common valid codecs.
        /// </summary>
        public const string ContainerValidationRegexStr = @"^[a-zA-Z0-9\-\._,|]{0,40}$";

        /// <summary>
        /// The level validation regex string.
        /// This regular expression matches strings representing a double.
        /// </summary>
        public const string LevelValidationRegexStr = @"-?[0-9]+(?:\.[0-9]+)?";

        private const string _defaultMjpegEncoder = "mjpeg";

        private const string QsvAlias = "qs";
        private const string VaapiAlias = "va";
        private const string D3d11vaAlias = "dx11";
        private const string VideotoolboxAlias = "vt";
        private const string RkmppAlias = "rk";
        private const string OpenclAlias = "ocl";
        private const string CudaAlias = "cu";
        private const string DrmAlias = "dr";
        private const string VulkanAlias = "vk";
        private readonly IApplicationPaths _appPaths;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly ISubtitleEncoder _subtitleEncoder;
        private readonly IConfiguration _config;
        private readonly IConfigurationManager _configurationManager;
        private readonly IPathManager _pathManager;

        // i915 hang was fixed by linux 6.2 (3f882f2)
        private readonly Version _minKerneli915Hang = new Version(5, 18);
        private readonly Version _maxKerneli915Hang = new Version(6, 1, 3);
        private readonly Version _minFixedKernel60i915Hang = new Version(6, 0, 18);
        private readonly Version _minKernelVersionAmdVkFmtModifier = new Version(5, 15);

        private readonly Version _minFFmpegImplicitHwaccel = new Version(6, 0);
        private readonly Version _minFFmpegHwaUnsafeOutput = new Version(6, 0);
        private readonly Version _minFFmpegOclCuTonemapMode = new Version(5, 1, 3);
        private readonly Version _minFFmpegSvtAv1Params = new Version(5, 1);
        private readonly Version _minFFmpegVaapiH26xEncA53CcSei = new Version(6, 0);
        private readonly Version _minFFmpegReadrateOption = new Version(5, 0);
        private readonly Version _minFFmpegWorkingVtHwSurface = new Version(7, 0, 1);
        private readonly Version _minFFmpegDisplayRotationOption = new Version(6, 0);
        private readonly Version _minFFmpegAdvancedTonemapMode = new Version(7, 0, 1);
        private readonly Version _minFFmpegAlteredVaVkInterop = new Version(7, 0, 1);
        private readonly Version _minFFmpegQsvVppTonemapOption = new Version(7, 0, 1);
        private readonly Version _minFFmpegQsvVppOutRangeOption = new Version(7, 0, 1);
        private readonly Version _minFFmpegVaapiDeviceVendorId = new Version(7, 0, 1);
        private readonly Version _minFFmpegQsvVppScaleModeOption = new Version(6, 0);
        private readonly Version _minFFmpegRkmppHevcDecDoviRpu = new Version(7, 1, 1);
        private readonly Version _minFFmpegReadrateCatchupOption = new Version(8, 0);
        private readonly Version _minFFmpegNoiseBsfDrop = new Version(5, 0);
        private readonly Version _minFFmpegAmfAv1EncCAQ = new Version(7, 1);

        private static readonly string[] _videoProfilesH264 =
        [
            "ConstrainedBaseline",
            "Baseline",
            "Extended",
            "Main",
            "High",
            "ProgressiveHigh",
            "ConstrainedHigh",
            "High10"
        ];

        private static readonly string[] _videoProfilesH265 =
        [
            "Main",
            "Main10"
        ];

        private static readonly string[] _videoProfilesAv1 =
        [
            "Main",
            "High",
            "Professional",
        ];

        private static readonly HashSet<string> _mp4ContainerNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "mp4",
            "m4a",
            "m4p",
            "m4b",
            "m4r",
            "m4v",
        };

        private static readonly TonemappingMode[] _legacyTonemapModes = [TonemappingMode.max, TonemappingMode.rgb];
        private static readonly TonemappingMode[] _advancedTonemapModes = [TonemappingMode.lum, TonemappingMode.itp];

        // Set max transcoding channels for encoders that can't handle more than a set amount of channels
        // AAC, FLAC, ALAC, libopus, libvorbis encoders all support at least 8 channels
        private static readonly Dictionary<string, int> _audioTranscodeChannelLookup = new(StringComparer.OrdinalIgnoreCase)
        {
            { "libmp3lame", 2 },
            { "libfdk_aac", 6 },
            { "ac3", 6 },
            { "eac3", 6 },
            { "dca", 6 },
            { "mlp", 6 },
            { "truehd", 6 },
        };

        private static readonly Dictionary<HardwareAccelerationType, string> _mjpegCodecMap = new()
        {
            { HardwareAccelerationType.vaapi, _defaultMjpegEncoder + "_vaapi" },
            { HardwareAccelerationType.qsv, _defaultMjpegEncoder + "_qsv" },
            { HardwareAccelerationType.videotoolbox, _defaultMjpegEncoder + "_videotoolbox" },
            { HardwareAccelerationType.rkmpp, _defaultMjpegEncoder + "_rkmpp" }
        };

        public static readonly string[] LosslessAudioCodecs =
        [
            "alac",
            "ape",
            "flac",
            "mlp",
            "truehd",
            "wavpack"
        ];

        public EncodingHelper(
            IApplicationPaths appPaths,
            IMediaEncoder mediaEncoder,
            ISubtitleEncoder subtitleEncoder,
            IConfiguration config,
            IConfigurationManager configurationManager,
            IPathManager pathManager)
        {
            _appPaths = appPaths;
            _mediaEncoder = mediaEncoder;
            _subtitleEncoder = subtitleEncoder;
            _config = config;
            _configurationManager = configurationManager;
            _pathManager = pathManager;
        }

        private enum DynamicHdrMetadataRemovalPlan
        {
            None,
            RemoveDovi,
            RemoveHdr10Plus,
        }

        /// <summary>
        /// The codec validation regex.
        /// This regular expression matches strings that consist of alphanumeric characters, hyphens,
        /// periods, underscores, commas, and vertical bars, with a length between 0 and 40 characters.
        /// This should matches all common valid codecs.
        /// </summary>
        [GeneratedRegex(ContainerValidationRegexStr)]
        public static partial Regex ContainerValidationRegex();

        /// <summary>
        /// The level validation regex string.
        /// This regular expression matches strings representing a double.
        /// </summary>
        [GeneratedRegex(LevelValidationRegexStr)]
        public static partial Regex LevelValidationRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhiteSpaceRegex();

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

        /// <summary>
        /// Gets the user agent param.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetUserAgentParam(EncodingJobInfo state)
        {
            if (state.RemoteHttpHeaders.TryGetValue("User-Agent", out string useragent))
            {
                return "-user_agent \"" + useragent + "\"";
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the referer param.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetRefererParam(EncodingJobInfo state)
        {
            if (state.RemoteHttpHeaders.TryGetValue("Referer", out string referer))
            {
                return "-referer \"" + referer + "\"";
            }

            return string.Empty;
        }

        public static string GetInputFormat(string container)
        {
            if (string.IsNullOrEmpty(container) || !ContainerValidationRegex().IsMatch(container))
            {
                return null;
            }

            container = container.Replace("mkv", "matroska", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(container, "ts", StringComparison.OrdinalIgnoreCase))
            {
                return "mpegts";
            }

            // For these need to find out the ffmpeg names
            if (string.Equals(container, "m2ts", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "wmv", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "mts", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "vob", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return GetInputFormatFromLegacyContainer(container);
        }

        /// <summary>
        /// Gets decoder from a codec.
        /// </summary>
        /// <param name="codec">Codec to use.</param>
        /// <returns>Decoder string.</returns>
        public string GetDecoderFromCodec(string codec)
        {
            // For these need to find out the ffmpeg names
            if (string.Equals(codec, "mp2", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(codec, "aac_latm", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(codec, "eac3", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (_mediaEncoder.SupportsDecoder(codec))
            {
                return codec;
            }

            return null;
        }

        /// <summary>
        /// Infers the audio codec based on the url.
        /// </summary>
        /// <param name="container">Container to use.</param>
        /// <returns>Codec string.</returns>
        public string InferAudioCodec(string container)
        {
            if (string.IsNullOrWhiteSpace(container))
            {
                // this may not work, but if the client is that broken we cannot do anything better
                return "aac";
            }

            var inferredCodec = container.ToLowerInvariant();

            return inferredCodec switch
            {
                "ogg" or "oga" or "ogv" or "webm" or "webma" => "opus",
                "m4a" or "m4b" or "mp4" or "mov" or "mkv" or "mka" => "aac",
                "ts" or "avi" or "flv" or "f4v" or "swf" => "mp3",
                // Containers that share their name with the codec they carry.
                "aac" or "ac3" or "alac" or "dts" or "eac3" or "flac" or "mp2" or "mp3" or "opus" or "truehd" or "vorbis" => inferredCodec,
                // Anything else - manifests such as m3u8/mpd in particular - names a container that
                // is not an audio codec. Never hand that name to ffmpeg as an encoder.
                _ => "aac"
            };
        }

        /// <summary>
        /// Infers the video codec.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <returns>System.Nullable{VideoCodecs}.</returns>
        public string InferVideoCodec(string url)
        {
            var ext = Path.GetExtension(url.AsSpan());

            if (ext.Equals(".asf", StringComparison.OrdinalIgnoreCase))
            {
                return "wmv";
            }

            if (ext.Equals(".webm", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: this may not always mean VP8, as the codec ages
                return "vp8";
            }

            if (ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ogv", StringComparison.OrdinalIgnoreCase))
            {
                return "theora";
            }

            if (ext.Equals(".m3u8", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ts", StringComparison.OrdinalIgnoreCase))
            {
                return "h264";
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

        /// <summary>
        /// Gets the input argument.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="segmentContainer">Segment Container.</param>
        /// <returns>Input arguments.</returns>
        public string GetInputArgument(EncodingJobInfo state, EncodingOptions options, string segmentContainer)
        {
            var arg = new StringBuilder();
            var inputVidHwaccelArgs = GetInputVideoHwaccelArgs(state, options);

            if (!string.IsNullOrEmpty(inputVidHwaccelArgs))
            {
                arg.Append(inputVidHwaccelArgs);
            }

            var canvasArgs = GetGraphicalSubCanvasSize(state);
            if (!string.IsNullOrEmpty(canvasArgs))
            {
                arg.Append(canvasArgs);
            }

            if (IsDvdOrBluRaySource(state))
            {
                AppendConcatInput(state, arg);
            }
            else
            {
                arg.Append(" -i ")
                    .Append(_mediaEncoder.GetInputPathArgument(state));
            }

            if (NeedsExternalSubtitleMuxing(state))
            {
                var subtitlePath = state.SubtitleStream.Path;
                var isGraphicalBurnIn = IsGraphicalSubtitleBurnIn(state);

                // dvdsub/vobsub graphical subtitles use .sub+.idx pairs
                subtitlePath = GetIdxSubtitlePath(subtitlePath);

                AppendSubtitleProbeArgs(state, arg);

                // Also seek the external subtitles stream.
                var seekSubParam = GetFastSeekCommandLineParameter(state, options, segmentContainer);
                if (!string.IsNullOrEmpty(seekSubParam))
                {
                    arg.Append(' ').Append(seekSubParam);
                }

                if (isGraphicalBurnIn && !string.IsNullOrEmpty(canvasArgs))
                {
                    arg.Append(canvasArgs);
                }

                arg.Append(" -i file:\"").Append(subtitlePath.EscapeProcessArgument()).Append('\"');
            }

            if (HasExternalAudioStream(state))
            {
                AppendExternalAudioInput(state, options, segmentContainer, arg);
            }

            // Disable auto inserted SW scaler for HW decoders in case of changed resolution.
            var isSwDecoder = string.IsNullOrEmpty(GetHardwareVideoDecoder(state, options));
            if (!isSwDecoder)
            {
                arg.Append(" -noautoscale");
            }

            return arg.ToString();
        }

        /// <summary>
        /// Gets the text subtitle param.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="enableAlpha">Enable alpha processing.</param>
        /// <param name="enableSub2video">Enable sub2video mode.</param>
        /// <returns>System.String.</returns>
        public string GetTextSubtitlesFilter(EncodingJobInfo state, bool enableAlpha, bool enableSub2video)
        {
            var seconds = Math.Round(TimeSpan.FromTicks(state.StartTimeTicks ?? 0).TotalSeconds);

            // hls always copies timestamps
            var setPtsParam = state.CopyTimestamps || state.TranscodingType != TranscodingJobType.Progressive
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",setpts=PTS -{0}/TB", seconds);

            var alphaParam = enableAlpha ? ":alpha=1" : string.Empty;
            var sub2videoParam = enableSub2video ? ":sub2video=1" : string.Empty;

            var fontPath = _pathManager.GetAttachmentFolderPath(state.MediaSource.Id);
            var fontParam = fontPath is null
                ? string.Empty
                : string.Format(
                    CultureInfo.InvariantCulture,
                    ":fontsdir='{0}'",
                    _mediaEncoder.EscapeSubtitleFilterPath(fontPath));

            if (state.SubtitleStream.IsExternal)
            {
                var charsetParam = GetSubtitleCharsetParam(state);

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "subtitles=f='{0}'{1}{2}{3}{4}{5}",
                    _mediaEncoder.EscapeSubtitleFilterPath(state.SubtitleStream.Path),
                    charsetParam,
                    alphaParam,
                    sub2videoParam,
                    fontParam,
                    setPtsParam);
            }

            var subtitlePath = _subtitleEncoder.GetSubtitleFilePath(
                    state.SubtitleStream,
                    state.MediaSource,
                    CancellationToken.None).GetAwaiter().GetResult();

            return string.Format(
                CultureInfo.InvariantCulture,
                "subtitles=f='{0}'{1}{2}{3}{4}",
                _mediaEncoder.EscapeSubtitleFilterPath(subtitlePath),
                alphaParam,
                sub2videoParam,
                fontParam,
                setPtsParam);
        }

        /// <summary>
        /// Enforces the resolution limit.
        /// </summary>
        /// <param name="state">The state.</param>
        public void EnforceResolutionLimit(EncodingJobInfo state)
        {
            var videoRequest = state.BaseRequest;

            // Switch the incoming params to be ceilings rather than fixed values
            videoRequest.MaxWidth = videoRequest.MaxWidth ?? videoRequest.Width;
            videoRequest.MaxHeight = videoRequest.MaxHeight ?? videoRequest.Height;

            videoRequest.Width = null;
            videoRequest.Height = null;
        }

        /// <summary>
        /// Gets the fast seek command line parameter.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="options">The options.</param>
        /// <param name="segmentContainer">Segment Container.</param>
        /// <returns>System.String.</returns>
        /// <value>The fast seek command line parameter.</value>
        public string GetFastSeekCommandLineParameter(EncodingJobInfo state, EncodingOptions options, string segmentContainer)
        {
            var time = state.BaseRequest.StartTimeTicks ?? 0;
            var maxTime = state.RunTimeTicks ?? 0;
            var seekParam = string.Empty;

            if (time > 0)
            {
                // For direct streaming/remuxing, HLS segments start at keyframes.
                // However, ffmpeg will seek to previous keyframe when the exact frame time is the input
                // Workaround this by adding 0.5s offset to the seeking time to get the exact keyframe on most videos.
                // This will help subtitle syncing.
                var isHlsRemuxing = state.IsVideoRequest && state.TranscodingType is TranscodingJobType.Hls && IsCopyCodec(state.OutputVideoCodec);
                var seekTick = isHlsRemuxing ? time + 5000000L : time;

                // Seeking beyond EOF makes no sense in transcoding. Clamp the seekTick value to
                // [0, RuntimeTicks - 5.0s], so that the muxer gets packets and avoid error codes.
                if (maxTime > 0)
                {
                    seekTick = Math.Clamp(seekTick, 0, Math.Max(maxTime - 50000000L, 0));
                }

                seekParam += string.Format(CultureInfo.InvariantCulture, "-ss {0}", _mediaEncoder.GetTimeParameter(seekTick));
            }

            return seekParam;
        }

        /// <summary>
        /// Gets the map args.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetMapArgs(EncodingJobInfo state)
        {
            // If we don't have known media info
            // If input is video, use -sn to drop subtitles
            // Otherwise just return empty
            if (HasNoKnownMediaStreams(state))
            {
                return state.IsInputVideo ? "-sn" : string.Empty;
            }

            // We have media info, but we don't know the stream index
            if (HasUnknownVideoStreamIndex(state))
            {
                return "-sn";
            }

            // We have media info, but we don't know the stream index
            if (HasUnknownAudioStreamIndex(state))
            {
                return state.IsInputVideo ? "-sn" : string.Empty;
            }

            return GetStreamMapArgs(state);
        }

        /// <summary>
        /// Gets the negative map args by filters.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="videoProcessFilters">The videoProcessFilters.</param>
        /// <returns>System.String.</returns>
        public string GetNegativeMapArgsByFilters(EncodingJobInfo state, string videoProcessFilters)
        {
            string args = string.Empty;

            // http://ffmpeg.org/ffmpeg-all.html#toc-Complex-filtergraphs-1
            if (state.VideoStream is not null && videoProcessFilters.Contains("-filter_complex", StringComparison.Ordinal))
            {
                int videoStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.VideoStream);

                args += string.Format(
                    CultureInfo.InvariantCulture,
                    "-map -0:{0} ",
                    videoStreamIndex);
            }

            return args;
        }

        /// <summary>
        /// Determines which stream will be used for playback.
        /// </summary>
        /// <param name="allStream">All stream.</param>
        /// <param name="desiredIndex">Index of the desired.</param>
        /// <param name="type">The type.</param>
        /// <param name="returnFirstIfNoIndex">if set to <c>true</c> [return first if no index].</param>
        /// <returns>MediaStream.</returns>
        public MediaStream GetMediaStream(IEnumerable<MediaStream> allStream, int? desiredIndex, MediaStreamType type, bool returnFirstIfNoIndex = true)
        {
            var streams = allStream.Where(s => s.Type == type).OrderBy(i => i.Index).ToList();

            if (desiredIndex.HasValue)
            {
                var stream = streams.FirstOrDefault(s => s.Index == desiredIndex.Value);

                if (stream is not null)
                {
                    return stream;
                }
            }

            if (returnFirstIfNoIndex && type == MediaStreamType.Audio)
            {
                return streams.FirstOrDefault(i => i.Channels.HasValue && i.Channels.Value > 0) ??
                       streams.FirstOrDefault();
            }

            // Just return the first one
            return returnFirstIfNoIndex ? streams.FirstOrDefault() : null;
        }

        /// <summary>
        /// Gets the number of threads.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="encodingOptions">Encoding options.</param>
        /// <param name="outputVideoCodec">Video codec to use.</param>
        /// <returns>Number of threads.</returns>
#nullable enable
        public static int GetNumberOfThreads(EncodingJobInfo? state, EncodingOptions encodingOptions, string? outputVideoCodec)
        {
            var threads = state?.BaseRequest.CpuCoreLimit ?? encodingOptions.EncodingThreadCount;

            if (threads <= 0)
            {
                // Automatically set thread count
                return 0;
            }

            return Math.Min(threads, Environment.ProcessorCount);
        }

#nullable disable
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
            if (state.AudioStream is not null
                && CanStreamCopyAudio(state, state.AudioStream, state.SupportedAudioCodecs, out audioCopyFailureReasons)
                && !preventHlsAudioCopy)
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

        private string GetFfmpegAnalyzeDurationArg(EncodingJobInfo state)
        {
            var analyzeDurationArgument = string.Empty;

            // Apply -analyzeduration as per the environment variable,
            // otherwise ffmpeg will break on certain files due to default value is 0.
            var ffmpegAnalyzeDuration = _config.GetFFmpegAnalyzeDuration() ?? string.Empty;

            if (state.MediaSource.AnalyzeDurationMs > 0)
            {
                analyzeDurationArgument = "-analyzeduration " + (state.MediaSource.AnalyzeDurationMs.Value * 1000).ToString(CultureInfo.InvariantCulture);
            }
            else if (!string.IsNullOrEmpty(ffmpegAnalyzeDuration))
            {
                analyzeDurationArgument = "-analyzeduration " + ffmpegAnalyzeDuration;
            }

            return analyzeDurationArgument;
        }

        private string GetFfmpegProbesizeArg()
        {
            var ffmpegProbeSize = _config.GetFFmpegProbeSize();

            if (!string.IsNullOrEmpty(ffmpegProbeSize))
            {
                return $"-probesize {ffmpegProbeSize}";
            }

            return string.Empty;
        }

        public string GetInputModifier(EncodingJobInfo state, EncodingOptions encodingOptions, string segmentContainer)
        {
            var inputModifier = GetInputProbeAndHeaderParams(state);

            inputModifier += " " + GetFastSeekCommandLineParameter(state, encodingOptions, segmentContainer);
            inputModifier = inputModifier.Trim();

            inputModifier = AppendRtspAndAudioSyncOptions(inputModifier, state);

            // The -fps_mode option cannot be applied to input
            if (NeedsInputVideoSync(state))
            {
                inputModifier += GetVideoSyncOption(state.InputVideoSync, _mediaEncoder.EncoderVersion);
            }

            int readrate = 0;
            if (ShouldReadAtNativeFramerate(state))
            {
                readrate = 1;
                inputModifier += " -re";
            }
            else if (NeedsSegmentDeletionReadrate(state, encodingOptions))
            {
                // Set an input read rate limit 10x for using SegmentDeletion with stream-copy
                // to prevent ffmpeg from exiting prematurely (due to fast drive)
                readrate = 10;
                inputModifier += $" -readrate {readrate}";
            }

            // Set a larger catchup value to revert to the old behavior,
            // otherwise, remuxing might stall due to this new option
            if (NeedsReadrateCatchup(readrate))
            {
                inputModifier += $" -readrate_catchup {readrate * 100}";
            }

            inputModifier = AppendInputFormatFlags(inputModifier, state);

            if (state.IsVideoRequest)
            {
                inputModifier = AppendExplicitInputFormat(inputModifier, state, encodingOptions);
            }

            if (state.MediaSource.RequiresLooping)
            {
                inputModifier += " -stream_loop -1 -reconnect_at_eof 1 -reconnect_streamed 1 -reconnect_delay_max 2";
            }

            return inputModifier;
        }

        private static string AppendRtspAndAudioSyncOptions(string inputModifier, EncodingJobInfo state)
        {
            if (state.InputProtocol == MediaProtocol.Rtsp)
            {
                inputModifier += " -rtsp_transport tcp+udp -rtsp_flags prefer_tcp";
            }

            if (!string.IsNullOrEmpty(state.InputAudioSync))
            {
                inputModifier += " -async " + state.InputAudioSync;
            }

            return inputModifier;
        }

        private bool NeedsInputVideoSync(EncodingJobInfo state)
        {
            return !string.IsNullOrEmpty(state.InputVideoSync) && _mediaEncoder.EncoderVersion < new Version(5, 1);
        }

        private static bool ShouldReadAtNativeFramerate(EncodingJobInfo state)
        {
            return state.ReadInputAtNativeFramerate && state.InputProtocol != MediaProtocol.Rtsp;
        }

        private bool NeedsReadrateCatchup(int readrate)
        {
            return readrate > 0 && _mediaEncoder.EncoderVersion >= _minFFmpegReadrateCatchupOption;
        }

        private static string AppendExplicitInputFormat(string inputModifier, EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            if (NeedsExplicitInputFormat(state, encodingOptions))
            {
                var inputFormat = GetInputFormat(state.InputContainer);
                if (!string.IsNullOrEmpty(inputFormat))
                {
                    inputModifier += " -f " + inputFormat;
                }
            }

            return inputModifier;
        }

        private static string AppendInputFormatFlags(string inputModifier, EncodingJobInfo state)
        {
            var flags = new List<string>();
            if (state.IgnoreInputDts)
            {
                flags.Add("+igndts");
            }

            if (state.IgnoreInputIndex)
            {
                flags.Add("+ignidx");
            }

            if (state.GenPtsInput || IsCopyCodec(state.OutputVideoCodec))
            {
                flags.Add("+genpts");
            }

            if (state.DiscardCorruptFramesInput)
            {
                flags.Add("+discardcorrupt");
            }

            if (state.EnableFastSeekInput)
            {
                flags.Add("+fastseek");
            }

            if (flags.Count > 0)
            {
                inputModifier += " -fflags " + string.Join(string.Empty, flags);
            }

            return inputModifier;
        }

        private bool NeedsSegmentDeletionReadrate(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            return encodingOptions.EnableSegmentDeletion
                && state.VideoStream is not null
                && state.TranscodingType == TranscodingJobType.Hls
                && IsCopyCodec(state.OutputVideoCodec)
                && _mediaEncoder.EncoderVersion >= _minFFmpegReadrateOption;
        }

        private static bool NeedsExplicitInputFormat(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            return !string.IsNullOrEmpty(state.InputContainer) && state.VideoType == VideoType.VideoFile && encodingOptions.HardwareAccelerationType != HardwareAccelerationType.none;
        }

        public void AttachMediaSourceInfo(
            EncodingJobInfo state,
            EncodingOptions encodingOptions,
            MediaSourceInfo mediaSource,
            string requestedUrl)
        {
            ArgumentNullException.ThrowIfNull(state);

            ArgumentNullException.ThrowIfNull(mediaSource);

            var path = mediaSource.Path;
            var protocol = mediaSource.Protocol;

            if (HasEncoderPathAndProtocol(mediaSource))
            {
                path = mediaSource.EncoderPath;
                protocol = mediaSource.EncoderProtocol.Value;
            }

            state.MediaPath = path;
            state.InputProtocol = protocol;
            state.InputContainer = mediaSource.Container;
            state.RunTimeTicks = mediaSource.RunTimeTicks;
            state.RemoteHttpHeaders = mediaSource.RequiredHttpHeaders;

            state.IsoType = mediaSource.IsoType;

            if (mediaSource.Timestamp.HasValue)
            {
                state.InputTimestamp = mediaSource.Timestamp.Value;
            }

            state.RunTimeTicks = mediaSource.RunTimeTicks;
            state.RemoteHttpHeaders = mediaSource.RequiredHttpHeaders;
            state.ReadInputAtNativeFramerate = mediaSource.ReadAtNativeFramerate;

            if (NeedsInputSync(state, mediaSource))
            {
                state.InputVideoSync = "-1";
                state.InputAudioSync = "1";
            }

            if (IsWmaOrAsfContainer(mediaSource))
            {
                // Seeing some stuttering when transcoding wma to audio-only HLS
                state.InputAudioSync = "1";
            }

            var mediaStreams = mediaSource.MediaStreams;

            if (state.IsVideoRequest)
            {
                var videoRequest = state.BaseRequest;

                if (string.IsNullOrEmpty(videoRequest.VideoCodec))
                {
                    InferRequestedVideoCodec(videoRequest, requestedUrl);
                }

                state.VideoStream = GetMediaStream(mediaStreams, videoRequest.VideoStreamIndex, MediaStreamType.Video);
                state.SubtitleStream = GetMediaStream(mediaStreams, videoRequest.SubtitleStreamIndex, MediaStreamType.Subtitle, false);
                state.SubtitleDeliveryMethod = videoRequest.SubtitleMethod;
                state.AudioStream = GetMediaStream(mediaStreams, videoRequest.AudioStreamIndex, MediaStreamType.Audio);

                SetInternalSubtitleStreamOffset(state, mediaStreams);

                EnforceResolutionLimit(state);

                NormalizeSubtitleEmbed(state);
            }
            else
            {
                state.AudioStream = GetMediaStream(mediaStreams, null, MediaStreamType.Audio, true);
            }

            state.MediaSource = mediaSource;

            var request = state.BaseRequest;
            ApplySupportedAudioCodecs(state, request);
            ApplySupportedVideoCodecs(state, request, encodingOptions);
        }

        private static bool HasEncoderPathAndProtocol(MediaSourceInfo mediaSource)
        {
            return !string.IsNullOrEmpty(mediaSource.EncoderPath) && mediaSource.EncoderProtocol.HasValue;
        }

        private static bool IsWmaOrAsfContainer(MediaSourceInfo mediaSource)
        {
            return string.Equals(mediaSource.Container, "wma", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaSource.Container, "asf", StringComparison.OrdinalIgnoreCase);
        }

        private void InferRequestedVideoCodec(BaseEncodingJobOptions videoRequest, string requestedUrl)
        {
            if (string.IsNullOrEmpty(requestedUrl))
            {
                requestedUrl = "test." + videoRequest.Container;
            }

            videoRequest.VideoCodec = InferVideoCodec(requestedUrl);
        }

        private static void SetInternalSubtitleStreamOffset(EncodingJobInfo state, IReadOnlyList<MediaStream> mediaStreams)
        {
            if (state.SubtitleStream is not null && !state.SubtitleStream.IsExternal)
            {
                state.InternalSubtitleStreamOffset = mediaStreams.Where(i => i.Type == MediaStreamType.Subtitle && !i.IsExternal).ToList().IndexOf(state.SubtitleStream);
            }
        }

        private static bool NeedsInputSync(EncodingJobInfo state, MediaSourceInfo mediaSource)
        {
            return (state.ReadInputAtNativeFramerate && !state.IsSegmentedLiveStream)
                || (mediaSource.Protocol == MediaProtocol.File
                && string.Equals(mediaSource.Container, "wtv", StringComparison.OrdinalIgnoreCase));
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

        private void NormalizeSubtitleEmbed(EncodingJobInfo state)
        {
            if (state.SubtitleStream is null || state.SubtitleDeliveryMethod != SubtitleDeliveryMethod.Embed)
            {
                return;
            }

            // This is tricky to remux in, after converting to dvdsub it's not positioned correctly
            // Therefore, let's just burn it in
            if (string.Equals(state.SubtitleStream.Codec, "DVBSUB", StringComparison.OrdinalIgnoreCase))
            {
                state.SubtitleDeliveryMethod = SubtitleDeliveryMethod.Encode;
            }
        }

        public string GetSubtitleEmbedArguments(EncodingJobInfo state)
        {
            if (state.SubtitleStream is null || state.SubtitleDeliveryMethod != SubtitleDeliveryMethod.Embed)
            {
                return string.Empty;
            }

            var format = state.SupportedSubtitleCodecs.FirstOrDefault();
            string codec;

            if (string.IsNullOrEmpty(format) || string.Equals(format, state.SubtitleStream.Codec, StringComparison.OrdinalIgnoreCase))
            {
                codec = "copy";
            }
            else
            {
                codec = format;
            }

            return " -codec:s:0 " + codec + " -disposition:s:0 default";
        }

        public static int FindIndex(IReadOnlyList<MediaStream> mediaStreams, MediaStream streamToFind)
        {
            var index = 0;
            var length = mediaStreams.Count;

            for (var i = 0; i < length; i++)
            {
                var currentMediaStream = mediaStreams[i];
                if (currentMediaStream == streamToFind)
                {
                    return index;
                }

                if (string.Equals(currentMediaStream.Path, streamToFind.Path, StringComparison.Ordinal))
                {
                    index++;
                }
            }

            return -1;
        }

        public static int GetSubtitleStreamIndexForFfmpeg(MediaSourceInfo mediaSource, MediaStream subtitleStream)
        {
            var index = FindIndex(mediaSource.MediaStreams, subtitleStream);
            if (IsIndexUnaffectedByBluRayHiddenStreams(index, mediaSource, subtitleStream))
            {
                return index;
            }

            var hiddenStreamsBefore = mediaSource.MediaStreams.Count(s =>
                s.Type == MediaStreamType.Audio
                && !s.IsExternal
                && (string.Equals(s.Codec, "truehd", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Codec, "atmos", StringComparison.OrdinalIgnoreCase))
                && s.Index < subtitleStream.Index);

            return index + hiddenStreamsBefore;
        }

        private static bool IsIndexUnaffectedByBluRayHiddenStreams(int index, MediaSourceInfo mediaSource, MediaStream subtitleStream)
        {
            return index == -1 || subtitleStream.IsExternal || mediaSource.VideoType != VideoType.BluRay;
        }

        public static bool IsCopyCodec(string codec)
        {
            return string.Equals(codec, "copy", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldEncodeSubtitle(EncodingJobInfo state)
        {
            return state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Encode
                   || (state.BaseRequest.AlwaysBurnInSubtitleWhenTranscoding && !IsCopyCodec(state.OutputVideoCodec));
        }

        private static bool NeedsExternalSubtitleMuxing(EncodingJobInfo state)
        {
            return state.SubtitleStream is not null
                && state.SubtitleStream.IsExternal
                && (state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Embed
                    || (ShouldEncodeSubtitle(state) && !state.SubtitleStream.IsTextSubtitleStream));
        }

        public static string GetVideoSyncOption(string videoSync, Version encoderVersion)
        {
            if (string.IsNullOrEmpty(videoSync))
            {
                return string.Empty;
            }

            return GetVersionedVideoSyncOption(videoSync, encoderVersion);
        }

        private static string GetVersionedVideoSyncOption(string videoSync, Version encoderVersion)
        {
            if (encoderVersion >= new Version(5, 1))
            {
                return GetFpsModeFromVideoSync(videoSync);
            }

            // -vsync is deprecated in FFmpeg 5.1 and will be removed in the future.
            return $" -vsync {videoSync}";
        }

        private static string GetFpsModeFromVideoSync(string videoSync)
        {
            if (int.TryParse(videoSync, CultureInfo.InvariantCulture, out var vsync))
            {
                return vsync switch
                {
                    -1 => " -fps_mode auto",
                    0 => " -fps_mode passthrough",
                    1 => " -fps_mode cfr",
                    2 => " -fps_mode vfr",
                    _ => string.Empty
                };
            }

            return string.Empty;
        }
    }
}
