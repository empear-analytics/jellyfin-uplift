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
        private static void AddDrmReverseMapping(List<string> mainFilters, bool doOclTonemap)
        {
            if (doOclTonemap)
            {
                // OUTPUT drm(nv12) surface(gem/dma-heap)
                // reverse-mapping via drm-opencl interop.
                mainFilters.Add("hwmap=derive_device=rkmpp:reverse=1");
                mainFilters.Add("format=drm_prime");
            }
        }

        private static bool NeedsRkrgaScaleFilters(bool hasSubs, bool doRkVppTranspose, bool isFullAfbcPipeline, bool doScaling)
        {
            return !hasSubs
                || doRkVppTranspose
                || !isFullAfbcPipeline
                || doScaling;
        }

        private static bool NeedsRkrgaMjpegFirstPass(bool isMjpegEncoder, bool doOclTonemap, bool doScaling, bool isScaleRatioSupported)
        {
            return isMjpegEncoder && !doOclTonemap && ((doScaling && isScaleRatioSupported) || !doScaling);
        }

        private static bool IsOverlayTallerThan(int? overlayW, int? overlayH, int subMaxH)
        {
            return overlayW.HasValue && overlayH.HasValue && overlayH.Value > subMaxH;
        }

        private static bool NeedsRkmppSwapWAndH(int rotation, bool isSwDecoder, bool isRkmppDecoder, bool doRkVppTranspose)
        {
            return Math.Abs(rotation) == 90 && (isSwDecoder || (isRkmppDecoder && doRkVppTranspose));
        }

        private static bool HasRkmppAssSubtitles(EncodingJobInfo state, bool hasSubs)
        {
            return hasSubs
                && (string.Equals(state.SubtitleStream.Codec, "ass", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state.SubtitleStream.Codec, "ssa", StringComparison.OrdinalIgnoreCase));
        }
    }
}
