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
        private string GetNvidiaOverlayAlphaFormatOption()
        {
            return _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayCudaAlphaFormat)
                ? ":alpha_format=premultiplied" : string.Empty;
        }

        private static bool NeedsCudaMemoryOutput(NeedsCudaMemoryOutputParts parts)
        {
            return (parts.IsNvDecoder && parts.IsSwEncoder) || (parts.IsUploadForCuTonemap && parts.HasSubs);
        }

        private static bool NeedsNvencHwUpload(bool isSwDecoder, bool isNvencEncoder, bool isUploadForCuTonemap)
        {
            return isSwDecoder && isNvencEncoder && !isUploadForCuTonemap;
        }

        private void AddNvidiaHwDeinterlaceFilter(EncodingJobInfo state, EncodingOptions options, List<string> mainFilters, bool doDeintH2645)
        {
            if (doDeintH2645)
            {
                var deintFilter = GetHwDeinterlaceFilter(state, options, "cuda");
                mainFilters.Add(deintFilter);
            }
        }

        private void AddNvidiaHwTonemapFilter(EncodingOptions options, List<string> mainFilters, bool doCuTonemap, bool isMjpegEncoder)
        {
            if (doCuTonemap)
            {
                var tonemapFilter = GetHwTonemapFilter(options, "cuda", "yuv420p", isMjpegEncoder);
                mainFilters.Add(tonemapFilter);
            }
        }

        private static void AddNvidiaHwTransposeFilter(List<string> mainFilters, bool doCuTranspose, string transposeDir)
        {
            if (doCuTranspose)
            {
                mainFilters.Add($"transpose_cuda=dir={transposeDir}");
            }
        }

        private static void AddNvidiaHwupload(List<string> mainFilters, bool doCuTonemap)
        {
            if (doCuTonemap)
            {
                mainFilters.Add("hwupload=derive_device=cuda");
            }
        }

        private string GetNvidiaHwOutFormat(EncodingJobInfo state, bool doCuTonemap)
        {
            var isRext = IsVideoStreamHevcRext(state);
            var outFormat = doCuTonemap ? (isRext ? "p010" : string.Empty) : "yuv420p";
            return outFormat;
        }

        private void NvidiaDecoderAddSwFilters(EncodingJobInfo state, EncodingOptions options, List<string> mainFilters, NvidiaDecoderSwContext context)
        {
            if (context.IsSwDecoder)
            {
                // INPUT sw surface(memory)
                // sw deint
                AddSwDeinterlaceFilter(state, options, mainFilters, context.DoDeintH2645);

                var outFormat = context.DoCuTonemap ? "p010le" : "yuv420p";
                var swScaleFilter = GetSwScaleFilter(state, options, context.VidEncoder, context.SwpInW, context.SwpInH, context.ThreeDFormat, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                // sw scale
                mainFilters.Add(swScaleFilter);
                mainFilters.Add($"format={outFormat}");

                // sw => hw
                AddNvidiaHwupload(mainFilters, context.DoCuTonemap);
            }
        }

        private void NvidiaDecoderAddCudaFilters(EncodingJobInfo state, EncodingOptions options, List<string> mainFilters, NvidiaDecoderCudaContext context)
        {
            if (context.IsNvDecoder)
            {
                // INPUT cuda surface(vram)
                // hw deint
                AddNvidiaHwDeinterlaceFilter(state, options, mainFilters, context.DoDeintH2645);

                // hw transpose
                AddNvidiaHwTransposeFilter(mainFilters, context.DoCuTranspose, context.TransposeDir);

                var outFormat = GetNvidiaHwOutFormat(state, context.DoCuTonemap);
                var hwScaleFilter = GetHwScaleFilter("scale", "cuda", outFormat, false, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                // hw scale
                mainFilters.Add(hwScaleFilter);
            }
        }

        private readonly record struct NvidiaDecoderSwContext(
            bool IsSwDecoder,
            bool DoDeintH2645,
            bool DoCuTonemap,
            string VidEncoder,
            int? SwpInW,
            int? SwpInH,
            Video3DFormat? ThreeDFormat,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH);

        private readonly record struct NvidiaDecoderCudaContext(
            bool IsNvDecoder,
            bool DoDeintH2645,
            bool DoCuTranspose,
            bool DoCuTonemap,
            string TransposeDir,
            int? SwpInW,
            int? SwpInH,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH);

        private readonly record struct NeedsCudaMemoryOutputParts(bool IsNvDecoder, bool IsSwEncoder, bool IsUploadForCuTonemap, bool HasSubs);
    }
}
