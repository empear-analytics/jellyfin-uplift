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

        private static bool IsQsvHwDecoder(string vidDecoder)
        {
            var isD3d11vaDecoder = vidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
            var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isQsvDecoder = vidDecoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
            return isQsvDecoder || isVaapiDecoder || isD3d11vaDecoder;
        }

        private bool IsQsvHwaccelUnsupported(bool isLinux, bool isWindows)
        {
            return (!isLinux && !isWindows) || !_mediaEncoder.SupportsHwaccel("qsv");
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

        private readonly record struct QsvHwDeviceParts(bool IsLinux, bool IsHwDecoder, bool IsHwTonemapAvailable);
    }
}
