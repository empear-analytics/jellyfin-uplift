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
        /// Gets the parameter of Intel QSV filter chain.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="vidEncoder">Video encoder to use.</param>
        /// <returns>The tuple contains three lists: main, sub and overlay filters.</returns>
        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetIntelVidFilterChain(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidEncoder)
        {
            if (options.HardwareAccelerationType != HardwareAccelerationType.qsv)
            {
                return (null, null, null);
            }

            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();
            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !vidEncoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
            var isQsvOclSupported = IsQsvOclSupported();
            var isIntelDx11OclSupported = IsIntelDx11OclSupported(isWindows, isQsvOclSupported);
            var isIntelVaapiOclSupported = IsIntelVaapiOclSupported(state, isLinux, isQsvOclSupported);

            // legacy qsv pipeline(copy-back)
            if (UseLegacyQsvPipeline(isSwDecoder, isSwEncoder, isIntelVaapiOclSupported, isIntelDx11OclSupported))
            {
                return GetSwVidFilterChain(state, options, vidEncoder);
            }

            // preferred qsv(vaapi) + opencl filters pipeline
            if (isIntelVaapiOclSupported)
            {
                return GetIntelQsvVaapiVidFiltersPrefered(state, options, vidDecoder, vidEncoder);
            }

            // preferred qsv(d3d11) + opencl filters pipeline
            if (isIntelDx11OclSupported)
            {
                return GetIntelQsvDx11VidFiltersPrefered(state, options, vidDecoder, vidEncoder);
            }

            return (null, null, null);
        }

        private bool UseLegacyQsvPipeline(bool isSwDecoder, bool isSwEncoder, bool isIntelVaapiOclSupported, bool isIntelDx11OclSupported)
        {
            return (isSwDecoder && isSwEncoder)
                || (!isIntelVaapiOclSupported && !isIntelDx11OclSupported)
                || !_mediaEncoder.SupportsFilter("alphasrc");
        }

        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetIntelQsvDx11VidFiltersPrefered(
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

            var isD3d11vaDecoder = vidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
            var isQsvDecoder = vidDecoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
            var isQsvEncoder = vidEncoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
            var isHwDecoder = isD3d11vaDecoder || isQsvDecoder;
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !isQsvEncoder;
            var isMjpegEncoder = vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);
            var isQsvInQsvOut = isHwDecoder && isQsvEncoder;

            var doDeintH2645 = IsDeinterlaceAvailable(state);
            var doVppTonemap = IsIntelVppTonemapAvailable(state, options);
            var doOclTonemap = NeedsOclTonemap(state, options, doVppTonemap);
            var doTonemap = doVppTonemap || doOclTonemap;

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);
            var hasAssSubs = HasAssSubtitles(state, hasSubs);
            var subW = state.SubtitleStream?.Width;
            var subH = state.SubtitleStream?.Height;

            var rotation = state.VideoStream?.Rotation ?? 0;
            var transposeDir = GetTransposeDirection(state, rotation);
            var doVppTranspose = !string.IsNullOrEmpty(transposeDir);
            var swapWAndH = Math.Abs(rotation) == 90 && IsSwOrTransposedHwDecode(isSwDecoder, isD3d11vaDecoder, isQsvDecoder, doVppTranspose);
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            /* Make main filters for video stream */
            var mainFilters = new List<string>();

            mainFilters.Add(GetOverwriteColorPropertiesParam(state, doTonemap));

            if (isSwDecoder)
            {
                // INPUT sw surface(memory)
                // sw deint
                AddSwDeinterlaceFilter(state, options, mainFilters, doDeintH2645);

                var outFormat = GetSwDecoderOutputFormat(doOclTonemap, hasGraphicalSubs);
                var swScaleFilter = GetSwScaleFilter(state, options, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH);
                swScaleFilter = ApplyMjpegFullRangeSwScale(swScaleFilter, isMjpegEncoder, doOclTonemap);

                // sw scale
                mainFilters.Add(swScaleFilter);
                mainFilters.Add($"format={outFormat}");

                // keep video at memory except ocl tonemap,
                // since the overhead caused by hwupload >>> using sw filter.
                // sw => hw
                AddOpenclHwupload(mainFilters, doOclTonemap);
            }
            else if (isD3d11vaDecoder || isQsvDecoder)
            {
                var isRext = IsVideoStreamHevcRext(state);
                var twoPassVppTonemap = false;
                var doVppFullRangeOut = CanDoQsvVppFullRangeOut(isMjpegEncoder);
                var doVppScaleModeHq = CanDoQsvVppScaleModeHq(isMjpegEncoder);
                var doVppProcamp = false;
                var procampParams = string.Empty;
                var procampParamsString = string.Empty;
                if (doVppTonemap)
                {
                    if (isRext)
                    {
                        // VPP tonemap requires p010 input
                        twoPassVppTonemap = true;
                    }

                    if (IsVppTonemappingBrightnessInRange(options))
                    {
                        procampParamsString += ":brightness={0}";
                        twoPassVppTonemap = doVppProcamp = true;
                    }

                    if (IsVppTonemappingContrastInRange(options))
                    {
                        procampParamsString += ":contrast={1}";
                        twoPassVppTonemap = doVppProcamp = true;
                    }

                    if (doVppProcamp)
                    {
                        procampParamsString += ":procamp=1:async_depth=2";
                        procampParams = string.Format(
                            CultureInfo.InvariantCulture,
                            procampParamsString,
                            options.VppTonemappingBrightness,
                            options.VppTonemappingContrast);
                    }
                }

                var outFormat = GetVppOutputFormat(doOclTonemap, doVppTranspose, isRext, twoPassVppTonemap);

                var swapOutputWandH = doVppTranspose && swapWAndH;
                var hwScaleFilter = GetHwScaleFilter("vpp", "qsv", outFormat, swapOutputWandH, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH);

                // d3d11va doesn't support dynamic pool size, use vpp filter ctx to relay
                // to prevent encoder async and bframes from exhausting the decoder pool.
                hwScaleFilter = AppendVppPassthroughOption(hwScaleFilter, isD3d11vaDecoder);

                hwScaleFilter = AppendVppTransposeOption(hwScaleFilter, doVppTranspose, transposeDir);

                if (NeedsVppMjpegOptions(hwScaleFilter, isMjpegEncoder))
                {
                    hwScaleFilter += GetVppFullRangeOption(doVppFullRangeOut, doOclTonemap);
                    hwScaleFilter += doVppScaleModeHq ? ":scale_mode=hq" : string.Empty;
                }

                if (NeedsVppTonemapOptions(hwScaleFilter, doVppTonemap))
                {
                    hwScaleFilter += GetVppTonemapOption(doVppProcamp, procampParams, twoPassVppTonemap);
                }

                IntelSecondAddD3d11vaToQsvHwmapIfNeeded(mainFilters, hwScaleFilter, doDeintH2645, isD3d11vaDecoder);

                // hw deint
                AddQsvHwDeinterlaceFilter(state, options, mainFilters, doDeintH2645);

                // hw transpose & scale & tonemap(w/o procamp)
                mainFilters.Add(hwScaleFilter);

                // hw tonemap(w/ procamp)
                AddTwoPassVppTonemapFilter(mainFilters, doVppTonemap, twoPassVppTonemap);

                // force bt709 just in case vpp tonemap is not triggered or using MSDK instead of VPL.
                AddBt709OverwriteFilter(state, mainFilters, doVppTonemap);
            }

            AddQsvToOpenclMapping(mainFilters, doOclTonemap, isHwDecoder);

            // hw tonemap
            AddOpenclTonemapFilter(options, mainFilters, doOclTonemap, isMjpegEncoder);

            var memoryOutput = false;
            var isUploadForOclTonemap = isSwDecoder && doOclTonemap;
            var isHwmapUsable = isSwEncoder && doOclTonemap;
            if (NeedsMemoryOutput(isHwDecoder, isSwEncoder, isUploadForOclTonemap))
            {
                memoryOutput = true;

                // OUTPUT nv12 surface(memory)
                // prefer hwmap to hwdownload on opencl.
                // qsv hwmap is not fully implemented for the time being.
                mainFilters.Add(isHwmapUsable ? "hwmap=mode=read" : "hwdownload");
                mainFilters.Add("format=nv12");
            }

            // OUTPUT nv12 surface(memory)
            if (IsSwDecodeToQsvEncode(isSwDecoder, isQsvEncoder))
            {
                memoryOutput = true;
            }

            AddMemoryOutputTextSubtitlesFilter(state, mainFilters, memoryOutput, hasTextSubs);

            AddQsvReverseMapping(mainFilters, isQsvInQsvOut, doOclTonemap);

            /* Make sub and overlay filters for subtitle stream */
            var context = new IntelQsvDx11FilterContext(
                hasSubs,
                hasGraphicalSubs,
                hasTextSubs,
                hasAssSubs,
                swpInW,
                swpInH,
                subW,
                subH,
                reqW,
                reqH,
                reqMaxW,
                reqMaxH);

            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            if (isQsvInQsvOut)
            {
                AddIntelQsvDx11QsvOutSubtitleFilters(state, subFilters, overlayFilters, context);
            }
            else if (memoryOutput)
            {
                AddIntelQsvDx11MemorySubtitleFilters(subFilters, overlayFilters, context);
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private static void IntelSecondAddD3d11vaToQsvHwmapIfNeeded(List<string> mainFilters, string hwScaleFilter, bool doDeintH2645, bool isD3d11vaDecoder)
        {
            if (isD3d11vaDecoder)
            {
                AddD3d11vaToQsvHwmap(mainFilters, hwScaleFilter, doDeintH2645);
            }
        }

        private void AddIntelQsvDx11QsvOutSubtitleFilters(EncodingJobInfo state, List<string> subFilters, List<string> overlayFilters, IntelQsvDx11FilterContext context)
        {
            if (context.HasSubs)
            {
                if (context.HasGraphicalSubs)
                {
                    // overlay_qsv can handle overlay scaling, setup a smaller height to reduce transfer overhead
                    var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, 1080);
                    subFilters.Add(subPreProcFilters);
                    subFilters.Add("format=bgra");
                }
                else if (context.HasTextSubs)
                {
                    var framerate = state.VideoStream?.RealFrameRate;
                    var subFramerate = GetSubtitleFramerate(context.HasAssSubs, framerate);

                    // alphasrc=s=1280x720:r=10:start=0,format=bgra,subtitles,hwupload
                    var alphaSrcFilter = GetAlphaSrcFilter(state, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, 1080, subFramerate);
                    var subTextSubtitlesFilter = GetTextSubtitlesFilter(state, true, true);
                    subFilters.Add(alphaSrcFilter);
                    subFilters.Add("format=bgra");
                    subFilters.Add(subTextSubtitlesFilter);
                }

                // qsv requires a fixed pool size.
                // default to 64 otherwise it will fail on certain iGPU.
                subFilters.Add("hwupload=derive_device=qsv:extra_hw_frames=64");

                var (overlayW, overlayH) = GetFixedOutputSize(context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                var overlaySize = GetOverlaySizeOption(overlayW, overlayH);
                var overlayQsvFilter = string.Format(
                    CultureInfo.InvariantCulture,
                    "overlay_qsv=eof_action=pass:repeatlast=0{0}",
                    overlaySize);
                overlayFilters.Add(overlayQsvFilter);
            }
        }

        private static void AddIntelQsvDx11MemorySubtitleFilters(List<string> subFilters, List<string> overlayFilters, IntelQsvDx11FilterContext context)
        {
            if (context.HasGraphicalSubs)
            {
                var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                subFilters.Add(subPreProcFilters);
                overlayFilters.Add("overlay=eof_action=pass:repeatlast=0");
            }
        }

        private static string GetSwDecoderOutputFormat(bool doOclTonemap, bool hasGraphicalSubs)
        {
            return doOclTonemap ? "yuv420p10le" : (hasGraphicalSubs ? "yuv420p" : "nv12");
        }

        private static string GetVppOutputFormat(bool doOclTonemap, bool doVppTranspose, bool isRext, bool twoPassVppTonemap)
        {
            var outFormat = doOclTonemap ? ((doVppTranspose || isRext) ? "p010" : string.Empty) : "nv12";
            outFormat = twoPassVppTonemap ? "p010" : outFormat;
            return outFormat;
        }

        private static string GetVppFullRangeOption(bool doVppFullRangeOut, bool doOclTonemap)
        {
            return (doVppFullRangeOut && !doOclTonemap) ? ":out_range=pc" : string.Empty;
        }

        private static string GetVppTonemapOption(bool doVppProcamp, string procampParams, bool twoPassVppTonemap)
        {
            return doVppProcamp ? procampParams : (twoPassVppTonemap ? string.Empty : ":tonemap=1");
        }

        private static float GetSubtitleFramerate(bool hasAssSubs, float? framerate)
        {
            return hasAssSubs ? Math.Min(framerate ?? 25, 60) : 10;
        }

        private static string GetOverlaySizeOption(int? overlayW, int? overlayH)
        {
            return HasFixedOverlaySize(overlayW, overlayH)
                ? $":w={overlayW.Value}:h={overlayH.Value}"
                : string.Empty;
        }

        private string GetTransposeDirection(EncodingJobInfo state, int rotation)
        {
            return rotation == 0 ? string.Empty : GetVideoTransposeDirection(state);
        }

        private void AddQsvHwDeinterlaceFilter(EncodingJobInfo state, EncodingOptions options, List<string> mainFilters, bool doDeintH2645)
        {
            if (doDeintH2645)
            {
                var deintFilter = GetHwDeinterlaceFilter(state, options, "qsv");
                mainFilters.Add(deintFilter);
            }
        }

        private void AddOpenclTonemapFilter(EncodingOptions options, List<string> mainFilters, bool doOclTonemap, bool isMjpegEncoder)
        {
            if (doOclTonemap)
            {
                var tonemapFilter = GetHwTonemapFilter(options, "opencl", "nv12", isMjpegEncoder);
                mainFilters.Add(tonemapFilter);
            }
        }

        private static void AddQsvToOpenclMapping(List<string> mainFilters, bool doOclTonemap, bool isHwDecoder)
        {
            if (NeedsHwToOpenclMapping(doOclTonemap, isHwDecoder))
            {
                // map from qsv to opencl via qsv(d3d11)-opencl interop.
                mainFilters.Add("hwmap=derive_device=opencl:mode=read");
            }
        }

        private static void AddQsvReverseMapping(List<string> mainFilters, bool isQsvInQsvOut, bool doOclTonemap)
        {
            if (NeedsQsvReverseMapping(isQsvInQsvOut, doOclTonemap))
            {
                // OUTPUT qsv(nv12) surface(vram)
                // reverse-mapping via qsv(d3d11)-opencl interop.
                mainFilters.Add("hwmap=derive_device=qsv:mode=write:reverse=1");
                mainFilters.Add("format=qsv");
            }
        }

        private static string AppendVppPassthroughOption(string hwScaleFilter, bool isD3d11vaDecoder)
        {
            if (NeedsVppPassthrough(hwScaleFilter, isD3d11vaDecoder))
            {
                hwScaleFilter += ":passthrough=0";
            }

            return hwScaleFilter;
        }

        private static string AppendVppTransposeOption(string hwScaleFilter, bool doVppTranspose, string transposeDir)
        {
            if (NeedsVppTransposeOption(hwScaleFilter, doVppTranspose))
            {
                hwScaleFilter += $":transpose={transposeDir}";
            }

            return hwScaleFilter;
        }

        private static void AddTwoPassVppTonemapFilter(List<string> mainFilters, bool doVppTonemap, bool twoPassVppTonemap)
        {
            if (NeedsTwoPassVppTonemap(doVppTonemap, twoPassVppTonemap))
            {
                mainFilters.Add("vpp_qsv=tonemap=1:format=nv12:async_depth=2");
            }
        }

        private void AddBt709OverwriteFilter(EncodingJobInfo state, List<string> mainFilters, bool doVppTonemap)
        {
            if (doVppTonemap)
            {
                mainFilters.Add(GetOverwriteColorPropertiesParam(state, false));
            }
        }

        private static bool IsSwOrTransposedHwDecode(bool isSwDecoder, bool isD3d11vaDecoder, bool isQsvDecoder, bool doVppTranspose)
        {
            return isSwDecoder || ((isD3d11vaDecoder || isQsvDecoder) && doVppTranspose);
        }

        private void AddMemoryOutputTextSubtitlesFilter(EncodingJobInfo state, List<string> mainFilters, bool memoryOutput, bool hasTextSubs)
        {
            if (memoryOutput)
            {
                AddTextSubtitlesFilter(state, mainFilters, hasTextSubs);
            }
        }

        private static bool NeedsVppPassthrough(string hwScaleFilter, bool isD3d11vaDecoder)
        {
            return !string.IsNullOrEmpty(hwScaleFilter) && isD3d11vaDecoder;
        }

        private static bool NeedsVppTransposeOption(string hwScaleFilter, bool doVppTranspose)
        {
            return !string.IsNullOrEmpty(hwScaleFilter) && doVppTranspose;
        }

        private static bool NeedsVppMjpegOptions(string hwScaleFilter, bool isMjpegEncoder)
        {
            return !string.IsNullOrEmpty(hwScaleFilter) && isMjpegEncoder;
        }

        private static bool NeedsVppTonemapOptions(string hwScaleFilter, bool doVppTonemap)
        {
            return !string.IsNullOrEmpty(hwScaleFilter) && doVppTonemap;
        }

        private static bool NeedsTwoPassVppTonemap(bool doVppTonemap, bool twoPassVppTonemap)
        {
            return doVppTonemap && twoPassVppTonemap;
        }

        private static bool NeedsHwToOpenclMapping(bool doOclTonemap, bool isHwDecoder)
        {
            return doOclTonemap && isHwDecoder;
        }

        private static bool IsSwDecodeToQsvEncode(bool isSwDecoder, bool isQsvEncoder)
        {
            return isSwDecoder && isQsvEncoder;
        }

        private static bool NeedsQsvReverseMapping(bool isQsvInQsvOut, bool doOclTonemap)
        {
            return isQsvInQsvOut && doOclTonemap;
        }

        private bool NeedsOclTonemap(EncodingJobInfo state, EncodingOptions options, bool doVppTonemap)
        {
            return !doVppTonemap && IsHwTonemapAvailable(state, options);
        }

        private static bool HasSubtitlesToEncode(EncodingJobInfo state)
        {
            return state.SubtitleStream is not null && ShouldEncodeSubtitle(state);
        }

        private static bool HasTextSubtitles(EncodingJobInfo state, bool hasSubs)
        {
            return hasSubs && state.SubtitleStream.IsTextSubtitleStream;
        }

        private static bool HasGraphicalSubtitles(EncodingJobInfo state, bool hasSubs)
        {
            return hasSubs && !state.SubtitleStream.IsTextSubtitleStream;
        }

        private static bool HasFixedOverlaySize(int? overlayW, int? overlayH)
        {
            return overlayW.HasValue && overlayH.HasValue;
        }

        private static void AddSwDeinterlaceFilter(EncodingJobInfo state, EncodingOptions options, List<string> mainFilters, bool doDeintH2645)
        {
            if (doDeintH2645)
            {
                var swDeintFilter = GetSwDeinterlaceFilter(state, options);
                mainFilters.Add(swDeintFilter);
            }
        }

        private static string ApplyMjpegFullRangeSwScale(string swScaleFilter, bool isMjpegEncoder, bool doOclTonemap)
        {
            if (isMjpegEncoder && !doOclTonemap)
            {
                // sw decoder + hw mjpeg encoder
                swScaleFilter = string.IsNullOrEmpty(swScaleFilter) ? "scale=out_range=pc" : $"{swScaleFilter}:out_range=pc";
            }

            return swScaleFilter;
        }

        private static void AddOpenclHwupload(List<string> mainFilters, bool doOclTonemap)
        {
            if (doOclTonemap)
            {
                mainFilters.Add("hwupload=derive_device=opencl");
            }
        }

        private void AddTextSubtitlesFilter(EncodingJobInfo state, List<string> mainFilters, bool hasTextSubs)
        {
            // text subtitles
            if (hasTextSubs)
            {
                var textSubtitlesFilter = GetTextSubtitlesFilter(state, false, false);
                mainFilters.Add(textSubtitlesFilter);
            }
        }

        private static void AddD3d11vaToQsvHwmap(List<string> mainFilters, string hwScaleFilter, bool doDeintH2645)
        {
            if (!string.IsNullOrEmpty(hwScaleFilter) || doDeintH2645)
            {
                // INPUT d3d11 surface(vram)
                // map from d3d11va to qsv.
                mainFilters.Add("hwmap=derive_device=qsv");
            }
        }

        private static bool HasAssSubtitles(EncodingJobInfo state, bool hasSubs)
        {
            return hasSubs
                && (string.Equals(state.SubtitleStream.Codec, "ass", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state.SubtitleStream.Codec, "ssa", StringComparison.OrdinalIgnoreCase));
        }

        private bool CanDoQsvVppFullRangeOut(bool isMjpegEncoder)
        {
            return isMjpegEncoder
                && _mediaEncoder.EncoderVersion >= _minFFmpegQsvVppOutRangeOption;
        }

        private bool CanDoQsvVppScaleModeHq(bool isMjpegEncoder)
        {
            return isMjpegEncoder
                && _mediaEncoder.EncoderVersion >= _minFFmpegQsvVppScaleModeOption;
        }

        private static bool IsVppTonemappingContrastInRange(EncodingOptions options)
        {
            return options.VppTonemappingContrast > 1
                && options.VppTonemappingContrast <= 10;
        }

        private static bool IsVppTonemappingBrightnessInRange(EncodingOptions options)
        {
            return options.VppTonemappingBrightness != 0
                && options.VppTonemappingBrightness >= -100
                && options.VppTonemappingBrightness <= 100;
        }

        private static bool NeedsMemoryOutput(bool isHwDecoder, bool isSwEncoder, bool isUploadForOclTonemap)
        {
            return (isHwDecoder && isSwEncoder) || isUploadForOclTonemap;
        }

        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetIntelQsvVaapiVidFiltersPrefered(
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
            var isQsvDecoder = vidDecoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
            var isQsvEncoder = vidEncoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
            var isHwDecoder = isVaapiDecoder || isQsvDecoder;
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !isQsvEncoder;
            var isMjpegEncoder = vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);
            var isQsvInQsvOut = isHwDecoder && isQsvEncoder;

            var doVaVppTonemap = IsIntelVppTonemapAvailable(state, options);
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
            var transposeDir = GetTransposeDirection(state, rotation);
            var doVppTranspose = !string.IsNullOrEmpty(transposeDir);
            var swapWAndH = Math.Abs(rotation) == 90 && IsSwOrTransposedVaapiOrQsvDecode(isSwDecoder, isVaapiDecoder, isQsvDecoder, doVppTranspose);
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            /* Make main filters for video stream */
            var mainFilters = new List<string>();

            mainFilters.Add(GetOverwriteColorPropertiesParam(state, doTonemap));

            var context = new IntelQsvVaapiFilterContext(hasSubs, hasGraphicalSubs, hasTextSubs, hasAssSubs, swpInW, swpInH, subW, subH, reqW, reqH, reqMaxW, reqMaxH);
            var decoderContext = new IntelDecoderVaapiContext(options, isVaapiDecoder, isQsvDecoder, isMjpegEncoder, doOclTonemap, doTonemap, doVppTranspose, transposeDir, swapWAndH, doDeintH2645);
            if (isSwDecoder)
            {
                // INPUT sw surface(memory)
                // sw deint
                AddSwDeinterlaceFilter(state, options, mainFilters, doDeintH2645);

                var outFormat = GetSwDecoderOutputFormat(doOclTonemap, hasGraphicalSubs);
                var swScaleFilter = GetSwScaleFilter(state, options, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH);
                swScaleFilter = ApplyMjpegFullRangeSwScale(swScaleFilter, isMjpegEncoder, doOclTonemap);

                // sw scale
                mainFilters.Add(swScaleFilter);
                mainFilters.Add($"format={outFormat}");

                // keep video at memory except ocl tonemap,
                // since the overhead caused by hwupload >>> using sw filter.
                // sw => hw
                AddOpenclHwupload(mainFilters, doOclTonemap);
            }
            else if (isVaapiDecoder || isQsvDecoder)
            {
                AddIntelDecoderVaapiHwFilters(state, mainFilters, context, decoderContext);
            }

            // vaapi vpp tonemap
            if (NeedsHwVaapiVppTonemap(doVaVppTonemap, isHwDecoder))
            {
                AddVaapiVppTonemapFilters(options, mainFilters, isQsvDecoder, isMjpegEncoder);
            }

            AddQsvVaapiToOpenclMapping(mainFilters, doOclTonemap, isHwDecoder);

            // ocl tonemap
            AddOpenclTonemapFilter(options, mainFilters, doOclTonemap, isMjpegEncoder);

            var memoryOutput = false;
            var isUploadForOclTonemap = isSwDecoder && doOclTonemap;
            var isHwmapUsable = CanHwmapToMemory(isSwEncoder, doOclTonemap, isVaapiDecoder);
            if (NeedsMemoryOutput(isHwDecoder, isSwEncoder, isUploadForOclTonemap))
            {
                memoryOutput = true;

                // OUTPUT nv12 surface(memory)
                // prefer hwmap to hwdownload on opencl/vaapi.
                // qsv hwmap is not fully implemented for the time being.
                mainFilters.Add(isHwmapUsable ? "hwmap=mode=read" : "hwdownload");
                mainFilters.Add("format=nv12");
            }

            // OUTPUT nv12 surface(memory)
            if (IsSwDecodeToQsvEncode(isSwDecoder, isQsvEncoder))
            {
                memoryOutput = true;
            }

            AddMemoryOutputTextSubtitlesFilter(state, mainFilters, memoryOutput, hasTextSubs);

            AddQsvInQsvOutMapping(mainFilters, isQsvInQsvOut, doOclTonemap, isVaapiDecoder);

            /* Make sub and overlay filters for subtitle stream */
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            if (isQsvInQsvOut)
            {
                AddIntelQsvVaapiQsvOutSubtitleFilters(state, subFilters, overlayFilters, context);
            }
            else if (memoryOutput)
            {
                AddIntelQsvVaapiMemorySubtitleFilters(subFilters, overlayFilters, context);
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private void AddIntelDecoderVaapiHwFilters(EncodingJobInfo state, List<string> mainFilters, IntelQsvVaapiFilterContext context, IntelDecoderVaapiContext decoderContext)
        {
            var hwFilterSuffix = GetHwFilterSuffix(decoderContext.IsVaapiDecoder);
            var isRext = IsVideoStreamHevcRext(state);
            var doVppFullRangeOut = CanDoQsvVppFullRangeOut(decoderContext.IsMjpegEncoder);
            var doVppScaleModeHq = CanDoQsvVppScaleModeHq(decoderContext.IsMjpegEncoder);

            // INPUT vaapi/qsv surface(vram)
            // hw deint
            if (decoderContext.DoDeintH2645)
            {
                var deintFilter = GetHwDeinterlaceFilter(state, decoderContext.Options, hwFilterSuffix);
                mainFilters.Add(deintFilter);
            }

            // hw transpose(vaapi vpp)
            AddIntelVaapiTransposeFilter(mainFilters, decoderContext.IsVaapiDecoder, decoderContext.DoVppTranspose, decoderContext.TransposeDir);

            var outFormat = GetHwScaleOutputFormat(decoderContext.DoTonemap, decoderContext.IsQsvDecoder, decoderContext.DoVppTranspose, isRext);
            var swapOutputWandH = NeedsQsvOutputSwap(decoderContext.IsQsvDecoder, decoderContext.DoVppTranspose, decoderContext.SwapWAndH);
            var hwScalePrefix = GetHwScalePrefix(decoderContext.IsQsvDecoder);
            var hwScaleFilter = GetHwScaleFilter(hwScalePrefix, hwFilterSuffix, outFormat, swapOutputWandH, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);

            hwScaleFilter = AppendQsvVppTransposeOption(hwScaleFilter, decoderContext.IsQsvDecoder, decoderContext.DoVppTranspose, decoderContext.TransposeDir);

            if (NeedsVppMjpegOptions(hwScaleFilter, decoderContext.IsMjpegEncoder))
            {
                hwScaleFilter += GetHwScaleFullRangeOption(decoderContext.IsQsvDecoder, doVppFullRangeOut, decoderContext.DoOclTonemap);
                hwScaleFilter += GetHwScaleModeOption(decoderContext.IsQsvDecoder, doVppScaleModeHq);
            }

            // allocate extra pool sizes for vaapi vpp scale
            hwScaleFilter = AppendVaapiExtraHwFramesOption(hwScaleFilter, decoderContext.IsVaapiDecoder);

            // hw transpose(qsv vpp) & scale
            mainFilters.Add(hwScaleFilter);
        }

        private void AddIntelQsvVaapiQsvOutSubtitleFilters(EncodingJobInfo state, List<string> subFilters, List<string> overlayFilters, IntelQsvVaapiFilterContext context)
        {
            if (context.HasSubs)
            {
                if (context.HasGraphicalSubs)
                {
                    // overlay_qsv can handle overlay scaling, setup a smaller height to reduce transfer overhead
                    var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, 1080);
                    subFilters.Add(subPreProcFilters);
                    subFilters.Add("format=bgra");
                }
                else if (context.HasTextSubs)
                {
                    var framerate = state.VideoStream?.RealFrameRate;
                    var subFramerate = GetSubtitleFramerate(context.HasAssSubs, framerate);

                    var alphaSrcFilter = GetAlphaSrcFilter(state, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, 1080, subFramerate);
                    var subTextSubtitlesFilter = GetTextSubtitlesFilter(state, true, true);
                    subFilters.Add(alphaSrcFilter);
                    subFilters.Add("format=bgra");
                    subFilters.Add(subTextSubtitlesFilter);
                }

                // qsv requires a fixed pool size.
                // default to 64 otherwise it will fail on certain iGPU.
                subFilters.Add("hwupload=derive_device=qsv:extra_hw_frames=64");

                var (overlayW, overlayH) = GetFixedOutputSize(context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                var overlaySize = GetOverlaySizeOption(overlayW, overlayH);
                var overlayQsvFilter = string.Format(
                    CultureInfo.InvariantCulture,
                    "overlay_qsv=eof_action=pass:repeatlast=0{0}",
                    overlaySize);
                overlayFilters.Add(overlayQsvFilter);
            }
        }

        private static void AddIntelQsvVaapiMemorySubtitleFilters(List<string> subFilters, List<string> overlayFilters, IntelQsvVaapiFilterContext context)
        {
            if (context.HasGraphicalSubs)
            {
                var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                subFilters.Add(subPreProcFilters);
                overlayFilters.Add("overlay=eof_action=pass:repeatlast=0");
            }
        }

        private static void AddIntelVaapiTransposeFilter(List<string> mainFilters, bool isVaapiDecoder, bool doVppTranspose, string transposeDir)
        {
            if (NeedsVaapiTranspose(isVaapiDecoder, doVppTranspose))
            {
                mainFilters.Add($"transpose_vaapi=dir={transposeDir}");
            }
        }

        private static string GetHwScaleOutputFormat(bool doTonemap, bool isQsvDecoder, bool doVppTranspose, bool isRext)
        {
            return doTonemap ? (((isQsvDecoder && doVppTranspose) || isRext) ? "p010" : string.Empty) : "nv12";
        }

        private static string GetHwScaleFullRangeOption(bool isQsvDecoder, bool doVppFullRangeOut, bool doOclTonemap)
        {
            return ((isQsvDecoder && !doVppFullRangeOut) || doOclTonemap) ? string.Empty : ":out_range=pc";
        }

        private static string GetHwScaleModeOption(bool isQsvDecoder, bool doVppScaleModeHq)
        {
            return isQsvDecoder ? (doVppScaleModeHq ? ":scale_mode=hq" : string.Empty) : ":mode=hq";
        }

        private static void AddQsvVaapiToOpenclMapping(List<string> mainFilters, bool doOclTonemap, bool isHwDecoder)
        {
            if (NeedsHwToOpenclMapping(doOclTonemap, isHwDecoder))
            {
                // map from qsv to opencl via qsv(vaapi)-opencl interop.
                mainFilters.Add("hwmap=derive_device=opencl:mode=read");
            }
        }

        private static string AppendQsvVppTransposeOption(string hwScaleFilter, bool isQsvDecoder, bool doVppTranspose, string transposeDir)
        {
            if (ShouldAppendQsvVppTranspose(hwScaleFilter, isQsvDecoder, doVppTranspose))
            {
                hwScaleFilter += $":transpose={transposeDir}";
            }

            return hwScaleFilter;
        }

        private static string AppendVaapiExtraHwFramesOption(string hwScaleFilter, bool isVaapiDecoder)
        {
            if (NeedsVaapiExtraHwFrames(hwScaleFilter, isVaapiDecoder))
            {
                hwScaleFilter += ":extra_hw_frames=24";
            }

            return hwScaleFilter;
        }

        private static string GetHwFilterSuffix(bool isVaapiDecoder)
        {
            return isVaapiDecoder ? "vaapi" : "qsv";
        }

        private static string GetHwScalePrefix(bool isQsvDecoder)
        {
            return isQsvDecoder ? "vpp" : "scale";
        }

        private static bool IsSwOrTransposedVaapiOrQsvDecode(bool isSwDecoder, bool isVaapiDecoder, bool isQsvDecoder, bool doVppTranspose)
        {
            return isSwDecoder || ((isVaapiDecoder || isQsvDecoder) && doVppTranspose);
        }

        private static void AddQsvInQsvOutMapping(List<string> mainFilters, bool isQsvInQsvOut, bool doOclTonemap, bool isVaapiDecoder)
        {
            if (isQsvInQsvOut)
            {
                AddQsvOutputMapping(mainFilters, doOclTonemap, isVaapiDecoder);
            }
        }

        private static bool NeedsVaapiTranspose(bool isVaapiDecoder, bool doVppTranspose)
        {
            return isVaapiDecoder && doVppTranspose;
        }

        private static bool NeedsVaapiExtraHwFrames(string hwScaleFilter, bool isVaapiDecoder)
        {
            return !string.IsNullOrEmpty(hwScaleFilter) && isVaapiDecoder;
        }

        private static bool NeedsHwVaapiVppTonemap(bool doVaVppTonemap, bool isHwDecoder)
        {
            return doVaVppTonemap && isHwDecoder;
        }

        private static bool CanHwmapToMemory(bool isSwEncoder, bool doOclTonemap, bool isVaapiDecoder)
        {
            return isSwEncoder && (doOclTonemap || isVaapiDecoder);
        }

        private static bool NeedsQsvOutputSwap(bool isQsvDecoder, bool doVppTranspose, bool swapWAndH)
        {
            return isQsvDecoder && doVppTranspose && swapWAndH;
        }

        private void AddVaapiVppTonemapFilters(EncodingOptions options, List<string> mainFilters, bool isQsvDecoder, bool isMjpegEncoder)
        {
            if (isQsvDecoder)
            {
                // map from qsv to vaapi.
                mainFilters.Add("hwmap=derive_device=vaapi");
                mainFilters.Add("format=vaapi");
            }

            var tonemapFilter = GetHwTonemapFilter(options, "vaapi", "nv12", isMjpegEncoder);
            mainFilters.Add(tonemapFilter);

            if (isQsvDecoder)
            {
                // map from vaapi to qsv.
                mainFilters.Add("hwmap=derive_device=qsv");
                mainFilters.Add("format=qsv");
            }
        }

        private static void AddQsvOutputMapping(List<string> mainFilters, bool doOclTonemap, bool isVaapiDecoder)
        {
            if (doOclTonemap)
            {
                // OUTPUT qsv(nv12) surface(vram)
                // reverse-mapping via qsv(vaapi)-opencl interop.
                // add extra pool size to avoid the 'cannot allocate memory' error on hevc_qsv.
                mainFilters.Add("hwmap=derive_device=qsv:mode=write:reverse=1:extra_hw_frames=16");
                mainFilters.Add("format=qsv");
            }
            else if (isVaapiDecoder)
            {
                mainFilters.Add("hwmap=derive_device=qsv");
                mainFilters.Add("format=qsv");
            }
        }

        private static bool ShouldAppendQsvVppTranspose(string hwScaleFilter, bool isQsvDecoder, bool doVppTranspose)
        {
            return !string.IsNullOrEmpty(hwScaleFilter) && isQsvDecoder && doVppTranspose;
        }

        private readonly record struct IntelQsvDx11FilterContext(
            bool HasSubs,
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

        private readonly record struct IntelQsvVaapiFilterContext(
            bool HasSubs,
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

        private readonly record struct IntelDecoderVaapiContext(
            EncodingOptions Options,
            bool IsVaapiDecoder,
            bool IsQsvDecoder,
            bool IsMjpegEncoder,
            bool DoOclTonemap,
            bool DoTonemap,
            bool DoVppTranspose,
            string TransposeDir,
            bool SwapWAndH,
            bool DoDeintH2645);
    }
}
