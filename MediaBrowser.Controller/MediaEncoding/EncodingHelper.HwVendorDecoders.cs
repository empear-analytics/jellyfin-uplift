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
        public string GetQsvHwVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();

            if (IsQsvDecodingUnavailable(new IsQsvDecodingUnavailableParts(isWindows, isLinux), options))
            {
                return null;
            }

            var isQsvOclSupported = IsQsvOclSupported();
            var isIntelDx11OclSupported = IsIntelDx11OclSupported(isWindows, isQsvOclSupported);
            var isIntelVaapiOclSupported = IsIntelVaapiOclSupported(state, isLinux, isQsvOclSupported);
            var hwSurface = HasQsvHwSurface(new HasQsvHwSurfaceParts(isIntelDx11OclSupported, isIntelVaapiOclSupported));

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

            var context = new HwDecoderQsvContext(
                state,
                options,
                videoStream,
                bitDepth,
                hwSurface,
                is8_10bitSwFormatsQsv,
                is8_10_12bitSwFormatsQsv);
            return ResolveQsvWideSwFormatAccelType(context);
        }

        private string ResolveQsvWideSwFormatAccelType(HwDecoderQsvContext context)
        {
            if (context.Is8_10bitSwFormatsQsv)
            {
                if (string.Equals(context.VideoStream.Codec, "vp9", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "vp9", context.BitDepth, context.HwSurface) + GetHwDecoderName(context.Options, "vp9", "qsv", "vp9", context.BitDepth);
                }

                if (string.Equals(context.VideoStream.Codec, "av1", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "av1", context.BitDepth, context.HwSurface) + GetHwDecoderName(context.Options, "av1", "qsv", "av1", context.BitDepth);
                }
            }

            if (!context.Is8_10_12bitSwFormatsQsv)
            {
                return null;
            }

            if (IsHevcOrH265Codec(context.VideoStream))
            {
                return GetHwaccelType(context.State, context.Options, "hevc", context.BitDepth, context.HwSurface) + GetHwDecoderName(context.Options, "hevc", "qsv", "hevc", context.BitDepth);
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

        private bool HasQsvHwSurface(HasQsvHwSurfaceParts parts)
        {
            return (parts.IsIntelDx11OclSupported || parts.IsIntelVaapiOclSupported)
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

        private static bool IsQsvDecodingUnavailable(IsQsvDecodingUnavailableParts parts, EncodingOptions options)
        {
            return (!parts.IsWindows && !parts.IsLinux)
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

            var context = new HwDecoderNvdecContext(
                state,
                options,
                videoStream,
                bitDepth,
                hwSurface,
                is8_10bitSwFormatsNvdec,
                is8_10_12bitSwFormatsNvdec);
            return ResolveNvdecWideSwFormatAccelType(context);
        }

        private string ResolveNvdecWideSwFormatAccelType(HwDecoderNvdecContext context)
        {
            if (context.Is8_10bitSwFormatsNvdec)
            {
                if (string.Equals("vp9", context.VideoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "vp9", context.BitDepth, context.HwSurface) + GetHwDecoderName(context.Options, "vp9", "cuvid", "vp9", context.BitDepth);
                }

                if (string.Equals("av1", context.VideoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "av1", context.BitDepth, context.HwSurface) + GetHwDecoderName(context.Options, "av1", "cuvid", "av1", context.BitDepth);
                }
            }

            if (!context.Is8_10_12bitSwFormatsNvdec)
            {
                return null;
            }

            if (IsHevcStreamCodec(context.VideoStream))
            {
                return GetHwaccelType(context.State, context.Options, "hevc", context.BitDepth, context.HwSurface) + GetHwDecoderName(context.Options, "hevc", "cuvid", "hevc", context.BitDepth);
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

            if (!is8_10bitSwFormatsAmf)
            {
                return null;
            }

            var context = new HwDecoderAmfContext(state, options, videoStream, bitDepth, hwSurface);
            return ResolveAmfWideSwFormatAccelType(context);
        }

        private string ResolveAmfWideSwFormatAccelType(HwDecoderAmfContext context)
        {
            if (IsHevcStreamCodec(context.VideoStream))
            {
                return GetHwaccelType(context.State, context.Options, "hevc", context.BitDepth, context.HwSurface);
            }

            if (string.Equals("vp9", context.VideoStream.Codec, StringComparison.OrdinalIgnoreCase))
            {
                return GetHwaccelType(context.State, context.Options, "vp9", context.BitDepth, context.HwSurface);
            }

            if (string.Equals("av1", context.VideoStream.Codec, StringComparison.OrdinalIgnoreCase))
            {
                return GetHwaccelType(context.State, context.Options, "av1", context.BitDepth, context.HwSurface);
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

            var context = new HwDecoderVaapiContext(
                state,
                options,
                videoStream,
                bitDepth,
                hwSurface,
                is8_10bitSwFormatsVaapi,
                is8_10_12bitSwFormatsVaapi);
            return ResolveVaapiWideSwFormatAccelType(context);
        }

        private string ResolveVaapiWideSwFormatAccelType(HwDecoderVaapiContext context)
        {
            if (context.Is8_10bitSwFormatsVaapi)
            {
                if (string.Equals("vp9", context.VideoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "vp9", context.BitDepth, context.HwSurface);
                }

                if (string.Equals("av1", context.VideoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "av1", context.BitDepth, context.HwSurface);
                }
            }

            if (!context.Is8_10_12bitSwFormatsVaapi)
            {
                return null;
            }

            if (IsHevcStreamCodec(context.VideoStream))
            {
                return GetHwaccelType(context.State, context.Options, "hevc", context.BitDepth, context.HwSurface);
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

            var context = new HwDecoderVideotoolboxContext(
                state,
                options,
                videoStream,
                bitDepth,
                useHwSurface,
                is8_10bitSwFormatsVt,
                is8_10_12bitSwFormatsVt,
                isAv1SupportedSwFormatsVt);
            return ResolveVideotoolboxWideSwFormatAccelType(context);
        }

        private string ResolveVideotoolboxWideSwFormatAccelType(HwDecoderVideotoolboxContext context)
        {
            if (context.Is8_10bitSwFormatsVt)
            {
                if (IsH264StreamCodec(context.VideoStream))
                {
                    return GetHwaccelType(context.State, context.Options, "h264", context.BitDepth, context.UseHwSurface);
                }

                if (string.Equals("vp9", context.VideoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "vp9", context.BitDepth, context.UseHwSurface);
                }
            }

            if (!context.Is8_10_12bitSwFormatsVt)
            {
                return null;
            }

            if (IsHevcStreamCodec(context.VideoStream))
            {
                return GetHwaccelType(context.State, context.Options, "hevc", context.BitDepth, context.UseHwSurface);
            }

            if (IsVideoToolboxAv1Decodable(context.VideoStream, context.IsAv1SupportedSwFormatsVt))
            {
                return GetHwaccelType(context.State, context.Options, "av1", context.BitDepth, context.UseHwSurface);
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
            var is8_10bitSwFormatsRkmpp = Is8Or10BitRkmppSwFormat(new Is8Or10BitRkmppSwFormatParts(is8bitSwFormatsRkmpp, is10bitSwFormatsRkmpp));

            // nv15 and nv20 are bit-stream only formats
            if (IsRkmppBitstreamOnlyFormat(new IsRkmppBitstreamOnlyFormatParts(is10bitSwFormatsRkmpp, hwSurface)))
            {
                return null;
            }

            var context = new HwDecoderRkmppContext(
                state,
                options,
                videoStream,
                bitDepth,
                hwSurface,
                isAfbcSupported,
                is8bitSwFormatsRkmpp,
                is8_10bitSwFormatsRkmpp);
            return ResolveRkmppEightBitAccelType(context);
        }

        private string ResolveRkmppEightBitAccelType(HwDecoderRkmppContext context)
        {
            if (context.Is8bitSwFormatsRkmpp)
            {
                if (string.Equals(context.VideoStream.Codec, "mpeg1video", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "mpeg1video", context.BitDepth, context.HwSurface);
                }

                if (string.Equals(context.VideoStream.Codec, "mpeg2video", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "mpeg2video", context.BitDepth, context.HwSurface);
                }

                if (string.Equals(context.VideoStream.Codec, "mpeg4", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "mpeg4", context.BitDepth, context.HwSurface);
                }

                if (string.Equals(context.VideoStream.Codec, "vp8", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(context.State, context.Options, "vp8", context.BitDepth, context.HwSurface);
                }
            }

            return ResolveRkmppWideSwFormatAccelType(context);
        }

        private string ResolveRkmppWideSwFormatAccelType(HwDecoderRkmppContext context)
        {
            if (!context.Is8_10bitSwFormatsRkmpp)
            {
                return null;
            }

            if (IsAvcOrH264Codec(context.VideoStream))
            {
                var accelType = GetHwaccelType(context.State, context.Options, "h264", context.BitDepth, context.HwSurface);
                return AppendAfbcRgaOption(accelType, context.IsAfbcSupported);
            }

            if (IsHevcOrH265Codec(context.VideoStream))
            {
                var accelType = GetHwaccelType(context.State, context.Options, "hevc", context.BitDepth, context.HwSurface);
                return AppendAfbcRgaOption(accelType, context.IsAfbcSupported);
            }

            if (string.Equals(context.VideoStream.Codec, "vp9", StringComparison.OrdinalIgnoreCase))
            {
                var accelType = GetHwaccelType(context.State, context.Options, "vp9", context.BitDepth, context.HwSurface);
                return AppendAfbcRgaOption(accelType, context.IsAfbcSupported);
            }

            if (string.Equals(context.VideoStream.Codec, "av1", StringComparison.OrdinalIgnoreCase))
            {
                // there's an issue about AV1 AFBC on RK3588, disable it for now until it's fixed upstream
                return GetHwaccelType(context.State, context.Options, "av1", context.BitDepth, context.HwSurface);
            }

            return null;
        }

        private static bool Is8Or10BitRkmppSwFormat(Is8Or10BitRkmppSwFormatParts parts)
        {
            return parts.Is8bitSwFormatsRkmpp || parts.Is10bitSwFormatsRkmpp;
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

        private static bool IsRkmppBitstreamOnlyFormat(IsRkmppBitstreamOnlyFormatParts parts)
        {
            return parts.Is10bitSwFormatsRkmpp && !parts.HwSurface;
        }

        private static string AppendAfbcRgaOption(string accelType, bool isAfbcSupported)
        {
            return accelType + (CanUseAfbcRga(accelType, isAfbcSupported) ? " -afbc rga" : string.Empty);
        }

        private static bool CanUseAfbcRga(string accelType, bool isAfbcSupported)
        {
            return !string.IsNullOrEmpty(accelType) && isAfbcSupported;
        }

        private readonly record struct HwDecoderNvdecContext(
            EncodingJobInfo State,
            EncodingOptions Options,
            MediaStream VideoStream,
            int BitDepth,
            bool HwSurface,
            bool Is8_10bitSwFormatsNvdec,
            bool Is8_10_12bitSwFormatsNvdec);

        private readonly record struct HwDecoderQsvContext(
            EncodingJobInfo State,
            EncodingOptions Options,
            MediaStream VideoStream,
            int BitDepth,
            bool HwSurface,
            bool Is8_10bitSwFormatsQsv,
            bool Is8_10_12bitSwFormatsQsv);

        private readonly record struct HwDecoderVaapiContext(
            EncodingJobInfo State,
            EncodingOptions Options,
            MediaStream VideoStream,
            int BitDepth,
            bool HwSurface,
            bool Is8_10bitSwFormatsVaapi,
            bool Is8_10_12bitSwFormatsVaapi);

        private readonly record struct HwDecoderVideotoolboxContext(
            EncodingJobInfo State,
            EncodingOptions Options,
            MediaStream VideoStream,
            int BitDepth,
            bool UseHwSurface,
            bool Is8_10bitSwFormatsVt,
            bool Is8_10_12bitSwFormatsVt,
            bool IsAv1SupportedSwFormatsVt);

        private readonly record struct HwDecoderAmfContext(
            EncodingJobInfo State,
            EncodingOptions Options,
            MediaStream VideoStream,
            int BitDepth,
            bool HwSurface);

        private readonly record struct HwDecoderRkmppContext(
            EncodingJobInfo State,
            EncodingOptions Options,
            MediaStream VideoStream,
            int BitDepth,
            bool HwSurface,
            bool IsAfbcSupported,
            bool Is8bitSwFormatsRkmpp,
            bool Is8_10bitSwFormatsRkmpp);

        private readonly record struct IsQsvDecodingUnavailableParts(bool IsWindows, bool IsLinux);

        private readonly record struct HasQsvHwSurfaceParts(bool IsIntelDx11OclSupported, bool IsIntelVaapiOclSupported);

        private readonly record struct Is8Or10BitRkmppSwFormatParts(bool Is8bitSwFormatsRkmpp, bool Is10bitSwFormatsRkmpp);

        private readonly record struct IsRkmppBitstreamOnlyFormatParts(bool Is10bitSwFormatsRkmpp, bool HwSurface);
    }
}
