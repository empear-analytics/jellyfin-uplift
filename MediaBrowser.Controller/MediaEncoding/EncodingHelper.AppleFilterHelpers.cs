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
        private static string AddAppleVtTonemapArgs(string hwScaleFilter, bool doVtTonemap)
        {
            if (doVtTonemap)
            {
                const string VtTonemapArgs = "color_matrix=bt709:color_primaries=bt709:color_transfer=bt709";

                // scale_vt can handle scaling & tonemapping in one shot, just like vpp_qsv.
                hwScaleFilter = string.IsNullOrEmpty(hwScaleFilter)
                    ? "scale_vt=" + VtTonemapArgs
                    : hwScaleFilter + ":" + VtTonemapArgs;
            }

            return hwScaleFilter;
        }

        private static (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetAppleLegacyHwSurfaceFilters(
            bool isVtEncoder,
            List<string> mainFilters,
            List<string> subFilters,
            List<string> overlayFilters)
        {
            // For old jellyfin-ffmpeg that has broken hwsurface, add a hwupload
            var needFiltering = mainFilters.Any(f => !string.IsNullOrEmpty(f)) ||
                                subFilters.Any(f => !string.IsNullOrEmpty(f)) ||
                                overlayFilters.Any(f => !string.IsNullOrEmpty(f));
            if (needFiltering)
            {
                // INPUT videotoolbox/memory surface(vram/uma)
                // this will pass-through automatically if in/out format matches.
                mainFilters.Insert(0, "hwupload");
                mainFilters.Insert(0, "format=nv12|p010le|videotoolbox_vld");

                if (!isVtEncoder)
                {
                    mainFilters.Add("hwdownload");
                    mainFilters.Add("format=nv12");
                }
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private static string GetAppleScaleFormat(EncodingJobInfo state, bool doMetalTonemap)
        {
            var scaleFormat = string.Empty;
            // Use P010 for Metal tone mapping, otherwise force an 8bit output.
            if (!string.Equals(state.VideoStream.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase))
            {
                if (doMetalTonemap)
                {
                    if (!string.Equals(state.VideoStream.PixelFormat, "yuv420p10le", StringComparison.OrdinalIgnoreCase))
                    {
                        scaleFormat = "p010le";
                    }
                }
                else
                {
                    scaleFormat = "nv12";
                }
            }

            return scaleFormat;
        }
    }
}
