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
        public static string GetSwDeinterlaceFilter(EncodingJobInfo state, EncodingOptions options)
        {
            var doubleRateDeint = options.DeinterlaceDoubleRate && state.VideoStream?.ReferenceFrameRate <= 30;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}={1}:-1:0",
                options.DeinterlaceMethod.ToString().ToLowerInvariant(),
                doubleRateDeint ? "1" : "0");
        }

        public string GetHwDeinterlaceFilter(EncodingJobInfo state, EncodingOptions options, string hwDeintSuffix)
        {
            var doubleRateDeint = IsHwDoubleRateDeinterlace(state, options);
            if (hwDeintSuffix.Contains("cuda", StringComparison.OrdinalIgnoreCase))
            {
                var useBwdif = CanUseCudaBwdif(options);

                return FormatBwdifYadifDeinterlaceFilter("{0}_cuda={1}:-1:0", useBwdif, doubleRateDeint);
            }

            if (hwDeintSuffix.Contains("opencl", StringComparison.OrdinalIgnoreCase))
            {
                var useBwdif = options.DeinterlaceMethod == DeinterlaceMethod.bwdif;

                if (SupportsOpenclDeinterlaceFilters())
                {
                    return FormatBwdifYadifDeinterlaceFilter("{0}_opencl={1}:-1:0", useBwdif, doubleRateDeint);
                }
            }

            if (hwDeintSuffix.Contains("vaapi", StringComparison.OrdinalIgnoreCase))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "deinterlace_vaapi=rate={0}",
                    doubleRateDeint ? "field" : "frame");
            }

            if (hwDeintSuffix.Contains("qsv", StringComparison.OrdinalIgnoreCase))
            {
                return "deinterlace_qsv=mode=2";
            }

            if (hwDeintSuffix.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase))
            {
                var useBwdif = CanUseVideotoolboxBwdif(options);

                return FormatBwdifYadifDeinterlaceFilter("{0}_videotoolbox={1}:-1:0", useBwdif, doubleRateDeint);
            }

            return string.Empty;
        }

        private static string FormatBwdifYadifDeinterlaceFilter(string format, bool useBwdif, bool doubleRateDeint)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                format,
                useBwdif ? "bwdif" : "yadif",
                doubleRateDeint ? "1" : "0");
        }

        private static bool IsHwDoubleRateDeinterlace(EncodingJobInfo state, EncodingOptions options)
        {
            return options.DeinterlaceDoubleRate && (state.VideoStream?.ReferenceFrameRate ?? 60) <= 30;
        }

        private bool CanUseCudaBwdif(EncodingOptions options)
        {
            return options.DeinterlaceMethod == DeinterlaceMethod.bwdif && _mediaEncoder.SupportsFilter("bwdif_cuda");
        }

        private bool SupportsOpenclDeinterlaceFilters()
        {
            return _mediaEncoder.SupportsFilter("yadif_opencl")
                && _mediaEncoder.SupportsFilter("bwdif_opencl");
        }

        private bool CanUseVideotoolboxBwdif(EncodingOptions options)
        {
            return options.DeinterlaceMethod == DeinterlaceMethod.bwdif && _mediaEncoder.SupportsFilter("bwdif_videotoolbox");
        }

        private string GetHwTonemapFilter(EncodingOptions options, string hwTonemapSuffix, string videoFormat, bool forceFullRange)
        {
            if (string.IsNullOrEmpty(hwTonemapSuffix))
            {
                return string.Empty;
            }

            var args = string.Empty;
            var algorithm = options.TonemappingAlgorithm.ToString().ToLowerInvariant();
            var mode = options.TonemappingMode.ToString().ToLowerInvariant();
            var range = forceFullRange ? TonemappingRange.pc : options.TonemappingRange;
            var rangeString = range.ToString().ToLowerInvariant();

            if (string.Equals(hwTonemapSuffix, "vaapi", StringComparison.OrdinalIgnoreCase))
            {
                return GetVaapiTonemapFilter(options, videoFormat);
            }
            else
            {
                args = GetGenericTonemapArgs(options, range);
            }

            return string.Format(
                    CultureInfo.InvariantCulture,
                    args,
                    hwTonemapSuffix,
                    videoFormat ?? "nv12",
                    algorithm,
                    options.TonemappingPeak,
                    options.TonemappingDesat,
                    mode,
                    options.TonemappingParam,
                    rangeString);
        }

        private static string GetVaapiTonemapFilter(EncodingOptions options, string videoFormat)
        {
            var doVaVppProcamp = false;
            var procampParams = string.Empty;
            if (IsVppTonemappingBrightnessInRange(options))
            {
                procampParams += "procamp_vaapi=b={0}";
                doVaVppProcamp = true;
            }

            if (options.VppTonemappingContrast > 1
                && options.VppTonemappingContrast <= 10)
            {
                procampParams += doVaVppProcamp ? ":c={1}" : "procamp_vaapi=c={1}";
                doVaVppProcamp = true;
            }

            var args = procampParams + "{2}tonemap_vaapi=format={3}:p=bt709:t=bt709:m=bt709:extra_hw_frames=32";

            return string.Format(
                    CultureInfo.InvariantCulture,
                    args,
                    options.VppTonemappingBrightness,
                    options.VppTonemappingContrast,
                    doVaVppProcamp ? "," : string.Empty,
                    videoFormat ?? "nv12");
        }

        private string GetGenericTonemapArgs(EncodingOptions options, TonemappingRange range)
        {
            var args = "tonemap_{0}=format={1}:p=bt709:t=bt709:m=bt709:tonemap={2}:peak={3}:desat={4}";

            var useLegacyTonemapModes = _mediaEncoder.EncoderVersion >= _minFFmpegOclCuTonemapMode
                                       && _legacyTonemapModes.Contains(options.TonemappingMode);

            var useAdvancedTonemapModes = _mediaEncoder.EncoderVersion >= _minFFmpegAdvancedTonemapMode
                                          && _advancedTonemapModes.Contains(options.TonemappingMode);

            if (useLegacyTonemapModes || useAdvancedTonemapModes)
            {
                args += ":tonemap_mode={5}";
            }

            if (options.TonemappingParam != 0)
            {
                args += ":param={6}";
            }

            if (range == TonemappingRange.tv || range == TonemappingRange.pc)
            {
                args += ":range={7}";
            }

            return args;
        }

        private string GetLibplaceboFilter(
            EncodingOptions options,
            string videoFormat,
            bool doTonemap,
            int? videoWidth,
            int? videoHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight,
            bool forceFullRange)
        {
            var (outWidth, outHeight) = GetFixedOutputSize(
                videoWidth,
                videoHeight,
                requestedWidth,
                requestedHeight,
                requestedMaxWidth,
                requestedMaxHeight);

            var isFormatFixed = !string.IsNullOrEmpty(videoFormat);
            var isSizeFixed = IsOutputSizeChanged(videoWidth, videoHeight, outWidth, outHeight);

            var sizeArg = isSizeFixed ? (":w=" + outWidth.Value + ":h=" + outHeight.Value) : string.Empty;
            var formatArg = isFormatFixed ? (":format=" + videoFormat) : string.Empty;
            var tonemapArg = string.Empty;

            // libplacebo only support full range RGB
            forceFullRange = IsLibplaceboFullRangeRequired(videoFormat, forceFullRange);

            if (doTonemap)
            {
                tonemapArg = GetLibplaceboTonemapArg(options, forceFullRange, tonemapArg);
            }
            else if (forceFullRange)
            {
                formatArg += ":range=pc";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "libplacebo=upscaler=none:downscaler=none{0}{1}{2}",
                sizeArg,
                formatArg,
                tonemapArg);
        }

        private static bool IsLibplaceboFullRangeRequired(string videoFormat, bool forceFullRange)
        {
            return forceFullRange
                || (videoFormat ?? string.Empty).Contains("rgb", StringComparison.OrdinalIgnoreCase)
                || (videoFormat ?? string.Empty).Contains("bgr", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLibplaceboTonemapArg(EncodingOptions options, bool forceFullRange, string tonemapArg)
        {
            var algorithm = options.TonemappingAlgorithm;
            var algorithmString = "clip";
            var mode = options.TonemappingMode;
            var range = forceFullRange ? TonemappingRange.pc : options.TonemappingRange;

            if (algorithm == TonemappingAlgorithm.bt2390)
            {
                algorithmString = "bt.2390";
            }
            else if (algorithm != TonemappingAlgorithm.none)
            {
                algorithmString = algorithm.ToString().ToLowerInvariant();
            }

            tonemapArg = $":tonemapping={algorithmString}:peak_detect=0:color_primaries=bt709:color_trc=bt709:colorspace=bt709";

            if (range == TonemappingRange.tv || range == TonemappingRange.pc)
            {
                tonemapArg += ":range=" + range.ToString().ToLowerInvariant();
            }

            return tonemapArg;
        }

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

            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isVaapiEncoder = vidEncoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isV4l2Encoder = vidEncoder.Contains("h264_v4l2m2m", StringComparison.OrdinalIgnoreCase);

            var doDeintH2645 = IsDeinterlaceAvailable(state);
            var doToneMap = IsSwTonemapAvailable(state, options);
            var requireDoviReshaping = doToneMap && state.VideoStream.VideoRangeType == VideoRangeType.DOVI;

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
            if (doDeintH2645)
            {
                var deintFilter = GetSwDeinterlaceFilter(state, options);
                mainFilters.Add(deintFilter);
            }

            var outFormat = isSwDecoder ? "yuv420p" : "nv12";
            var swScaleFilter = GetSwScaleFilter(state, options, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH);
            outFormat = ResolveSwOutputFormat(outFormat, isVaapiEncoder, isV4l2Encoder);

            // sw scale
            mainFilters.Add(swScaleFilter);

            // sw tonemap
            if (doToneMap)
            {
                AddSwTonemapFilter(options, mainFilters, requireDoviReshaping, outFormat);
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

        private static string ResolveSwOutputFormat(string outFormat, bool isVaapiEncoder, bool isV4l2Encoder)
        {
            if (isVaapiEncoder)
            {
                outFormat = "nv12";
            }
            else if (isV4l2Encoder)
            {
                outFormat = "yuv420p";
            }

            return outFormat;
        }

        private static void AddSwTonemapFilter(EncodingOptions options, List<string> mainFilters, bool requireDoviReshaping, string outFormat)
        {
            // tonemapx requires yuv420p10 input for dovi reshaping, let ffmpeg convert the frame when necessary
            var tonemapFormat = requireDoviReshaping ? "yuv420p" : outFormat;
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
                HardwareAccelerationType.videotoolbox => GetAppleVidFilterChain(state, options, outputVideoCodec),
                HardwareAccelerationType.rkmpp => GetRkmppVidFilterChain(state, options, outputVideoCodec),
                _ => GetSwVidFilterChain(state, options, outputVideoCodec),
            };

            mainFilters?.RemoveAll(string.IsNullOrEmpty);
            subFilters?.RemoveAll(string.IsNullOrEmpty);
            overlayFilters?.RemoveAll(string.IsNullOrEmpty);

            var framerate = GetFramerateParam(state);
            if (ShouldAddFpsFilter(mainFilters, framerate))
            {
                AddFpsFilter(state, mainFilters, framerate);
            }

            var mainStr = JoinMainFiltersForVideoProcessing(mainFilters);

            if (overlayFilters?.Count == 0)
            {
                // -vf "scale..."
                return string.IsNullOrEmpty(mainStr) ? string.Empty : " -vf \"" + mainStr + "\"";
            }

            if (HasSubtitleOverlay(state, subFilters, overlayFilters))
            {
                // overlay graphical/text subtitles
                var subStr = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}",
                        string.Join(',', subFilters));

                var overlayStr = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}",
                        string.Join(',', overlayFilters));

                var mapPrefix = Convert.ToInt32(state.SubtitleStream.IsExternal);
                var subtitleStreamIndex = GetSubtitleStreamIndexForFfmpeg(state.MediaSource, state.SubtitleStream);
                var videoStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.VideoStream);

                if (hasSubs)
                {
                    // -filter_complex "[0:s]scale=s[sub]..."
                    var filterStr = GetSubtitleFilterComplexTemplate(mainStr, hasTextSubs);

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

        private static string GetSubtitleFilterComplexTemplate(string mainStr, bool hasTextSubs)
        {
            var filterStr = string.IsNullOrEmpty(mainStr)
                ? " -filter_complex \"[{0}:{1}]{4}[sub];[0:{2}][sub]{5}\""
                : " -filter_complex \"[{0}:{1}]{4}[sub];[0:{2}]{3}[main];[main][sub]{5}\"";

            if (hasTextSubs)
            {
                filterStr = string.IsNullOrEmpty(mainStr)
                    ? " -filter_complex \"{4}[sub];[0:{2}][sub]{5}\""
                    : " -filter_complex \"{4}[sub];[0:{2}]{3}[main];[main][sub]{5}\"";
            }

            return filterStr;
        }

        private static bool ShouldAddFpsFilter(List<string> mainFilters, double? framerate)
        {
            return mainFilters is not null && framerate.HasValue;
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
    }
}
