#nullable disable

#pragma warning disable CS1591
// We need lowercase normalized string for ffmpeg
#pragma warning disable CA1308

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
        public string GetVideoTransposeDirection(EncodingJobInfo state)
        {
            return (state.VideoStream?.Rotation ?? 0) switch
            {
                90 => "cclock",
                180 => "reversal",
                -90 => "clock",
                -180 => "reversal",
                _ => string.Empty
            };
        }

        /// <summary>
        /// Gets the parameter of video processing filters.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="outputVideoCodec">Video codec to use.</param>
        /// <returns>The video processing filters parameter.</returns>
        public string GetVideoProcessingFilterParam(
            EncodingJobInfo state,
            EncodingOptions options,
            string outputVideoCodec)
        {
            var videoStream = state.VideoStream;
            if (videoStream is null)
            {
                return string.Empty;
            }

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);

            List<string> mainFilters;
            List<string> subFilters;
            List<string> overlayFilters;

            (mainFilters, subFilters, overlayFilters) = options.HardwareAccelerationType switch
            {
                HardwareAccelerationType.vaapi => GetVaapiVidFilterChain(state, options, outputVideoCodec),
                HardwareAccelerationType.amf => GetAmdVidFilterChain(state, options, outputVideoCodec),
                HardwareAccelerationType.qsv => GetIntelVidFilterChain(state, options, outputVideoCodec),
                HardwareAccelerationType.nvenc => GetNvidiaVidFilterChain(state, options, outputVideoCodec),
                _ => VideoFilterChainOtherHardware(state, options, outputVideoCodec),
            };

            mainFilters?.RemoveAll(string.IsNullOrEmpty);
            subFilters?.RemoveAll(string.IsNullOrEmpty);
            overlayFilters?.RemoveAll(string.IsNullOrEmpty);

            return VideoFilterOverlayParam(state, new VideoFilterContext(mainFilters, subFilters, overlayFilters, hasSubs, hasTextSubs));
        }

        private string VideoFilterOverlayParam(EncodingJobInfo state, VideoFilterContext context)
        {
            var framerate = GetFramerateParam(state);
            if (ShouldAddFpsFilter(context.MainFilters, framerate))
            {
                AddFpsFilter(state, context.MainFilters, framerate);
            }

            var mainStr = JoinMainFiltersForVideoProcessing(context.MainFilters);

            if (context.OverlayFilters?.Count == 0)
            {
                // -vf "scale..."
                return GetMainOnlyVideoFilterParam(mainStr);
            }

            if (HasSubtitleOverlay(state, context.SubFilters, context.OverlayFilters))
            {
                // overlay graphical/text subtitles
                var subStr = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}",
                        string.Join(',', context.SubFilters));

                var overlayStr = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}",
                        string.Join(',', context.OverlayFilters));

                var mapPrefix = Convert.ToInt32(state.SubtitleStream.IsExternal);
                var subtitleStreamIndex = GetSubtitleStreamIndexForFfmpeg(state.MediaSource, state.SubtitleStream);
                var videoStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.VideoStream);

                if (context.HasSubs)
                {
                    // -filter_complex "[0:s]scale=s[sub]..."
                    var filterStr = GetSubtitleFilterComplexTemplate(new GetSubtitleFilterComplexTemplateParts(mainStr, context.HasTextSubs));

                    return string.Format(
                        CultureInfo.InvariantCulture,
                        filterStr,
                        mapPrefix,
                        subtitleStreamIndex,
                        videoStreamIndex,
                        mainStr,
                        subStr,
                        overlayStr);
                }
            }

            return string.Empty;
        }

        private static string JoinMainFiltersForVideoProcessing(List<string> mainFilters)
        {
            var mainStr = string.Empty;
            if (mainFilters?.Count > 0)
            {
                mainStr = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}",
                    string.Join(',', mainFilters));
            }

            return mainStr;
        }

        private static string GetSubtitleFilterComplexTemplate(GetSubtitleFilterComplexTemplateParts parts)
        {
            var filterStr = string.IsNullOrEmpty(parts.MainStr)
                ? " -filter_complex \"[{0}:{1}]{4}[sub];[0:{2}][sub]{5}\""
                : " -filter_complex \"[{0}:{1}]{4}[sub];[0:{2}]{3}[main];[main][sub]{5}\"";

            if (parts.HasTextSubs)
            {
                filterStr = string.IsNullOrEmpty(parts.MainStr)
                    ? " -filter_complex \"{4}[sub];[0:{2}][sub]{5}\""
                    : " -filter_complex \"{4}[sub];[0:{2}]{3}[main];[main][sub]{5}\"";
            }

            return filterStr;
        }

        private static bool ShouldAddFpsFilter(List<string> mainFilters, double? framerate)
        {
            return mainFilters is not null && framerate.HasValue;
        }

        private static string GetMainOnlyVideoFilterParam(string mainStr)
        {
            return string.IsNullOrEmpty(mainStr) ? string.Empty : " -vf \"" + mainStr + "\"";
        }

        private static void AddFpsFilter(EncodingJobInfo state, List<string> mainFilters, double? framerate)
        {
            var doDeintH2645 = IsDeinterlaceAvailable(state);
            var fpsFilter = string.Format(CultureInfo.InvariantCulture, "fps={0}", framerate.Value);

            // For filter chain containing the deinterlace filter,
            // place the fps filter at the end to preserve temporal info.
            if (doDeintH2645)
            {
                mainFilters.Add(fpsFilter);
            }
            else
            {
                mainFilters.Insert(0, fpsFilter);
            }
        }

        private static bool HasSubtitleOverlay(EncodingJobInfo state, List<string> subFilters, List<string> overlayFilters)
        {
            return overlayFilters?.Count > 0
                && subFilters?.Count > 0
                && state.SubtitleStream is not null;
        }

        private readonly record struct VideoFilterContext(
            List<string> MainFilters,
            List<string> SubFilters,
            List<string> OverlayFilters,
            bool HasSubs,
            bool HasTextSubs);

        private readonly record struct GetSubtitleFilterComplexTemplateParts(string MainStr, bool HasTextSubs);
    }
}
