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
        private string GetRkmppDeviceArgs(string alias)
        {
            alias ??= RkmppAlias;

            // device selection in rk is not supported.
            return " -init_hw_device rkmpp=" + alias;
        }

        private string GetVideoToolboxDeviceArgs(string alias)
        {
            alias ??= VideotoolboxAlias;

            // device selection in vt is not supported.
            return " -init_hw_device videotoolbox=" + alias;
        }

        private string GetVulkanDeviceArgs(int deviceIndex, string deviceName, string srcDeviceAlias, string alias)
        {
            alias ??= VulkanAlias;
            deviceIndex = deviceIndex >= 0
                ? deviceIndex
                : 0;
            var vendorOpts = string.IsNullOrEmpty(deviceName)
                ? ":" + deviceIndex
                : ":" + "\"" + deviceName + "\"";
            var options = string.IsNullOrEmpty(srcDeviceAlias)
                ? vendorOpts
                : "@" + srcDeviceAlias;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device vulkan={0}{1}",
                alias,
                options);
        }

        private string HwDeviceDispatchTail(HwDeviceDispatchContext context)
        {
            if (context.OptHwaccelType == HardwareAccelerationType.videotoolbox)
            {
                if (IsVideotoolboxHwaccelUnsupported(context.IsMacOS))
                {
                    return string.Empty;
                }

                if (IsHwDeviceVideotoolboxUnused(new IsHwDeviceVideotoolboxUnusedParts(context.VidDecoder, context.VidEncoder)))
                {
                    return string.Empty;
                }

                // videotoolbox hw filter does not require device selection
                context.Args.Append(GetVideoToolboxDeviceArgs(VideotoolboxAlias));
            }
            else
            {
                return HwDeviceDispatchRkmpp(context);
            }

            return GetHwDeviceArgsWithDecoder(context.Args, context.VidDecoder);
        }

        private string HwDeviceDispatchRkmpp(HwDeviceDispatchContext context)
        {
            if (context.OptHwaccelType == HardwareAccelerationType.rkmpp)
            {
                if (IsRkmppHwaccelUnsupported(context.IsLinux))
                {
                    return string.Empty;
                }

                var isRkmppDecoder = context.VidDecoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
                var isRkmppEncoder = context.VidEncoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
                if (IsHwDeviceUnused(isRkmppDecoder, isRkmppEncoder))
                {
                    return string.Empty;
                }

                AppendRkmppHwDeviceArgs(context.Args, isRkmppDecoder, context.IsHwTonemapAvailable);
            }

            return GetHwDeviceArgsWithDecoder(context.Args, context.VidDecoder);
        }

        private static bool IsHwDeviceVideotoolboxUnused(IsHwDeviceVideotoolboxUnusedParts parts)
        {
            var isVideotoolboxDecoder = parts.VidDecoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
            var isVideotoolboxEncoder = parts.VidEncoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
            return IsHwDeviceUnused(isVideotoolboxDecoder, isVideotoolboxEncoder);
        }

        private bool IsVideotoolboxHwaccelUnsupported(bool isMacOS)
        {
            return !isMacOS || !_mediaEncoder.SupportsHwaccel("videotoolbox");
        }

        private bool IsRkmppHwaccelUnsupported(bool isLinux)
        {
            return !isLinux || !_mediaEncoder.SupportsHwaccel("rkmpp");
        }

        private void AppendRkmppHwDeviceArgs(StringBuilder args, bool isRkmppDecoder, bool isHwTonemapAvailable)
        {
            args.Append(GetRkmppDeviceArgs(RkmppAlias));

            var filterDevArgs = string.Empty;
            var doOclTonemap = isHwTonemapAvailable && IsOpenclFullSupported();

            if (doOclTonemap && !isRkmppDecoder)
            {
                args.Append(GetOpenclDeviceArgs(0, null, RkmppAlias, OpenclAlias));
                filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
            }

            args.Append(filterDevArgs);
        }

        private readonly record struct IsHwDeviceVideotoolboxUnusedParts(string VidDecoder, string VidEncoder);
    }
}
