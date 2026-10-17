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
        private string GetNvidiaOverlayAlphaFormatOption()
        {
            return _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayCudaAlphaFormat)
                ? ":alpha_format=premultiplied" : string.Empty;
        }

        private static bool NeedsCudaMemoryOutput(bool isNvDecoder, bool isSwEncoder, bool isUploadForCuTonemap, bool hasSubs)
        {
            return (isNvDecoder && isSwEncoder) || (isUploadForCuTonemap && hasSubs);
        }

        private static bool NeedsNvencHwUpload(bool isSwDecoder, bool isNvencEncoder, bool isUploadForCuTonemap)
        {
            return isSwDecoder && isNvencEncoder && !isUploadForCuTonemap;
        }

        private void AddNvidiaHwDeinterlaceFilter(EncodingJobInfo state, EncodingOptions options, List<string> mainFilters, bool doDeintH2645)
        {
            if (doDeintH2645)
            {
                var deintFilter = GetHwDeinterlaceFilter(state, options, "cuda");
                mainFilters.Add(deintFilter);
            }
        }

        private void AddNvidiaHwTonemapFilter(EncodingOptions options, List<string> mainFilters, bool doCuTonemap, bool isMjpegEncoder)
        {
            if (doCuTonemap)
            {
                var tonemapFilter = GetHwTonemapFilter(options, "cuda", "yuv420p", isMjpegEncoder);
                mainFilters.Add(tonemapFilter);
            }
        }

        private static void AddNvidiaHwTransposeFilter(List<string> mainFilters, bool doCuTranspose, string transposeDir)
        {
            if (doCuTranspose)
            {
                mainFilters.Add($"transpose_cuda=dir={transposeDir}");
            }
        }

        private static void AddNvidiaHwupload(List<string> mainFilters, bool doCuTonemap)
        {
            if (doCuTonemap)
            {
                mainFilters.Add("hwupload=derive_device=cuda");
            }
        }
    }
}
