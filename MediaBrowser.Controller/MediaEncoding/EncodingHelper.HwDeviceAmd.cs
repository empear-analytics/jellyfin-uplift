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

        private static bool IsHwDeviceAmfUnused(IsHwDeviceAmfUnusedParts parts)
        {
            var isD3d11vaDecoder = parts.VidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
            var isAmfEncoder = parts.VidEncoder.Contains("amf", StringComparison.OrdinalIgnoreCase);
            return IsHwDeviceUnused(isD3d11vaDecoder, isAmfEncoder);
        }

        private bool IsAmfHwaccelUnsupported(bool isWindows)
        {
            return !isWindows || !_mediaEncoder.SupportsHwaccel("d3d11va");
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

        private readonly record struct IsHwDeviceAmfUnusedParts(string VidDecoder, string VidEncoder);
    }
}
