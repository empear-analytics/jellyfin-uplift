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

            if (!string.IsNullOrEmpty(videoStream.Codec) && hardwareAccelerationType != HardwareAccelerationType.none)
            {
                var bitDepth = GetVideoColorBitDepth(state);

                // Only HEVC, VP9 and AV1 formats have 10-bit hardware decoder support for most platforms
                if (IsTenBitWithoutCommonHwDecoder(videoStream, bitDepth))
                {
                    bool hasHardwareHi10P = HasHardwareHi10PDecoder(hardwareAccelerationType);

                    if (!hasHardwareHi10P
                        && string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase))
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

                var decoder = hardwareAccelerationType switch
                {
                    HardwareAccelerationType.vaapi => GetVaapiVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.amf => GetAmfVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.qsv => GetQsvHwVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.nvenc => GetNvdecVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.videotoolbox => GetVideotoolboxVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.rkmpp => GetRkmppVidDecoder(state, options, videoStream, bitDepth),
                    _ => string.Empty
                };

                if (!string.IsNullOrEmpty(decoder))
                {
                    return decoder;
                }
            }

            // leave blank so ffmpeg will decide
            return null;
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
            var stripRotationData = NeedsDisplayRotationStrip(state, ffmpegVersion);
            var stripRotationDataArgs = stripRotationData ? " -display_rotation 0" : string.Empty;

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

            // Intel qsv/d3d11va/vaapi
            if (hardwareAccelerationType == HardwareAccelerationType.qsv)
            {
                if (options.PreferSystemNativeHwDecoder)
                {
                    if (IsHwaccelAvailable(isVaapiSupported, isCodecAvailable))
                    {
                        return GetVaapiHwaccelOption(outputHwSurface, stripRotationDataArgs, profileMismatch, isAv1);
                    }

                    if (IsHwaccelAvailable(isD3d11Supported, isCodecAvailable))
                    {
                        return GetD3d11vaHwaccelOption(outputHwSurface, stripRotationDataArgs, profileMismatch, isAv1);
                    }
                }
                else
                {
                    if (IsHwaccelAvailable(isQsvSupported, isCodecAvailable))
                    {
                        return GetQsvHwaccelOption(outputHwSurface, stripRotationDataArgs);
                    }
                }
            }

            // Nvidia cuda
            if (hardwareAccelerationType == HardwareAccelerationType.nvenc)
            {
                if (IsHwaccelAvailable(isCudaSupported, isCodecAvailable))
                {
                    if (options.EnableEnhancedNvdecDecoder)
                    {
                        // set -threads 1 to nvdec decoder explicitly since it doesn't implement threading support.
                        return GetCudaNvdecHwaccelOption(outputHwSurface, stripRotationDataArgs, nvdecNoInternalCopy, isAv1);
                    }

                    // cuvid decoder doesn't have threading issue.
                    return GetCudaHwaccelOption(outputHwSurface, stripRotationDataArgs);
                }
            }

            // Amd d3d11va
            if (hardwareAccelerationType == HardwareAccelerationType.amf)
            {
                if (IsHwaccelAvailable(isD3d11Supported, isCodecAvailable))
                {
                    return GetD3d11vaHwaccelOption(outputHwSurface, stripRotationDataArgs, profileMismatch, isAv1);
                }
            }

            // Vaapi
            if (IsVaapiHwaccelUsable(hardwareAccelerationType, isVaapiSupported, isCodecAvailable))
            {
                return GetVaapiHwaccelOption(outputHwSurface, stripRotationDataArgs, profileMismatch, isAv1);
            }

            // Apple videotoolbox
            if (IsVideotoolboxHwaccelUsable(hardwareAccelerationType, isVideotoolboxSupported, isCodecAvailable))
            {
                return GetVideotoolboxHwaccelOption(outputHwSurface, stripRotationDataArgs);
            }

            // Rockchip rkmpp
            if (IsRkmppHwaccelUsable(hardwareAccelerationType, isRkmppSupported, isCodecAvailable))
            {
                return GetRkmppHwaccelOption(outputHwSurface, stripRotationDataArgs);
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

        public string GetQsvHwVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();

            if (IsQsvDecodingUnavailable(isWindows, isLinux, options))
            {
                return null;
            }

            var isQsvOclSupported = IsQsvOclSupported();
            var isIntelDx11OclSupported = IsIntelDx11OclSupported(isWindows, isQsvOclSupported);
            var isIntelVaapiOclSupported = IsIntelVaapiOclSupported(state, isLinux, isQsvOclSupported);
            var hwSurface = HasQsvHwSurface(isIntelDx11OclSupported, isIntelVaapiOclSupported);

            var is8bitSwFormatsQsv = Is8BitYuv420SwFormat(videoStream);
            var is8_10bitSwFormatsQsv = Is8Or10BitYuv420SwFormat(videoStream, is8bitSwFormatsQsv);
            var is8_10_12bitSwFormatsQsv = IsQsv8To12BitSwFormat(videoStream, is8_10bitSwFormatsQsv);
            // TODO: add more 8/10bit and 4:4:4 formats for Qsv after finishing the ffcheck tool

            if (is8bitSwFormatsQsv)
            {
                if (IsAvcOrH264Codec(videoStream))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, hwSurface) + GetHwDecoderName(options, "h264", "qsv", "h264", bitDepth);
                }

                if (string.Equals(videoStream.Codec, "vc1", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vc1", bitDepth, hwSurface) + GetHwDecoderName(options, "vc1", "qsv", "vc1", bitDepth);
                }

                if (string.Equals(videoStream.Codec, "vp8", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, hwSurface) + GetHwDecoderName(options, "vp8", "qsv", "vp8", bitDepth);
                }

                if (string.Equals(videoStream.Codec, "mpeg2video", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface) + GetHwDecoderName(options, "mpeg2", "qsv", "mpeg2video", bitDepth);
                }
            }

            if (is8_10bitSwFormatsQsv)
            {
                if (string.Equals(videoStream.Codec, "vp9", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, hwSurface) + GetHwDecoderName(options, "vp9", "qsv", "vp9", bitDepth);
                }

                if (string.Equals(videoStream.Codec, "av1", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface) + GetHwDecoderName(options, "av1", "qsv", "av1", bitDepth);
                }
            }

            if (is8_10_12bitSwFormatsQsv)
            {
                if (IsHevcOrH265Codec(videoStream))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, hwSurface) + GetHwDecoderName(options, "hevc", "qsv", "hevc", bitDepth);
                }
            }

            return null;
        }

        private bool IsQsvOclSupported()
        {
            return _mediaEncoder.SupportsHwaccel("qsv") && IsOpenclFullSupported();
        }

        private bool IsIntelDx11OclSupported(bool isWindows, bool isQsvOclSupported)
        {
            return isWindows
                && _mediaEncoder.SupportsHwaccel("d3d11va")
                && isQsvOclSupported;
        }

        private bool IsIntelVaapiOclSupported(EncodingJobInfo state, bool isLinux, bool isQsvOclSupported)
        {
            return isLinux
                && IsVaapiSupported(state)
                && isQsvOclSupported;
        }

        private bool HasQsvHwSurface(bool isIntelDx11OclSupported, bool isIntelVaapiOclSupported)
        {
            return (isIntelDx11OclSupported || isIntelVaapiOclSupported)
                && _mediaEncoder.SupportsFilter("alphasrc");
        }

        private static bool Is8BitYuv420SwFormat(MediaStream videoStream)
        {
            return string.Equals("yuv420p", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuvj420p", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Is8Or10BitYuv420SwFormat(MediaStream videoStream, bool is8bitSwFormats)
        {
            return is8bitSwFormats || string.Equals("yuv420p10le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsQsv8To12BitSwFormat(MediaStream videoStream, bool is8_10bitSwFormatsQsv)
        {
            return is8_10bitSwFormatsQsv
                || string.Equals("yuv422p", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv444p", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv422p10le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv444p10le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv420p12le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv422p12le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv444p12le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAvcOrH264Codec(MediaStream videoStream)
        {
            return string.Equals(videoStream.Codec, "avc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHevcOrH265Codec(MediaStream videoStream)
        {
            return string.Equals(videoStream.Codec, "hevc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoStream.Codec, "h265", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsQsvDecodingUnavailable(bool isWindows, bool isLinux, EncodingOptions options)
        {
            return (!isWindows && !isLinux)
                || options.HardwareAccelerationType != HardwareAccelerationType.qsv;
        }

        public string GetNvdecVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            if (IsNvdecDecodingUnavailable(options))
            {
                return null;
            }

            return ResolveNvdecAccelType(state, options, videoStream, bitDepth);
        }

        private string ResolveNvdecAccelType(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var hwSurface = HasCudaHwSurface();
            var is8bitSwFormatsNvdec = Is8BitYuv420SwFormat(videoStream);
            var is8_10bitSwFormatsNvdec = Is8Or10BitYuv420SwFormat(videoStream, is8bitSwFormatsNvdec);
            var is8_10_12bitSwFormatsNvdec = IsNvdec8To12BitSwFormat(videoStream, is8_10bitSwFormatsNvdec);
            // TODO: add more 8/10/12bit and 4:4:4 formats for Nvdec after finishing the ffcheck tool

            if (is8bitSwFormatsNvdec)
            {
                if (IsH264StreamCodec(videoStream))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, hwSurface) + GetHwDecoderName(options, "h264", "cuvid", "h264", bitDepth);
                }

                if (string.Equals("mpeg2video", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface) + GetHwDecoderName(options, "mpeg2", "cuvid", "mpeg2video", bitDepth);
                }

                if (string.Equals("vc1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vc1", bitDepth, hwSurface) + GetHwDecoderName(options, "vc1", "cuvid", "vc1", bitDepth);
                }

                if (string.Equals("mpeg4", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg4", bitDepth, hwSurface) + GetHwDecoderName(options, "mpeg4", "cuvid", "mpeg4", bitDepth);
                }

                if (string.Equals("vp8", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, hwSurface) + GetHwDecoderName(options, "vp8", "cuvid", "vp8", bitDepth);
                }
            }

            if (is8_10bitSwFormatsNvdec)
            {
                if (string.Equals("vp9", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, hwSurface) + GetHwDecoderName(options, "vp9", "cuvid", "vp9", bitDepth);
                }

                if (string.Equals("av1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface) + GetHwDecoderName(options, "av1", "cuvid", "av1", bitDepth);
                }
            }

            if (is8_10_12bitSwFormatsNvdec)
            {
                if (IsHevcStreamCodec(videoStream))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, hwSurface) + GetHwDecoderName(options, "hevc", "cuvid", "hevc", bitDepth);
                }
            }

            return null;
        }

        private bool HasCudaHwSurface()
        {
            return IsCudaFullSupported() && _mediaEncoder.SupportsFilter("alphasrc");
        }

        private static bool IsNvdec8To12BitSwFormat(MediaStream videoStream, bool is8_10bitSwFormatsNvdec)
        {
            return is8_10bitSwFormatsNvdec
                || string.Equals("yuv444p", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv444p10le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv420p12le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv444p12le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNvdecDecodingUnavailable(EncodingOptions options)
        {
            return (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
                || options.HardwareAccelerationType != HardwareAccelerationType.nvenc;
        }

        public string GetAmfVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            if (IsAmfDecodingUnavailable(options))
            {
                return null;
            }

            return ResolveAmfAccelType(state, options, videoStream, bitDepth);
        }

        private string ResolveAmfAccelType(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var hwSurface = HasD3d11OclHwSurface();
            var is8bitSwFormatsAmf = Is8BitYuv420SwFormat(videoStream);
            var is8_10bitSwFormatsAmf = Is8Or10BitYuv420SwFormat(videoStream, is8bitSwFormatsAmf);

            if (is8bitSwFormatsAmf)
            {
                if (IsH264StreamCodec(videoStream))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, hwSurface);
                }

                if (string.Equals("mpeg2video", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface);
                }

                if (string.Equals("vc1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vc1", bitDepth, hwSurface);
                }
            }

            if (is8_10bitSwFormatsAmf)
            {
                if (IsHevcStreamCodec(videoStream))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, hwSurface);
                }

                if (string.Equals("vp9", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, hwSurface);
                }

                if (string.Equals("av1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface);
                }
            }

            return null;
        }

        private static bool IsAmfDecodingUnavailable(EncodingOptions options)
        {
            return !OperatingSystem.IsWindows()
                || options.HardwareAccelerationType != HardwareAccelerationType.amf;
        }

        private bool HasD3d11OclHwSurface()
        {
            return _mediaEncoder.SupportsHwaccel("d3d11va")
                && IsOpenclFullSupported()
                && _mediaEncoder.SupportsFilter("alphasrc");
        }

        public string GetVaapiVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            if (IsVaapiDecodingUnavailable(options))
            {
                return null;
            }

            return ResolveVaapiAccelType(state, options, videoStream, bitDepth);
        }

        private string ResolveVaapiAccelType(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var hwSurface = HasVaapiHwSurface(state);
            var is8bitSwFormatsVaapi = Is8BitYuv420SwFormat(videoStream);
            var is8_10bitSwFormatsVaapi = Is8Or10BitYuv420SwFormat(videoStream, is8bitSwFormatsVaapi);
            var is8_10_12bitSwFormatsVaapi = IsQsv8To12BitSwFormat(videoStream, is8_10bitSwFormatsVaapi);

            if (is8bitSwFormatsVaapi)
            {
                if (IsH264StreamCodec(videoStream))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, hwSurface);
                }

                if (string.Equals("mpeg2video", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface);
                }

                if (string.Equals("vc1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vc1", bitDepth, hwSurface);
                }

                if (string.Equals("vp8", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, hwSurface);
                }
            }

            if (is8_10bitSwFormatsVaapi)
            {
                if (string.Equals("vp9", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, hwSurface);
                }

                if (string.Equals("av1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface);
                }
            }

            if (is8_10_12bitSwFormatsVaapi)
            {
                if (IsHevcStreamCodec(videoStream))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, hwSurface);
                }
            }

            return null;
        }

        private static bool IsVaapiDecodingUnavailable(EncodingOptions options)
        {
            return !OperatingSystem.IsLinux()
                || options.HardwareAccelerationType != HardwareAccelerationType.vaapi;
        }

        private bool HasVaapiHwSurface(EncodingJobInfo state)
        {
            return IsVaapiSupported(state)
                && IsVaapiFullSupported()
                && IsOpenclFullSupported()
                && _mediaEncoder.SupportsFilter("alphasrc");
        }

        private static bool IsH264StreamCodec(MediaStream videoStream)
        {
            return string.Equals("avc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                || string.Equals("h264", videoStream.Codec, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHevcStreamCodec(MediaStream videoStream)
        {
            return string.Equals("hevc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                || string.Equals("h265", videoStream.Codec, StringComparison.OrdinalIgnoreCase);
        }

        public string GetVideotoolboxVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            if (IsVideotoolboxDecodingUnavailable(options))
            {
                return null;
            }

            return ResolveVideotoolboxAccelType(state, options, videoStream, bitDepth);
        }

        private string ResolveVideotoolboxAccelType(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var is8bitSwFormatsVt = Is8BitYuv420SwFormat(videoStream);
            var is8_10bitSwFormatsVt = Is8Or10BitYuv420SwFormat(videoStream, is8bitSwFormatsVt);
            var is8_10_12bitSwFormatsVt = IsQsv8To12BitSwFormat(videoStream, is8_10bitSwFormatsVt);
            var isAv1SupportedSwFormatsVt = IsVtAv1SupportedSwFormat(videoStream, is8_10bitSwFormatsVt);

            // The related patches make videotoolbox hardware surface working is only available in jellyfin-ffmpeg 7.0.1 at the moment.
            bool useHwSurface = CanUseVideotoolboxHwSurface();

            if (is8bitSwFormatsVt)
            {
                if (string.Equals("vp8", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, useHwSurface);
                }
            }

            if (is8_10bitSwFormatsVt)
            {
                if (IsH264StreamCodec(videoStream))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, useHwSurface);
                }

                if (string.Equals("vp9", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, useHwSurface);
                }
            }

            if (is8_10_12bitSwFormatsVt)
            {
                if (IsHevcStreamCodec(videoStream))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, useHwSurface);
                }

                if (IsVideoToolboxAv1Decodable(videoStream, isAv1SupportedSwFormatsVt))
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, useHwSurface);
                }
            }

            return null;
        }

        private static bool IsVideotoolboxDecodingUnavailable(EncodingOptions options)
        {
            return !OperatingSystem.IsMacOS()
                || options.HardwareAccelerationType != HardwareAccelerationType.videotoolbox;
        }

        private static bool IsVtAv1SupportedSwFormat(MediaStream videoStream, bool is8_10bitSwFormatsVt)
        {
            return is8_10bitSwFormatsVt || string.Equals("yuv420p12le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanUseVideotoolboxHwSurface()
        {
            return (_mediaEncoder.EncoderVersion >= _minFFmpegWorkingVtHwSurface) && IsVideoToolboxFullSupported();
        }

        private bool IsVideoToolboxAv1Decodable(MediaStream videoStream, bool isAv1SupportedSwFormatsVt)
        {
            return string.Equals("av1", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                && isAv1SupportedSwFormatsVt
                && _mediaEncoder.IsVideoToolboxAv1DecodeAvailable;
        }

        public string GetRkmppVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var isLinux = OperatingSystem.IsLinux();

            if (IsRkmppDecodingUnavailable(options, isLinux))
            {
                return null;
            }

            return ResolveRkmppAccelType(state, options, videoStream, bitDepth);
        }

        private string ResolveRkmppAccelType(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var inW = state.VideoStream?.Width;
            var inH = state.VideoStream?.Height;
            var reqW = state.BaseRequest.Width;
            var reqH = state.BaseRequest.Height;
            var reqMaxW = state.BaseRequest.MaxWidth;
            var reqMaxH = state.BaseRequest.MaxHeight;

            // rkrga RGA2e supports range from 1/16 to 16
            if (!IsScaleRatioSupported(inW, inH, reqW, reqH, reqMaxW, reqMaxH, 16.0f))
            {
                return null;
            }

            var isRkmppOclSupported = IsRkmppOclSupported();
            var hwSurface = HasRkmppHwSurface(isRkmppOclSupported);

            // rkrga RGA3 supports range from 1/8 to 8
            var isAfbcSupported = hwSurface && IsScaleRatioSupported(inW, inH, reqW, reqH, reqMaxW, reqMaxH, 8.0f);

            // TODO: add more 8/10bit and 4:2:2 formats for Rkmpp after finishing the ffcheck tool
            var is8bitSwFormatsRkmpp = Is8BitYuv420SwFormat(videoStream);
            var is10bitSwFormatsRkmpp = string.Equals("yuv420p10le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase);
            var is8_10bitSwFormatsRkmpp = is8bitSwFormatsRkmpp || is10bitSwFormatsRkmpp;

            // nv15 and nv20 are bit-stream only formats
            if (IsRkmppBitstreamOnlyFormat(is10bitSwFormatsRkmpp, hwSurface))
            {
                return null;
            }

            if (is8bitSwFormatsRkmpp)
            {
                if (string.Equals(videoStream.Codec, "mpeg1video", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg1video", bitDepth, hwSurface);
                }

                if (string.Equals(videoStream.Codec, "mpeg2video", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface);
                }

                if (string.Equals(videoStream.Codec, "mpeg4", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg4", bitDepth, hwSurface);
                }

                if (string.Equals(videoStream.Codec, "vp8", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, hwSurface);
                }
            }

            if (is8_10bitSwFormatsRkmpp)
            {
                if (IsAvcOrH264Codec(videoStream))
                {
                    var accelType = GetHwaccelType(state, options, "h264", bitDepth, hwSurface);
                    return accelType + (CanUseAfbcRga(accelType, isAfbcSupported) ? " -afbc rga" : string.Empty);
                }

                if (IsHevcOrH265Codec(videoStream))
                {
                    var accelType = GetHwaccelType(state, options, "hevc", bitDepth, hwSurface);
                    return accelType + (CanUseAfbcRga(accelType, isAfbcSupported) ? " -afbc rga" : string.Empty);
                }

                if (string.Equals(videoStream.Codec, "vp9", StringComparison.OrdinalIgnoreCase))
                {
                    var accelType = GetHwaccelType(state, options, "vp9", bitDepth, hwSurface);
                    return accelType + (CanUseAfbcRga(accelType, isAfbcSupported) ? " -afbc rga" : string.Empty);
                }

                if (string.Equals(videoStream.Codec, "av1", StringComparison.OrdinalIgnoreCase))
                {
                    // there's an issue about AV1 AFBC on RK3588, disable it for now until it's fixed upstream
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface);
                }
            }

            return null;
        }

        private static bool IsRkmppDecodingUnavailable(EncodingOptions options, bool isLinux)
        {
            return !isLinux
                || options.HardwareAccelerationType != HardwareAccelerationType.rkmpp;
        }

        private bool IsRkmppOclSupported()
        {
            return IsRkmppFullSupported() && IsOpenclFullSupported();
        }

        private bool HasRkmppHwSurface(bool isRkmppOclSupported)
        {
            return isRkmppOclSupported
                && _mediaEncoder.SupportsFilter("alphasrc");
        }

        private static bool IsRkmppBitstreamOnlyFormat(bool is10bitSwFormatsRkmpp, bool hwSurface)
        {
            return is10bitSwFormatsRkmpp && !hwSurface;
        }

        private static bool CanUseAfbcRga(string accelType, bool isAfbcSupported)
        {
            return !string.IsNullOrEmpty(accelType) && isAfbcSupported;
        }
    }
}
