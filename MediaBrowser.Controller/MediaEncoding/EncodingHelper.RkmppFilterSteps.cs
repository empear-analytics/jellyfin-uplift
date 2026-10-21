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
        private void AddRkmppDrmSubtitleFilters(EncodingJobInfo state, List<string> subFilters, List<string> overlayFilters, RkmppSubtitleFilterContext context)
        {
            var subMaxH = 1080;
            if (context.HasGraphicalSubs)
            {
                var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, subMaxH);
                subFilters.Add(subPreProcFilters);
                subFilters.Add("format=bgra");
            }
            else if (context.HasTextSubs)
            {
                var framerate = state.VideoStream?.RealFrameRate;
                var subFramerate = context.HasAssSubs ? Math.Min(framerate ?? 25, 60) : 10;

                // alphasrc=s=1280x720:r=10:start=0,format=bgra,subtitles,hwupload
                var alphaSrcFilter = GetAlphaSrcFilter(state, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, subMaxH, subFramerate);
                var subTextSubtitlesFilter = GetTextSubtitlesFilter(state, true, true);
                subFilters.Add(alphaSrcFilter);
                subFilters.Add("format=bgra");
                subFilters.Add(subTextSubtitlesFilter);
            }

            subFilters.Add("hwupload=derive_device=rkmpp");

            // offload 1080p+ subtitles swscale upscaling from CPU to RGA
            var (overlayW, overlayH) = GetFixedOutputSize(context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
            if (IsOverlayTallerThan(overlayW, overlayH, subMaxH))
            {
                subFilters.Add($"vpp_rkrga=w={overlayW.Value}:h={overlayH.Value}:format=bgra:afbc=1");
            }

            // try enabling AFBC to save DDR bandwidth
            var hwOverlayFilter = "overlay_rkrga=eof_action=pass:repeatlast=0:format=nv12";
            if (context.IsEncoderSupportAfbc)
            {
                hwOverlayFilter += ":afbc=1";
            }

            overlayFilters.Add(hwOverlayFilter);
        }

        private static void AddRkmppMemorySubtitleFilters(List<string> subFilters, List<string> overlayFilters, RkmppSubtitleFilterContext context)
        {
            if (context.HasGraphicalSubs)
            {
                var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                subFilters.Add(subPreProcFilters);
                overlayFilters.Add("overlay=eof_action=pass:repeatlast=0");
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

        private readonly record struct RkmppSubtitleFilterContext(
            bool IsEncoderSupportAfbc,
            bool HasGraphicalSubs,
            bool HasTextSubs,
            bool HasAssSubs,
            int? SwpInW,
            int? SwpInH,
            int? SubW,
            int? SubH,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH);

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
    }
}
