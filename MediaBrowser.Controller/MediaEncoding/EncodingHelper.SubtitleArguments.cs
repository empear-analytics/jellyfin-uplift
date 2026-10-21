#nullable disable

#pragma warning disable CS1591
// We need lowercase normalized string for ffmpeg
#pragma warning disable CA1308

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
        /// Gets the input argument.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="segmentContainer">Segment Container.</param>
        /// <returns>Input arguments.</returns>
        public string GetInputArgument(EncodingJobInfo state, EncodingOptions options, string segmentContainer)
        {
            var arg = new StringBuilder();
            var inputVidHwaccelArgs = GetInputVideoHwaccelArgs(state, options);

            if (!string.IsNullOrEmpty(inputVidHwaccelArgs))
            {
                arg.Append(inputVidHwaccelArgs);
            }

            var canvasArgs = GetGraphicalSubCanvasSize(state);
            if (!string.IsNullOrEmpty(canvasArgs))
            {
                arg.Append(canvasArgs);
            }

            if (IsDvdOrBluRaySource(state))
            {
                AppendConcatInput(state, arg);
            }
            else
            {
                arg.Append(" -i ")
                    .Append(_mediaEncoder.GetInputPathArgument(state));
            }

            if (NeedsExternalSubtitleMuxing(state))
            {
                var subtitlePath = state.SubtitleStream.Path;
                var isGraphicalBurnIn = IsGraphicalSubtitleBurnIn(state);

                // dvdsub/vobsub graphical subtitles use .sub+.idx pairs
                subtitlePath = GetIdxSubtitlePath(subtitlePath);

                AppendSubtitleProbeArgs(state, arg);

                // Also seek the external subtitles stream.
                var seekSubParam = GetFastSeekCommandLineParameter(state, options, segmentContainer);
                if (!string.IsNullOrEmpty(seekSubParam))
                {
                    arg.Append(' ').Append(seekSubParam);
                }

                if (isGraphicalBurnIn && !string.IsNullOrEmpty(canvasArgs))
                {
                    arg.Append(canvasArgs);
                }

                arg.Append(" -i file:\"").Append(subtitlePath.EscapeProcessArgument()).Append('\"');
            }

            if (HasExternalAudioStream(state))
            {
                AppendExternalAudioInput(state, options, segmentContainer, arg);
            }

            // Disable auto inserted SW scaler for HW decoders in case of changed resolution.
            var isSwDecoder = string.IsNullOrEmpty(GetHardwareVideoDecoder(state, options));
            if (!isSwDecoder)
            {
                arg.Append(" -noautoscale");
            }

            return arg.ToString();
        }

        /// <summary>
        /// Gets the text subtitle param.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="enableAlpha">Enable alpha processing.</param>
        /// <param name="enableSub2video">Enable sub2video mode.</param>
        /// <returns>System.String.</returns>
        public string GetTextSubtitlesFilter(EncodingJobInfo state, bool enableAlpha, bool enableSub2video)
        {
            var seconds = Math.Round(TimeSpan.FromTicks(state.StartTimeTicks ?? 0).TotalSeconds);

            // hls always copies timestamps
            var setPtsParam = state.CopyTimestamps || state.TranscodingType != TranscodingJobType.Progressive
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",setpts=PTS -{0}/TB", seconds);

            var alphaParam = enableAlpha ? ":alpha=1" : string.Empty;
            var sub2videoParam = enableSub2video ? ":sub2video=1" : string.Empty;

            var fontPath = _pathManager.GetAttachmentFolderPath(state.MediaSource.Id);
            var fontParam = fontPath is null
                ? string.Empty
                : string.Format(
                    CultureInfo.InvariantCulture,
                    ":fontsdir='{0}'",
                    _mediaEncoder.EscapeSubtitleFilterPath(fontPath));

            if (state.SubtitleStream.IsExternal)
            {
                var charsetParam = GetSubtitleCharsetParam(state);

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "subtitles=f='{0}'{1}{2}{3}{4}{5}",
                    _mediaEncoder.EscapeSubtitleFilterPath(state.SubtitleStream.Path),
                    charsetParam,
                    alphaParam,
                    sub2videoParam,
                    fontParam,
                    setPtsParam);
            }

            var subtitlePath = _subtitleEncoder.GetSubtitleFilePath(
                    state.SubtitleStream,
                    state.MediaSource,
                    CancellationToken.None).GetAwaiter().GetResult();

            return string.Format(
                CultureInfo.InvariantCulture,
                "subtitles=f='{0}'{1}{2}{3}{4}",
                _mediaEncoder.EscapeSubtitleFilterPath(subtitlePath),
                alphaParam,
                sub2videoParam,
                fontParam,
                setPtsParam);
        }

        public string GetSubtitleEmbedArguments(EncodingJobInfo state)
        {
            if (state.SubtitleStream is null || state.SubtitleDeliveryMethod != SubtitleDeliveryMethod.Embed)
            {
                return string.Empty;
            }

            var format = state.SupportedSubtitleCodecs.FirstOrDefault();
            string codec;

            if (string.IsNullOrEmpty(format) || string.Equals(format, state.SubtitleStream.Codec, StringComparison.OrdinalIgnoreCase))
            {
                codec = "copy";
            }
            else
            {
                codec = format;
            }

            return " -codec:s:0 " + codec + " -disposition:s:0 default";
        }

        public static int GetSubtitleStreamIndexForFfmpeg(MediaSourceInfo mediaSource, MediaStream subtitleStream)
        {
            var index = FindIndex(mediaSource.MediaStreams, subtitleStream);
            if (IsIndexUnaffectedByBluRayHiddenStreams(index, mediaSource, subtitleStream))
            {
                return index;
            }

            var hiddenStreamsBefore = mediaSource.MediaStreams.Count(s =>
                s.Type == MediaStreamType.Audio
                && !s.IsExternal
                && (string.Equals(s.Codec, "truehd", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Codec, "atmos", StringComparison.OrdinalIgnoreCase))
                && s.Index < subtitleStream.Index);

            return index + hiddenStreamsBefore;
        }

        private static bool ShouldEncodeSubtitle(EncodingJobInfo state)
        {
            return state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Encode
                   || (state.BaseRequest.AlwaysBurnInSubtitleWhenTranscoding && !IsCopyCodec(state.OutputVideoCodec));
        }

        private static bool NeedsExternalSubtitleMuxing(EncodingJobInfo state)
        {
            return state.SubtitleStream is not null
                && state.SubtitleStream.IsExternal
                && (state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Embed
                    || (ShouldEncodeSubtitle(state) && !state.SubtitleStream.IsTextSubtitleStream));
        }
    }
}
