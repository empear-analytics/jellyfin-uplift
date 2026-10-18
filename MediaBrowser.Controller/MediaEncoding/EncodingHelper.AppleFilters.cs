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
            var doMetalTonemap = !doVtTonemap && IsHwTonemapAvailable(state, options);
            var usingHwSurface = isVtDecoder && (_mediaEncoder.EncoderVersion >= _minFFmpegWorkingVtHwSurface);

            var rotation = state.VideoStream?.Rotation ?? 0;
            var transposeDir = rotation == 0 ? string.Empty : GetVideoTransposeDirection(state);
            var doVtTranspose = !string.IsNullOrEmpty(transposeDir) && _mediaEncoder.SupportsFilter("transpose_vt");
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
            var mainFilters = new List<string>();

            // hw deint
            if (doDeintH2645)
            {
                var deintFilter = GetHwDeinterlaceFilter(state, options, "videotoolbox");
                mainFilters.Add(deintFilter);
            }

            // hw transpose
            if (doVtTranspose)
            {
                mainFilters.Add($"transpose_vt=dir={transposeDir}");
            }

            if (doVtTonemap)
            {
                const string VtTonemapArgs = "color_matrix=bt709:color_primaries=bt709:color_transfer=bt709";

                // scale_vt can handle scaling & tonemapping in one shot, just like vpp_qsv.
                hwScaleFilter = string.IsNullOrEmpty(hwScaleFilter)
                    ? "scale_vt=" + VtTonemapArgs
                    : hwScaleFilter + ":" + VtTonemapArgs;
            }

            // hw scale & vt tonemap
            mainFilters.Add(hwScaleFilter);

            // Metal tonemap
            if (doMetalTonemap)
            {
                var tonemapFilter = GetHwTonemapFilter(options, "videotoolbox", "nv12", isMjpegEncoder);
                mainFilters.Add(tonemapFilter);
            }

            /* Make sub and overlay filters for subtitle stream */
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();

            if (hasSubs)
            {
                if (hasGraphicalSubs)
                {
                    var subW = state.SubtitleStream?.Width;
                    var subH = state.SubtitleStream?.Height;
                    var subPreProcFilters = GetGraphicalSubPreProcessFilters(swpInW, swpInH, subW, subH, reqW, reqH, reqMaxW, reqMaxH);
                    subFilters.Add(subPreProcFilters);
                    subFilters.Add("format=bgra");
                }
                else if (hasTextSubs)
                {
                    var framerate = state.VideoStream?.RealFrameRate;
                    var subFramerate = GetSubtitleFramerate(hasAssSubs, framerate);

                    var alphaSrcFilter = GetAlphaSrcFilter(state, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH, subFramerate);
                    var subTextSubtitlesFilter = GetTextSubtitlesFilter(state, true, true);
                    subFilters.Add(alphaSrcFilter);
                    subFilters.Add("format=bgra");
                    subFilters.Add(subTextSubtitlesFilter);
                }

                subFilters.Add("hwupload");
                overlayFilters.Add("overlay_videotoolbox=eof_action=pass:repeatlast=0");
            }

            if (usingHwSurface)
            {
                if (!isVtEncoder)
                {
                    mainFilters.Add("hwdownload");
                    mainFilters.Add("format=nv12");
                }

                return (mainFilters, subFilters, overlayFilters);
            }

            // For old jellyfin-ffmpeg that has broken hwsurface, add a hwupload
            var needFiltering = mainFilters.Any(f => !string.IsNullOrEmpty(f)) ||
                                subFilters.Any(f => !string.IsNullOrEmpty(f)) ||
                                overlayFilters.Any(f => !string.IsNullOrEmpty(f));
            if (needFiltering)
            {
                // INPUT videotoolbox/memory surface(vram/uma)
                // this will pass-through automatically if in/out format matches.
                mainFilters.Insert(0, "hwupload");
                mainFilters.Insert(0, "format=nv12|p010le|videotoolbox_vld");

                if (!isVtEncoder)
                {
                    mainFilters.Add("hwdownload");
                    mainFilters.Add("format=nv12");
                }
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private static string GetAppleScaleFormat(EncodingJobInfo state, bool doMetalTonemap)
        {
            var scaleFormat = string.Empty;
            // Use P010 for Metal tone mapping, otherwise force an 8bit output.
            if (!string.Equals(state.VideoStream.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase))
            {
                if (doMetalTonemap)
                {
                    if (!string.Equals(state.VideoStream.PixelFormat, "yuv420p10le", StringComparison.OrdinalIgnoreCase))
                    {
                        scaleFormat = "p010le";
                    }
                }
                else
                {
                    scaleFormat = "nv12";
                }
            }

            return scaleFormat;
        }
    }
}
