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
        private string GetVaapiDeviceArgs(VaapiDeviceNodeParts nodeParts, string kernelDriver, string vendorId, VaapiDeviceAliasParts aliasParts)
        {
            var alias = aliasParts.Alias;
            alias ??= VaapiAlias;
            var haveVendorId = !string.IsNullOrEmpty(vendorId)
                && _mediaEncoder.EncoderVersion >= _minFFmpegVaapiDeviceVendorId;

            // Priority: 'renderNodePath' > 'vendorId' > 'kernelDriver'
            var driverOpts = File.Exists(nodeParts.RenderNodePath)
                ? nodeParts.RenderNodePath
                : (haveVendorId ? $",vendor_id={vendorId}" : (string.IsNullOrEmpty(kernelDriver) ? string.Empty : $",kernel_driver={kernelDriver}"));

            // 'driver' behaves similarly to env LIBVA_DRIVER_NAME
            driverOpts += string.IsNullOrEmpty(nodeParts.Driver) ? string.Empty : ",driver=" + nodeParts.Driver;

            var options = string.IsNullOrEmpty(aliasParts.SrcDeviceAlias)
                ? (string.IsNullOrEmpty(driverOpts) ? string.Empty : ":" + driverOpts)
                : "@" + aliasParts.SrcDeviceAlias;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device vaapi={0}{1}",
                alias,
                options);
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

            return GetHwDeviceAndDecoderArgs(state, options, vidEncoder);
        }

        private string GetHwDeviceAndDecoderArgs(EncodingJobInfo state, EncodingOptions options, string vidEncoder)
        {
            var args = new StringBuilder();
            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();
            var isMacOS = OperatingSystem.IsMacOS();
            var optHwaccelType = options.HardwareAccelerationType;
            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isHwTonemapAvailable = IsHwTonemapAvailable(state, options);

            if (optHwaccelType == HardwareAccelerationType.vaapi)
            {
                if (IsVaapiHwaccelUnsupported(isLinux))
                {
                    return string.Empty;
                }

                var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                var isVaapiEncoder = vidEncoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                if (IsHwDeviceUnused(isVaapiDecoder, isVaapiEncoder))
                {
                    return string.Empty;
                }

                AppendVaapiHwDeviceArgs(args, options, isVaapiDecoder, isHwTonemapAvailable);
            }
            else
            {
                return HwDeviceDispatchQsv(options, new HwDeviceDispatchContext(optHwaccelType, args, vidDecoder, vidEncoder, isLinux, isWindows, isMacOS, isHwTonemapAvailable));
            }

            return GetHwDeviceArgsWithDecoder(args, vidDecoder);
        }

        private static string GetHwDeviceArgsWithDecoder(StringBuilder args, string vidDecoder)
        {
            if (!string.IsNullOrEmpty(vidDecoder))
            {
                args.Append(vidDecoder);
            }

            return args.ToString().Trim();
        }

        private static bool IsHwDeviceUnused(bool isHwDecoder, bool isHwEncoder)
        {
            return !isHwDecoder && !isHwEncoder;
        }

        private bool IsVaapiHwaccelUnsupported(bool isLinux)
        {
            return !isLinux || !_mediaEncoder.SupportsHwaccel("vaapi");
        }

        private void AppendVaapiHwDeviceArgs(StringBuilder args, EncodingOptions options, bool isVaapiDecoder, bool isHwTonemapAvailable)
        {
            if (_mediaEncoder.IsVaapiDeviceInteliHD)
            {
                args.Append(GetVaapiDeviceArgs(new VaapiDeviceNodeParts(options.VaapiDevice, "iHD"), null, null, new VaapiDeviceAliasParts(null, VaapiAlias)));
            }
            else if (_mediaEncoder.IsVaapiDeviceInteli965)
            {
                // Only override i965 since it has lower priority than iHD in libva lookup.
                Environment.SetEnvironmentVariable("LIBVA_DRIVER_NAME", "i965");
                Environment.SetEnvironmentVariable("LIBVA_DRIVER_NAME_JELLYFIN", "i965");
                args.Append(GetVaapiDeviceArgs(new VaapiDeviceNodeParts(options.VaapiDevice, "i965"), null, null, new VaapiDeviceAliasParts(null, VaapiAlias)));
            }

            var filterDevArgs = string.Empty;
            var doOclTonemap = isHwTonemapAvailable && IsOpenclFullSupported();

            if (_mediaEncoder.IsVaapiDeviceInteliHD || _mediaEncoder.IsVaapiDeviceInteli965)
            {
                filterDevArgs = AppendVaapiIntelOpenclDeviceArgs(args, doOclTonemap, isVaapiDecoder, filterDevArgs);
            }
            else if (_mediaEncoder.IsVaapiDeviceAmd)
            {
                filterDevArgs = AppendVaapiAmdHwDeviceArgs(args, options, doOclTonemap);
            }
            else if (doOclTonemap)
            {
                args.Append(GetOpenclDeviceArgs(0, null, null, OpenclAlias));
                filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
            }

            args.Append(filterDevArgs);
        }

        private readonly record struct HwDeviceDispatchContext(
            HardwareAccelerationType OptHwaccelType,
            StringBuilder Args,
            string VidDecoder,
            string VidEncoder,
            bool IsLinux,
            bool IsWindows,
            bool IsMacOS,
            bool IsHwTonemapAvailable);

        private readonly record struct VaapiDeviceNodeParts(string RenderNodePath, string Driver);

        private readonly record struct VaapiDeviceAliasParts(string SrcDeviceAlias, string Alias);
    }
}
