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
        /// Gets the parameter of Apple VideoToolBox filter chain.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="vidEncoder">Video encoder to use.</param>
        /// <returns>The tuple contains three lists: main, sub and overlay filters.</returns>
        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetAppleVidFilterChain(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidEncoder)
        {
            if (options.HardwareAccelerationType != HardwareAccelerationType.videotoolbox)
            {
                return (null, null, null);
            }

            // ReSharper disable once InconsistentNaming
            var isMacOS = OperatingSystem.IsMacOS();
            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isVtDecoder = vidDecoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
            var isVtEncoder = vidEncoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
            var isVtFullSupported = isMacOS && IsVideoToolboxFullSupported();

            // legacy videotoolbox pipeline (disable hw filters)
            if (UseLegacyVideoToolboxPipeline(isVtEncoder, isVtDecoder, isVtFullSupported))
            {
                return GetSwVidFilterChain(state, options, vidEncoder);
            }

            // preferred videotoolbox + metal filters pipeline
            return GetAppleVidFiltersPreferred(state, options, vidDecoder, vidEncoder);
        }

        private bool UseLegacyVideoToolboxPipeline(bool isVtEncoder, bool isVtDecoder, bool isVtFullSupported)
        {
            return !(isVtEncoder || isVtDecoder)
                || !isVtFullSupported
                || !_mediaEncoder.SupportsFilter("alphasrc");
        }

        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetAppleVidFiltersPreferred(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidDecoder,
            string vidEncoder)
        {
            var isVtEncoder = vidEncoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
            var isVtDecoder = vidDecoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
            var isMjpegEncoder = vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);

            var inW = state.VideoStream?.Width;
            var inH = state.VideoStream?.Height;
            var reqW = state.BaseRequest.Width;
            var reqH = state.BaseRequest.Height;
            var reqMaxW = state.BaseRequest.MaxWidth;
            var reqMaxH = state.BaseRequest.MaxHeight;
            var threeDFormat = state.MediaSource.Video3DFormat;

            var doDeintH2645 = IsDeinterlaceAvailable(state);
            var doVtTonemap = IsVideoToolboxTonemapAvailable(state, options);
            var doMetalTonemap = IsAppleMetalTonemapNeeded(state, options, doVtTonemap);
            var usingHwSurface = IsAppleHwSurfaceWorking(isVtDecoder);

            var rotation = state.VideoStream?.Rotation ?? 0;
            var transposeDir = rotation == 0 ? string.Empty : GetVideoTransposeDirection(state);
            var doVtTranspose = IsAppleVtTransposeAvailable(transposeDir);
            var swapWAndH = Math.Abs(rotation) == 90 && doVtTranspose;
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            var scaleFormat = GetAppleScaleFormat(state, doMetalTonemap);

            var hwScaleFilter = GetHwScaleFilter("scale", "vt", scaleFormat, false, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH);

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);
            var hasAssSubs = HasAssSubtitles(state, hasSubs);

            /* Make main filters for video stream */
            var mainContext = new AppleMainFilterContext(
                state,
                options,
                doDeintH2645,
                doVtTranspose,
                doVtTonemap,
                doMetalTonemap,
                isMjpegEncoder);
            var mainFilters = GetAppleMainFilters(hwScaleFilter, transposeDir, mainContext);

            /* Make sub and overlay filters for subtitle stream */
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();

            var context = new AppleSubtitleFilterContext(
                hasSubs,
                hasGraphicalSubs,
                hasTextSubs,
                hasAssSubs,
                swpInW,
                swpInH,
                reqW,
                reqH,
                reqMaxW,
                reqMaxH);
            AddAppleSubtitleFilters(state, subFilters, overlayFilters, context);

            if (usingHwSurface)
            {
                if (!isVtEncoder)
                {
                    mainFilters.Add("hwdownload");
                    mainFilters.Add("format=nv12");
                }

                return (mainFilters, subFilters, overlayFilters);
            }

            return GetAppleLegacyHwSurfaceFilters(isVtEncoder, mainFilters, subFilters, overlayFilters);
        }

        private List<string> GetAppleMainFilters(string hwScaleFilter, string transposeDir, AppleMainFilterContext context)
        {
            var mainFilters = new List<string>();

            // hw deint
            if (context.DoDeintH2645)
            {
                var deintFilter = GetHwDeinterlaceFilter(context.State, context.Options, "videotoolbox");
                mainFilters.Add(deintFilter);
            }

            // hw transpose
            if (context.DoVtTranspose)
            {
                mainFilters.Add($"transpose_vt=dir={transposeDir}");
            }

            hwScaleFilter = AddAppleVtTonemapArgs(hwScaleFilter, context.DoVtTonemap);

            // hw scale & vt tonemap
            mainFilters.Add(hwScaleFilter);

            // Metal tonemap
            if (context.DoMetalTonemap)
            {
                var tonemapFilter = GetHwTonemapFilter(context.Options, "videotoolbox", "nv12", context.IsMjpegEncoder);
                mainFilters.Add(tonemapFilter);
            }

            return mainFilters;
        }

        private void AddAppleSubtitleFilters(EncodingJobInfo state, List<string> subFilters, List<string> overlayFilters, AppleSubtitleFilterContext context)
        {
            if (context.HasSubs)
            {
                if (context.HasGraphicalSubs)
                {
                    var subW = state.SubtitleStream?.Width;
                    var subH = state.SubtitleStream?.Height;
                    var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, subW, subH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                    subFilters.Add(subPreProcFilters);
                    subFilters.Add("format=bgra");
                }
                else if (context.HasTextSubs)
                {
                    var framerate = state.VideoStream?.RealFrameRate;
                    var subFramerate = GetSubtitleFramerate(context.HasAssSubs, framerate);

                    var alphaSrcFilter = GetAlphaSrcFilter(state, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH, subFramerate);
                    var subTextSubtitlesFilter = GetTextSubtitlesFilter(state, true, true);
                    subFilters.Add(alphaSrcFilter);
                    subFilters.Add("format=bgra");
                    subFilters.Add(subTextSubtitlesFilter);
                }

                subFilters.Add("hwupload");
                overlayFilters.Add("overlay_videotoolbox=eof_action=pass:repeatlast=0");
            }
        }

        private readonly record struct AppleSubtitleFilterContext(
            bool HasSubs,
            bool HasGraphicalSubs,
            bool HasTextSubs,
            bool HasAssSubs,
            int? SwpInW,
            int? SwpInH,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH);

        private readonly record struct AppleMainFilterContext(
            EncodingJobInfo State,
            EncodingOptions Options,
            bool DoDeintH2645,
            bool DoVtTranspose,
            bool DoVtTonemap,
            bool DoMetalTonemap,
            bool IsMjpegEncoder);
    }
}
