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
        public static (int? Width, int? Height) GetFixedOutputSize(
            int? videoWidth,
            int? videoHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight)
        {
            if (IsDimensionUnknown(videoWidth, requestedWidth))
            {
                return (null, null);
            }

            if (IsDimensionUnknown(videoHeight, requestedHeight))
            {
                return (null, null);
            }

            int inputWidth = Convert.ToInt32(videoWidth ?? requestedWidth, CultureInfo.InvariantCulture);
            int inputHeight = Convert.ToInt32(videoHeight ?? requestedHeight, CultureInfo.InvariantCulture);
            int outputWidth = requestedWidth ?? inputWidth;
            int outputHeight = requestedHeight ?? inputHeight;

            return LimitScaleOutputSize(outputWidth, outputHeight, requestedMaxWidth, requestedMaxHeight);
        }

        private static (int? Width, int? Height) LimitScaleOutputSize(int outputWidth, int outputHeight, int? requestedMaxWidth, int? requestedMaxHeight)
        {
            // Don't transcode video to bigger than 4k when using HW.
            int maximumWidth = Math.Min(requestedMaxWidth ?? outputWidth, 4096);
            int maximumHeight = Math.Min(requestedMaxHeight ?? outputHeight, 4096);

            if (outputWidth > maximumWidth || outputHeight > maximumHeight)
            {
                var scaleW = (double)maximumWidth / outputWidth;
                var scaleH = (double)maximumHeight / outputHeight;
                var scale = Math.Min(scaleW, scaleH);
                outputWidth = Math.Min(maximumWidth, Convert.ToInt32(outputWidth * scale));
                outputHeight = Math.Min(maximumHeight, Convert.ToInt32(outputHeight * scale));
            }

            outputWidth = 2 * (outputWidth / 2);
            outputHeight = 2 * (outputHeight / 2);

            return (outputWidth, outputHeight);
        }

        private static bool IsDimensionUnknown(int? videoDimension, int? requestedDimension)
        {
            return !videoDimension.HasValue && !requestedDimension.HasValue;
        }

        public static bool IsScaleRatioSupported(
            int? videoWidth,
            int? videoHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight,
            double? maxScaleRatio)
        {
            var (outWidth, outHeight) = GetFixedOutputSize(
                videoWidth,
                videoHeight,
                requestedWidth,
                requestedHeight,
                requestedMaxWidth,
                requestedMaxHeight);

            if (HasMissingScaleInput(videoWidth, videoHeight, outWidth, outHeight, maxScaleRatio))
            {
                return false;
            }

            var minScaleRatio = 1.0f / maxScaleRatio;
            var scaleRatioW = (double)outWidth / (double)videoWidth;
            var scaleRatioH = (double)outHeight / (double)videoHeight;

            if (IsScaleRatioOutOfRange(scaleRatioW, scaleRatioH, minScaleRatio, maxScaleRatio))
            {
                return false;
            }

            return true;
        }

        private static bool HasMissingScaleInput(int? videoWidth, int? videoHeight, int? outWidth, int? outHeight, double? maxScaleRatio)
        {
            return !videoWidth.HasValue
                || !videoHeight.HasValue
                || !outWidth.HasValue
                || !outHeight.HasValue
                || !maxScaleRatio.HasValue
                || (maxScaleRatio.Value < 1.0f);
        }

        private static bool IsScaleRatioOutOfRange(double scaleRatioW, double scaleRatioH, double? minScaleRatio, double? maxScaleRatio)
        {
            return scaleRatioW < minScaleRatio
                || scaleRatioW > maxScaleRatio
                || scaleRatioH < minScaleRatio
                || scaleRatioH > maxScaleRatio;
        }

        public static string GetHwScaleFilter(
            string hwScalePrefix,
            string hwScaleSuffix,
            string videoFormat,
            bool swapOutputWandH,
            int? videoWidth,
            int? videoHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight)
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

            var swpOutW = swapOutputWandH ? outHeight.Value : outWidth.Value;
            var swpOutH = swapOutputWandH ? outWidth.Value : outHeight.Value;

            var arg1 = isSizeFixed ? $"=w={swpOutW}:h={swpOutH}" : string.Empty;
            var arg2 = isFormatFixed ? $"format={videoFormat}" : string.Empty;
            if (isFormatFixed)
            {
                arg2 = (isSizeFixed ? ':' : '=') + arg2;
            }

            if (NeedsHwScaleFilter(hwScaleSuffix, isSizeFixed, isFormatFixed))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}{2}{3}",
                    hwScalePrefix ?? "scale",
                    hwScaleSuffix,
                    arg1,
                    arg2);
            }

            return string.Empty;
        }

        private static bool IsOutputSizeChanged(int? videoWidth, int? videoHeight, int? outWidth, int? outHeight)
        {
            return !videoWidth.HasValue
                || outWidth.Value != videoWidth.Value
                || !videoHeight.HasValue
                || outHeight.Value != videoHeight.Value;
        }

        private static bool NeedsHwScaleFilter(string hwScaleSuffix, bool isSizeFixed, bool isFormatFixed)
        {
            return !string.IsNullOrEmpty(hwScaleSuffix) && (isSizeFixed || isFormatFixed);
        }

        public static string GetGraphicalSubPreProcessFilters(
            int? videoWidth,
            int? videoHeight,
            int? subtitleWidth,
            int? subtitleHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight)
        {
            var (outWidth, outHeight) = GetFixedOutputSize(
                videoWidth,
                videoHeight,
                requestedWidth,
                requestedHeight,
                requestedMaxWidth,
                requestedMaxHeight);

            if (IsMissingOrNonPositiveSize(outWidth, outHeight))
            {
                return string.Empty;
            }

            // Automatically add padding based on subtitle input
            var filters = @"scale,scale=-1:{1}:fast_bilinear,crop,pad=max({0}\,iw):max({1}\,ih):(ow-iw)/2:(oh-ih)/2:black@0,crop={0}:{1}";

            if (HasPositiveSize(subtitleWidth, subtitleHeight))
            {
                var videoDar = (double)outWidth.Value / outHeight.Value;
                var subtitleDar = (double)subtitleWidth.Value / subtitleHeight.Value;

                // No need to add padding when DAR is the same -> 1080p PGSSUB on 2160p video
                if (Math.Abs(videoDar - subtitleDar) < 0.01f)
                {
                    filters = @"scale,scale={0}:{1}:fast_bilinear";
                }
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                filters,
                outWidth.Value,
                outHeight.Value);
        }

        private static bool IsMissingOrNonPositiveSize(int? width, int? height)
        {
            return !width.HasValue
                || !height.HasValue
                || width.Value <= 0
                || height.Value <= 0;
        }

        private static bool HasPositiveSize(int? width, int? height)
        {
            return width.HasValue
                && height.HasValue
                && width.Value > 0
                && height.Value > 0;
        }

        public static string GetAlphaSrcFilter(
            EncodingJobInfo state,
            int? videoWidth,
            int? videoHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight,
            float? framerate)
        {
            var reqTicks = state.BaseRequest.StartTimeTicks ?? 0;
            var startTime = TimeSpan.FromTicks(reqTicks).ToString(@"hh\\\:mm\\\:ss\\\.fff", CultureInfo.InvariantCulture);
            var (outWidth, outHeight) = GetFixedOutputSize(
                videoWidth,
                videoHeight,
                requestedWidth,
                requestedHeight,
                requestedMaxWidth,
                requestedMaxHeight);

            if (outWidth.HasValue && outHeight.HasValue)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "alphasrc=s={0}x{1}:r={2}:start='{3}'",
                    outWidth.Value,
                    outHeight.Value,
                    framerate ?? 25,
                    reqTicks > 0 ? startTime : 0);
            }

            return string.Empty;
        }

        public static string GetSwScaleFilter(
            EncodingJobInfo state,
            EncodingOptions options,
            string videoEncoder,
            int? videoWidth,
            int? videoHeight,
            Video3DFormat? threedFormat,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight)
        {
            var isV4l2 = string.Equals(videoEncoder, "h264_v4l2m2m", StringComparison.OrdinalIgnoreCase);
            var isMjpeg = videoEncoder is not null && videoEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);
            var scaleVal = isV4l2 ? 64 : 2;
            var targetAr = isMjpeg ? "(a*sar)" : "a"; // manually calculate AR when using mjpeg encoder

            // If fixed dimensions were supplied
            if (requestedWidth.HasValue && requestedHeight.HasValue)
            {
                return GetFixedDimensionsSwScaleFilter(threedFormat, requestedWidth, requestedHeight, isV4l2);
            }

            // If Max dimensions were supplied, for width selects lowest even number between input width and width req size and selects lowest even number from in width*display aspect and requested size

            if (requestedMaxWidth.HasValue && requestedMaxHeight.HasValue)
            {
                var maxWidthParam = requestedMaxWidth.Value.ToString(CultureInfo.InvariantCulture);
                var maxHeightParam = requestedMaxHeight.Value.ToString(CultureInfo.InvariantCulture);

                return string.Format(
                    CultureInfo.InvariantCulture,
                    @"scale=trunc(min(max(iw\,ih*{3})\,min({0}\,{1}*{3}))/{2})*{2}:trunc(min(max(iw/{3}\,ih)\,min({0}/{3}\,{1}))/2)*2",
                    maxWidthParam,
                    maxHeightParam,
                    scaleVal,
                    targetAr);
            }

            // If a fixed width was requested
            if (requestedWidth.HasValue)
            {
                return GetFixedWidthSwScaleFilter(threedFormat, requestedWidth, targetAr);
            }

            // If a fixed height was requested
            if (requestedHeight.HasValue)
            {
                var heightParam = requestedHeight.Value.ToString(CultureInfo.InvariantCulture);

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "scale=trunc(oh*{2}/{1})*{1}:{0}",
                    heightParam,
                    scaleVal,
                    targetAr);
            }

            return GetScaleFilterForMaxWidthOrHeight(requestedMaxWidth, requestedMaxHeight, scaleVal, targetAr);
        }

        private static string GetScaleFilterForMaxWidthOrHeight(int? requestedMaxWidth, int? requestedMaxHeight, int scaleVal, string targetAr)
        {
            // If a max width was requested
            if (requestedMaxWidth.HasValue)
            {
                var maxWidthParam = requestedMaxWidth.Value.ToString(CultureInfo.InvariantCulture);

                return string.Format(
                    CultureInfo.InvariantCulture,
                    @"scale=trunc(min(max(iw\,ih*{2})\,{0})/{1})*{1}:trunc(ow/{2}/2)*2",
                    maxWidthParam,
                    scaleVal,
                    targetAr);
            }

            // If a max height was requested
            if (requestedMaxHeight.HasValue)
            {
                var maxHeightParam = requestedMaxHeight.Value.ToString(CultureInfo.InvariantCulture);

                return string.Format(
                    CultureInfo.InvariantCulture,
                    @"scale=trunc(oh*{2}/{1})*{1}:min(max(iw/{2}\,ih)\,{0})",
                    maxHeightParam,
                    scaleVal,
                    targetAr);
            }

            return string.Empty;
        }

        private static string GetFixedDimensionsSwScaleFilter(Video3DFormat? threedFormat, int? requestedWidth, int? requestedHeight, bool isV4l2)
        {
            if (isV4l2)
            {
                var widthParam = requestedWidth.Value.ToString(CultureInfo.InvariantCulture);
                var heightParam = requestedHeight.Value.ToString(CultureInfo.InvariantCulture);

                return string.Format(
                        CultureInfo.InvariantCulture,
                        "scale=trunc({0}/64)*64:trunc({1}/2)*2",
                        widthParam,
                        heightParam);
            }

            return GetFixedSwScaleFilter(threedFormat, requestedWidth.Value, requestedHeight.Value);
        }

        private static string GetFixedWidthSwScaleFilter(Video3DFormat? threedFormat, int? requestedWidth, string targetAr)
        {
            if (threedFormat.HasValue)
            {
                // This method can handle 0 being passed in for the requested height
                return GetFixedSwScaleFilter(threedFormat, requestedWidth.Value, 0);
            }

            var widthParam = requestedWidth.Value.ToString(CultureInfo.InvariantCulture);

            return string.Format(
                CultureInfo.InvariantCulture,
                "scale={0}:trunc(ow/{1}/2)*2",
                widthParam,
                targetAr);
        }

        private static string GetFixedSwScaleFilter(Video3DFormat? threedFormat, int requestedWidth, int requestedHeight)
        {
            var widthParam = requestedWidth.ToString(CultureInfo.InvariantCulture);
            var heightParam = requestedHeight.ToString(CultureInfo.InvariantCulture);

            string filter = null;

            filter = Get3DFixedSwScaleFilter(threedFormat, filter);

            // default
            if (filter is null)
            {
                filter = GetDefaultFixedSwScaleFilter(requestedHeight, filter);
            }

            return string.Format(CultureInfo.InvariantCulture, filter, widthParam, heightParam);
        }

        private static string Get3DFixedSwScaleFilter(Video3DFormat? threedFormat, string filter)
        {
            if (threedFormat.HasValue)
            {
                switch (threedFormat.Value)
                {
                    case Video3DFormat.HalfSideBySide:
                        filter = @"crop=iw/2:ih:0:0,scale=(iw*2):ih,setdar=dar=a,crop=min(iw\,ih*dar):min(ih\,iw/dar):(iw-min(iw\,iw*sar))/2:(ih - min (ih\,ih/sar))/2,setsar=sar=1,scale={0}:trunc({0}/dar/2)*2";
                        // hsbs crop width in half,scale to correct size, set the display aspect,crop out any black bars we may have made the scale width to requestedWidth. Work out the correct height based on the display aspect it will maintain the aspect where -1 in this case (3d) may not.
                        break;
                    case Video3DFormat.FullSideBySide:
                        filter = @"crop=iw/2:ih:0:0,setdar=dar=a,crop=min(iw\,ih*dar):min(ih\,iw/dar):(iw-min(iw\,iw*sar))/2:(ih - min (ih\,ih/sar))/2,setsar=sar=1,scale={0}:trunc({0}/dar/2)*2";
                        // fsbs crop width in half,set the display aspect,crop out any black bars we may have made the scale width to requestedWidth.
                        break;
                    case Video3DFormat.HalfTopAndBottom:
                        filter = @"crop=iw:ih/2:0:0,scale=(iw*2):ih),setdar=dar=a,crop=min(iw\,ih*dar):min(ih\,iw/dar):(iw-min(iw\,iw*sar))/2:(ih - min (ih\,ih/sar))/2,setsar=sar=1,scale={0}:trunc({0}/dar/2)*2";
                        // htab crop height in half,scale to correct size, set the display aspect,crop out any black bars we may have made the scale width to requestedWidth
                        break;
                    case Video3DFormat.FullTopAndBottom:
                        filter = @"crop=iw:ih/2:0:0,setdar=dar=a,crop=min(iw\,ih*dar):min(ih\,iw/dar):(iw-min(iw\,iw*sar))/2:(ih - min (ih\,ih/sar))/2,setsar=sar=1,scale={0}:trunc({0}/dar/2)*2";
                        // ftab crop height in half, set the display aspect,crop out any black bars we may have made the scale width to requestedWidth
                        break;
                    default:
                        break;
                }
            }

            return filter;
        }

        private static string GetDefaultFixedSwScaleFilter(int requestedHeight, string filter)
        {
            if (requestedHeight > 0)
            {
                filter = "scale=trunc({0}/2)*2:trunc({1}/2)*2";
            }
            else
            {
                filter = "scale={0}:trunc({0}/a/2)*2";
            }

            return filter;
        }
    }
}
