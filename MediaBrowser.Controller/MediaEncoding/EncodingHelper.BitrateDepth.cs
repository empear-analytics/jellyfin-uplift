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
        private static int GetBitDepthCompensatedBitrate(int bitrate, MediaStream videoStream, int targetBitDepth)
        {
            if (IsBitrateCompensationUnneededForBitDepth(videoStream, targetBitDepth)
                || videoStream.BitRate is not int sourceBitrate)
            {
                return bitrate;
            }

            // Bit depth reduction requires bit rate compensation because
            // 10-bit encoded video is significantly more efficient.
            var factor = sourceBitrate switch
            {
                <= 4_000_000 => 1.5,
                <= 8_000_000 => 1.2,
                <= 12_000_000 => 1.1,
                _ => 1.05
            };

            return Convert.ToInt32(bitrate * factor);
        }

        private static bool IsBitrateCompensationUnneededForBitDepth(MediaStream videoStream, int targetBitDepth)
        {
            return targetBitDepth >= 10
                || (videoStream.BitDepth is int bitDepth && bitDepth <= targetBitDepth);
        }
    }
}
