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
        private string GetCudaDeviceArgs(int deviceIndex, string alias)
        {
            alias ??= CudaAlias;
            deviceIndex = deviceIndex >= 0
                ? deviceIndex
                : 0;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device cuda={0}:{1}",
                alias,
                deviceIndex);
        }

        private string GetOpenclDeviceArgs(int deviceIndex, string deviceVendorName, string srcDeviceAlias, string alias)
        {
            alias ??= OpenclAlias;
            deviceIndex = deviceIndex >= 0
                ? deviceIndex
                : 0;
            var vendorOpts = string.IsNullOrEmpty(deviceVendorName)
                ? ":0.0"
                : ":." + deviceIndex + ",device_vendor=\"" + deviceVendorName + "\"";
            var options = string.IsNullOrEmpty(srcDeviceAlias)
                ? vendorOpts
                : "@" + srcDeviceAlias;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device opencl={0}{1}",
                alias,
                options);
        }

        private string HwDeviceDispatchRemaining(HwDeviceDispatchContext context)
        {
            if (context.OptHwaccelType == HardwareAccelerationType.nvenc)
            {
                if (IsNvencHwaccelUnsupported(context.IsLinux, context.IsWindows))
                {
                    return string.Empty;
                }

                if (IsHwDeviceNvencUnused(new IsHwDeviceNvencUnusedParts(context.VidDecoder, context.VidEncoder)))
                {
                    return string.Empty;
                }

                context.Args.Append(GetCudaDeviceArgs(0, CudaAlias))
                     .Append(GetFilterHwDeviceArgs(CudaAlias));
            }
            else
            {
                return HwDeviceDispatchAmf(context);
            }

            return GetHwDeviceArgsWithDecoder(context.Args, context.VidDecoder);
        }

        private static bool IsHwDeviceNvencUnused(IsHwDeviceNvencUnusedParts parts)
        {
            var isNvencEncoder = parts.VidEncoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
            var isHwDecoder = IsNvencHwDecoder(parts.VidDecoder);
            return IsHwDeviceUnused(isHwDecoder, isNvencEncoder);
        }

        private static bool IsNvencHwDecoder(string vidDecoder)
        {
            var isCuvidDecoder = vidDecoder.Contains("cuvid", StringComparison.OrdinalIgnoreCase);
            var isNvdecDecoder = vidDecoder.Contains("cuda", StringComparison.OrdinalIgnoreCase);
            return isNvdecDecoder || isCuvidDecoder;
        }

        private bool IsNvencHwaccelUnsupported(bool isLinux, bool isWindows)
        {
            return (!isLinux && !isWindows) || !IsCudaFullSupported();
        }

        private readonly record struct IsHwDeviceNvencUnusedParts(string VidDecoder, string VidEncoder);
    }
}
