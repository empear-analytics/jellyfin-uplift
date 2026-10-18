#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Probing
{
    public partial class ProbeResultNormalizer
    {
        private static string GetAspectRatio(MediaStreamInfo info)
        {
            var original = info.DisplayAspectRatio;

            var parts = (original ?? string.Empty).Split(':');
            if (!TryParseDisplayAspectRatioParts(parts, out var width, out var height))
            {
                width = info.Width.Value;
                height = info.Height.Value;
            }

            return GetStandardAspectRatioOrOriginal(original, width, height);
        }

        private static string GetStandardAspectRatioOrOriginal(string original, int width, int height)
        {
            if (width > 0 && height > 0)
            {
                double ratio = width;
                ratio /= height;

                if (IsClose(ratio, 1.777777778, .03))
                {
                    return "16:9";
                }

                if (IsClose(ratio, 1.3333333333, .05))
                {
                    return "4:3";
                }

                if (IsClose(ratio, 1.41))
                {
                    return "1.41:1";
                }

                if (IsClose(ratio, 1.5))
                {
                    return "1.5:1";
                }

                if (IsClose(ratio, 1.6))
                {
                    return "1.6:1";
                }

                if (IsClose(ratio, 1.66666666667))
                {
                    return "5:3";
                }

                if (IsClose(ratio, 1.85, .02))
                {
                    return "1.85:1";
                }

                if (IsClose(ratio, 2.35, .025))
                {
                    return "2.35:1";
                }

                if (IsClose(ratio, 2.4, .025))
                {
                    return "2.40:1";
                }
            }

            return original;
        }

        private static bool TryParseDisplayAspectRatioParts(string[] parts, out int width, out int height)
        {
            width = default;
            height = default;
            return parts.Length == 2
                    && int.TryParse(parts[0], CultureInfo.InvariantCulture, out width)
                    && int.TryParse(parts[1], CultureInfo.InvariantCulture, out height)
                    && width > 0
                    && height > 0;
        }
    }
}
