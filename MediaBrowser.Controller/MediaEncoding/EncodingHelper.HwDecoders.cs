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
        /// Gets the ffmpeg option string for the hardware accelerated video decoder.
        /// </summary>
        /// <param name="state">The encoding job info.</param>
        /// <param name="options">The encoding options.</param>
        /// <returns>The option string or null if none available.</returns>
        protected string GetHardwareVideoDecoder(EncodingJobInfo state, EncodingOptions options)
        {
            var videoStream = state.VideoStream;
            var mediaSource = state.MediaSource;
            if (videoStream is null || mediaSource is null)
            {
                return null;
            }

            // HWA decoders can handle both video files and video folders.
            var videoType = state.VideoType;
            if (IsUnsupportedHwDecodeVideoType(videoType))
            {
                return null;
            }

            if (IsCopyCodec(state.OutputVideoCodec))
            {
                return null;
            }

            return ResolveHwDecoderByType(state, options, videoStream);
        }

        private string ResolveHwDecoderByType(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream)
        {
            var hardwareAccelerationType = options.HardwareAccelerationType;

            if (string.IsNullOrEmpty(videoStream.Codec) || hardwareAccelerationType == HardwareAccelerationType.none)
            {
                // leave blank so ffmpeg will decide
                return null;
            }

            var bitDepth = GetVideoColorBitDepth(state);

            // Only HEVC, VP9 and AV1 formats have 10-bit hardware decoder support for most platforms
            if (IsTenBitWithoutCommonHwDecoder(videoStream, bitDepth))
            {
                bool hasHardwareHi10P = HasHardwareHi10PDecoder(hardwareAccelerationType);

                if (IsH264WithoutHwHi10P(hasHardwareHi10P, videoStream))
                {
                    return null;
                }
            }

            // Block unsupported H.264 Hi422P and Hi444PP profiles, which can be encoded with 4:2:0 pixel format
            if (IsH264Hi422OrHi444Profile(videoStream))
            {
                // VideoToolbox on Apple Silicon has H.264 Hi444PP and theoretically also has Hi422P
                if (!IsAppleSiliconVideoToolbox(hardwareAccelerationType))
                {
                    return null;
                }
            }

            var context = new HwDecoderByTypeContext(state, options, videoStream, bitDepth, hardwareAccelerationType);
            return ResolveHwDecoderByAccelerationType(context);
        }

        private string ResolveHwDecoderByAccelerationType(HwDecoderByTypeContext context)
        {
            var decoder = context.HardwareAccelerationType switch
            {
                HardwareAccelerationType.vaapi => GetVaapiVidDecoder(context.State, context.Options, context.VideoStream, context.BitDepth),
                HardwareAccelerationType.amf => GetAmfVidDecoder(context.State, context.Options, context.VideoStream, context.BitDepth),
                HardwareAccelerationType.qsv => GetQsvHwVidDecoder(context.State, context.Options, context.VideoStream, context.BitDepth),
                _ => ResolveHwDecoderByRemainingAccelerationType(context)
            };

            if (!string.IsNullOrEmpty(decoder))
            {
                return decoder;
            }

            // leave blank so ffmpeg will decide
            return null;
        }

        private string ResolveHwDecoderByRemainingAccelerationType(HwDecoderByTypeContext context)
        {
            return context.HardwareAccelerationType switch
            {
                HardwareAccelerationType.nvenc => GetNvdecVidDecoder(context.State, context.Options, context.VideoStream, context.BitDepth),
                HardwareAccelerationType.videotoolbox => GetVideotoolboxVidDecoder(context.State, context.Options, context.VideoStream, context.BitDepth),
                HardwareAccelerationType.rkmpp => GetRkmppVidDecoder(context.State, context.Options, context.VideoStream, context.BitDepth),
                _ => string.Empty
            };
        }

        private static bool IsH264WithoutHwHi10P(bool hasHardwareHi10P, MediaStream videoStream)
        {
            return !hasHardwareHi10P
                && string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAppleSiliconVideoToolbox(HardwareAccelerationType hardwareAccelerationType)
        {
            return hardwareAccelerationType == HardwareAccelerationType.videotoolbox
                && RuntimeInformation.OSArchitecture.Equals(Architecture.Arm64);
        }

        private static bool HasHardwareHi10PDecoder(HardwareAccelerationType hardwareAccelerationType)
        {
            // RKMPP has H.264 Hi10P decoder
            bool hasHardwareHi10P = hardwareAccelerationType == HardwareAccelerationType.rkmpp;

            // VideoToolbox on Apple Silicon has H.264 Hi10P mode enabled after macOS 14.6
            if (hardwareAccelerationType == HardwareAccelerationType.videotoolbox)
            {
                var ver = Environment.OSVersion.Version;
                var arch = RuntimeInformation.OSArchitecture;
                if (arch.Equals(Architecture.Arm64) && ver >= new Version(14, 6))
                {
                    hasHardwareHi10P = true;
                }
            }

            return hasHardwareHi10P;
        }

        private static bool IsUnsupportedHwDecodeVideoType(VideoType videoType)
        {
            return videoType != VideoType.VideoFile
                && videoType != VideoType.Iso
                && videoType != VideoType.Dvd
                && videoType != VideoType.BluRay;
        }

        private static bool IsTenBitWithoutCommonHwDecoder(MediaStream videoStream, int bitDepth)
        {
            return bitDepth == 10
                && !(string.Equals(videoStream.Codec, "hevc", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(videoStream.Codec, "h265", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(videoStream.Codec, "vp9", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(videoStream.Codec, "av1", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsH264Hi422OrHi444Profile(MediaStream videoStream)
        {
            return string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase)
                && ((videoStream.Profile?.Contains("4:2:2", StringComparison.OrdinalIgnoreCase) ?? false)
                    || (videoStream.Profile?.Contains("4:4:4", StringComparison.OrdinalIgnoreCase) ?? false));
        }

        /// <summary>
        /// Gets a hw decoder name.
        /// </summary>
        /// <param name="options">Encoding options.</param>
        /// <param name="decoderPrefix">Decoder prefix.</param>
        /// <param name="decoderSuffix">Decoder suffix.</param>
        /// <param name="videoCodec">Video codec to use.</param>
        /// <param name="bitDepth">Video color bit depth.</param>
        /// <returns>Hardware decoder name.</returns>
        public string GetHwDecoderName(EncodingOptions options, string decoderPrefix, string decoderSuffix, string videoCodec, int bitDepth)
        {
            if (IsMissingDecoderPrefixOrSuffix(decoderPrefix, decoderSuffix))
            {
                return null;
            }

            var decoderName = decoderPrefix + '_' + decoderSuffix;

            var isCodecAvailable = IsHwDecoderCodecAvailable(options, decoderName, videoCodec);

            // VideoToolbox decoders have built-in SW fallback
            if (IsNonVtTenBitCodecAvailable(options, bitDepth, isCodecAvailable))
            {
                if (IsHevc10BitDecodingDisabled(options, videoCodec))
                {
                    return null;
                }

                if (IsVp910BitDecodingDisabled(options, videoCodec))
                {
                    return null;
                }
            }

            return GetDecoderOptionUnlessSuperseded(options, decoderSuffix, decoderName, isCodecAvailable);
        }

        private static string GetDecoderOptionUnlessSuperseded(EncodingOptions options, string decoderSuffix, string decoderName, bool isCodecAvailable)
        {
            if (IsCuvidSupersededByNvdec(options, decoderSuffix))
            {
                return null;
            }

            if (IsQsvSupersededByNativeDecoder(options, decoderSuffix))
            {
                return null;
            }

            if (string.Equals(decoderSuffix, "rkmpp", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return isCodecAvailable ? (" -c:v " + decoderName) : null;
        }

        private static bool IsMissingDecoderPrefixOrSuffix(string decoderPrefix, string decoderSuffix)
        {
            return string.IsNullOrEmpty(decoderPrefix) || string.IsNullOrEmpty(decoderSuffix);
        }

        private bool IsHwDecoderCodecAvailable(EncodingOptions options, string decoderName, string videoCodec)
        {
            return _mediaEncoder.SupportsDecoder(decoderName) && options.HardwareDecodingCodecs.Contains(videoCodec, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCuvidSupersededByNvdec(EncodingOptions options, string decoderSuffix)
        {
            return string.Equals(decoderSuffix, "cuvid", StringComparison.OrdinalIgnoreCase) && options.EnableEnhancedNvdecDecoder;
        }

        private static bool IsQsvSupersededByNativeDecoder(EncodingOptions options, string decoderSuffix)
        {
            return string.Equals(decoderSuffix, "qsv", StringComparison.OrdinalIgnoreCase) && options.PreferSystemNativeHwDecoder;
        }

        private static bool IsNonVtTenBitCodecAvailable(EncodingOptions options, int bitDepth, bool isCodecAvailable)
        {
            return bitDepth == 10
                && isCodecAvailable
                && (options.HardwareAccelerationType != HardwareAccelerationType.videotoolbox);
        }

        private static bool IsHevc10BitDecodingDisabled(EncodingOptions options, string videoCodec)
        {
            return string.Equals(videoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
                && options.HardwareDecodingCodecs.Contains("hevc", StringComparison.OrdinalIgnoreCase)
                && !options.EnableDecodingColorDepth10Hevc;
        }

        private static bool IsVp910BitDecodingDisabled(EncodingOptions options, string videoCodec)
        {
            return string.Equals(videoCodec, "vp9", StringComparison.OrdinalIgnoreCase)
                && options.HardwareDecodingCodecs.Contains("vp9", StringComparison.OrdinalIgnoreCase)
                && !options.EnableDecodingColorDepth10Vp9;
        }

        /// <summary>
        /// Gets a hwaccel type to use as a hardware decoder depending on the system.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="videoCodec">Video codec to use.</param>
        /// <param name="bitDepth">Video color bit depth.</param>
        /// <param name="outputHwSurface">Specifies if output hw surface.</param>
        /// <returns>Hardware accelerator type.</returns>
        public string GetHwaccelType(EncodingJobInfo state, EncodingOptions options, string videoCodec, int bitDepth, bool outputHwSurface)
        {
            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();
            var isMacOS = OperatingSystem.IsMacOS();
            var isD3d11Supported = IsD3d11HwaccelSupported(isWindows);
            var isVaapiSupported = IsLinuxVaapiSupported(state, isLinux);
            var isCudaSupported = IsCudaHwaccelSupported(isLinux, isWindows);
            var isQsvSupported = IsQsvHwaccelSupported(isLinux, isWindows);
            var isVideotoolboxSupported = IsVideotoolboxHwaccelSupported(isMacOS);
            var isRkmppSupported = IsLinuxRkmppSupported(isLinux);
            var isCodecAvailable = options.HardwareDecodingCodecs.Contains(videoCodec, StringComparison.OrdinalIgnoreCase);
            var hardwareAccelerationType = options.HardwareAccelerationType;

            var ffmpegVersion = _mediaEncoder.EncoderVersion;

            // Set the av1 codec explicitly to trigger hw accelerator, otherwise libdav1d will be used.
            var isAv1 = NeedsExplicitAv1Decoder(ffmpegVersion, videoCodec);

            // Allow profile mismatch if decoding H.264 baseline with d3d11va and vaapi hwaccels.
            var profileMismatch = IsH264BaselineProfile(state, videoCodec);

            // Disable the extra internal copy in nvdec. We already handle it in filter chain.
            var nvdecNoInternalCopy = ffmpegVersion >= _minFFmpegHwaUnsafeOutput;

            // Strip the display rotation side data from the transposed fmp4 output stream.
            var stripRotationDataArgs = GetStripRotationDataArgs(state, ffmpegVersion);

            // VideoToolbox decoders have built-in SW fallback
            if (IsNonVideotoolboxCodecAvailable(options, isCodecAvailable))
            {
                if (IsHevcHwDecodingCodec(options, videoCodec))
                {
                    if (IsVideoStreamHevcRext(state))
                    {
                        if (IsHevcRext10BitDecodingDisabled(options, bitDepth))
                        {
                            return null;
                        }

                        if (IsHevcRext12BitDecodingDisabled(options, bitDepth))
                        {
                            return null;
                        }

                        if (IsNonIHDVaapi(hardwareAccelerationType))
                        {
                            return null;
                        }
                    }
                    else if (IsHevcTenBitDecodingDisabled(options, bitDepth))
                    {
                        return null;
                    }
                }

                if (IsVp9TenBitHwDecodingDisabled(options, videoCodec, bitDepth))
                {
                    return null;
                }
            }

            var context = new HwDecoderHwaccelContext(
                hardwareAccelerationType,
                isD3d11Supported,
                isVaapiSupported,
                isCudaSupported,
                isVideotoolboxSupported,
                isRkmppSupported,
                isCodecAvailable,
                outputHwSurface,
                stripRotationDataArgs,
                nvdecNoInternalCopy,
                profileMismatch,
                isAv1);
            return GetHwaccelTypeFromIntel(context, options, isQsvSupported);
        }

        private string GetHwaccelTypeFromIntel(HwDecoderHwaccelContext context, EncodingOptions options, bool isQsvSupported)
        {
            // Intel qsv/d3d11va/vaapi
            if (context.HardwareAccelerationType != HardwareAccelerationType.qsv)
            {
                return GetHwaccelTypeFromNvidia(context, options);
            }

            if (!options.PreferSystemNativeHwDecoder)
            {
                if (IsHwaccelAvailable(isQsvSupported, context.IsCodecAvailable))
                {
                    return GetQsvHwaccelOption(context.OutputHwSurface, context.StripRotationDataArgs);
                }

                return GetHwaccelTypeFromNvidia(context, options);
            }

            if (IsHwaccelAvailable(context.IsVaapiSupported, context.IsCodecAvailable))
            {
                return GetVaapiHwaccelOption(context.OutputHwSurface, context.StripRotationDataArgs, context.ProfileMismatch, context.IsAv1);
            }

            if (IsHwaccelAvailable(context.IsD3d11Supported, context.IsCodecAvailable))
            {
                return GetD3d11vaHwaccelOption(context.OutputHwSurface, context.StripRotationDataArgs, context.ProfileMismatch, context.IsAv1);
            }

            return GetHwaccelTypeFromNvidia(context, options);
        }

        private string GetHwaccelTypeFromNvidia(HwDecoderHwaccelContext context, EncodingOptions options)
        {
            // Nvidia cuda
            if (context.HardwareAccelerationType == HardwareAccelerationType.nvenc)
            {
                if (IsHwaccelAvailable(context.IsCudaSupported, context.IsCodecAvailable))
                {
                    if (options.EnableEnhancedNvdecDecoder)
                    {
                        // set -threads 1 to nvdec decoder explicitly since it doesn't implement threading support.
                        return GetCudaNvdecHwaccelOption(context.OutputHwSurface, context.StripRotationDataArgs, context.NvdecNoInternalCopy, context.IsAv1);
                    }

                    // cuvid decoder doesn't have threading issue.
                    return GetCudaHwaccelOption(context.OutputHwSurface, context.StripRotationDataArgs);
                }
            }

            return GetHwaccelTypeFromRemainingTypes(context);
        }

        private static string GetHwaccelTypeFromRemainingTypes(HwDecoderHwaccelContext context)
        {
            // Amd d3d11va
            if (context.HardwareAccelerationType == HardwareAccelerationType.amf)
            {
                if (IsHwaccelAvailable(context.IsD3d11Supported, context.IsCodecAvailable))
                {
                    return GetD3d11vaHwaccelOption(context.OutputHwSurface, context.StripRotationDataArgs, context.ProfileMismatch, context.IsAv1);
                }
            }

            // Vaapi
            if (IsVaapiHwaccelUsable(context.HardwareAccelerationType, context.IsVaapiSupported, context.IsCodecAvailable))
            {
                return GetVaapiHwaccelOption(context.OutputHwSurface, context.StripRotationDataArgs, context.ProfileMismatch, context.IsAv1);
            }

            // Apple videotoolbox
            if (IsVideotoolboxHwaccelUsable(context.HardwareAccelerationType, context.IsVideotoolboxSupported, context.IsCodecAvailable))
            {
                return GetVideotoolboxHwaccelOption(context.OutputHwSurface, context.StripRotationDataArgs);
            }

            // Rockchip rkmpp
            if (IsRkmppHwaccelUsable(context.HardwareAccelerationType, context.IsRkmppSupported, context.IsCodecAvailable))
            {
                return GetRkmppHwaccelOption(context.OutputHwSurface, context.StripRotationDataArgs);
            }

            return null;
        }

        private static string GetVaapiHwaccelOption(bool outputHwSurface, string stripRotationDataArgs, bool profileMismatch, bool isAv1)
        {
            return " -hwaccel vaapi" + (outputHwSurface ? " -hwaccel_output_format vaapi -noautorotate" + stripRotationDataArgs : string.Empty)
                + (profileMismatch ? " -hwaccel_flags +allow_profile_mismatch" : string.Empty) + (isAv1 ? " -c:v av1" : string.Empty);
        }

        private static string GetD3d11vaHwaccelOption(bool outputHwSurface, string stripRotationDataArgs, bool profileMismatch, bool isAv1)
        {
            return " -hwaccel d3d11va" + (outputHwSurface ? " -hwaccel_output_format d3d11 -noautorotate" + stripRotationDataArgs : string.Empty)
                + (profileMismatch ? " -hwaccel_flags +allow_profile_mismatch" : string.Empty) + " -threads 2" + (isAv1 ? " -c:v av1" : string.Empty);
        }

        private static string GetQsvHwaccelOption(bool outputHwSurface, string stripRotationDataArgs)
        {
            return " -hwaccel qsv" + (outputHwSurface ? " -hwaccel_output_format qsv -noautorotate" + stripRotationDataArgs : string.Empty);
        }

        private static string GetCudaNvdecHwaccelOption(bool outputHwSurface, string stripRotationDataArgs, bool nvdecNoInternalCopy, bool isAv1)
        {
            return " -hwaccel cuda" + (outputHwSurface ? " -hwaccel_output_format cuda -noautorotate" + stripRotationDataArgs : string.Empty)
                + (nvdecNoInternalCopy ? " -hwaccel_flags +unsafe_output" : string.Empty) + " -threads 1" + (isAv1 ? " -c:v av1" : string.Empty);
        }

        private static string GetCudaHwaccelOption(bool outputHwSurface, string stripRotationDataArgs)
        {
            return " -hwaccel cuda" + (outputHwSurface ? " -hwaccel_output_format cuda -noautorotate" + stripRotationDataArgs : string.Empty);
        }

        private static string GetVideotoolboxHwaccelOption(bool outputHwSurface, string stripRotationDataArgs)
        {
            return " -hwaccel videotoolbox" + (outputHwSurface ? " -hwaccel_output_format videotoolbox_vld" : string.Empty) + " -noautorotate" + stripRotationDataArgs;
        }

        private static string GetRkmppHwaccelOption(bool outputHwSurface, string stripRotationDataArgs)
        {
            return " -hwaccel rkmpp" + (outputHwSurface ? " -hwaccel_output_format drm_prime -noautorotate" + stripRotationDataArgs : string.Empty);
        }

        private string GetStripRotationDataArgs(EncodingJobInfo state, Version ffmpegVersion)
        {
            var stripRotationData = NeedsDisplayRotationStrip(state, ffmpegVersion);
            return stripRotationData ? " -display_rotation 0" : string.Empty;
        }

        private static bool IsHwaccelAvailable(bool isHwaccelSupported, bool isCodecAvailable)
        {
            return isHwaccelSupported && isCodecAvailable;
        }

        private bool IsD3d11HwaccelSupported(bool isWindows)
        {
            return isWindows && _mediaEncoder.SupportsHwaccel("d3d11va");
        }

        private bool IsLinuxVaapiSupported(EncodingJobInfo state, bool isLinux)
        {
            return isLinux && IsVaapiSupported(state);
        }

        private bool IsCudaHwaccelSupported(bool isLinux, bool isWindows)
        {
            return (isLinux || isWindows) && IsCudaFullSupported();
        }

        private bool IsQsvHwaccelSupported(bool isLinux, bool isWindows)
        {
            return (isLinux || isWindows) && _mediaEncoder.SupportsHwaccel("qsv");
        }

        private bool IsVideotoolboxHwaccelSupported(bool isMacOS)
        {
            return isMacOS && _mediaEncoder.SupportsHwaccel("videotoolbox");
        }

        private bool IsLinuxRkmppSupported(bool isLinux)
        {
            return isLinux && IsRkmppFullSupported();
        }

        private bool NeedsExplicitAv1Decoder(Version ffmpegVersion, string videoCodec)
        {
            return ffmpegVersion < _minFFmpegImplicitHwaccel
                && string.Equals(videoCodec, "av1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsH264BaselineProfile(EncodingJobInfo state, string videoCodec)
        {
            return string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase)
                && string.Equals(state.VideoStream?.Profile, "baseline", StringComparison.OrdinalIgnoreCase);
        }

        private bool NeedsDisplayRotationStrip(EncodingJobInfo state, Version ffmpegVersion)
        {
            return (state.VideoStream?.Rotation ?? 0) != 0
                && ffmpegVersion >= _minFFmpegDisplayRotationOption;
        }

        private static bool IsNonVideotoolboxCodecAvailable(EncodingOptions options, bool isCodecAvailable)
        {
            return isCodecAvailable
                && (options.HardwareAccelerationType != HardwareAccelerationType.videotoolbox);
        }

        private static bool IsHevcHwDecodingCodec(EncodingOptions options, string videoCodec)
        {
            return string.Equals(videoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
                && options.HardwareDecodingCodecs.Contains("hevc", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHevcRext10BitDecodingDisabled(EncodingOptions options, int bitDepth)
        {
            return bitDepth <= 10 && !options.EnableDecodingColorDepth10HevcRext;
        }

        private static bool IsHevcRext12BitDecodingDisabled(EncodingOptions options, int bitDepth)
        {
            return bitDepth == 12 && !options.EnableDecodingColorDepth12HevcRext;
        }

        private bool IsNonIHDVaapi(HardwareAccelerationType hardwareAccelerationType)
        {
            return hardwareAccelerationType == HardwareAccelerationType.vaapi
                && !_mediaEncoder.IsVaapiDeviceInteliHD;
        }

        private static bool IsHevcTenBitDecodingDisabled(EncodingOptions options, int bitDepth)
        {
            return bitDepth == 10 && !options.EnableDecodingColorDepth10Hevc;
        }

        private static bool IsVp9TenBitHwDecodingDisabled(EncodingOptions options, string videoCodec, int bitDepth)
        {
            return string.Equals(videoCodec, "vp9", StringComparison.OrdinalIgnoreCase)
                && options.HardwareDecodingCodecs.Contains("vp9", StringComparison.OrdinalIgnoreCase)
                && bitDepth == 10
                && !options.EnableDecodingColorDepth10Vp9;
        }

        private static bool IsVaapiHwaccelUsable(HardwareAccelerationType hardwareAccelerationType, bool isVaapiSupported, bool isCodecAvailable)
        {
            return hardwareAccelerationType == HardwareAccelerationType.vaapi
                && isVaapiSupported
                && isCodecAvailable;
        }

        private static bool IsVideotoolboxHwaccelUsable(HardwareAccelerationType hardwareAccelerationType, bool isVideotoolboxSupported, bool isCodecAvailable)
        {
            return hardwareAccelerationType == HardwareAccelerationType.videotoolbox
                && isVideotoolboxSupported
                && isCodecAvailable;
        }

        private static bool IsRkmppHwaccelUsable(HardwareAccelerationType hardwareAccelerationType, bool isRkmppSupported, bool isCodecAvailable)
        {
            return hardwareAccelerationType == HardwareAccelerationType.rkmpp
                && isRkmppSupported
                && isCodecAvailable;
        }

        private readonly record struct HwDecoderByTypeContext(
            EncodingJobInfo State,
            EncodingOptions Options,
            MediaStream VideoStream,
            int BitDepth,
            HardwareAccelerationType HardwareAccelerationType);

        private readonly record struct HwDecoderHwaccelContext(
            HardwareAccelerationType HardwareAccelerationType,
            bool IsD3d11Supported,
            bool IsVaapiSupported,
            bool IsCudaSupported,
            bool IsVideotoolboxSupported,
            bool IsRkmppSupported,
            bool IsCodecAvailable,
            bool OutputHwSurface,
            string StripRotationDataArgs,
            bool NvdecNoInternalCopy,
            bool ProfileMismatch,
            bool IsAv1);
    }
}
