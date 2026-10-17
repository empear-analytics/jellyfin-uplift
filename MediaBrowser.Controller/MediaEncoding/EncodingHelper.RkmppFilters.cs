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
        /// Gets the parameter of Rockchip RKMPP/RKRGA filter chain.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="vidEncoder">Video encoder to use.</param>
        /// <returns>The tuple contains three lists: main, sub and overlay filters.</returns>
        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetRkmppVidFilterChain(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidEncoder)
        {
            if (options.HardwareAccelerationType != HardwareAccelerationType.rkmpp)
            {
                return (null, null, null);
            }

            var isLinux = OperatingSystem.IsLinux();
            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !vidEncoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
            var isRkmppOclSupported = isLinux && IsRkmppFullSupported() && IsOpenclFullSupported();

            if (UseLegacyHwPipeline(isSwDecoder, isSwEncoder, isRkmppOclSupported))
            {
                return GetSwVidFilterChain(state, options, vidEncoder);
            }

            // preferred rkmpp + rkrga + opencl filters pipeline
            if (isRkmppOclSupported)
            {
                return GetRkmppVidFiltersPrefered(state, options, vidDecoder, vidEncoder);
            }

            return (null, null, null);
        }

        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetRkmppVidFiltersPrefered(
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

            var isRkmppDecoder = vidDecoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
            var isRkmppEncoder = vidEncoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
            var isSwDecoder = !isRkmppDecoder;
            var isSwEncoder = !isRkmppEncoder;
            var isMjpegEncoder = vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);
            var isDrmInDrmOut = isRkmppDecoder && isRkmppEncoder;
            var isEncoderSupportAfbc = isRkmppEncoder
                && (vidEncoder.Contains("h264", StringComparison.OrdinalIgnoreCase)
                    || vidEncoder.Contains("hevc", StringComparison.OrdinalIgnoreCase));

            var doDeintH2645 = IsDeinterlaceAvailable(state);
            var doOclTonemap = IsHwTonemapAvailable(state, options);

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);
            var hasAssSubs = hasSubs
                && (string.Equals(state.SubtitleStream.Codec, "ass", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state.SubtitleStream.Codec, "ssa", StringComparison.OrdinalIgnoreCase));
            var subW = state.SubtitleStream?.Width;
            var subH = state.SubtitleStream?.Height;

            var rotation = state.VideoStream?.Rotation ?? 0;
            var transposeDir = rotation == 0 ? string.Empty : GetVideoTransposeDirection(state);
            var doRkVppTranspose = !string.IsNullOrEmpty(transposeDir);
            var swapWAndH = Math.Abs(rotation) == 90 && (isSwDecoder || (isRkmppDecoder && doRkVppTranspose));
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            /* Make main filters for video stream */
            var mainFilters = new List<string>();

            mainFilters.Add(GetOverwriteColorPropertiesParam(state, doOclTonemap));

            if (isSwDecoder)
            {
                // INPUT sw surface(memory)
                // sw deint
                AddSwDeinterlaceFilter(state, options, mainFilters, doDeintH2645);

                var outFormat = doOclTonemap ? "yuv420p10le" : (hasGraphicalSubs ? "yuv420p" : "nv12");
                var swScaleFilter = GetSwScaleFilter(state, options, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH);
                swScaleFilter = AppendRkmppSwScaleOptions(swScaleFilter, isMjpegEncoder, doOclTonemap);

                // sw scale
                mainFilters.Add(swScaleFilter);
                mainFilters.Add($"format={outFormat}");

                // keep video at memory except ocl tonemap,
                // since the overhead caused by hwupload >>> using sw filter.
                // sw => hw
                if (doOclTonemap)
                {
                    mainFilters.Add("hwupload=derive_device=opencl");
                }
            }
            else if (isRkmppDecoder)
            {
                // INPUT rkmpp/drm surface(gem/dma-heap)

                var isFullAfbcPipeline = isEncoderSupportAfbc && isDrmInDrmOut && !doOclTonemap;
                var swapOutputWandH = doRkVppTranspose && swapWAndH;
                var outFormat = doOclTonemap ? "p010" : "nv12";
                var hwScaleFilter = GetHwScaleFilter("vpp", "rkrga", outFormat, swapOutputWandH, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH);
                var doScaling = !string.IsNullOrEmpty(GetHwScaleFilter("vpp", "rkrga", string.Empty, swapOutputWandH, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH));

                if (NeedsRkrgaScaleFilters(hasSubs, doRkVppTranspose, isFullAfbcPipeline, doScaling))
                {
                    var isScaleRatioSupported = IsScaleRatioSupported(inW, inH, reqW, reqH, reqMaxW, reqMaxH, 8.0f);

                    // RGA3 hardware only support (1/8 ~ 8) scaling in each blit operation,
                    // but in Trickplay there's a case: (3840/320 == 12), enable 2pass for it
                    if (doScaling && !isScaleRatioSupported)
                    {
                        // Vendor provided BSP kernel has an RGA driver bug that causes the output to be corrupted for P010 format.
                        // Use NV15 instead of P010 to avoid the issue.
                        // SDR inputs are using BGRA formats already which is not affected.
                        var intermediateFormat = doOclTonemap ? "nv15" : (isMjpegEncoder ? "bgra" : outFormat);
                        var hwScaleFilterFirstPass = $"scale_rkrga=w=iw/7.9:h=ih/7.9:format={intermediateFormat}:force_original_aspect_ratio=increase:force_divisible_by=4:afbc=1";
                        mainFilters.Add(hwScaleFilterFirstPass);
                    }

                    // The RKMPP MJPEG encoder on some newer chip models no longer supports RGB input.
                    // Use 2pass here to enable RGA output of full-range YUV in the 2nd pass.
                    if (NeedsRkrgaMjpegFirstPass(isMjpegEncoder, doOclTonemap, doScaling, isScaleRatioSupported))
                    {
                        var hwScaleFilterFirstPass = "vpp_rkrga=format=bgra:afbc=1";
                        mainFilters.Add(hwScaleFilterFirstPass);
                    }

                    hwScaleFilter = AppendRkmppHwScaleOptions(hwScaleFilter, doRkVppTranspose, transposeDir, isFullAfbcPipeline);

                    // hw transpose & scale
                    mainFilters.Add(hwScaleFilter);
                }
            }

            if (NeedsHwToOpenclMapping(doOclTonemap, isRkmppDecoder))
            {
                // map from rkmpp/drm to opencl via drm-opencl interop.
                mainFilters.Add("hwmap=derive_device=opencl");
            }

            // ocl tonemap
            if (doOclTonemap)
            {
                var tonemapFilter = GetHwTonemapFilter(options, "opencl", "nv12", isMjpegEncoder);
                mainFilters.Add(tonemapFilter);
            }

            var memoryOutput = false;
            var isUploadForOclTonemap = isSwDecoder && doOclTonemap;
            if (NeedsMemoryOutput(isRkmppDecoder, isSwEncoder, isUploadForOclTonemap))
            {
                memoryOutput = true;

                // OUTPUT nv12 surface(memory)
                mainFilters.Add("hwdownload");
                mainFilters.Add("format=nv12");
            }

            // OUTPUT nv12 surface(memory)
            if (isSwDecoder && isRkmppEncoder)
            {
                memoryOutput = true;
            }

            if (memoryOutput)
            {
                AddTextSubtitlesFilter(state, mainFilters, hasTextSubs);
            }

            if (isDrmInDrmOut)
            {
                AddDrmReverseMapping(mainFilters, doOclTonemap);
            }

            /* Make sub and overlay filters for subtitle stream */
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            if (isDrmInDrmOut)
            {
                if (hasSubs)
                {
                    var subMaxH = 1080;
                    if (hasGraphicalSubs)
                    {
                        var subPreProcFilters = GetGraphicalSubPreProcessFilters(swpInW, swpInH, subW, subH, reqW, reqH, reqMaxW, subMaxH);
                        subFilters.Add(subPreProcFilters);
                        subFilters.Add("format=bgra");
                    }
                    else if (hasTextSubs)
                    {
                        var framerate = state.VideoStream?.RealFrameRate;
                        var subFramerate = hasAssSubs ? Math.Min(framerate ?? 25, 60) : 10;

                        // alphasrc=s=1280x720:r=10:start=0,format=bgra,subtitles,hwupload
                        var alphaSrcFilter = GetAlphaSrcFilter(state, swpInW, swpInH, reqW, reqH, reqMaxW, subMaxH, subFramerate);
                        var subTextSubtitlesFilter = GetTextSubtitlesFilter(state, true, true);
                        subFilters.Add(alphaSrcFilter);
                        subFilters.Add("format=bgra");
                        subFilters.Add(subTextSubtitlesFilter);
                    }

                    subFilters.Add("hwupload=derive_device=rkmpp");

                    // offload 1080p+ subtitles swscale upscaling from CPU to RGA
                    var (overlayW, overlayH) = GetFixedOutputSize(swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH);
                    if (IsOverlayTallerThan(overlayW, overlayH, subMaxH))
                    {
                        subFilters.Add($"vpp_rkrga=w={overlayW.Value}:h={overlayH.Value}:format=bgra:afbc=1");
                    }

                    // try enabling AFBC to save DDR bandwidth
                    var hwOverlayFilter = "overlay_rkrga=eof_action=pass:repeatlast=0:format=nv12";
                    if (isEncoderSupportAfbc)
                    {
                        hwOverlayFilter += ":afbc=1";
                    }

                    overlayFilters.Add(hwOverlayFilter);
                }
            }
            else if (memoryOutput)
            {
                if (hasGraphicalSubs)
                {
                    var subPreProcFilters = GetGraphicalSubPreProcessFilters(swpInW, swpInH, subW, subH, reqW, reqH, reqMaxW, reqMaxH);
                    subFilters.Add(subPreProcFilters);
                    overlayFilters.Add("overlay=eof_action=pass:repeatlast=0");
                }
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private static string AppendRkmppSwScaleOptions(string swScaleFilter, bool isMjpegEncoder, bool doOclTonemap)
        {
            if (isMjpegEncoder && !doOclTonemap)
            {
                // sw decoder + hw mjpeg encoder
                swScaleFilter = string.IsNullOrEmpty(swScaleFilter) ? "scale=out_range=pc" : $"{swScaleFilter}:out_range=pc";
            }

            if (!string.IsNullOrEmpty(swScaleFilter))
            {
                swScaleFilter += ":flags=fast_bilinear";
            }

            return swScaleFilter;
        }

        private static string AppendRkmppHwScaleOptions(string hwScaleFilter, bool doRkVppTranspose, string transposeDir, bool isFullAfbcPipeline)
        {
            if (!string.IsNullOrEmpty(hwScaleFilter) && doRkVppTranspose)
            {
                hwScaleFilter += $":transpose={transposeDir}";
            }

            // try enabling AFBC to save DDR bandwidth
            if (!string.IsNullOrEmpty(hwScaleFilter) && isFullAfbcPipeline)
            {
                hwScaleFilter += ":afbc=1";
            }

            return hwScaleFilter;
        }
    }
}
