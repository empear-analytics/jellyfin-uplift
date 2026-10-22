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

                return FormatBwdifYadifDeinterlaceFilter(new FormatBwdifYadifDeinterlaceFilterParts("{0}_cuda={1}:-1:0", useBwdif, doubleRateDeint));
            }

            if (hwDeintSuffix.Contains("opencl", StringComparison.OrdinalIgnoreCase))
            {
                var useBwdif = options.DeinterlaceMethod == DeinterlaceMethod.bwdif;

                if (SupportsOpenclDeinterlaceFilters())
                {
                    return FormatBwdifYadifDeinterlaceFilter(new FormatBwdifYadifDeinterlaceFilterParts("{0}_opencl={1}:-1:0", useBwdif, doubleRateDeint));
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

                return FormatBwdifYadifDeinterlaceFilter(new FormatBwdifYadifDeinterlaceFilterParts("{0}_videotoolbox={1}:-1:0", useBwdif, doubleRateDeint));
            }

            return string.Empty;
        }

        private static string FormatBwdifYadifDeinterlaceFilter(FormatBwdifYadifDeinterlaceFilterParts parts)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                parts.Format,
                parts.UseBwdif ? "bwdif" : "yadif",
                parts.DoubleRateDeint ? "1" : "0");
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

        private readonly record struct FormatBwdifYadifDeinterlaceFilterParts(string Format, bool UseBwdif, bool DoubleRateDeint);
    }
}
