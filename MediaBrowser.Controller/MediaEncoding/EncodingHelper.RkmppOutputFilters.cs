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
    }
}
