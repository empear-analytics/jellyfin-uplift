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
        private static void AddAmdD3d11OutputMapping(List<string> mainFilters, bool isDxInDxOut, bool isUploadForOclTonemap, bool hasSubs)
        {
            if (NeedsAmfHwOutputMap(isDxInDxOut, isUploadForOclTonemap, hasSubs))
            {
                // OUTPUT d3d11(nv12) surface(vram)
                // reverse-mapping via d3d11-opencl interop.
                mainFilters.Add("hwmap=derive_device=d3d11va:mode=write:reverse=1");
                mainFilters.Add("format=d3d11");
            }
        }

        private static void AddAmdHwTransferToMemory(List<string> mainFilters, bool hasGraphicalSubs)
        {
            // OUTPUT nv12 surface(memory)
            // prefer hwmap to hwdownload on opencl.
            var hwTransferFilter = hasGraphicalSubs ? "hwdownload" : "hwmap=mode=read";
            mainFilters.Add(hwTransferFilter);
            mainFilters.Add("format=nv12");
        }

        private static bool NeedsAmfHwUpload(bool isSwDecoder, bool isAmfEncoder, bool isUploadForOclTonemap)
        {
            return isSwDecoder && isAmfEncoder && !isUploadForOclTonemap;
        }

        private static bool NeedsAmfHwOutputMap(bool isDxInDxOut, bool isUploadForOclTonemap, bool hasSubs)
        {
            return (isDxInDxOut || isUploadForOclTonemap) && !hasSubs;
        }

        private static void AddAmdOclTonemapUpload(List<string> mainFilters, bool doOclTonemap)
        {
            if (doOclTonemap)
            {
                mainFilters.Add("hwupload=derive_device=d3d11va:extra_hw_frames=24");
                mainFilters.Add("format=d3d11");
                mainFilters.Add("hwmap=derive_device=opencl:mode=read");
            }
        }
    }
}
