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
            var doOclTranspose = IsAmdOclTransposeAvailable(transposeDir);
            var swapWAndH = IsAmdRotatedWidthHeightSwapped(new IsAmdRotatedWidthHeightSwappedParts(rotation, isSwDecoder, isD3d11vaDecoder, doOclTranspose));
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            /* Make main filters for video stream */
            var mainFilters = new List<string>();

            mainFilters.Add(GetOverwriteColorPropertiesParam(state, doOclTonemap));

            var swDecoderContext = new AmdSecondSwDecoderContext(isSwDecoder, doDeintH2645, doOclTonemap, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH, mainFilters);
            AddAmdSecondSwDecoderFilters(state, options, swDecoderContext);

            var d3d11vaDecoderContext = new AmdDecoderD3d11vaContext(isD3d11vaDecoder, doDeintH2645, doOclTranspose, transposeDir, doOclTonemap, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH, mainFilters);
            AddAmdDecoderD3d11vaFilters(state, options, d3d11vaDecoderContext);

            // hw tonemap
            AddOpenclTonemapFilter(options, mainFilters, doOclTonemap, isMjpegEncoder);

            var memoryOutput = false;
            var isUploadForOclTonemap = IsAmdUploadForOclTonemap(isSwDecoder, doOclTonemap);
            if (IsAmdD3d11vaToSwEncoder(isD3d11vaDecoder, isSwEncoder))
            {
                memoryOutput = true;
                AddAmdHwTransferToMemory(mainFilters, hasGraphicalSubs);
            }

            // OUTPUT yuv420p surface
            if (NeedsAmfHwUpload(new NeedsAmfHwUploadParts(isSwDecoder, isAmfEncoder, isUploadForOclTonemap)))
            {
                memoryOutput = true;
            }

            AddMemoryOutputTextSubtitlesFilter(state, mainFilters, memoryOutput, hasTextSubs);

            AddAmdD3d11OutputMapping(mainFilters, new AddAmdD3d11OutputMappingParts(isDxInDxOut, isUploadForOclTonemap, hasSubs));

            /* Make sub and overlay filters for subtitle stream */
            var context = new AmdSubtitleFilterContext(hasSubs, hasGraphicalSubs, hasTextSubs, hasAssSubs, swpInW, swpInH, subW, subH, reqW, reqH, reqMaxW, reqMaxH);
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            if (NeedsAmdOpenclSubtitleOverlay(isDxInDxOut, isUploadForOclTonemap))
            {
                AddAmdDxInDxOutSubtitleFilters(state, subFilters, overlayFilters, context);
            }
            else if (memoryOutput)
            {
                AddAmdMemorySubtitleFilters(subFilters, overlayFilters, context);
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private static bool IsAmdD3d11vaToSwEncoder(bool isD3d11vaDecoder, bool isSwEncoder)
        {
            return isD3d11vaDecoder && isSwEncoder;
        }

        private static bool NeedsAmdOpenclSubtitleOverlay(bool isDxInDxOut, bool isUploadForOclTonemap)
        {
            return isDxInDxOut || isUploadForOclTonemap;
        }

        private static bool IsAmdUploadForOclTonemap(bool isSwDecoder, bool doOclTonemap)
        {
            return isSwDecoder && doOclTonemap;
        }

        private void AddAmdSecondSwDecoderFilters(EncodingJobInfo state, EncodingOptions options, AmdSecondSwDecoderContext context)
        {
            if (context.IsSwDecoder)
            {
                // INPUT sw surface(memory)
                // sw deint
                AddSwDeinterlaceFilter(state, options, context.MainFilters, context.DoDeintH2645);

                var outFormat = GetAmdSwOutputFormat(context.DoOclTonemap);
                var swScaleFilter = GetSwScaleFilter(state, options, context.VidEncoder, context.SwpInW, context.SwpInH, context.ThreeDFormat, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                // sw scale
                context.MainFilters.Add(swScaleFilter);
                context.MainFilters.Add($"format={outFormat}");

                // keep video at memory except ocl tonemap,
                // since the overhead caused by hwupload >>> using sw filter.
                // sw => hw
                AddAmdOclTonemapUpload(context.MainFilters, context.DoOclTonemap);
            }
        }

        private void AddAmdDxInDxOutSubtitleFilters(EncodingJobInfo state, List<string> subFilters, List<string> overlayFilters, AmdSubtitleFilterContext context)
        {
            if (context.HasSubs)
            {
                var alphaFormatOpt = string.Empty;
                if (context.HasGraphicalSubs)
                {
                    var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                    subFilters.Add(subPreProcFilters);
                    subFilters.Add("format=yuva420p");
                }
                else if (context.HasTextSubs)
                {
                    var subFramerate = GetAmdSubtitleFramerate(state, context.HasAssSubs);

                    // alphasrc=s=1280x720:r=10:start=0,format=yuva420p,subtitles,hwupload
                    var alphaSrcFilter = GetAlphaSrcFilter(state, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH, subFramerate);
                    var subTextSubtitlesFilter = GetTextSubtitlesFilter(state, true, true);
                    subFilters.Add(alphaSrcFilter);
                    subFilters.Add("format=yuva420p");
                    subFilters.Add(subTextSubtitlesFilter);

                    alphaFormatOpt = GetAmdOverlayAlphaFormatOption();
                }

                subFilters.Add("hwupload=derive_device=opencl");
                overlayFilters.Add($"overlay_opencl=eof_action=pass:repeatlast=0{alphaFormatOpt}");
                overlayFilters.Add("hwmap=derive_device=d3d11va:mode=write:reverse=1");
                overlayFilters.Add("format=d3d11");
            }
        }

        private static void AddAmdMemorySubtitleFilters(List<string> subFilters, List<string> overlayFilters, AmdSubtitleFilterContext context)
        {
            if (context.HasGraphicalSubs)
            {
                var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                subFilters.Add(subPreProcFilters);
                overlayFilters.Add("overlay=eof_action=pass:repeatlast=0");
            }
        }

        private readonly record struct AmdSubtitleFilterContext(
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

        private readonly record struct AmdSecondSwDecoderContext(
            bool IsSwDecoder,
            bool DoDeintH2645,
            bool DoOclTonemap,
            string VidEncoder,
            int? SwpInW,
            int? SwpInH,
            Video3DFormat? ThreeDFormat,
            int? ReqW,
            int? ReqH,
            int? ReqMaxW,
            int? ReqMaxH,
            List<string> MainFilters);
    }
}
