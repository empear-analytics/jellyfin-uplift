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
        /// Gets the parameter of AMD AMF filter chain.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="vidEncoder">Video encoder to use.</param>
        /// <returns>The tuple contains three lists: main, sub and overlay filters.</returns>
        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetAmdVidFilterChain(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidEncoder)
        {
            if (options.HardwareAccelerationType != HardwareAccelerationType.amf)
            {
                return (null, null, null);
            }

            var isWindows = OperatingSystem.IsWindows();
            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !vidEncoder.Contains("amf", StringComparison.OrdinalIgnoreCase);
            var isAmfDx11OclSupported = isWindows && _mediaEncoder.SupportsHwaccel("d3d11va") && IsOpenclFullSupported();

            // legacy d3d11va pipeline(copy-back)
            if (UseLegacyHwPipeline(isSwDecoder, isSwEncoder, isAmfDx11OclSupported))
            {
                return GetSwVidFilterChain(state, options, vidEncoder);
            }

            // preferred d3d11va + opencl filters + amf pipeline
            return GetAmdDx11VidFiltersPrefered(state, options, vidDecoder, vidEncoder);
        }

        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetAmdDx11VidFiltersPrefered(
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
            var isAmfEncoder = vidEncoder.Contains("amf", StringComparison.OrdinalIgnoreCase);
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !isAmfEncoder;
            var isMjpegEncoder = vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);
            var isDxInDxOut = isD3d11vaDecoder && isAmfEncoder;

            var doDeintH2645 = IsDeinterlaceAvailable(state);
            var doOclTonemap = IsHwTonemapAvailable(state, options);

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);
            var hasAssSubs = HasAssSubtitles(state, hasSubs);
            var subW = state.SubtitleStream?.Width;
            var subH = state.SubtitleStream?.Height;

            var rotation = state.VideoStream?.Rotation ?? 0;
            var transposeDir = GetTransposeDirection(state, rotation);
            var doOclTranspose = !string.IsNullOrEmpty(transposeDir)
                && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.TransposeOpenclReversal);
            var swapWAndH = Math.Abs(rotation) == 90 && (isSwDecoder || (isD3d11vaDecoder && doOclTranspose));
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

                var outFormat = doOclTonemap ? "yuv420p10le" : "yuv420p";
                var swScaleFilter = GetSwScaleFilter(state, options, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH);
                // sw scale
                mainFilters.Add(swScaleFilter);
                mainFilters.Add($"format={outFormat}");

                // keep video at memory except ocl tonemap,
                // since the overhead caused by hwupload >>> using sw filter.
                // sw => hw
                AddAmdOclTonemapUpload(mainFilters, doOclTonemap);
            }

            if (isD3d11vaDecoder)
            {
                // INPUT d3d11 surface(vram)
                // map from d3d11va to opencl via d3d11-opencl interop.
                mainFilters.Add("hwmap=derive_device=opencl:mode=read");

                // hw deint
                if (doDeintH2645)
                {
                    var deintFilter = GetHwDeinterlaceFilter(state, options, "opencl");
                    mainFilters.Add(deintFilter);
                }

                // hw transpose
                if (doOclTranspose)
                {
                    mainFilters.Add($"transpose_opencl=dir={transposeDir}");
                }

                var outFormat = doOclTonemap ? string.Empty : "nv12";
                var hwScaleFilter = GetHwScaleFilter("scale", "opencl", outFormat, false, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH);
                // hw scale
                mainFilters.Add(hwScaleFilter);
            }

            // hw tonemap
            AddOpenclTonemapFilter(options, mainFilters, doOclTonemap, isMjpegEncoder);

            var memoryOutput = false;
            var isUploadForOclTonemap = isSwDecoder && doOclTonemap;
            if (isD3d11vaDecoder && isSwEncoder)
            {
                memoryOutput = true;
                AddAmdHwTransferToMemory(mainFilters, hasGraphicalSubs);
            }

            // OUTPUT yuv420p surface
            if (NeedsAmfHwUpload(isSwDecoder, isAmfEncoder, isUploadForOclTonemap))
            {
                memoryOutput = true;
            }

            AddMemoryOutputTextSubtitlesFilter(state, mainFilters, memoryOutput, hasTextSubs);

            AddAmdD3d11OutputMapping(mainFilters, isDxInDxOut, isUploadForOclTonemap, hasSubs);

            /* Make sub and overlay filters for subtitle stream */
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            if (isDxInDxOut || isUploadForOclTonemap)
            {
                if (hasSubs)
                {
                    var alphaFormatOpt = string.Empty;
                    if (hasGraphicalSubs)
                    {
                        var subPreProcFilters = GetGraphicalSubPreProcessFilters(swpInW, swpInH, subW, subH, reqW, reqH, reqMaxW, reqMaxH);
                        subFilters.Add(subPreProcFilters);
                        subFilters.Add("format=yuva420p");
                    }
                    else if (hasTextSubs)
                    {
                        var framerate = state.VideoStream?.RealFrameRate;
                        var subFramerate = GetSubtitleFramerate(hasAssSubs, framerate);

                        // alphasrc=s=1280x720:r=10:start=0,format=yuva420p,subtitles,hwupload
                        var alphaSrcFilter = GetAlphaSrcFilter(state, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH, subFramerate);
                        var subTextSubtitlesFilter = GetTextSubtitlesFilter(state, true, true);
                        subFilters.Add(alphaSrcFilter);
                        subFilters.Add("format=yuva420p");
                        subFilters.Add(subTextSubtitlesFilter);

                        alphaFormatOpt = _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayOpenclAlphaFormat)
                            ? ":alpha_format=premultiplied" : string.Empty;
                    }

                    subFilters.Add("hwupload=derive_device=opencl");
                    overlayFilters.Add($"overlay_opencl=eof_action=pass:repeatlast=0{alphaFormatOpt}");
                    overlayFilters.Add("hwmap=derive_device=d3d11va:mode=write:reverse=1");
                    overlayFilters.Add("format=d3d11");
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
    }
}
