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

        private string GetD3d11vaDeviceArgs(int deviceIndex, string deviceVendorId, string alias)
        {
            alias ??= D3d11vaAlias;
            deviceIndex = deviceIndex >= 0 ? deviceIndex : 0;
            var options = string.IsNullOrEmpty(deviceVendorId)
                ? deviceIndex.ToString(CultureInfo.InvariantCulture)
                : ",vendor=" + deviceVendorId;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device d3d11va={0}:{1}",
                alias,
                options);
        }

        private string GetDrmDeviceArgs(string renderNodePath, string alias)
        {
            alias ??= DrmAlias;
            renderNodePath = renderNodePath ?? "/dev/dri/renderD128";

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device drm={0}:{1}",
                alias,
                renderNodePath);
        }

        private string GetQsvDeviceArgs(string renderNodePath, string alias)
        {
            var arg = " -init_hw_device qsv=" + (alias ?? QsvAlias);
            if (OperatingSystem.IsLinux())
            {
                // derive qsv from vaapi device
                return GetVaapiDeviceArgs(new VaapiDeviceNodeParts(renderNodePath, "iHD"), "i915", "0x8086", new VaapiDeviceAliasParts(null, VaapiAlias)) + arg + "@" + VaapiAlias;
            }

            if (OperatingSystem.IsWindows())
            {
                // on Windows, the deviceIndex is an int
                if (int.TryParse(renderNodePath, NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceIndex))
                {
                    return GetD3d11vaDeviceArgs(deviceIndex, string.Empty, D3d11vaAlias) + arg + "@" + D3d11vaAlias;
                }

                // derive qsv from d3d11va device
                return GetD3d11vaDeviceArgs(0, "0x8086", D3d11vaAlias) + arg + "@" + D3d11vaAlias;
            }

            return null;
        }

        private string HwDeviceDispatchQsv(EncodingOptions options, HwDeviceDispatchContext context)
        {
            if (context.OptHwaccelType == HardwareAccelerationType.qsv)
            {
                if (IsQsvHwaccelUnsupported(context.IsLinux, context.IsWindows))
                {
                    return string.Empty;
                }

                var isQsvEncoder = context.VidEncoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
                var isHwDecoder = IsQsvHwDecoder(context.VidDecoder);
                if (IsHwDeviceUnused(isHwDecoder, isQsvEncoder))
                {
                    return string.Empty;
                }

                AppendQsvHwDeviceArgs(context.Args, options, new QsvHwDeviceParts(context.IsLinux, isHwDecoder, context.IsHwTonemapAvailable));
            }
            else
            {
                return HwDeviceDispatchRemaining(context);
            }

            return GetHwDeviceArgsWithDecoder(context.Args, context.VidDecoder);
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

        private string HwDeviceDispatchAmf(HwDeviceDispatchContext context)
        {
            if (context.OptHwaccelType == HardwareAccelerationType.amf)
            {
                if (IsAmfHwaccelUnsupported(context.IsWindows))
                {
                    return string.Empty;
                }

                if (IsHwDeviceAmfUnused(new IsHwDeviceAmfUnusedParts(context.VidDecoder, context.VidEncoder)))
                {
                    return string.Empty;
                }

                AppendAmfHwDeviceArgs(context.Args);
            }
            else
            {
                return HwDeviceDispatchTail(context);
            }

            return GetHwDeviceArgsWithDecoder(context.Args, context.VidDecoder);
        }

        private static bool IsHwDeviceNvencUnused(IsHwDeviceNvencUnusedParts parts)
        {
            var isNvencEncoder = parts.VidEncoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
            var isHwDecoder = IsNvencHwDecoder(parts.VidDecoder);
            return IsHwDeviceUnused(isHwDecoder, isNvencEncoder);
        }

        private static bool IsHwDeviceAmfUnused(IsHwDeviceAmfUnusedParts parts)
        {
            var isD3d11vaDecoder = parts.VidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
            var isAmfEncoder = parts.VidEncoder.Contains("amf", StringComparison.OrdinalIgnoreCase);
            return IsHwDeviceUnused(isD3d11vaDecoder, isAmfEncoder);
        }

        private static bool IsQsvHwDecoder(string vidDecoder)
        {
            var isD3d11vaDecoder = vidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
            var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isQsvDecoder = vidDecoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
            return isQsvDecoder || isVaapiDecoder || isD3d11vaDecoder;
        }

        private static bool IsNvencHwDecoder(string vidDecoder)
        {
            var isCuvidDecoder = vidDecoder.Contains("cuvid", StringComparison.OrdinalIgnoreCase);
            var isNvdecDecoder = vidDecoder.Contains("cuda", StringComparison.OrdinalIgnoreCase);
            return isNvdecDecoder || isCuvidDecoder;
        }

        private bool IsAmfHwaccelUnsupported(bool isWindows)
        {
            return !isWindows || !_mediaEncoder.SupportsHwaccel("d3d11va");
        }

        private bool IsQsvHwaccelUnsupported(bool isLinux, bool isWindows)
        {
            return (!isLinux && !isWindows) || !_mediaEncoder.SupportsHwaccel("qsv");
        }

        private bool IsNvencHwaccelUnsupported(bool isLinux, bool isWindows)
        {
            return (!isLinux && !isWindows) || !IsCudaFullSupported();
        }

        private string AppendVaapiIntelOpenclDeviceArgs(StringBuilder args, bool doOclTonemap, bool isVaapiDecoder, string filterDevArgs)
        {
            if (doOclTonemap && !isVaapiDecoder)
            {
                args.Append(GetOpenclDeviceArgs(0, null, VaapiAlias, OpenclAlias));
                filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
            }

            return filterDevArgs;
        }

        private string AppendVaapiAmdHwDeviceArgs(StringBuilder args, EncodingOptions options, bool doOclTonemap)
        {
            string filterDevArgs;

            // Disable AMD EFC feature since it's still unstable in upstream Mesa.
            Environment.SetEnvironmentVariable("AMD_DEBUG", "noefc");

            if (IsAmdVulkanDrmInteropUsable())
            {
                args.Append(GetDrmDeviceArgs(options.VaapiDevice, DrmAlias));
                args.Append(GetVaapiDeviceArgs(new VaapiDeviceNodeParts(null, null), null, null, new VaapiDeviceAliasParts(DrmAlias, VaapiAlias)));
                args.Append(GetVulkanDeviceArgs(0, null, DrmAlias, VulkanAlias));

                // libplacebo wants an explicitly set vulkan filter device.
                filterDevArgs = GetFilterHwDeviceArgs(VulkanAlias);
            }
            else
            {
                args.Append(GetVaapiDeviceArgs(new VaapiDeviceNodeParts(options.VaapiDevice, null), null, null, new VaapiDeviceAliasParts(null, VaapiAlias)));
                filterDevArgs = GetFilterHwDeviceArgs(VaapiAlias);

                if (doOclTonemap)
                {
                    // ROCm/ROCr OpenCL runtime
                    args.Append(GetOpenclDeviceArgs(0, "Advanced Micro Devices", null, OpenclAlias));
                    filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                }
            }

            return filterDevArgs;
        }

        private bool IsAmdVulkanDrmInteropUsable()
        {
            return IsVulkanFullSupported()
                && _mediaEncoder.IsVaapiDeviceSupportVulkanDrmInterop
                && Environment.OSVersion.Version >= _minKernelVersionAmdVkFmtModifier;
        }

        private void AppendQsvHwDeviceArgs(StringBuilder args, EncodingOptions options, QsvHwDeviceParts parts)
        {
            args.Append(GetQsvDeviceArgs(options.QsvDevice, QsvAlias));
            var filterDevArgs = GetFilterHwDeviceArgs(QsvAlias);
            // child device used by qsv.
            if (_mediaEncoder.SupportsHwaccel("vaapi") || _mediaEncoder.SupportsHwaccel("d3d11va"))
            {
                if (parts.IsHwTonemapAvailable && IsOpenclFullSupported())
                {
                    var srcAlias = parts.IsLinux ? VaapiAlias : D3d11vaAlias;
                    args.Append(GetOpenclDeviceArgs(0, null, srcAlias, OpenclAlias));
                    if (!parts.IsHwDecoder)
                    {
                        filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                    }
                }
            }

            args.Append(filterDevArgs);
        }

        private void AppendAmfHwDeviceArgs(StringBuilder args)
        {
            // no dxva video processor hw filter.
            args.Append(GetD3d11vaDeviceArgs(0, "0x1002", D3d11vaAlias));
            var filterDevArgs = string.Empty;
            if (IsOpenclFullSupported())
            {
                args.Append(GetOpenclDeviceArgs(0, null, D3d11vaAlias, OpenclAlias));
                filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
            }

            args.Append(filterDevArgs);
        }

        private readonly record struct IsHwDeviceNvencUnusedParts(string VidDecoder, string VidEncoder);

        private readonly record struct IsHwDeviceAmfUnusedParts(string VidDecoder, string VidEncoder);

        private readonly record struct QsvHwDeviceParts(bool IsLinux, bool IsHwDecoder, bool IsHwTonemapAvailable);
    }
}
