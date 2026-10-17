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

        private string GetVaapiDeviceArgs(string renderNodePath, string driver, string kernelDriver, string vendorId, string srcDeviceAlias, string alias)
        {
            alias ??= VaapiAlias;
            var haveVendorId = !string.IsNullOrEmpty(vendorId)
                && _mediaEncoder.EncoderVersion >= _minFFmpegVaapiDeviceVendorId;

            // Priority: 'renderNodePath' > 'vendorId' > 'kernelDriver'
            var driverOpts = File.Exists(renderNodePath)
                ? renderNodePath
                : (haveVendorId ? $",vendor_id={vendorId}" : (string.IsNullOrEmpty(kernelDriver) ? string.Empty : $",kernel_driver={kernelDriver}"));

            // 'driver' behaves similarly to env LIBVA_DRIVER_NAME
            driverOpts += string.IsNullOrEmpty(driver) ? string.Empty : ",driver=" + driver;

            var options = string.IsNullOrEmpty(srcDeviceAlias)
                ? (string.IsNullOrEmpty(driverOpts) ? string.Empty : ":" + driverOpts)
                : "@" + srcDeviceAlias;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device vaapi={0}{1}",
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
                return GetVaapiDeviceArgs(renderNodePath, "iHD", "i915", "0x8086", null, VaapiAlias) + arg + "@" + VaapiAlias;
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

        private string GetFilterHwDeviceArgs(string alias)
        {
            return string.IsNullOrEmpty(alias)
                ? string.Empty
                : " -filter_hw_device " + alias;
        }

        public string GetGraphicalSubCanvasSize(EncodingJobInfo state)
        {
            // DVBSUB uses the fixed canvas size 720x576
            if (IsEncodedNonDvbGraphicalSubtitle(state))
            {
                var subtitleWidth = state.SubtitleStream?.Width;
                var subtitleHeight = state.SubtitleStream?.Height;

                if (HasPositiveSize(subtitleWidth, subtitleHeight))
                {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        " -canvas_size {0}x{1}",
                        subtitleWidth.Value,
                        subtitleHeight.Value);
                }
            }

            return string.Empty;
        }

        private static bool IsEncodedNonDvbGraphicalSubtitle(EncodingJobInfo state)
        {
            return state.SubtitleStream is not null
                && ShouldEncodeSubtitle(state)
                && !state.SubtitleStream.IsTextSubtitleStream
                && !string.Equals(state.SubtitleStream.Codec, "DVBSUB", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the input video hwaccel argument.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <returns>Input video hwaccel arguments.</returns>
        public string GetInputVideoHwaccelArgs(EncodingJobInfo state, EncodingOptions options)
        {
            if (!state.IsVideoRequest)
            {
                return string.Empty;
            }

            var vidEncoder = GetVideoEncoder(state, options) ?? string.Empty;
            if (IsCopyCodec(vidEncoder))
            {
                return string.Empty;
            }

            var args = new StringBuilder();
            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();
            var isMacOS = OperatingSystem.IsMacOS();
            var optHwaccelType = options.HardwareAccelerationType;
            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isHwTonemapAvailable = IsHwTonemapAvailable(state, options);

            if (optHwaccelType == HardwareAccelerationType.vaapi)
            {
                if (!isLinux || !_mediaEncoder.SupportsHwaccel("vaapi"))
                {
                    return string.Empty;
                }

                var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                var isVaapiEncoder = vidEncoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                if (!isVaapiDecoder && !isVaapiEncoder)
                {
                    return string.Empty;
                }

                AppendVaapiHwDeviceArgs(args, options, isVaapiDecoder, isHwTonemapAvailable);
            }
            else if (optHwaccelType == HardwareAccelerationType.qsv)
            {
                if (IsQsvHwaccelUnsupported(isLinux, isWindows))
                {
                    return string.Empty;
                }

                var isD3d11vaDecoder = vidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
                var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                var isQsvDecoder = vidDecoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
                var isQsvEncoder = vidEncoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
                var isHwDecoder = isQsvDecoder || isVaapiDecoder || isD3d11vaDecoder;
                if (!isHwDecoder && !isQsvEncoder)
                {
                    return string.Empty;
                }

                AppendQsvHwDeviceArgs(args, options, isLinux, isHwDecoder, isHwTonemapAvailable);
            }
            else if (optHwaccelType == HardwareAccelerationType.nvenc)
            {
                if (IsNvencHwaccelUnsupported(isLinux, isWindows))
                {
                    return string.Empty;
                }

                var isCuvidDecoder = vidDecoder.Contains("cuvid", StringComparison.OrdinalIgnoreCase);
                var isNvdecDecoder = vidDecoder.Contains("cuda", StringComparison.OrdinalIgnoreCase);
                var isNvencEncoder = vidEncoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
                var isHwDecoder = isNvdecDecoder || isCuvidDecoder;
                if (!isHwDecoder && !isNvencEncoder)
                {
                    return string.Empty;
                }

                args.Append(GetCudaDeviceArgs(0, CudaAlias))
                     .Append(GetFilterHwDeviceArgs(CudaAlias));
            }
            else if (optHwaccelType == HardwareAccelerationType.amf)
            {
                if (!isWindows || !_mediaEncoder.SupportsHwaccel("d3d11va"))
                {
                    return string.Empty;
                }

                var isD3d11vaDecoder = vidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
                var isAmfEncoder = vidEncoder.Contains("amf", StringComparison.OrdinalIgnoreCase);
                if (!isD3d11vaDecoder && !isAmfEncoder)
                {
                    return string.Empty;
                }

                AppendAmfHwDeviceArgs(args);
            }
            else if (optHwaccelType == HardwareAccelerationType.videotoolbox)
            {
                if (!isMacOS || !_mediaEncoder.SupportsHwaccel("videotoolbox"))
                {
                    return string.Empty;
                }

                var isVideotoolboxDecoder = vidDecoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
                var isVideotoolboxEncoder = vidEncoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
                if (!isVideotoolboxDecoder && !isVideotoolboxEncoder)
                {
                    return string.Empty;
                }

                // videotoolbox hw filter does not require device selection
                args.Append(GetVideoToolboxDeviceArgs(VideotoolboxAlias));
            }
            else if (optHwaccelType == HardwareAccelerationType.rkmpp)
            {
                if (!isLinux || !_mediaEncoder.SupportsHwaccel("rkmpp"))
                {
                    return string.Empty;
                }

                var isRkmppDecoder = vidDecoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
                var isRkmppEncoder = vidEncoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
                if (!isRkmppDecoder && !isRkmppEncoder)
                {
                    return string.Empty;
                }

                AppendRkmppHwDeviceArgs(args, isRkmppDecoder, isHwTonemapAvailable);
            }

            if (!string.IsNullOrEmpty(vidDecoder))
            {
                args.Append(vidDecoder);
            }

            return args.ToString().Trim();
        }

        private bool IsQsvHwaccelUnsupported(bool isLinux, bool isWindows)
        {
            return (!isLinux && !isWindows) || !_mediaEncoder.SupportsHwaccel("qsv");
        }

        private bool IsNvencHwaccelUnsupported(bool isLinux, bool isWindows)
        {
            return (!isLinux && !isWindows) || !IsCudaFullSupported();
        }

        private void AppendVaapiHwDeviceArgs(StringBuilder args, EncodingOptions options, bool isVaapiDecoder, bool isHwTonemapAvailable)
        {
            if (_mediaEncoder.IsVaapiDeviceInteliHD)
            {
                args.Append(GetVaapiDeviceArgs(options.VaapiDevice, "iHD", null, null, null, VaapiAlias));
            }
            else if (_mediaEncoder.IsVaapiDeviceInteli965)
            {
                // Only override i965 since it has lower priority than iHD in libva lookup.
                Environment.SetEnvironmentVariable("LIBVA_DRIVER_NAME", "i965");
                Environment.SetEnvironmentVariable("LIBVA_DRIVER_NAME_JELLYFIN", "i965");
                args.Append(GetVaapiDeviceArgs(options.VaapiDevice, "i965", null, null, null, VaapiAlias));
            }

            var filterDevArgs = string.Empty;
            var doOclTonemap = isHwTonemapAvailable && IsOpenclFullSupported();

            if (_mediaEncoder.IsVaapiDeviceInteliHD || _mediaEncoder.IsVaapiDeviceInteli965)
            {
                if (doOclTonemap && !isVaapiDecoder)
                {
                    args.Append(GetOpenclDeviceArgs(0, null, VaapiAlias, OpenclAlias));
                    filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                }
            }
            else if (_mediaEncoder.IsVaapiDeviceAmd)
            {
                // Disable AMD EFC feature since it's still unstable in upstream Mesa.
                Environment.SetEnvironmentVariable("AMD_DEBUG", "noefc");

                if (IsAmdVulkanDrmInteropUsable())
                {
                    args.Append(GetDrmDeviceArgs(options.VaapiDevice, DrmAlias));
                    args.Append(GetVaapiDeviceArgs(null, null, null, null, DrmAlias, VaapiAlias));
                    args.Append(GetVulkanDeviceArgs(0, null, DrmAlias, VulkanAlias));

                    // libplacebo wants an explicitly set vulkan filter device.
                    filterDevArgs = GetFilterHwDeviceArgs(VulkanAlias);
                }
                else
                {
                    args.Append(GetVaapiDeviceArgs(options.VaapiDevice, null, null, null, null, VaapiAlias));
                    filterDevArgs = GetFilterHwDeviceArgs(VaapiAlias);

                    if (doOclTonemap)
                    {
                        // ROCm/ROCr OpenCL runtime
                        args.Append(GetOpenclDeviceArgs(0, "Advanced Micro Devices", null, OpenclAlias));
                        filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                    }
                }
            }
            else if (doOclTonemap)
            {
                args.Append(GetOpenclDeviceArgs(0, null, null, OpenclAlias));
                filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
            }

            args.Append(filterDevArgs);
        }

        private bool IsAmdVulkanDrmInteropUsable()
        {
            return IsVulkanFullSupported()
                && _mediaEncoder.IsVaapiDeviceSupportVulkanDrmInterop
                && Environment.OSVersion.Version >= _minKernelVersionAmdVkFmtModifier;
        }

        private void AppendQsvHwDeviceArgs(StringBuilder args, EncodingOptions options, bool isLinux, bool isHwDecoder, bool isHwTonemapAvailable)
        {
            args.Append(GetQsvDeviceArgs(options.QsvDevice, QsvAlias));
            var filterDevArgs = GetFilterHwDeviceArgs(QsvAlias);
            // child device used by qsv.
            if (_mediaEncoder.SupportsHwaccel("vaapi") || _mediaEncoder.SupportsHwaccel("d3d11va"))
            {
                if (isHwTonemapAvailable && IsOpenclFullSupported())
                {
                    var srcAlias = isLinux ? VaapiAlias : D3d11vaAlias;
                    args.Append(GetOpenclDeviceArgs(0, null, srcAlias, OpenclAlias));
                    if (!isHwDecoder)
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
    }
}
