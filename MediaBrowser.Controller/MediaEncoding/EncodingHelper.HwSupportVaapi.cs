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
        private bool IsVaapiSupported(EncodingJobInfo state)
        {
            // vaapi will throw an error with this input
            // [vaapi @ 0x7faed8000960] No VAAPI support for codec mpeg4 profile -99.
            if (string.Equals(state.VideoStream?.Codec, "mpeg4", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return _mediaEncoder.SupportsHwaccel("vaapi");
        }

        private bool IsVaapiFullSupported()
        {
            return IsVaapiDrmScaleDeinterlaceSupported()
                   && _mediaEncoder.SupportsFilter("tonemap_vaapi")
                   && _mediaEncoder.SupportsFilter("procamp_vaapi")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayVaapiFrameSync)
                   && _mediaEncoder.SupportsFilter("transpose_vaapi")
                   && _mediaEncoder.SupportsFilter("hwupload_vaapi");
        }

        private bool IsVaapiDrmScaleDeinterlaceSupported()
        {
            return _mediaEncoder.SupportsHwaccel("drm")
                   && _mediaEncoder.SupportsHwaccel("vaapi")
                   && _mediaEncoder.SupportsFilter("scale_vaapi")
                   && _mediaEncoder.SupportsFilter("deinterlace_vaapi");
        }

        private bool IsRkmppFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("rkmpp")
                   && _mediaEncoder.SupportsFilter("scale_rkrga")
                   && _mediaEncoder.SupportsFilter("vpp_rkrga")
                   && _mediaEncoder.SupportsFilter("overlay_rkrga");
        }

        private bool IsVideoStreamHevcRext(EncodingJobInfo state)
        {
            var videoStream = state.VideoStream;
            if (videoStream is null)
            {
                return false;
            }

            return string.Equals(videoStream.Codec, "hevc", StringComparison.OrdinalIgnoreCase)
                   && IsHevcRextProfileOrPixelFormat(videoStream);
        }

        private static bool IsHevcRextProfileOrPixelFormat(MediaStream videoStream)
        {
            return string.Equals(videoStream.Profile, "Rext", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(videoStream.PixelFormat, "yuv420p12le", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(videoStream.PixelFormat, "yuv422p", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(videoStream.PixelFormat, "yuv422p10le", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(videoStream.PixelFormat, "yuv422p12le", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(videoStream.PixelFormat, "yuv444p", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(videoStream.PixelFormat, "yuv444p10le", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(videoStream.PixelFormat, "yuv444p12le", StringComparison.OrdinalIgnoreCase);
        }
    }
}
