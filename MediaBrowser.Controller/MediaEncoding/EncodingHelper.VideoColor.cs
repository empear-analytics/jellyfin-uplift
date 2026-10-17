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
        public string GetOverwriteColorPropertiesParam(EncodingJobInfo state, bool isTonemapAvailable)
        {
            if (isTonemapAvailable)
            {
                return GetInputHdrParam(state.VideoStream?.ColorTransfer);
            }

            return GetOutputSdrParam(null);
        }

        public string GetInputHdrParam(string colorTransfer)
        {
            if (string.Equals(colorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase))
            {
                // HLG
                return "setparams=color_primaries=bt2020:color_trc=arib-std-b67:colorspace=bt2020nc";
            }

            // HDR10
            return "setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc";
        }

        public string GetOutputSdrParam(string tonemappingRange)
        {
            // SDR
            if (string.Equals(tonemappingRange, "tv", StringComparison.OrdinalIgnoreCase))
            {
                return "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=tv";
            }

            if (string.Equals(tonemappingRange, "pc", StringComparison.OrdinalIgnoreCase))
            {
                return "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=pc";
            }

            return "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709";
        }

        public static int GetVideoColorBitDepth(EncodingJobInfo state)
        {
            var videoStream = state.VideoStream;
            if (videoStream is not null)
            {
                if (videoStream.BitDepth.HasValue)
                {
                    return videoStream.BitDepth.Value;
                }

                if (Is8BitPixelFormat(videoStream))
                {
                    return 8;
                }

                if (Is10BitPixelFormat(videoStream))
                {
                    return 10;
                }

                if (Is12BitPixelFormat(videoStream))
                {
                    return 12;
                }

                return 8;
            }

            return 0;
        }

        private static bool Is8BitPixelFormat(MediaStream videoStream)
        {
            return string.Equals(videoStream.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoStream.PixelFormat, "yuvj420p", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoStream.PixelFormat, "yuv422p", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoStream.PixelFormat, "yuv444p", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Is10BitPixelFormat(MediaStream videoStream)
        {
            return string.Equals(videoStream.PixelFormat, "yuv420p10le", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoStream.PixelFormat, "yuv422p10le", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoStream.PixelFormat, "yuv444p10le", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Is12BitPixelFormat(MediaStream videoStream)
        {
            return string.Equals(videoStream.PixelFormat, "yuv420p12le", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoStream.PixelFormat, "yuv422p12le", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoStream.PixelFormat, "yuv444p12le", StringComparison.OrdinalIgnoreCase);
        }
    }
}
