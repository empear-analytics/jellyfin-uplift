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
        /// <summary>
        /// Gets the parameter of Intel/AMD VAAPI filter chain.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="vidEncoder">Video encoder to use.</param>
        /// <returns>The tuple contains three lists: main, sub and overlay filters.</returns>
        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetVaapiVidFilterChain(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidEncoder)
        {
            if (options.HardwareAccelerationType != HardwareAccelerationType.vaapi)
            {
                return (null, null, null);
            }

            var isLinux = OperatingSystem.IsLinux();
            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !vidEncoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isVaapiFullSupported = IsVaapiFullSupportedOnLinux(state, isLinux);
            var isVaapiOclSupported = IsVaapiFullAndOpenclSupported(isVaapiFullSupported);
            var isVaapiVkSupported = IsVaapiFullAndVulkanSupported(isVaapiFullSupported);

            // legacy vaapi pipeline(copy-back)
            if (UseLegacyHwPipeline(isSwDecoder, isSwEncoder, isVaapiOclSupported))
            {
                var swFilterChain = GetSwVidFilterChain(state, options, vidEncoder);

                if (!isSwEncoder)
                {
                    var newfilters = new List<string>();
                    var noOverlay = swFilterChain.OverlayFilters.Count == 0;
                    newfilters.AddRange(noOverlay ? swFilterChain.MainFilters : swFilterChain.OverlayFilters);
                    newfilters.Add("hwupload=derive_device=vaapi");

                    var mainFilters = noOverlay ? newfilters : swFilterChain.MainFilters;
                    var overlayFilters = noOverlay ? swFilterChain.OverlayFilters : newfilters;
                    return (mainFilters, swFilterChain.SubFilters, overlayFilters);
                }

                return swFilterChain;
            }

            // preferred vaapi + opencl filters pipeline
            if (_mediaEncoder.IsVaapiDeviceInteliHD)
            {
                // Intel iHD path, with extra vpp tonemap and overlay support.
                return GetIntelVaapiFullVidFiltersPrefered(state, options, vidDecoder, vidEncoder);
            }

            // preferred vaapi + vulkan filters pipeline
            if (IsAmdVaapiVulkanPipelineUsable(isVaapiVkSupported))
            {
                // AMD radeonsi path(targeting Polaris/gfx8+), with extra vulkan tonemap and overlay support.
                return GetAmdVaapiFullVidFiltersPrefered(state, options, vidDecoder, vidEncoder);
            }

            // Intel i965 and Amd legacy driver path, only featuring scale and deinterlace support.
            return GetVaapiLimitedVidFiltersPrefered(state, options, vidDecoder, vidEncoder);
        }

        private bool IsVaapiFullAndOpenclSupported(bool isVaapiFullSupported)
        {
            return isVaapiFullSupported && IsOpenclFullSupported();
        }

        private bool IsVaapiFullAndVulkanSupported(bool isVaapiFullSupported)
        {
            return isVaapiFullSupported && IsVulkanFullSupported();
        }

        private bool UseLegacyHwPipeline(bool isSwDecoder, bool isSwEncoder, bool isHwOclSupported)
        {
            return (isSwDecoder && isSwEncoder)
                || !isHwOclSupported
                || !_mediaEncoder.SupportsFilter("alphasrc");
        }

        private bool IsAmdVaapiVulkanPipelineUsable(bool isVaapiVkSupported)
        {
            return _mediaEncoder.IsVaapiDeviceAmd
                && isVaapiVkSupported
                && _mediaEncoder.IsVaapiDeviceSupportVulkanDrmInterop
                && Environment.OSVersion.Version >= _minKernelVersionAmdVkFmtModifier;
        }

        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetIntelVaapiFullVidFiltersPrefered(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidDecoder,
            string vidEncoder)
        {
            var inW = state.VideoStream?.Width;
            var inH = state.VideoStream?.Height;
            var reqW = state.BaseRequest.Width;
            var reqH = state.BaseRequest.Height;
            var reqMaxW = state.BaseRequest.MaxWidth;
            var reqMaxH = state.BaseRequest.MaxHeight;
            var threeDFormat = state.MediaSource.Video3DFormat;

            var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isVaapiEncoder = vidEncoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !isVaapiEncoder;
            var isMjpegEncoder = vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);
            var isVaInVaOut = isVaapiDecoder && isVaapiEncoder;

            var doVaVppTonemap = CanDoVaapiVppTonemap(state, options, isVaapiDecoder);
            var doOclTonemap = NeedsOclTonemap(state, options, doVaVppTonemap);
            var doTonemap = doVaVppTonemap || doOclTonemap;
            var doDeintH2645 = IsDeinterlaceAvailable(state);

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);
            var hasAssSubs = HasAssSubtitles(state, hasSubs);
            var subW = state.SubtitleStream?.Width;
            var subH = state.SubtitleStream?.Height;

            var rotation = state.VideoStream?.Rotation ?? 0;
            var transposeDir = rotation == 0 ? string.Empty : GetVideoTransposeDirection(state);
            var doVaVppTranspose = !string.IsNullOrEmpty(transposeDir);
            var swapWAndH = NeedsVaapiSwapWAndH(rotation, isSwDecoder, isVaapiDecoder, doVaVppTranspose);
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            /* Make main filters for video stream */
            var mainFilters = new List<string>();

            mainFilters.Add(GetOverwriteColorPropertiesParam(state, doTonemap));

            var context = new VaapiIntelFullContext(state, hasGraphicalSubs, hasTextSubs, hasAssSubs, swpInW, swpInH, subW, subH, reqW, reqH, reqMaxW, reqMaxH);

            VaapiDecoderIntelFilters(context, new VaapiDecoderIntelContext(isSwDecoder, isVaapiDecoder, doOclTonemap, doTonemap, isMjpegEncoder, transposeDir, doVaVppTranspose, vidEncoder, threeDFormat, doDeintH2645), options, mainFilters);

            // vaapi vpp tonemap
            if (NeedsHwVaapiVppTonemap(doVaVppTonemap, isVaapiDecoder))
            {
                var tonemapFilter = GetHwTonemapFilter(options, "vaapi", "nv12", isMjpegEncoder);
                mainFilters.Add(tonemapFilter);
            }

            AddVaapiToOpenclMapping(mainFilters, doOclTonemap, isVaapiDecoder);

            // ocl tonemap
            AddOpenclTonemapFilter(options, mainFilters, doOclTonemap, isMjpegEncoder);

            AddVaapiReverseMapping(mainFilters, doOclTonemap, isVaInVaOut);

            var memoryOutput = false;
            var isUploadForOclTonemap = isSwDecoder && doOclTonemap;
            var isHwmapNotUsable = IsHwmapNotUsableForVaapi(isUploadForOclTonemap, isVaapiEncoder);
            if (NeedsMemoryOutput(isVaapiDecoder, isSwEncoder, isUploadForOclTonemap))
            {
                memoryOutput = true;

                // OUTPUT nv12 surface(memory)
                // prefer hwmap to hwdownload on opencl/vaapi.
                AddIntelMemoryOutputFilters(mainFilters, isHwmapNotUsable);
            }

            // OUTPUT nv12 surface(memory)
            memoryOutput = MarkVaapiSwUploadOutput(memoryOutput, isSwDecoder, isVaapiEncoder);

            AddVaapiMemoryOutputTail(context, mainFilters, memoryOutput, isVaapiEncoder);

            /* Make sub and overlay filters for subtitle stream */
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            if (isVaInVaOut)
            {
                AddVaapiVaInVaOutSubtitleFiltersIfAny(context, subFilters, overlayFilters, hasSubs);
            }
            else if (memoryOutput)
            {
                AddVaapiMemoryGraphicalSubtitleFiltersIfAny(context, isVaapiEncoder, subFilters, overlayFilters);
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private void VaapiDecoderIntelFilters(VaapiIntelFullContext context, VaapiDecoderIntelContext decoder, EncodingOptions options, List<string> mainFilters)
        {
            if (decoder.IsSwDecoder)
            {
                // INPUT sw surface(memory)
                // sw deint
                AddSwDeinterlaceFilter(context.State, options, mainFilters, decoder.DoDeintH2645);

                var outFormat = decoder.DoOclTonemap ? "yuv420p10le" : "nv12";
                var swScaleFilter = GetSwScaleFilter(context.State, options, decoder.VidEncoder, context.SwpInW, context.SwpInH, decoder.ThreeDFormat, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                swScaleFilter = ApplyMjpegFullRangeSwScale(swScaleFilter, decoder.IsMjpegEncoder, decoder.DoOclTonemap);

                // sw scale
                mainFilters.Add(swScaleFilter);
                mainFilters.Add($"format={outFormat}");

                // keep video at memory except ocl tonemap,
                // since the overhead caused by hwupload >>> using sw filter.
                // sw => hw
                AddOpenclHwupload(mainFilters, decoder.DoOclTonemap);
            }
            else if (decoder.IsVaapiDecoder)
            {
                var isRext = IsVideoStreamHevcRext(context.State);

                // INPUT vaapi surface(vram)
                // hw deint
                AddVaapiHwDeinterlaceFilter(context.State, options, mainFilters, decoder.DoDeintH2645);

                // hw transpose
                AddVaapiTransposeFilter(mainFilters, decoder.TransposeDir, decoder.DoVaVppTranspose);

                var outFormat = decoder.DoTonemap ? (isRext ? "p010" : string.Empty) : "nv12";
                var hwScaleFilter = GetHwScaleFilter("scale", "vaapi", outFormat, false, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);

                hwScaleFilter = ApplyVaapiMjpegScaleOptions(hwScaleFilter, decoder.IsMjpegEncoder, decoder.DoOclTonemap);

                // allocate extra pool sizes for vaapi vpp
                hwScaleFilter = ApplyVaapiExtraHwFrames(hwScaleFilter);

                // hw scale
                mainFilters.Add(hwScaleFilter);
            }
        }

        private void AddVaapiVaInVaOutSubtitleFiltersIfAny(VaapiIntelFullContext context, List<string> subFilters, List<string> overlayFilters, bool hasSubs)
        {
            if (hasSubs)
            {
                AddVaapiVaInVaOutSubtitleFilters(context, subFilters, overlayFilters);
            }
        }

        private static void AddVaapiMemoryGraphicalSubtitleFiltersIfAny(VaapiIntelFullContext context, bool isVaapiEncoder, List<string> subFilters, List<string> overlayFilters)
        {
            if (context.HasGraphicalSubs)
            {
                AddVaapiMemoryGraphicalSubtitleFilters(context, isVaapiEncoder, subFilters, overlayFilters);
            }
        }

        private static bool MarkVaapiSwUploadOutput(bool memoryOutput, bool isSwDecoder, bool isVaapiEncoder)
        {
            if (IsSwDecodeToVaapiEncode(isSwDecoder, isVaapiEncoder))
            {
                memoryOutput = true;
            }

            return memoryOutput;
        }

        private static void AddVaapiMemoryGraphicalSubtitleFilters(VaapiIntelFullContext context, bool isVaapiEncoder, List<string> subFilters, List<string> overlayFilters)
        {
            var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
            subFilters.Add(subPreProcFilters);
            overlayFilters.Add("overlay=eof_action=pass:repeatlast=0");

            if (isVaapiEncoder)
            {
                overlayFilters.Add("hwupload_vaapi");
            }
        }

        private void AddVaapiVaInVaOutSubtitleFilters(VaapiIntelFullContext context, List<string> subFilters, List<string> overlayFilters)
        {
            if (context.HasGraphicalSubs)
            {
                // overlay_vaapi can handle overlay scaling, setup a smaller height to reduce transfer overhead
                var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, 1080);
                subFilters.Add(subPreProcFilters);
                subFilters.Add("format=bgra");
            }
            else if (context.HasTextSubs)
            {
                var framerate = context.State.VideoStream?.RealFrameRate;
                var subFramerate = context.HasAssSubs ? Math.Min(framerate ?? 25, 60) : 10;

                var alphaSrcFilter = GetAlphaSrcFilter(context.State, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, 1080, subFramerate);
                var subTextSubtitlesFilter = GetTextSubtitlesFilter(context.State, true, true);
                subFilters.Add(alphaSrcFilter);
                subFilters.Add("format=bgra");
                subFilters.Add(subTextSubtitlesFilter);
            }

            subFilters.Add("hwupload=derive_device=vaapi");

            var (overlayW, overlayH) = GetFixedOutputSize(context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
            var overlaySize = GetOverlaySizeOption(overlayW, overlayH);
            AddOverlayVaapiFilter(overlayFilters, overlaySize);
        }

        private void AddVaapiMemoryOutputTail(VaapiIntelFullContext context, List<string> mainFilters, bool memoryOutput, bool isVaapiEncoder)
        {
            AddMemoryOutputTextSubtitlesFilter(context.State, mainFilters, memoryOutput, context.HasTextSubs);

            if (NeedsVaapiUploadFromMemory(memoryOutput, isVaapiEncoder))
            {
                AddVaapiHwupload(mainFilters, context.HasGraphicalSubs);
            }
        }

        private static void AddVaapiToOpenclMapping(List<string> mainFilters, bool doOclTonemap, bool isVaapiDecoder)
        {
            if (NeedsHwToOpenclMapping(doOclTonemap, isVaapiDecoder))
            {
                // map from vaapi to opencl via vaapi-opencl interop(Intel only).
                mainFilters.Add("hwmap=derive_device=opencl:mode=read");
            }
        }

        private static void AddVaapiReverseMapping(List<string> mainFilters, bool doOclTonemap, bool isVaInVaOut)
        {
            if (NeedsVaapiReverseMapping(doOclTonemap, isVaInVaOut))
            {
                // OUTPUT vaapi(nv12) surface(vram)
                // reverse-mapping via vaapi-opencl interop.
                mainFilters.Add("hwmap=derive_device=vaapi:mode=write:reverse=1");
                mainFilters.Add("format=vaapi");
            }
        }

        private static void AddVulkanToVaapiMapping(List<string> filters)
        {
            filters.Add("hwmap=derive_device=vaapi");
            filters.Add("format=vaapi");
        }

        private static void AddVulkanHwDownload(List<string> mainFilters, bool isSwEncoder, bool doVkTonemap, bool isVaapiDecoder)
        {
            if (NeedsVulkanHwDownload(isSwEncoder, doVkTonemap, isVaapiDecoder))
            {
                mainFilters.Add("hwdownload");
                mainFilters.Add("format=nv12");
            }
        }

        private static void AddVulkanTransposeIfNeeded(List<string> mainFilters, bool doVkTranspose, string transposeDir)
        {
            if (doVkTranspose)
            {
                AddVulkanTransposeFilter(mainFilters, transposeDir);
            }
        }

        private static void AddVaapiHwUploadUnlessTonemap(List<string> mainFilters, bool isSwDecoder, bool isVaapiEncoder, bool doVkTonemap)
        {
            if (NeedsVaapiHwUpload(isSwDecoder, isVaapiEncoder, doVkTonemap))
            {
                mainFilters.Add("hwupload_vaapi");
            }
        }

        private static void AddLimitedOpenclMappingIfNeeded(List<string> mainFilters, bool doOclTonemap, bool isVaapiDecoder, bool isi965Driver)
        {
            if (NeedsHwToOpenclMapping(doOclTonemap, isVaapiDecoder))
            {
                AddLimitedVaapiToOpenclMapping(mainFilters, isi965Driver);
            }
        }

        private static void AddLimitedReverseMappingIfNeeded(List<string> mainFilters, bool doOclTonemap, bool isVaInVaOut, bool isi965Driver)
        {
            if (NeedsVaapiReverseMapping(doOclTonemap, isVaInVaOut))
            {
                AddI965ReverseMapping(mainFilters, isi965Driver);
            }
        }

        private static void AddNonMjpegScaleVaapiFilter(List<string> mainFilters, bool isMjpegEncoder, EncodingOptions options)
        {
            var hwCscFilter = "scale_vaapi=format=nv12";

            hwCscFilter = ApplyNonMjpegTvOutRange(hwCscFilter, isMjpegEncoder, options);

            mainFilters.Add(hwCscFilter);
        }

        private static void AddVkTonemapScaleVaapiFilter(List<string> overlayFilters, bool doVkTonemap, EncodingOptions options)
        {
            var hwCscFilter = "scale_vaapi=format=nv12";

            hwCscFilter = ApplyVkTonemapTvOutRange(hwCscFilter, doVkTonemap, options);

            overlayFilters.Add(hwCscFilter);
        }

        private static void AddIntelMemoryOutputFilters(List<string> mainFilters, bool isHwmapNotUsable)
        {
            mainFilters.Add(isHwmapNotUsable ? "hwdownload" : "hwmap=mode=read");
            mainFilters.Add("format=nv12");
        }

        private static void AddLimitedMemoryOutputFilters(List<string> mainFilters, bool isHwmapNotUsable)
        {
            mainFilters.Add(isHwmapNotUsable ? "hwdownload" : "hwmap");
            mainFilters.Add("format=nv12");
        }

        private bool IsVaapiFullSupportedOnLinux(EncodingJobInfo state, bool isLinux)
        {
            return isLinux && IsVaapiSupported(state) && IsVaapiFullSupported();
        }

        private static void AddOverlayVaapiFilter(List<string> overlayFilters, string overlaySize)
        {
            var overlayVaapiFilter = string.Format(
                CultureInfo.InvariantCulture,
                "overlay_vaapi=eof_action=pass:repeatlast=0{0}",
                overlaySize);
            overlayFilters.Add(overlayVaapiFilter);
        }

        private static void AddVulkanHwupload(List<string> filters)
        {
            filters.Add("hwupload=derive_device=vulkan");
            filters.Add("format=vulkan");
        }

        private static void AddVulkanSwOverlayOutput(List<string> overlayFilters)
        {
            overlayFilters.Add("scale_vulkan=format=nv12");
            overlayFilters.Add("hwdownload");
            overlayFilters.Add("format=nv12");
        }

        private bool CanDoVaapiVppTonemap(EncodingJobInfo state, EncodingOptions options, bool isVaapiDecoder)
        {
            return isVaapiDecoder && IsIntelVppTonemapAvailable(state, options);
        }

        private static bool NeedsVaapiSwapWAndH(int rotation, bool isSwDecoder, bool isVaapiDecoder, bool doVaVppTranspose)
        {
            return Math.Abs(rotation) == 90 && (isSwDecoder || (isVaapiDecoder && doVaVppTranspose));
        }

        private static bool NeedsVaapiReverseMapping(bool doOclTonemap, bool isVaInVaOut)
        {
            return doOclTonemap && isVaInVaOut;
        }

        private static bool IsHwmapNotUsableForVaapi(bool isUploadForOclTonemap, bool isVaapiEncoder)
        {
            return isUploadForOclTonemap && isVaapiEncoder;
        }

        private static bool IsSwDecodeToVaapiEncode(bool isSwDecoder, bool isVaapiEncoder)
        {
            return isSwDecoder && isVaapiEncoder;
        }

        private static bool NeedsVaapiUploadFromMemory(bool memoryOutput, bool isVaapiEncoder)
        {
            return memoryOutput && isVaapiEncoder;
        }

        private void AddVaapiHwDeinterlaceFilter(EncodingJobInfo state, EncodingOptions options, List<string> mainFilters, bool doDeintH2645)
        {
            if (doDeintH2645)
            {
                var deintFilter = GetHwDeinterlaceFilter(state, options, "vaapi");
                mainFilters.Add(deintFilter);
            }
        }

        private static void AddVaapiTransposeFilter(List<string> mainFilters, string transposeDir, bool doVaVppTranspose)
        {
            if (doVaVppTranspose)
            {
                mainFilters.Add($"transpose_vaapi=dir={transposeDir}");
            }
        }

        private static string ApplyVaapiMjpegScaleOptions(string hwScaleFilter, bool isMjpegEncoder, bool doOclTonemap)
        {
            if (NeedsVppMjpegOptions(hwScaleFilter, isMjpegEncoder))
            {
                hwScaleFilter += doOclTonemap ? string.Empty : ":out_range=pc";
                hwScaleFilter += ":mode=hq";
            }

            return hwScaleFilter;
        }

        private static string ApplyVaapiExtraHwFrames(string hwScaleFilter)
        {
            if (!string.IsNullOrEmpty(hwScaleFilter))
            {
                hwScaleFilter += ":extra_hw_frames=24";
            }

            return hwScaleFilter;
        }

        private static void AddVaapiHwupload(List<string> mainFilters, bool hasGraphicalSubs)
        {
            if (!hasGraphicalSubs)
            {
                mainFilters.Add("hwupload_vaapi");
            }
        }

        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetAmdVaapiFullVidFiltersPrefered(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidDecoder,
            string vidEncoder)
        {
            var inW = state.VideoStream?.Width;
            var inH = state.VideoStream?.Height;
            var reqW = state.BaseRequest.Width;
            var reqH = state.BaseRequest.Height;
            var reqMaxW = state.BaseRequest.MaxWidth;
            var reqMaxH = state.BaseRequest.MaxHeight;
            var threeDFormat = state.MediaSource.Video3DFormat;

            var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isVaapiEncoder = vidEncoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !isVaapiEncoder;
            var isMjpegEncoder = vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);

            var doVkTonemap = IsVulkanHwTonemapAvailable(state, options);
            var doDeintH2645 = IsDeinterlaceAvailable(state);

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);
            var hasAssSubs = HasAssSubtitles(state, hasSubs);

            var rotation = state.VideoStream?.Rotation ?? 0;
            var transposeDir = rotation == 0 ? string.Empty : GetVideoTransposeDirection(state);
            var doVkTranspose = isVaapiDecoder && !string.IsNullOrEmpty(transposeDir);
            var swapWAndH = NeedsVaapiSwapWAndH(rotation, isSwDecoder, isVaapiDecoder, doVkTranspose);
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            /* Make main filters for video stream */
            var mainFilters = new List<string>();

            mainFilters.Add(GetOverwriteColorPropertiesParam(state, doVkTonemap));

            var context = new VaapiAmdFullContext(state, options, doVkTonemap, hasGraphicalSubs, hasTextSubs, hasAssSubs, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH);

            VaapiDecoderAmdFilters(context, new VaapiDecoderAmdContext(isSwDecoder, isVaapiDecoder, hasSubs, doDeintH2645, doVkTranspose, vidEncoder, threeDFormat, isMjpegEncoder, inW, inH), mainFilters);

            // vk transpose
            AddVulkanTransposeIfNeeded(mainFilters, doVkTranspose, transposeDir);

            // vk libplacebo
            if (NeedsVulkanTonemapOrSubs(doVkTonemap, hasSubs))
            {
                AddVaapiLibplaceboFilters(context, isMjpegEncoder, mainFilters);
            }

            if (IsVulkanTonemapWithoutSubs(doVkTonemap, hasSubs))
            {
                AddVaapiVulkanTonemapOutput(context, isMjpegEncoder, doDeintH2645, mainFilters);
            }

            if (!hasSubs)
            {
                // OUTPUT nv12 surface(memory)
                AddVulkanHwDownload(mainFilters, isSwEncoder, doVkTonemap, isVaapiDecoder);

                AddVaapiHwUploadUnlessTonemap(mainFilters, isSwDecoder, isVaapiEncoder, doVkTonemap);
            }

            /* Make sub and overlay filters for subtitle stream */
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            if (hasSubs)
            {
                AddVaapiAmdSubtitleSourceFilters(context, subFilters);

                AddVulkanHwupload(subFilters);

                overlayFilters.Add("overlay_vulkan=eof_action=pass:repeatlast=0");

                if (isSwEncoder)
                {
                    // OUTPUT nv12 surface(memory)
                    AddVulkanSwOverlayOutput(overlayFilters);
                }
                else if (isVaapiEncoder)
                {
                    AddVaapiVulkanOverlayOutput(context, overlayFilters, doDeintH2645);
                }
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private void VaapiDecoderAmdFilters(VaapiAmdFullContext context, VaapiDecoderAmdContext decoder, List<string> mainFilters)
        {
            if (decoder.IsSwDecoder)
            {
                // INPUT sw surface(memory)
                // sw deint
                AddSwDeinterlaceFilter(context.State, context.Options, mainFilters, decoder.DoDeintH2645);

                if (NeedsVulkanTonemapOrSubs(context.DoVkTonemap, decoder.HasSubs))
                {
                    // sw => hw
                    AddVulkanHwupload(mainFilters);
                }
                else
                {
                    AddVaapiAmdSwScaleFilters(context, decoder.VidEncoder, decoder.ThreeDFormat, mainFilters);
                }
            }
            else
            {
                VaapiDecoderAmdHwFilters(context, decoder, mainFilters);
            }
        }

        private void VaapiDecoderAmdHwFilters(VaapiAmdFullContext context, VaapiDecoderAmdContext decoder, List<string> mainFilters)
        {
            if (decoder.IsVaapiDecoder)
            {
                // INPUT vaapi surface(vram)
                if (NeedsVulkanInterop(decoder.DoVkTranspose, context.DoVkTonemap, decoder.HasSubs))
                {
                    AddAmdVaapiToVulkanMapFilters(mainFilters, decoder.DoVkTranspose, context.DoVkTonemap, decoder.HasSubs);
                }
                else
                {
                    // hw deint
                    AddVaapiHwDeinterlaceFilter(context.State, context.Options, mainFilters, decoder.DoDeintH2645);

                    // hw scale
                    var hwScaleFilter = GetHwScaleFilter("scale", "vaapi", "nv12", false, decoder.InW, decoder.InH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);

                    hwScaleFilter = ApplyAmdVaapiMjpegFullRange(hwScaleFilter, decoder.IsMjpegEncoder, context.DoVkTonemap);

                    mainFilters.Add(hwScaleFilter);
                }
            }
        }

        private void AddVaapiAmdSubtitleSourceFilters(VaapiAmdFullContext context, List<string> subFilters)
        {
            if (context.HasGraphicalSubs)
            {
                var subW = context.State.SubtitleStream?.Width;
                var subH = context.State.SubtitleStream?.Height;
                var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, subW, subH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                subFilters.Add(subPreProcFilters);
                subFilters.Add("format=bgra");
            }
            else if (context.HasTextSubs)
            {
                var framerate = context.State.VideoStream?.RealFrameRate;
                var subFramerate = context.HasAssSubs ? Math.Min(framerate ?? 25, 60) : 10;

                var alphaSrcFilter = GetAlphaSrcFilter(context.State, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH, subFramerate);
                var subTextSubtitlesFilter = GetTextSubtitlesFilter(context.State, true, true);
                subFilters.Add(alphaSrcFilter);
                subFilters.Add("format=bgra");
                subFilters.Add(subTextSubtitlesFilter);
            }
        }

        private void AddVaapiVulkanOverlayOutput(VaapiAmdFullContext context, List<string> overlayFilters, bool doDeintH2645)
        {
            // OUTPUT vaapi(nv12) surface(vram)
            // map from vulkan/drm to vaapi via interop (Polaris/gfx8+).
            AddVulkanToVaapiMapping(overlayFilters);

            // clear the surf->meta_offset and output nv12
            AddVkTonemapScaleVaapiFilter(overlayFilters, context.DoVkTonemap, context.Options);

            // hw deint
            AddVaapiHwDeinterlaceFilter(context.State, context.Options, overlayFilters, doDeintH2645);
        }

        private void AddVaapiLibplaceboFilters(VaapiAmdFullContext context, bool isMjpegEncoder, List<string> mainFilters)
        {
            var libplaceboFilter = GetLibplaceboFilter(context.Options, new GetLibplaceboFilterParts("bgra", context.DoVkTonemap, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH), isMjpegEncoder);
            mainFilters.Add(libplaceboFilter);
            mainFilters.Add("format=vulkan");
        }

        private void AddVaapiAmdSwScaleFilters(VaapiAmdFullContext context, string vidEncoder, Video3DFormat? threeDFormat, List<string> mainFilters)
        {
            // sw scale
            var swScaleFilter = GetSwScaleFilter(context.State, context.Options, vidEncoder, context.SwpInW, context.SwpInH, threeDFormat, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
            mainFilters.Add(swScaleFilter);
            mainFilters.Add("format=nv12");
        }

        private void AddVaapiVulkanTonemapOutput(VaapiAmdFullContext context, bool isMjpegEncoder, bool doDeintH2645, List<string> mainFilters)
        {
            // OUTPUT vaapi(nv12) surface(vram)
            // map from vulkan/drm to vaapi via interop (Polaris/gfx8+).
            AddVulkanToVaapiMapping(mainFilters);

            // clear the surf->meta_offset and output nv12
            AddNonMjpegScaleVaapiFilter(mainFilters, isMjpegEncoder, context.Options);

            // hw deint
            AddVaapiHwDeinterlaceFilter(context.State, context.Options, mainFilters, doDeintH2645);
        }

        private static bool NeedsVulkanTonemapOrSubs(bool doVkTonemap, bool hasSubs)
        {
            return doVkTonemap || hasSubs;
        }

        private static bool IsVulkanTonemapWithoutSubs(bool doVkTonemap, bool hasSubs)
        {
            return doVkTonemap && !hasSubs;
        }

        private static string ApplyAmdVaapiMjpegFullRange(string hwScaleFilter, bool isMjpegEncoder, bool doVkTonemap)
        {
            if (ShouldUseFullRangeMjpegScale(hwScaleFilter, isMjpegEncoder, doVkTonemap))
            {
                hwScaleFilter += ":out_range=pc:mode=hq";
            }

            return hwScaleFilter;
        }

        private static string ApplyNonMjpegTvOutRange(string hwCscFilter, bool isMjpegEncoder, EncodingOptions options)
        {
            if (!isMjpegEncoder && options.TonemappingRange != TonemappingRange.pc)
            {
                hwCscFilter += ":out_range=tv";
            }

            return hwCscFilter;
        }

        private static string ApplyVkTonemapTvOutRange(string hwCscFilter, bool doVkTonemap, EncodingOptions options)
        {
            if (ShouldOutputTvRange(doVkTonemap, options))
            {
                hwCscFilter += ":out_range=tv";
            }

            return hwCscFilter;
        }

        private static bool NeedsVulkanInterop(bool doVkTranspose, bool doVkTonemap, bool hasSubs)
        {
            return doVkTranspose || doVkTonemap || hasSubs;
        }

        private static bool ShouldUseFullRangeMjpegScale(string hwScaleFilter, bool isMjpegEncoder, bool doVkTonemap)
        {
            return !string.IsNullOrEmpty(hwScaleFilter) && isMjpegEncoder && !doVkTonemap;
        }

        private static bool NeedsVulkanHwDownload(bool isSwEncoder, bool doVkTonemap, bool isVaapiDecoder)
        {
            return isSwEncoder && (doVkTonemap || isVaapiDecoder);
        }

        private static bool NeedsVaapiHwUpload(bool isSwDecoder, bool isVaapiEncoder, bool doVkTonemap)
        {
            return isSwDecoder && isVaapiEncoder && !doVkTonemap;
        }

        private static bool ShouldOutputTvRange(bool doVkTonemap, EncodingOptions options)
        {
            return !doVkTonemap || (doVkTonemap && options.TonemappingRange != TonemappingRange.pc);
        }

        private void AddAmdVaapiToVulkanMapFilters(List<string> mainFilters, bool doVkTranspose, bool doVkTonemap, bool hasSubs)
        {
            // map from vaapi to vulkan/drm via interop (Polaris/gfx8+).
            if (_mediaEncoder.EncoderVersion >= _minFFmpegAlteredVaVkInterop)
            {
                if (doVkTranspose || !_mediaEncoder.IsVaapiDeviceSupportVulkanDrmModifier)
                {
                    // disable the indirect va-drm-vk mapping since it's no longer reliable.
                    mainFilters.Add("hwmap=derive_device=drm");
                    mainFilters.Add("format=drm_prime");
                    mainFilters.Add("hwmap=derive_device=vulkan");
                    mainFilters.Add("format=vulkan");

                    // workaround for libplacebo using the imported vulkan frame on gfx8.
                    if (!_mediaEncoder.IsVaapiDeviceSupportVulkanDrmModifier)
                    {
                        mainFilters.Add("scale_vulkan");
                    }
                }
                else if (doVkTonemap || hasSubs)
                {
                    // non ad-hoc libplacebo also accepts drm_prime direct input.
                    mainFilters.Add("hwmap=derive_device=drm");
                    mainFilters.Add("format=drm_prime");
                }
            }
            else // legacy va-vk mapping that works only in jellyfin-ffmpeg6
            {
                mainFilters.Add("hwmap=derive_device=vulkan");
                mainFilters.Add("format=vulkan");
            }
        }

        private static void AddVulkanTransposeFilter(List<string> mainFilters, string transposeDir)
        {
            if (string.Equals(transposeDir, "reversal", StringComparison.OrdinalIgnoreCase))
            {
                mainFilters.Add("flip_vulkan");
            }
            else
            {
                mainFilters.Add($"transpose_vulkan=dir={transposeDir}");
            }
        }

        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetVaapiLimitedVidFiltersPrefered(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidDecoder,
            string vidEncoder)
        {
            var inW = state.VideoStream?.Width;
            var inH = state.VideoStream?.Height;
            var reqW = state.BaseRequest.Width;
            var reqH = state.BaseRequest.Height;
            var reqMaxW = state.BaseRequest.MaxWidth;
            var reqMaxH = state.BaseRequest.MaxHeight;
            var threeDFormat = state.MediaSource.Video3DFormat;

            var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isVaapiEncoder = vidEncoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !isVaapiEncoder;
            var isMjpegEncoder = vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);
            var isVaInVaOut = isVaapiDecoder && isVaapiEncoder;
            var isi965Driver = _mediaEncoder.IsVaapiDeviceInteli965;
            var isAmdDriver = _mediaEncoder.IsVaapiDeviceAmd;

            var doDeintH2645 = IsDeinterlaceAvailable(state);
            var doOclTonemap = IsHwTonemapAvailable(state, options);

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);

            var rotation = state.VideoStream?.Rotation ?? 0;
            var swapWAndH = NeedsSwSwapWAndH(rotation, isSwDecoder);
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            /* Make main filters for video stream */
            var mainFilters = new List<string>();

            mainFilters.Add(GetOverwriteColorPropertiesParam(state, doOclTonemap));

            var hwDecoderContext = new VaapiLimitedHwDecoderContext(options, doDeintH2645, doOclTonemap, isMjpegEncoder, isVaapiDecoder, inW, inH, reqW, reqH, reqMaxW, reqMaxH, mainFilters);
            var outFormat = string.Empty;
            if (isSwDecoder)
            {
                // INPUT sw surface(memory)
                // sw deint
                AddSwDeinterlaceFilter(state, options, mainFilters, doDeintH2645);

                outFormat = doOclTonemap ? "yuv420p10le" : "nv12";
                var swScaleFilter = GetSwScaleFilter(state, options, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH);
                swScaleFilter = ApplyMjpegFullRangeSwScale(swScaleFilter, isMjpegEncoder, doOclTonemap);

                // sw scale
                mainFilters.Add(swScaleFilter);
                mainFilters.Add("format=" + outFormat);

                // keep video at memory except ocl tonemap,
                // since the overhead caused by hwupload >>> using sw filter.
                // sw => hw
                AddOpenclHwupload(mainFilters, doOclTonemap);
            }
            else
            {
                AddVaapiLimitedHwDecoderFilters(state, hwDecoderContext);
            }

            AddLimitedOpenclMappingIfNeeded(mainFilters, doOclTonemap, isVaapiDecoder, isi965Driver);

            // ocl tonemap
            AddOpenclTonemapFilter(options, mainFilters, doOclTonemap, isMjpegEncoder);

            AddLimitedReverseMappingIfNeeded(mainFilters, doOclTonemap, isVaInVaOut, isi965Driver);

            var memoryOutput = false;
            var isUploadForOclTonemap = NeedsLimitedUploadForOclTonemap(doOclTonemap, isSwDecoder, isVaapiDecoder, isi965Driver);
            var isHwmapNotUsable = hasGraphicalSubs || isUploadForOclTonemap;
            var isHwmapForSubs = NeedsHwmapForSubs(hasSubs, isVaapiDecoder);
            var isHwUnmapForTextSubs = NeedsHwUnmapForTextSubs(hasTextSubs, isVaInVaOut, isUploadForOclTonemap);
            var context = new VaapiLimitedContext(state, hasGraphicalSubs, hasTextSubs, isVaapiEncoder, isVaapiDecoder, isSwEncoder, isUploadForOclTonemap, isHwmapForSubs, isHwmapNotUsable, isHwUnmapForTextSubs);
            memoryOutput = AddVaapiLimitedMemoryOutput(context, mainFilters, memoryOutput);

            // OUTPUT nv12 surface(memory)
            memoryOutput = MarkVaapiSwUploadOutput(memoryOutput, isSwDecoder, isVaapiEncoder);

            AddVaapiLimitedSubtitleOutput(context, mainFilters, memoryOutput);

            /* Make sub and overlay filters for subtitle stream */
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            var subtitleContext = new VaapiLimitedSubtitleContext(memoryOutput, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH);
            AddVaapiLimitedSubtitleFilters(context, subtitleContext, subFilters, overlayFilters);

            return (mainFilters, subFilters, overlayFilters);
        }

        private void AddVaapiLimitedHwDecoderFilters(EncodingJobInfo state, VaapiLimitedHwDecoderContext context)
        {
            if (context.IsVaapiDecoder)
            {
                // INPUT vaapi surface(vram)
                // hw deint
                AddVaapiHwDeinterlaceFilter(state, context.Options, context.MainFilters, context.DoDeintH2645);

                var outFormat = context.DoOclTonemap ? string.Empty : "nv12";
                var hwScaleFilter = GetHwScaleFilter("scale", "vaapi", outFormat, false, context.InW, context.InH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);

                hwScaleFilter = ApplyVaapiMjpegScaleOptions(hwScaleFilter, context.IsMjpegEncoder, context.DoOclTonemap);

                // allocate extra pool sizes for vaapi vpp
                hwScaleFilter = ApplyVaapiExtraHwFrames(hwScaleFilter);

                // hw scale
                context.MainFilters.Add(hwScaleFilter);
            }
        }

        private static void AddVaapiLimitedSubtitleFilters(VaapiLimitedContext context, VaapiLimitedSubtitleContext subtitleContext, List<string> subFilters, List<string> overlayFilters)
        {
            if (subtitleContext.MemoryOutput)
            {
                if (context.HasGraphicalSubs)
                {
                    var subW = context.State.SubtitleStream?.Width;
                    var subH = context.State.SubtitleStream?.Height;
                    var subPreProcFilters = GetGraphicalSubPreProcessFilters(subtitleContext.SwpInW, subtitleContext.SwpInH, subW, subH, subtitleContext.ReqW, subtitleContext.ReqH, subtitleContext.ReqMaxW, subtitleContext.ReqMaxH);
                    subFilters.Add(subPreProcFilters);
                    overlayFilters.Add("overlay=eof_action=pass:repeatlast=0");

                    if (context.IsVaapiEncoder)
                    {
                        overlayFilters.Add("hwupload_vaapi");
                    }
                }
            }
        }

        private static bool AddVaapiLimitedMemoryOutput(VaapiLimitedContext context, List<string> mainFilters, bool memoryOutput)
        {
            if (NeedsMemoryOutput(context.IsVaapiDecoder, context.IsSwEncoder, context.IsUploadForOclTonemap) || context.IsHwmapForSubs)
            {
                memoryOutput = true;

                // OUTPUT nv12 surface(memory)
                // prefer hwmap to hwdownload on opencl/vaapi.
                AddLimitedMemoryOutputFilters(mainFilters, context.IsHwmapNotUsable);
            }

            return memoryOutput;
        }

        private void AddVaapiLimitedSubtitleOutput(VaapiLimitedContext context, List<string> mainFilters, bool memoryOutput)
        {
            AddMemoryOutputTextSubtitlesFilter(context.State, mainFilters, memoryOutput, context.HasTextSubs);

            if (context.IsHwUnmapForTextSubs)
            {
                mainFilters.Add("hwmap");
                mainFilters.Add("format=vaapi");
            }
            else if (NeedsVaapiUploadFromMemory(memoryOutput, context.IsVaapiEncoder))
            {
                AddVaapiHwupload(mainFilters, context.HasGraphicalSubs);
            }
        }

        private static bool NeedsLimitedUploadForOclTonemap(bool doOclTonemap, bool isSwDecoder, bool isVaapiDecoder, bool isi965Driver)
        {
            return doOclTonemap && (isSwDecoder || (isVaapiDecoder && !isi965Driver));
        }

        private static bool NeedsHwUnmapForTextSubs(bool hasTextSubs, bool isVaInVaOut, bool isUploadForOclTonemap)
        {
            return hasTextSubs && isVaInVaOut && !isUploadForOclTonemap;
        }

        private static bool NeedsHwmapForSubs(bool hasSubs, bool isVaapiDecoder)
        {
            return hasSubs && isVaapiDecoder;
        }

        private static bool NeedsSwSwapWAndH(int rotation, bool isSwDecoder)
        {
            return Math.Abs(rotation) == 90 && isSwDecoder;
        }

        private static void AddLimitedVaapiToOpenclMapping(List<string> mainFilters, bool isi965Driver)
        {
            if (isi965Driver)
            {
                // map from vaapi to opencl via vaapi-opencl interop(Intel only).
                mainFilters.Add("hwmap=derive_device=opencl");
            }
            else
            {
                mainFilters.Add("hwdownload");
                mainFilters.Add("format=p010le");
                mainFilters.Add("hwupload=derive_device=opencl");
            }
        }

        private static void AddI965ReverseMapping(List<string> mainFilters, bool isi965Driver)
        {
            if (isi965Driver)
            {
                // OUTPUT vaapi(nv12) surface(vram)
                // reverse-mapping via vaapi-opencl interop.
                mainFilters.Add("hwmap=derive_device=vaapi:reverse=1");
                mainFilters.Add("format=vaapi");
            }
        }

        private readonly record struct VaapiIntelFullContext(
            EncodingJobInfo State,
            bool HasGraphicalSubs,
            bool HasTextSubs,
            bool HasAssSubs,
            int? SwpInW,
            int? SwpInH,
            int? SubW,
            int? SubH,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH);

        private readonly record struct VaapiDecoderIntelContext(
            bool IsSwDecoder,
            bool IsVaapiDecoder,
            bool DoOclTonemap,
            bool DoTonemap,
            bool IsMjpegEncoder,
            string TransposeDir,
            bool DoVaVppTranspose,
            string VidEncoder,
            Video3DFormat? ThreeDFormat,
            bool DoDeintH2645);

        private readonly record struct VaapiAmdFullContext(
            EncodingJobInfo State,
            EncodingOptions Options,
            bool DoVkTonemap,
            bool HasGraphicalSubs,
            bool HasTextSubs,
            bool HasAssSubs,
            int? SwpInW,
            int? SwpInH,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH);

        private readonly record struct VaapiLimitedContext(
            EncodingJobInfo State,
            bool HasGraphicalSubs,
            bool HasTextSubs,
            bool IsVaapiEncoder,
            bool IsVaapiDecoder,
            bool IsSwEncoder,
            bool IsUploadForOclTonemap,
            bool IsHwmapForSubs,
            bool IsHwmapNotUsable,
            bool IsHwUnmapForTextSubs);

        private readonly record struct VaapiDecoderAmdContext(
            bool IsSwDecoder,
            bool IsVaapiDecoder,
            bool HasSubs,
            bool DoDeintH2645,
            bool DoVkTranspose,
            string VidEncoder,
            Video3DFormat? ThreeDFormat,
            bool IsMjpegEncoder,
            int? InW,
            int? InH);

        private readonly record struct VaapiLimitedHwDecoderContext(EncodingOptions Options, bool DoDeintH2645, bool DoOclTonemap, bool IsMjpegEncoder, bool IsVaapiDecoder, int? InW, int? InH, int? ReqW, int? ReqH, int? ReqMaxW, int? ReqMaxH, List<string> MainFilters);

        private readonly record struct VaapiLimitedSubtitleContext(bool MemoryOutput, int? SwpInW, int? SwpInH, int? ReqW, int? ReqH, int? ReqMaxW, int? ReqMaxH);
    }
}
