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

        private string GetLibplaceboFilter(EncodingOptions options, GetLibplaceboFilterParts parts, bool forceFullRange)
        {
            var (outWidth, outHeight) = GetFixedOutputSize(
                parts.VideoWidth,
                parts.VideoHeight,
                parts.RequestedWidth,
                parts.RequestedHeight,
                parts.RequestedMaxWidth,
                parts.RequestedMaxHeight);

            var isFormatFixed = !string.IsNullOrEmpty(parts.VideoFormat);
            var isSizeFixed = IsOutputSizeChanged(parts.VideoWidth, parts.VideoHeight, outWidth, outHeight);

            var sizeArg = isSizeFixed ? (":w=" + outWidth.Value + ":h=" + outHeight.Value) : string.Empty;
            var formatArg = isFormatFixed ? (":format=" + parts.VideoFormat) : string.Empty;
            var tonemapArg = string.Empty;

            // libplacebo only support full range RGB
            forceFullRange = IsLibplaceboFullRangeRequired(new IsLibplaceboFullRangeRequiredParts(parts.VideoFormat, forceFullRange));

            if (parts.DoTonemap)
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

        private static bool IsLibplaceboFullRangeRequired(IsLibplaceboFullRangeRequiredParts parts)
        {
            return parts.ForceFullRange
                || (parts.VideoFormat ?? string.Empty).Contains("rgb", StringComparison.OrdinalIgnoreCase)
                || (parts.VideoFormat ?? string.Empty).Contains("bgr", StringComparison.OrdinalIgnoreCase);
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

        private readonly record struct GetLibplaceboFilterParts(string VideoFormat, bool DoTonemap, int? VideoWidth, int? VideoHeight, int? RequestedWidth, int? RequestedHeight, int? RequestedMaxWidth, int? RequestedMaxHeight);

        private readonly record struct IsLibplaceboFullRangeRequiredParts(string VideoFormat, bool ForceFullRange);
    }
}
