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
        private bool IsSwTonemapAvailable(EncodingJobInfo state, EncodingOptions options)
        {
            if (IsSwTonemapInputUnsupported(state))
            {
                return false;
            }

            return state.VideoStream.VideoRange == VideoRange.HDR;
        }

        private bool IsSwTonemapInputUnsupported(EncodingJobInfo state)
        {
            return state.VideoStream is null
                || GetVideoColorBitDepth(state) < 10
                || !_mediaEncoder.SupportsFilter("tonemapx");
        }

        private bool IsHwTonemapAvailable(EncodingJobInfo state, EncodingOptions options)
        {
            if (IsHwTonemapInputUnsupported(state, options))
            {
                return false;
            }

            if (state.VideoStream.VideoRange == VideoRange.HDR
                && state.VideoStream.VideoRangeType == VideoRangeType.DOVI)
            {
                // Only native SW decoder, HW accelerator and hevc_rkmpp decoder can parse dovi rpu.
                var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;

                var isRkmppDecoder = vidDecoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
                if (CanRkmppParseDoviRpu(state, isRkmppDecoder))
                {
                    return true;
                }

                return CanHwTonemapDoviWithDecoder(vidDecoder);
            }

            // GPU tonemapping supports all HDR RangeTypes
            return state.VideoStream.VideoRange == VideoRange.HDR;
        }

        private static bool CanHwTonemapDoviWithDecoder(string vidDecoder)
        {
                var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
                var isNvdecDecoder = vidDecoder.Contains("cuda", StringComparison.OrdinalIgnoreCase);
                var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                var isD3d11vaDecoder = vidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
                var isVideoToolBoxDecoder = vidDecoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
                return isSwDecoder || isNvdecDecoder || isVaapiDecoder || isD3d11vaDecoder || isVideoToolBoxDecoder;
        }

        private bool IsHwTonemapInputUnsupported(EncodingJobInfo state, EncodingOptions options)
        {
            return state.VideoStream is null
                || !options.EnableTonemapping
                || GetVideoColorBitDepth(state) < 10;
        }

        private bool CanRkmppParseDoviRpu(EncodingJobInfo state, bool isRkmppDecoder)
        {
            return isRkmppDecoder
                && _mediaEncoder.EncoderVersion >= _minFFmpegRkmppHevcDecDoviRpu
                && string.Equals(state.VideoStream?.Codec, "hevc", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsVulkanHwTonemapAvailable(EncodingJobInfo state, EncodingOptions options)
        {
            if (state.VideoStream is null)
            {
                return false;
            }

            // libplacebo has partial Dolby Vision to SDR tonemapping support.
            return options.EnableTonemapping
                   && state.VideoStream.VideoRange == VideoRange.HDR
                   && GetVideoColorBitDepth(state) == 10;
        }

        private bool IsIntelVppTonemapAvailable(EncodingJobInfo state, EncodingOptions options)
        {
            if (IsVppTonemapInputUnsupported(state, options))
            {
                return false;
            }

            // prefer 'tonemap_vaapi' over 'vpp_qsv' on Linux for supporting Gen9/KBLx.
            // 'vpp_qsv' requires VPL, which is only supported on Gen12/TGLx and newer.
            if (IsWindowsQsvVppTonemapUnsupported(options))
            {
                return false;
            }

            return state.VideoStream.VideoRange == VideoRange.HDR
                   && (state.VideoStream.VideoRangeType == VideoRangeType.HDR10
                       || IsHdr10Plus(state.VideoStream)
                       || IsDoviWithHdr10Bl(state.VideoStream));
        }

        private static bool IsVppTonemapInputUnsupported(EncodingJobInfo state, EncodingOptions options)
        {
            return state.VideoStream is null
                || !options.EnableVppTonemapping
                || GetVideoColorBitDepth(state) < 10;
        }

        private bool IsWindowsQsvVppTonemapUnsupported(EncodingOptions options)
        {
            return OperatingSystem.IsWindows()
                && options.HardwareAccelerationType == HardwareAccelerationType.qsv
                && _mediaEncoder.EncoderVersion < _minFFmpegQsvVppTonemapOption;
        }

        private bool IsVideoToolboxTonemapAvailable(EncodingJobInfo state, EncodingOptions options)
        {
            if (IsVideoToolboxTonemapInputUnsupported(state, options))
            {
                return false;
            }

            // Certain DV profile 5 video works in Safari with direct playing, but the VideoToolBox does not produce correct mapping results with transcoding.
            // All other HDR formats working.
            return state.VideoStream.VideoRange == VideoRange.HDR
                   && (state.VideoStream.VideoRangeType == VideoRangeType.HDR10
                       || IsHdr10Plus(state.VideoStream)
                       || IsDoviWithHdr10Bl(state.VideoStream)
                       || state.VideoStream.VideoRangeType == VideoRangeType.HLG
                       || state.VideoStream.VideoRangeType == VideoRangeType.DOVIInvalid);
        }

        private static bool IsVideoToolboxTonemapInputUnsupported(EncodingJobInfo state, EncodingOptions options)
        {
            return state.VideoStream is null
                || !options.EnableVideoToolboxTonemapping
                || GetVideoColorBitDepth(state) < 10;
        }

        private static bool IsDeinterlaceAvailable(EncodingJobInfo state)
        {
            var doDeintH264 = state.DeInterlace("h264", true) || state.DeInterlace("avc", true);
            var doDeintHevc = state.DeInterlace("h265", true) || state.DeInterlace("hevc", true);
            return doDeintH264 || doDeintHevc;
        }
    }
}
