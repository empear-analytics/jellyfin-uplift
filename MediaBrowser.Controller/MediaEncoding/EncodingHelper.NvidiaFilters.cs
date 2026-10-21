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
        /// Gets the parameter of Nvidia NVENC filter chain.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="vidEncoder">Video encoder to use.</param>
        /// <returns>The tuple contains three lists: main, sub and overlay filters.</returns>
        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetNvidiaVidFilterChain(
            EncodingJobInfo state,
            EncodingOptions options,
            string vidEncoder)
        {
            if (options.HardwareAccelerationType != HardwareAccelerationType.nvenc)
            {
                return (null, null, null);
            }

            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !vidEncoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase);

            // legacy cuvid pipeline(copy-back)
            if (UseLegacyCudaPipeline(isSwDecoder, isSwEncoder))
            {
                return GetSwVidFilterChain(state, options, vidEncoder);
            }

            // preferred nvdec/cuvid + cuda filters + nvenc pipeline
            return GetNvidiaVidFiltersPrefered(state, options, vidDecoder, vidEncoder);
        }

        private bool UseLegacyCudaPipeline(bool isSwDecoder, bool isSwEncoder)
        {
            return (isSwDecoder && isSwEncoder)
                || !IsCudaFullSupported()
                || !_mediaEncoder.SupportsFilter("alphasrc");
        }

        public (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters) GetNvidiaVidFiltersPrefered(
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

            var isNvDecoder = vidDecoder.Contains("cuda", StringComparison.OrdinalIgnoreCase);
            var isNvencEncoder = vidEncoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
            var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
            var isSwEncoder = !isNvencEncoder;
            var isMjpegEncoder = vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase);
            var isCuInCuOut = isNvDecoder && isNvencEncoder;

            var doubleRateDeint = IsHwDoubleRateDeinterlace(state, options);
            var doDeintH2645 = IsDeinterlaceAvailable(state);
            var doCuTonemap = IsHwTonemapAvailable(state, options);

            var hasSubs = HasSubtitlesToEncode(state);
            var hasTextSubs = HasTextSubtitles(state, hasSubs);
            var hasGraphicalSubs = HasGraphicalSubtitles(state, hasSubs);
            var hasAssSubs = HasAssSubtitles(state, hasSubs);
            var subW = state.SubtitleStream?.Width;
            var subH = state.SubtitleStream?.Height;

            var rotation = state.VideoStream?.Rotation ?? 0;
            var transposeDir = GetTransposeDirection(state, rotation);
            var doCuTranspose = !string.IsNullOrEmpty(transposeDir) && _mediaEncoder.SupportsFilter("transpose_cuda");
            var swapWAndH = NeedsVaapiSwapWAndH(rotation, isSwDecoder, isNvDecoder, doCuTranspose);
            var swpInW = swapWAndH ? inH : inW;
            var swpInH = swapWAndH ? inW : inH;

            /* Make main filters for video stream */
            var mainFilters = new List<string>();

            mainFilters.Add(GetOverwriteColorPropertiesParam(state, doCuTonemap));

            var swDecoderContext = new NvidiaDecoderSwContext(isSwDecoder, doDeintH2645, doCuTonemap, vidEncoder, swpInW, swpInH, threeDFormat, reqW, reqH, reqMaxW, reqMaxH);
            NvidiaDecoderAddSwFilters(state, options, mainFilters, swDecoderContext);

            var nvDecoderContext = new NvidiaDecoderCudaContext(isNvDecoder, doDeintH2645, doCuTranspose, doCuTonemap, transposeDir, swpInW, swpInH, reqW, reqH, reqMaxW, reqMaxH);
            NvidiaDecoderAddCudaFilters(state, options, mainFilters, nvDecoderContext);

            // hw tonemap
            AddNvidiaHwTonemapFilter(options, mainFilters, doCuTonemap, isMjpegEncoder);

            var memoryOutput = false;
            var isUploadForCuTonemap = isSwDecoder && doCuTonemap;
            if (NeedsCudaMemoryOutput(new NeedsCudaMemoryOutputParts(isNvDecoder, isSwEncoder, isUploadForCuTonemap, hasSubs)))
            {
                memoryOutput = true;

                // OUTPUT yuv420p surface(memory)
                mainFilters.Add("hwdownload");
                mainFilters.Add("format=yuv420p");
            }

            // OUTPUT yuv420p surface(memory)
            if (NeedsNvencHwUpload(isSwDecoder, isNvencEncoder, isUploadForCuTonemap))
            {
                memoryOutput = true;
            }

            AddMemoryOutputTextSubtitlesFilter(state, mainFilters, memoryOutput, hasTextSubs);

            // OUTPUT cuda(yuv420p) surface(vram)

            /* Make sub and overlay filters for subtitle stream */
            var context = new NvidiaSubtitleFilterContext(hasSubs, hasGraphicalSubs, hasTextSubs, hasAssSubs, swpInW, swpInH, subW, subH, reqW, reqH, reqMaxW, reqMaxH);
            var subFilters = new List<string>();
            var overlayFilters = new List<string>();
            if (isCuInCuOut)
            {
                AddNvidiaCuInCuOutSubtitleFilters(state, subFilters, overlayFilters, context);
            }
            else
            {
                AddNvidiaMemorySubtitleFilters(subFilters, overlayFilters, context);
            }

            return (mainFilters, subFilters, overlayFilters);
        }

        private void AddNvidiaCuInCuOutSubtitleFilters(EncodingJobInfo state, List<string> subFilters, List<string> overlayFilters, NvidiaSubtitleFilterContext context)
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
                    var framerate = state.VideoStream?.RealFrameRate;
                    var subFramerate = GetSubtitleFramerate(context.HasAssSubs, framerate);

                    // alphasrc=s=1280x720:r=10:start=0,format=yuva420p,subtitles,hwupload
                    var alphaSrcFilter = GetAlphaSrcFilter(state, context.SwpInW, context.SwpInH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH, subFramerate);
                    var subTextSubtitlesFilter = GetTextSubtitlesFilter(state, true, true);
                    subFilters.Add(alphaSrcFilter);
                    subFilters.Add("format=yuva420p");
                    subFilters.Add(subTextSubtitlesFilter);

                    alphaFormatOpt = GetNvidiaOverlayAlphaFormatOption();
                }

                subFilters.Add("hwupload=derive_device=cuda");
                overlayFilters.Add($"overlay_cuda=eof_action=pass:repeatlast=0{alphaFormatOpt}");
            }
        }

        private static void AddNvidiaMemorySubtitleFilters(List<string> subFilters, List<string> overlayFilters, NvidiaSubtitleFilterContext context)
        {
            if (context.HasGraphicalSubs)
            {
                var subPreProcFilters = GetGraphicalSubPreProcessFilters(context.SwpInW, context.SwpInH, context.SubW, context.SubH, context.ReqW, context.ReqH, context.ReqMaxW, context.ReqMaxH);
                subFilters.Add(subPreProcFilters);
                overlayFilters.Add("overlay=eof_action=pass:repeatlast=0");
            }
        }

        private readonly record struct NvidiaSubtitleFilterContext(
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
    }
}
