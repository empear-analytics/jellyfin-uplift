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
        /// <summary>
        /// Gets the parameter of software filter chain.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="vidEncoder">Video encoder to use.</param>
        /// <returns>The tuple contains three lists: main, sub and overlay filters.</returns>
        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetSwVidFilterChain(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidEncoder)
        {
            var inW = state.VideoStream?.Width;
            var inH = state.VideoStream?.Height;
            var reqW = state.BaseRequest.Width;
            var reqH = state.BaseRequest.Height;
            var reqMaxW = state.BaseRequest.MaxWidth;
            var reqMaxH = state.BaseRequest.MaxHeight;
            var threeDFormat = state.MediaSource.Video3DFormat;

            var isSwDecoder = SwFilterChainIsSoftwareDecoder(state, options);
            var isVaapiEncoder = vidEncoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isV4l2Encoder = vidEncoder.Contains("h264_v4l2m2m", StringComparison.OrdinalIgnoreCase);

            var doDeintH2645 = IsDeinterlaceAvailable(state);
            var doToneMap = IsSwTonemapAvailable(state, options);
            var requireDoviReshaping = IsSwDoviReshapingRequired(state, doToneMap);

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);

            var rotation = state.VideoStream?.Rotation ?? 0;
            var swapWAndH = Math.Abs(rotation) == 90;
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            /* Make main filters for video stream */
            var mainFilters = new List<string>();

            mainFilters.Add(GetOverwriteColorPropertiesParam(state, doToneMap));

            // INPUT sw surface(memory/copy-back from vram)
            // sw deint
            SwFilterChainAddDeinterlace(state, options, doDeintH2645, mainFilters);

            var outFormat = isSwDecoder ? "yuv420p" : "nv12";
            var swScaleFilter = GetSwScaleFilter(state, options, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH);
            outFormat = ResolveSwOutputFormat(outFormat, new ResolveSwOutputFormatParts(isVaapiEncoder, isV4l2Encoder));

            // sw scale
            mainFilters.Add(swScaleFilter);

            // sw tonemap
            if (doToneMap)
            {
                AddSwTonemapFilter(options, mainFilters, new AddSwTonemapFilterParts(requireDoviReshaping, outFormat));
            }
            else
            {
                // OUTPUT yuv420p/nv12 surface(memory)
                mainFilters.Add("format=" + outFormat);
            }

            /* Make sub and overlay filters for subtitle stream */
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            if (hasTextSubs)
            {
                // subtitles=f='*.ass':alpha=0
                var textSubtitlesFilter = GetTextSubtitlesFilter(state, false, false);
                mainFilters.Add(textSubtitlesFilter);
            }
            else if (hasGraphicalSubs)
            {
                var subW = state.SubtitleStream?.Width;
                var subH = state.SubtitleStream?.Height;
                var subPreProcFilters = GetGraphicalSubPreProcessFilters(swpInW, swpInH, subW, subH, reqW, reqH, reqMaxW, reqMaxH);
                subFilters.Add(subPreProcFilters);
                overlayFilters.Add("overlay=eof_action=pass:repeatlast=0");
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private bool SwFilterChainIsSoftwareDecoder(EncodingJobInfo state, EncodingOptions options)
        {
            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            return string.IsNullOrEmpty(vidDecoder);
        }

        private static void SwFilterChainAddDeinterlace(EncodingJobInfo state, EncodingOptions options, bool doDeintH2645, List<string> mainFilters)
        {
            if (doDeintH2645)
            {
                var deintFilter = GetSwDeinterlaceFilter(state, options);
                mainFilters.Add(deintFilter);
            }
        }

        private static string ResolveSwOutputFormat(string outFormat, ResolveSwOutputFormatParts parts)
        {
            if (parts.IsVaapiEncoder)
            {
                outFormat = "nv12";
            }
            else if (parts.IsV4l2Encoder)
            {
                outFormat = "yuv420p";
            }

            return outFormat;
        }

        private static bool IsSwDoviReshapingRequired(EncodingJobInfo state, bool doToneMap)
        {
            return doToneMap && state.VideoStream.VideoRangeType == VideoRangeType.DOVI;
        }

        private static void AddSwTonemapFilter(EncodingOptions options, List<string> mainFilters, AddSwTonemapFilterParts parts)
        {
            // tonemapx requires yuv420p10 input for dovi reshaping, let ffmpeg convert the frame when necessary
            var tonemapFormat = parts.RequireDoviReshaping ? "yuv420p" : parts.OutFormat;
            var tonemapArgString = "tonemapx=tonemap={0}:desat={1}:peak={2}:t=bt709:m=bt709:p=bt709:format={3}";

            if (options.TonemappingParam != 0)
            {
                tonemapArgString += ":param={4}";
            }

            var range = options.TonemappingRange;
            if (range == TonemappingRange.tv || range == TonemappingRange.pc)
            {
                tonemapArgString += ":range={5}";
            }

            var tonemapArgs = string.Format(
                CultureInfo.InvariantCulture,
                tonemapArgString,
                options.TonemappingAlgorithm,
                options.TonemappingDesat,
                options.TonemappingPeak,
                tonemapFormat,
                options.TonemappingParam,
                options.TonemappingRange);

            mainFilters.Add(tonemapArgs);
        }

        private (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) VideoFilterChainOtherHardware(EncodingJobInfo state, EncodingOptions options, string outputVideoCodec)
        {
            return options.HardwareAccelerationType switch
            {
                HardwareAccelerationType.videotoolbox => GetAppleVidFilterChain(state, options, outputVideoCodec),
                HardwareAccelerationType.rkmpp => GetRkmppVidFilterChain(state, options, outputVideoCodec),
                _ => GetSwVidFilterChain(state, options, outputVideoCodec),
            };
        }

        private readonly record struct ResolveSwOutputFormatParts(bool IsVaapiEncoder, bool IsV4l2Encoder);

        private readonly record struct AddSwTonemapFilterParts(bool RequireDoviReshaping, string OutFormat);
    }
}
