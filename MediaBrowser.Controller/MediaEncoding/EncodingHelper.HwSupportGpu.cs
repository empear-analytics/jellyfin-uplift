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
        private bool IsOpenclFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("opencl")
                   && _mediaEncoder.SupportsFilter("scale_opencl")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.TonemapOpenclBt2390)
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayOpenclFrameSync);

            // Let transpose_opencl optional for the time being.
        }

        private bool IsCudaFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("cuda")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.ScaleCudaFormat)
                   && _mediaEncoder.SupportsFilter("yadif_cuda")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.TonemapCudaName)
                   && _mediaEncoder.SupportsFilter("overlay_cuda")
                   && _mediaEncoder.SupportsFilter("hwupload_cuda");

            // Let transpose_cuda optional for the time being.
        }

        private bool IsVulkanFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("vulkan")
                   && _mediaEncoder.SupportsFilter("libplacebo")
                   && _mediaEncoder.SupportsFilter("scale_vulkan")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayVulkanFrameSync)
                   && _mediaEncoder.SupportsFilter("transpose_vulkan")
                   && _mediaEncoder.SupportsFilter("flip_vulkan");
        }

        private bool IsVideoToolboxFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("videotoolbox")
                && _mediaEncoder.SupportsFilter("yadif_videotoolbox")
                && _mediaEncoder.SupportsFilter("overlay_videotoolbox")
                && _mediaEncoder.SupportsFilter("tonemap_videotoolbox")
                && _mediaEncoder.SupportsFilter("scale_vt");

            // Let transpose_vt optional for the time being.
        }
    }
}
