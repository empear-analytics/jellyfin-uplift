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

            var context = new HwDecoderByTypeContext(state, options, videoStream, bitDepth, hardwareAccelerationType);
            return ResolveHwDecoderUnlessUnsupportedProfile(context);
        }

        private string ResolveHwDecoderUnlessUnsupportedProfile(HwDecoderByTypeContext context)
        {
            // Block unsupported H.264 Hi422P and Hi444PP profiles, which can be encoded with 4:2:0 pixel format
            if (IsH264Hi422OrHi444Profile(context.VideoStream))
            {
                // VideoToolbox on Apple Silicon has H.264 Hi444PP and theoretically also has Hi422P
                if (!IsAppleSiliconVideoToolbox(context.HardwareAccelerationType))
                {
                    return null;
                }
            }

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
            if (IsMissingDecoderPrefixOrSuffix(new HwDecoderAffixes(decoderPrefix, decoderSuffix)))
            {
                return null;
            }

            var decoderName = decoderPrefix + '_' + decoderSuffix;

            var isCodecAvailable = IsHwDecoderCodecAvailable(options, new HwDecoderNameAndCodec(decoderName, videoCodec));

            // VideoToolbox decoders have built-in SW fallback
            if (IsNonVtTenBitCodecAvailable(new HwDecoderBitDepthOptions(options, bitDepth), isCodecAvailable))
            {
                if (IsHevc10BitDecodingDisabled(new HwDecoderCodecOptions(options, videoCodec)))
                {
                    return null;
                }

                if (IsVp910BitDecodingDisabled(new HwDecoderCodecOptions(options, videoCodec)))
                {
                    return null;
                }
            }

            return GetDecoderOptionUnlessSuperseded(new HwDecoderSuffixOptions(options, decoderSuffix), decoderName, isCodecAvailable);
        }

        private static string GetDecoderOptionUnlessSuperseded(HwDecoderSuffixOptions suffixOptions, string decoderName, bool isCodecAvailable)
        {
            if (IsCuvidSupersededByNvdec(suffixOptions))
            {
                return null;
            }

            if (IsQsvSupersededByNativeDecoder(suffixOptions))
            {
                return null;
            }

            if (string.Equals(suffixOptions.DecoderSuffix, "rkmpp", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return isCodecAvailable ? (" -c:v " + decoderName) : null;
        }

        private static bool IsMissingDecoderPrefixOrSuffix(HwDecoderAffixes affixes)
        {
            return string.IsNullOrEmpty(affixes.DecoderPrefix) || string.IsNullOrEmpty(affixes.DecoderSuffix);
        }

        private bool IsHwDecoderCodecAvailable(EncodingOptions options, HwDecoderNameAndCodec nameAndCodec)
        {
            return _mediaEncoder.SupportsDecoder(nameAndCodec.DecoderName) && options.HardwareDecodingCodecs.Contains(nameAndCodec.VideoCodec, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCuvidSupersededByNvdec(HwDecoderSuffixOptions suffixOptions)
        {
            return string.Equals(suffixOptions.DecoderSuffix, "cuvid", StringComparison.OrdinalIgnoreCase) && suffixOptions.Options.EnableEnhancedNvdecDecoder;
        }

        private static bool IsQsvSupersededByNativeDecoder(HwDecoderSuffixOptions suffixOptions)
        {
            return string.Equals(suffixOptions.DecoderSuffix, "qsv", StringComparison.OrdinalIgnoreCase) && suffixOptions.Options.PreferSystemNativeHwDecoder;
        }

        private static bool IsNonVtTenBitCodecAvailable(HwDecoderBitDepthOptions bitDepthOptions, bool isCodecAvailable)
        {
            return bitDepthOptions.BitDepth == 10
                && isCodecAvailable
                && (bitDepthOptions.Options.HardwareAccelerationType != HardwareAccelerationType.videotoolbox);
        }

        private static bool IsHevc10BitDecodingDisabled(HwDecoderCodecOptions codecOptions)
        {
            return string.Equals(codecOptions.VideoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
                && codecOptions.Options.HardwareDecodingCodecs.Contains("hevc", StringComparison.OrdinalIgnoreCase)
                && !codecOptions.Options.EnableDecodingColorDepth10Hevc;
        }

        private static bool IsVp910BitDecodingDisabled(HwDecoderCodecOptions codecOptions)
        {
            return string.Equals(codecOptions.VideoCodec, "vp9", StringComparison.OrdinalIgnoreCase)
                && codecOptions.Options.HardwareDecodingCodecs.Contains("vp9", StringComparison.OrdinalIgnoreCase)
                && !codecOptions.Options.EnableDecodingColorDepth10Vp9;
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
            var isCudaSupported = IsCudaHwaccelSupported(new HwDecoderHostPlatform(isLinux, isWindows));
            var isQsvSupported = IsQsvHwaccelSupported(new HwDecoderHostPlatform(isLinux, isWindows));
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
                if (IsHevcHwDecodingCodec(new HwDecoderCodecOptions(options, videoCodec)))
                {
                    if (IsHevcDecodingBlocked(state, new HwDecoderBitDepthOptions(options, bitDepth), hardwareAccelerationType))
                    {
                        return null;
                    }
                }

                if (IsVp9TenBitHwDecodingDisabled(new HwDecoderCodecOptions(options, videoCodec), bitDepth))
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

        private bool IsHevcDecodingBlocked(EncodingJobInfo state, HwDecoderBitDepthOptions bitDepthOptions, HardwareAccelerationType hardwareAccelerationType)
        {
            if (IsVideoStreamHevcRext(state))
            {
                if (IsHevcRext10BitDecodingDisabled(bitDepthOptions))
                {
                    return true;
                }

                if (IsHevcRext12BitDecodingDisabled(bitDepthOptions))
                {
                    return true;
                }

                if (IsNonIHDVaapi(hardwareAccelerationType))
                {
                    return true;
                }
            }
            else if (IsHevcTenBitDecodingDisabled(bitDepthOptions))
            {
                return true;
            }

            return false;
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
                if (IsHwaccelAvailable(new HwDecoderAvailability(isQsvSupported, context.IsCodecAvailable)))
                {
                    return GetQsvHwaccelOption(new HwDecoderOutputSurface(context.OutputHwSurface, context.StripRotationDataArgs));
                }

                return GetHwaccelTypeFromNvidia(context, options);
            }

            if (IsHwaccelAvailable(new HwDecoderAvailability(context.IsVaapiSupported, context.IsCodecAvailable)))
            {
                return GetVaapiHwaccelOption(new HwDecoderOutputSurface(context.OutputHwSurface, context.StripRotationDataArgs), new HwDecoderProfileFlags(context.ProfileMismatch, context.IsAv1));
            }

            if (IsHwaccelAvailable(new HwDecoderAvailability(context.IsD3d11Supported, context.IsCodecAvailable)))
            {
                return GetD3d11vaHwaccelOption(new HwDecoderOutputSurface(context.OutputHwSurface, context.StripRotationDataArgs), new HwDecoderProfileFlags(context.ProfileMismatch, context.IsAv1));
            }

            return GetHwaccelTypeFromNvidia(context, options);
        }

        private string GetHwaccelTypeFromNvidia(HwDecoderHwaccelContext context, EncodingOptions options)
        {
            // Nvidia cuda
            if (context.HardwareAccelerationType == HardwareAccelerationType.nvenc)
            {
                if (IsHwaccelAvailable(new HwDecoderAvailability(context.IsCudaSupported, context.IsCodecAvailable)))
                {
                    if (options.EnableEnhancedNvdecDecoder)
                    {
                        // set -threads 1 to nvdec decoder explicitly since it doesn't implement threading support.
                        return GetCudaNvdecHwaccelOption(new HwDecoderOutputSurface(context.OutputHwSurface, context.StripRotationDataArgs), new HwDecoderNvdecFlags(context.NvdecNoInternalCopy, context.IsAv1));
                    }

                    // cuvid decoder doesn't have threading issue.
                    return GetCudaHwaccelOption(new HwDecoderOutputSurface(context.OutputHwSurface, context.StripRotationDataArgs));
                }
            }

            return GetHwaccelTypeFromRemainingTypes(context);
        }

        private static string GetHwaccelTypeFromRemainingTypes(HwDecoderHwaccelContext context)
        {
            // Amd d3d11va
            if (context.HardwareAccelerationType == HardwareAccelerationType.amf)
            {
                if (IsHwaccelAvailable(new HwDecoderAvailability(context.IsD3d11Supported, context.IsCodecAvailable)))
                {
                    return GetD3d11vaHwaccelOption(new HwDecoderOutputSurface(context.OutputHwSurface, context.StripRotationDataArgs), new HwDecoderProfileFlags(context.ProfileMismatch, context.IsAv1));
                }
            }

            // Vaapi
            if (IsVaapiHwaccelUsable(context.HardwareAccelerationType, context.IsVaapiSupported, context.IsCodecAvailable))
            {
                return GetVaapiHwaccelOption(new HwDecoderOutputSurface(context.OutputHwSurface, context.StripRotationDataArgs), new HwDecoderProfileFlags(context.ProfileMismatch, context.IsAv1));
            }

            // Apple videotoolbox
            if (IsVideotoolboxHwaccelUsable(context.HardwareAccelerationType, context.IsVideotoolboxSupported, context.IsCodecAvailable))
            {
                return GetVideotoolboxHwaccelOption(new HwDecoderOutputSurface(context.OutputHwSurface, context.StripRotationDataArgs));
            }

            // Rockchip rkmpp
            if (IsRkmppHwaccelUsable(context.HardwareAccelerationType, context.IsRkmppSupported, context.IsCodecAvailable))
            {
                return GetRkmppHwaccelOption(new HwDecoderOutputSurface(context.OutputHwSurface, context.StripRotationDataArgs));
            }

            return null;
        }

        private static string GetVaapiHwaccelOption(HwDecoderOutputSurface outputSurface, HwDecoderProfileFlags profileFlags)
        {
            return " -hwaccel vaapi" + (outputSurface.OutputHwSurface ? " -hwaccel_output_format vaapi -noautorotate" + outputSurface.StripRotationDataArgs : string.Empty)
                + (profileFlags.ProfileMismatch ? " -hwaccel_flags +allow_profile_mismatch" : string.Empty) + (profileFlags.IsAv1 ? " -c:v av1" : string.Empty);
        }

        private static string GetD3d11vaHwaccelOption(HwDecoderOutputSurface outputSurface, HwDecoderProfileFlags profileFlags)
        {
            return " -hwaccel d3d11va" + (outputSurface.OutputHwSurface ? " -hwaccel_output_format d3d11 -noautorotate" + outputSurface.StripRotationDataArgs : string.Empty)
                + (profileFlags.ProfileMismatch ? " -hwaccel_flags +allow_profile_mismatch" : string.Empty) + " -threads 2" + (profileFlags.IsAv1 ? " -c:v av1" : string.Empty);
        }

        private static string GetQsvHwaccelOption(HwDecoderOutputSurface outputSurface)
        {
            return " -hwaccel qsv" + (outputSurface.OutputHwSurface ? " -hwaccel_output_format qsv -noautorotate" + outputSurface.StripRotationDataArgs : string.Empty);
        }

        private static string GetCudaNvdecHwaccelOption(HwDecoderOutputSurface outputSurface, HwDecoderNvdecFlags nvdecFlags)
        {
            return " -hwaccel cuda" + (outputSurface.OutputHwSurface ? " -hwaccel_output_format cuda -noautorotate" + outputSurface.StripRotationDataArgs : string.Empty)
                + (nvdecFlags.NvdecNoInternalCopy ? " -hwaccel_flags +unsafe_output" : string.Empty) + " -threads 1" + (nvdecFlags.IsAv1 ? " -c:v av1" : string.Empty);
        }

        private static string GetCudaHwaccelOption(HwDecoderOutputSurface outputSurface)
        {
            return " -hwaccel cuda" + (outputSurface.OutputHwSurface ? " -hwaccel_output_format cuda -noautorotate" + outputSurface.StripRotationDataArgs : string.Empty);
        }

        private static string GetVideotoolboxHwaccelOption(HwDecoderOutputSurface outputSurface)
        {
            return " -hwaccel videotoolbox" + (outputSurface.OutputHwSurface ? " -hwaccel_output_format videotoolbox_vld" : string.Empty) + " -noautorotate" + outputSurface.StripRotationDataArgs;
        }

        private static string GetRkmppHwaccelOption(HwDecoderOutputSurface outputSurface)
        {
            return " -hwaccel rkmpp" + (outputSurface.OutputHwSurface ? " -hwaccel_output_format drm_prime -noautorotate" + outputSurface.StripRotationDataArgs : string.Empty);
        }

        private string GetStripRotationDataArgs(EncodingJobInfo state, Version ffmpegVersion)
        {
            var stripRotationData = NeedsDisplayRotationStrip(state, ffmpegVersion);
            return stripRotationData ? " -display_rotation 0" : string.Empty;
        }

        private static bool IsHwaccelAvailable(HwDecoderAvailability availability)
        {
            return availability.IsHwaccelSupported && availability.IsCodecAvailable;
        }

        private bool IsD3d11HwaccelSupported(bool isWindows)
        {
            return isWindows && _mediaEncoder.SupportsHwaccel("d3d11va");
        }

        private bool IsLinuxVaapiSupported(EncodingJobInfo state, bool isLinux)
        {
            return isLinux && IsVaapiSupported(state);
        }

        private bool IsCudaHwaccelSupported(HwDecoderHostPlatform platform)
        {
            return (platform.IsLinux || platform.IsWindows) && IsCudaFullSupported();
        }

        private bool IsQsvHwaccelSupported(HwDecoderHostPlatform platform)
        {
            return (platform.IsLinux || platform.IsWindows) && _mediaEncoder.SupportsHwaccel("qsv");
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

        private static bool IsHevcHwDecodingCodec(HwDecoderCodecOptions codecOptions)
        {
            return string.Equals(codecOptions.VideoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
                && codecOptions.Options.HardwareDecodingCodecs.Contains("hevc", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHevcRext10BitDecodingDisabled(HwDecoderBitDepthOptions bitDepthOptions)
        {
            return bitDepthOptions.BitDepth <= 10 && !bitDepthOptions.Options.EnableDecodingColorDepth10HevcRext;
        }

        private static bool IsHevcRext12BitDecodingDisabled(HwDecoderBitDepthOptions bitDepthOptions)
        {
            return bitDepthOptions.BitDepth == 12 && !bitDepthOptions.Options.EnableDecodingColorDepth12HevcRext;
        }

        private bool IsNonIHDVaapi(HardwareAccelerationType hardwareAccelerationType)
        {
            return hardwareAccelerationType == HardwareAccelerationType.vaapi
                && !_mediaEncoder.IsVaapiDeviceInteliHD;
        }

        private static bool IsHevcTenBitDecodingDisabled(HwDecoderBitDepthOptions bitDepthOptions)
        {
            return bitDepthOptions.BitDepth == 10 && !bitDepthOptions.Options.EnableDecodingColorDepth10Hevc;
        }

        private static bool IsVp9TenBitHwDecodingDisabled(HwDecoderCodecOptions codecOptions, int bitDepth)
        {
            return string.Equals(codecOptions.VideoCodec, "vp9", StringComparison.OrdinalIgnoreCase)
                && codecOptions.Options.HardwareDecodingCodecs.Contains("vp9", StringComparison.OrdinalIgnoreCase)
                && bitDepth == 10
                && !codecOptions.Options.EnableDecodingColorDepth10Vp9;
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

        private readonly record struct HwDecoderOutputSurface(bool OutputHwSurface, string StripRotationDataArgs);

        private readonly record struct HwDecoderProfileFlags(bool ProfileMismatch, bool IsAv1);

        private readonly record struct HwDecoderNvdecFlags(bool NvdecNoInternalCopy, bool IsAv1);

        private readonly record struct HwDecoderHostPlatform(bool IsLinux, bool IsWindows);

        private readonly record struct HwDecoderBitDepthOptions(EncodingOptions Options, int BitDepth);

        private readonly record struct HwDecoderCodecOptions(EncodingOptions Options, string VideoCodec);

        private readonly record struct HwDecoderSuffixOptions(EncodingOptions Options, string DecoderSuffix);

        private readonly record struct HwDecoderAvailability(bool IsHwaccelSupported, bool IsCodecAvailable);

        private readonly record struct HwDecoderAffixes(string DecoderPrefix, string DecoderSuffix);

        private readonly record struct HwDecoderNameAndCodec(string DecoderName, string VideoCodec);
    }
}
