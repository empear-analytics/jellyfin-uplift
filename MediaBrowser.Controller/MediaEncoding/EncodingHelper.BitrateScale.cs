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
        private static double GetVideoBitrateScaleFactor(string codec)
        {
            // hevc & vp9 - 40% more efficient than h.264
            if (IsHevcOrVp9Codec(codec))
            {
                return .6;
            }

            // av1 - 50% more efficient than h.264
            if (string.Equals(codec, "av1", StringComparison.OrdinalIgnoreCase))
            {
                return .5;
            }

            return 1;
        }

        private static bool IsHevcOrVp9Codec(string codec)
        {
            return string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "vp9", StringComparison.OrdinalIgnoreCase);
        }

        public static int ScaleBitrate(int bitrate, string inputVideoCodec, string outputVideoCodec)
        {
            var inputScaleFactor = GetVideoBitrateScaleFactor(inputVideoCodec);
            var outputScaleFactor = GetVideoBitrateScaleFactor(outputVideoCodec);

            // Don't scale the real bitrate lower than the requested bitrate
            var scaleFactor = Math.Max(outputScaleFactor / inputScaleFactor, 1);

            if (bitrate <= 500000)
            {
                scaleFactor = Math.Max(scaleFactor, 4);
            }
            else if (bitrate <= 1000000)
            {
                scaleFactor = Math.Max(scaleFactor, 3);
            }
            else if (bitrate <= 2000000)
            {
                scaleFactor = Math.Max(scaleFactor, 2.5);
            }
            else if (bitrate <= 3000000)
            {
                scaleFactor = Math.Max(scaleFactor, 2);
            }
            else if (bitrate >= 30000000)
            {
                // Don't scale beyond 30Mbps, it is hardly visually noticeable for most codecs with our prefer speed encoding
                // and will cause extremely high bitrate to be used for av1->h264 transcoding that will overload clients and encoders
                scaleFactor = 1;
            }

            return Convert.ToInt32(scaleFactor * bitrate);
        }
    }
}
