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
        private static bool IsDvdOrBluRaySource(EncodingJobInfo state)
        {
            return state.MediaSource.VideoType == VideoType.Dvd || state.MediaSource.VideoType == VideoType.BluRay;
        }

        private static bool IsGraphicalSubtitleBurnIn(EncodingJobInfo state)
        {
            return ShouldEncodeSubtitle(state) && !state.SubtitleStream.IsTextSubtitleStream;
        }

        private static bool HasExternalAudioStream(EncodingJobInfo state)
        {
            return state.AudioStream is not null && state.AudioStream.IsExternal;
        }

        private void AppendConcatInput(EncodingJobInfo state, StringBuilder arg)
        {
            var concatFilePath = Path.Join(_configurationManager.CommonApplicationPaths.CachePath, "concat", state.MediaSource.Id + ".concat");
            if (!File.Exists(concatFilePath))
            {
                _mediaEncoder.GenerateConcatConfig(state.MediaSource, concatFilePath);
            }

            arg.Append(" -f concat -safe 0 -i \"")
                .Append(concatFilePath)
                .Append("\" ");
        }

        private static string GetIdxSubtitlePath(string subtitlePath)
        {
            var subtitleExtension = Path.GetExtension(subtitlePath.AsSpan());
            if (subtitleExtension.Equals(".sub", StringComparison.OrdinalIgnoreCase))
            {
                var idxFile = Path.ChangeExtension(subtitlePath, ".idx");
                if (File.Exists(idxFile))
                {
                    subtitlePath = idxFile;
                }
            }

            return subtitlePath;
        }

        private void AppendExternalAudioInput(EncodingJobInfo state, EncodingOptions options, string segmentContainer, StringBuilder arg)
        {
            // Also seek the external audio stream.
            var seekAudioParam = GetFastSeekCommandLineParameter(state, options, segmentContainer);
            if (!string.IsNullOrEmpty(seekAudioParam))
            {
                arg.Append(' ').Append(seekAudioParam);
            }

            arg.Append(" -i \"").Append(state.AudioStream.Path.EscapeProcessArgument()).Append('"');
        }

        private string GetSubtitleCharsetParam(EncodingJobInfo state)
        {
            var charsetParam = string.Empty;

            if (!string.IsNullOrEmpty(state.SubtitleStream.Language))
            {
                var charenc = _subtitleEncoder.GetSubtitleFileCharacterSet(
                        state.SubtitleStream,
                        state.SubtitleStream.Language,
                        state.MediaSource,
                        CancellationToken.None).GetAwaiter().GetResult();

                if (!string.IsNullOrEmpty(charenc))
                {
                    charsetParam = ":charenc=" + charenc;
                }
            }

            return charsetParam;
        }

        private static bool HasNoKnownMediaStreams(EncodingJobInfo state)
        {
            return state.VideoStream is null && state.AudioStream is null;
        }

        private static bool HasUnknownVideoStreamIndex(EncodingJobInfo state)
        {
            return state.VideoStream is not null && state.VideoStream.Index == -1;
        }

        private static bool HasUnknownAudioStreamIndex(EncodingJobInfo state)
        {
            return state.AudioStream is not null && state.AudioStream.Index == -1;
        }

        private static bool IsSubtitleExcludedFromMap(EncodingJobInfo state, SubtitleDeliveryMethod subtitleMethod)
        {
            return state.SubtitleStream is null || subtitleMethod == SubtitleDeliveryMethod.Hls;
        }

        private static bool IsExternalGraphicalSubtitle(EncodingJobInfo state)
        {
            return state.SubtitleStream.IsExternal && !state.SubtitleStream.IsTextSubtitleStream;
        }

        private static string AppendAudioMapArgs(string args, EncodingJobInfo state)
        {
            int audioStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.AudioStream);
            if (state.AudioStream.IsExternal)
            {
                bool hasExternalSubAsInput = NeedsExternalSubtitleMuxing(state);
                int externalAudioMapIndex = hasExternalSubAsInput ? 2 : 1;

                args += string.Format(
                    CultureInfo.InvariantCulture,
                    " -map {0}:{1}",
                    externalAudioMapIndex,
                    audioStreamIndex);
            }
            else
            {
                args += string.Format(
                    CultureInfo.InvariantCulture,
                    " -map 0:{0}",
                    audioStreamIndex);
            }

            return args;
        }

        private static string AppendEmbeddedSubtitleMapArgs(string args, EncodingJobInfo state)
        {
            if (state.SubtitleStream.IsExternal)
            {
                // External subtitle file is added as second FFmpeg input.
                // For single-stream files (SRT/ASS/VTT) the in-file index is always 0.
                // For multi-stream containers (MKS) we count how many streams from
                // the same file appear before the selected one.
                var inFileIndex = state.MediaSource.MediaStreams
                    .Where(s => string.Equals(s.Path, state.SubtitleStream.Path, StringComparison.Ordinal))
                    .TakeWhile(s => s.Index != state.SubtitleStream.Index)
                    .Count();

                args += string.Format(
                    CultureInfo.InvariantCulture,
                    " -map 1:{0}",
                    inFileIndex);
            }
            else
            {
                int subtitleStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.SubtitleStream);

                args += string.Format(
                    CultureInfo.InvariantCulture,
                    " -map 0:{0}",
                    subtitleStreamIndex);
            }

            return args;
        }

        private static string GetInputFormatFromLegacyContainer(string container)
        {
            if (string.Equals(container, "mpg", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "mpeg", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "rec", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "dvr-ms", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "ogm", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "divx", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "tp", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return GetInputFormatFromStreamingContainer(container);
        }

        private static string GetInputFormatFromStreamingContainer(string container)
        {
            if (string.Equals(container, "rmvb", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "rtp", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Seeing reported failures here, not sure yet if this is related to specifying input format
            if (string.Equals(container, "m4v", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // obviously don't do this for strm files
            if (string.Equals(container, "strm", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // ISO files don't have an ffmpeg format
            if (string.Equals(container, "iso", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return container;
        }

        private static string GetNonAacAudioEncoder(string codec)
        {
            if (string.Equals(codec, "mp3", StringComparison.OrdinalIgnoreCase))
            {
                return "libmp3lame";
            }

            if (string.Equals(codec, "vorbis", StringComparison.OrdinalIgnoreCase))
            {
                return "libvorbis";
            }

            if (string.Equals(codec, "opus", StringComparison.OrdinalIgnoreCase))
            {
                return "libopus";
            }

            if (string.Equals(codec, "flac", StringComparison.OrdinalIgnoreCase))
            {
                return "flac";
            }

            if (string.Equals(codec, "dts", StringComparison.OrdinalIgnoreCase))
            {
                return "dca";
            }

            if (string.Equals(codec, "alac", StringComparison.OrdinalIgnoreCase))
            {
                // The ffmpeg upstream breaks the AudioToolbox ALAC encoder in version 6.1 but fixes it in version 7.0.
                // Since ALAC is lossless in quality and the AudioToolbox encoder is not faster,
                // its only benefit is a smaller file size.
                // To prevent problems, use the ffmpeg native encoder instead.
                return "alac";
            }

            return codec.ToLowerInvariant();
        }

        private string GetInputProbeAndHeaderParams(EncodingJobInfo state)
        {
            var inputModifier = string.Empty;
            var analyzeDurationArgument = GetFfmpegAnalyzeDurationArg(state);

            if (!string.IsNullOrEmpty(analyzeDurationArgument))
            {
                inputModifier += " " + analyzeDurationArgument;
            }

            inputModifier = inputModifier.Trim();

            // Apply -probesize if configured
            var ffmpegProbeSizeArgument = GetFfmpegProbesizeArg();

            if (!string.IsNullOrEmpty(ffmpegProbeSizeArgument))
            {
                inputModifier += " " + ffmpegProbeSizeArgument;
            }

            var userAgentParam = GetUserAgentParam(state);

            if (!string.IsNullOrEmpty(userAgentParam))
            {
                inputModifier += " " + userAgentParam;
            }

            inputModifier = inputModifier.Trim();

            var refererParam = GetRefererParam(state);

            if (!string.IsNullOrEmpty(refererParam))
            {
                inputModifier += " " + refererParam;
            }

            inputModifier = inputModifier.Trim();

            return inputModifier;
        }

        private void AppendSubtitleProbeArgs(EncodingJobInfo state, StringBuilder arg)
        {
            // Use analyzeduration also for subtitle streams to improve resolution detection  with streams inside MKS files
            var analyzeDurationArgument = GetFfmpegAnalyzeDurationArg(state);
            if (!string.IsNullOrEmpty(analyzeDurationArgument))
            {
                arg.Append(' ').Append(analyzeDurationArgument);
            }

            // Apply probesize, too, if configured
            var ffmpegProbeSizeArgument = GetFfmpegProbesizeArg();
            if (!string.IsNullOrEmpty(ffmpegProbeSizeArgument))
            {
                arg.Append(' ').Append(ffmpegProbeSizeArgument);
            }
        }

        private static string GetStreamMapArgs(EncodingJobInfo state)
        {
            var args = string.Empty;

            if (state.VideoStream is not null)
            {
                int videoStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.VideoStream);

                args += string.Format(
                    CultureInfo.InvariantCulture,
                    "-map 0:{0}",
                    videoStreamIndex);
            }
            else
            {
                // No known video stream
                args += "-vn";
            }

            if (state.AudioStream is not null)
            {
                args = AppendAudioMapArgs(args, state);
            }
            else
            {
                args += " -map -0:a";
            }

            var subtitleMethod = state.SubtitleDeliveryMethod;
            if (IsSubtitleExcludedFromMap(state, subtitleMethod))
            {
                args += " -map -0:s";
            }
            else if (subtitleMethod == SubtitleDeliveryMethod.Embed)
            {
                args = AppendEmbeddedSubtitleMapArgs(args, state);
            }
            else if (IsExternalGraphicalSubtitle(state))
            {
                int externalSubtitleStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.SubtitleStream);

                args += string.Format(
                    CultureInfo.InvariantCulture,
                    " -map 1:{0} -sn",
                    externalSubtitleStreamIndex);
            }

            return args;
        }
    }
}
