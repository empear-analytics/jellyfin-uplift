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
        private void AddRkmppDecoderFilters(EncodingJobInfo state, EncodingOptions options, RkmppDecoderSwContext swDecoderContext, RkmppDecoderChainContext context)
        {
            if (context.IsSwDecoder)
            {
                RkmppDecoderAddSwFilters(state, options, context.MainFilters, swDecoderContext);
            }
            else
            {
                RkmppDecoderAddRkrgaFilters(swDecoderContext, context);
            }
        }

        private static void RkmppDecoderAddRkrgaFilters(RkmppDecoderSwContext swDecoderContext, RkmppDecoderChainContext context)
        {
            if (context.IsRkmppDecoder)
            {
                // INPUT rkmpp/drm surface(gem/dma-heap)

                var isFullAfbcPipeline = context.IsEncoderSupportAfbc && context.IsDrmInDrmOut && !swDecoderContext.DoOclTonemap;
                var swapOutputWandH = context.DoRkVppTranspose && context.SwapWAndH;
                var outFormat = swDecoderContext.DoOclTonemap ? "p010" : "nv12";
                var hwScaleFilter = GetHwScaleFilter("vpp", "rkrga", outFormat, swapOutputWandH, swDecoderContext.SwpInW, swDecoderContext.SwpInH, swDecoderContext.ReqW, swDecoderContext.ReqH, swDecoderContext.ReqMaxW, swDecoderContext.ReqMaxH);
                var doScaling = !string.IsNullOrEmpty(GetHwScaleFilter("vpp", "rkrga", string.Empty, swapOutputWandH, swDecoderContext.SwpInW, swDecoderContext.SwpInH, swDecoderContext.ReqW, swDecoderContext.ReqH, swDecoderContext.ReqMaxW, swDecoderContext.ReqMaxH));

                var hwScaleContext = new RkmppDecoderHwScaleContext(context.InW, context.InH, swDecoderContext.ReqW, swDecoderContext.ReqH, swDecoderContext.ReqMaxW, swDecoderContext.ReqMaxH, swDecoderContext.DoOclTonemap, swDecoderContext.IsMjpegEncoder, doScaling, context.DoRkVppTranspose, isFullAfbcPipeline, context.TransposeDir);
                if (NeedsRkrgaScaleFilters(context.HasSubs, context.DoRkVppTranspose, isFullAfbcPipeline, doScaling))
                {
                    RkmppDecoderAddHwScaleFilters(context.MainFilters, hwScaleFilter, outFormat, hwScaleContext);
                }
            }
        }

        private static string AppendRkmppSwScaleOptions(string swScaleFilter, bool isMjpegEncoder, bool doOclTonemap)
        {
            if (isMjpegEncoder && !doOclTonemap)
            {
                // sw decoder + hw mjpeg encoder
                swScaleFilter = string.IsNullOrEmpty(swScaleFilter) ? "scale=out_range=pc" : $"{swScaleFilter}:out_range=pc";
            }

            if (!string.IsNullOrEmpty(swScaleFilter))
            {
                swScaleFilter += ":flags=fast_bilinear";
            }

            return swScaleFilter;
        }

        private static string AppendRkmppHwScaleOptions(string hwScaleFilter, bool doRkVppTranspose, string transposeDir, bool isFullAfbcPipeline)
        {
            if (!string.IsNullOrEmpty(hwScaleFilter) && doRkVppTranspose)
            {
                hwScaleFilter += $":transpose={transposeDir}";
            }

            // try enabling AFBC to save DDR bandwidth
            if (!string.IsNullOrEmpty(hwScaleFilter) && isFullAfbcPipeline)
            {
                hwScaleFilter += ":afbc=1";
            }

            return hwScaleFilter;
        }

        private static void RkmppDecoderAddHwScaleFilters(List<string> mainFilters, string hwScaleFilter, string outFormat, RkmppDecoderHwScaleContext context)
        {
            var isScaleRatioSupported = IsScaleRatioSupported(context.InW, context.InH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH, 8.0f);

            // RGA3 hardware only support (1/8 ~ 8) scaling in each blit operation,
            // but in Trickplay there's a case: (3840/320 == 12), enable 2pass for it
            if (context.DoScaling && !isScaleRatioSupported)
            {
                AddRkmppScaleFirstPass(mainFilters, context.DoOclTonemap, context.IsMjpegEncoder, outFormat);
            }

            // The RKMPP MJPEG encoder on some newer chip models no longer supports RGB input.
            // Use 2pass here to enable RGA output of full-range YUV in the 2nd pass.
            if (NeedsRkrgaMjpegFirstPass(context.IsMjpegEncoder, context.DoOclTonemap, context.DoScaling, isScaleRatioSupported))
            {
                var hwScaleFilterFirstPass = "vpp_rkrga=format=bgra:afbc=1";
                mainFilters.Add(hwScaleFilterFirstPass);
            }

            hwScaleFilter = AppendRkmppHwScaleOptions(hwScaleFilter, context.DoRkVppTranspose, context.TransposeDir, context.IsFullAfbcPipeline);

            // hw transpose & scale
            mainFilters.Add(hwScaleFilter);
        }

        private void RkmppDecoderAddSwFilters(EncodingJobInfo state, EncodingOptions options, List<string> mainFilters, RkmppDecoderSwContext context)
        {
            // INPUT sw surface(memory)
            // sw deint
            AddSwDeinterlaceFilter(state, options, mainFilters, context.DoDeintH2645);

            var outFormat = GetRkmppSwOutFormat(context.DoOclTonemap, context.HasGraphicalSubs);
            var swScaleFilter = GetSwScaleFilter(state, options, context.VidEncoder, context.SwpInW, context.SwpInH, context.ThreeDFormat, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
            swScaleFilter = AppendRkmppSwScaleOptions(swScaleFilter, context.IsMjpegEncoder, context.DoOclTonemap);

            // sw scale
            mainFilters.Add(swScaleFilter);
            mainFilters.Add($"format={outFormat}");

            // keep video at memory except ocl tonemap,
            // since the overhead caused by hwupload >>> using sw filter.
            // sw => hw
            if (context.DoOclTonemap)
            {
                mainFilters.Add("hwupload=derive_device=opencl");
            }
        }

        private readonly record struct RkmppDecoderHwScaleContext(
            int? InW,
            int? InH,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH,
            bool DoOclTonemap,
            bool IsMjpegEncoder,
            bool DoScaling,
            bool DoRkVppTranspose,
            bool IsFullAfbcPipeline,
            string TransposeDir);

        private readonly record struct RkmppDecoderSwContext(
            string VidEncoder,
            bool DoDeintH2645,
            bool DoOclTonemap,
            bool HasGraphicalSubs,
            bool IsMjpegEncoder,
            int? SwpInW,
            int? SwpInH,
            Video3DFormat? ThreeDFormat,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH);

        private readonly record struct RkmppDecoderChainContext(bool IsSwDecoder, bool IsRkmppDecoder, bool IsEncoderSupportAfbc, bool IsDrmInDrmOut, bool DoRkVppTranspose, bool SwapWAndH, int? InW, int? InH, string TransposeDir, bool HasSubs, List<string> MainFilters);
    }
}
