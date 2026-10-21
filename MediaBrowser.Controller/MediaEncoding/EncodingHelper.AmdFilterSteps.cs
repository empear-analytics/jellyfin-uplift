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
        private static void AddAmdD3d11OutputMapping(List<string> mainFilters, AddAmdD3d11OutputMappingParts parts)
        {
            if (NeedsAmfHwOutputMap(new NeedsAmfHwOutputMapParts(parts.IsDxInDxOut, parts.IsUploadForOclTonemap, parts.HasSubs)))
            {
                // OUTPUT d3d11(nv12) surface(vram)
                // reverse-mapping via d3d11-opencl interop.
                mainFilters.Add("hwmap=derive_device=d3d11va:mode=write:reverse=1");
                mainFilters.Add("format=d3d11");
            }
        }

        private static void AddAmdHwTransferToMemory(List<string> mainFilters, bool hasGraphicalSubs)
        {
            // OUTPUT nv12 surface(memory)
            // prefer hwmap to hwdownload on opencl.
            var hwTransferFilter = hasGraphicalSubs ? "hwdownload" : "hwmap=mode=read";
            mainFilters.Add(hwTransferFilter);
            mainFilters.Add("format=nv12");
        }

        private static bool NeedsAmfHwUpload(NeedsAmfHwUploadParts parts)
        {
            return parts.IsSwDecoder && parts.IsAmfEncoder && !parts.IsUploadForOclTonemap;
        }

        private static bool NeedsAmfHwOutputMap(NeedsAmfHwOutputMapParts parts)
        {
            return (parts.IsDxInDxOut || parts.IsUploadForOclTonemap) && !parts.HasSubs;
        }

        private static void AddAmdOclTonemapUpload(List<string> mainFilters, bool doOclTonemap)
        {
            if (doOclTonemap)
            {
                mainFilters.Add("hwupload=derive_device=d3d11va:extra_hw_frames=24");
                mainFilters.Add("format=d3d11");
                mainFilters.Add("hwmap=derive_device=opencl:mode=read");
            }
        }

        private void AddAmdOclHwDeinterlaceFilter(EncodingJobInfo state, EncodingOptions options, List<string> mainFilters, bool doDeintH2645)
        {
            if (doDeintH2645)
            {
                var deintFilter = GetHwDeinterlaceFilter(state, options, "opencl");
                mainFilters.Add(deintFilter);
            }
        }

        private static void AddAmdOclTransposeFilter(List<string> mainFilters, bool doOclTranspose, string transposeDir)
        {
            if (doOclTranspose)
            {
                mainFilters.Add($"transpose_opencl=dir={transposeDir}");
            }
        }

        private static bool IsAmdRotatedWidthHeightSwapped(IsAmdRotatedWidthHeightSwappedParts parts)
        {
            return Math.Abs(parts.Rotation) == 90 && (parts.IsSwDecoder || (parts.IsD3d11vaDecoder && parts.DoOclTranspose));
        }

        private bool IsAmdOclTransposeAvailable(string transposeDir)
        {
            return !string.IsNullOrEmpty(transposeDir)
                && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.TransposeOpenclReversal);
        }

        private string GetAmdOverlayAlphaFormatOption()
        {
            return _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayOpenclAlphaFormat)
                ? ":alpha_format=premultiplied" : string.Empty;
        }

        private static float GetAmdSubtitleFramerate(EncodingJobInfo state, bool hasAssSubs)
        {
            var framerate = state.VideoStream?.RealFrameRate;
            return GetSubtitleFramerate(hasAssSubs, framerate);
        }

        private static string GetAmdSwOutputFormat(bool doOclTonemap)
        {
            return doOclTonemap ? "yuv420p10le" : "yuv420p";
        }

        private static string GetAmdOclScaleOutputFormat(bool doOclTonemap)
        {
            return doOclTonemap ? string.Empty : "nv12";
        }

        private void AddAmdDecoderD3d11vaFilters(EncodingJobInfo state, EncodingOptions options, AmdDecoderD3d11vaContext context)
        {
            if (context.IsD3d11vaDecoder)
            {
                // INPUT d3d11 surface(vram)
                // map from d3d11va to opencl via d3d11-opencl interop.
                context.MainFilters.Add("hwmap=derive_device=opencl:mode=read");

                // hw deint
                AddAmdOclHwDeinterlaceFilter(state, options, context.MainFilters, context.DoDeintH2645);

                // hw transpose
                AddAmdOclTransposeFilter(context.MainFilters, context.DoOclTranspose, context.TransposeDir);

                var outFormat = GetAmdOclScaleOutputFormat(context.DoOclTonemap);
                var hwScaleFilter = GetHwScaleFilter("scale", "opencl", outFormat, false, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                // hw scale
                context.MainFilters.Add(hwScaleFilter);
            }
        }

        private readonly record struct AmdDecoderD3d11vaContext(
            bool IsD3d11vaDecoder,
            bool DoDeintH2645,
            bool DoOclTranspose,
            string TransposeDir,
            bool DoOclTonemap,
            int? SwpInW,
            int? SwpInH,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH,
            List<string> MainFilters);

        private readonly record struct NeedsAmfHwUploadParts(bool IsSwDecoder, bool IsAmfEncoder, bool IsUploadForOclTonemap);

        private readonly record struct AddAmdD3d11OutputMappingParts(bool IsDxInDxOut, bool IsUploadForOclTonemap, bool HasSubs);

        private readonly record struct NeedsAmfHwOutputMapParts(bool IsDxInDxOut, bool IsUploadForOclTonemap, bool HasSubs);

        private readonly record struct IsAmdRotatedWidthHeightSwappedParts(int Rotation, bool IsSwDecoder, bool IsD3d11vaDecoder, bool DoOclTranspose);
    }
}
