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
    }
}
