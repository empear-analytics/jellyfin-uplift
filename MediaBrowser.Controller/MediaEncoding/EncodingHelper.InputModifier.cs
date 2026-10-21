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
        /// Gets the user agent param.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetUserAgentParam(EncodingJobInfo state)
        {
            if (state.RemoteHttpHeaders.TryGetValue("User-Agent", out string useragent))
            {
                return "-user_agent \"" + useragent + "\"";
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the referer param.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetRefererParam(EncodingJobInfo state)
        {
            if (state.RemoteHttpHeaders.TryGetValue("Referer", out string referer))
            {
                return "-referer \"" + referer + "\"";
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the fast seek command line parameter.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="options">The options.</param>
        /// <param name="segmentContainer">Segment Container.</param>
        /// <returns>System.String.</returns>
        /// <value>The fast seek command line parameter.</value>
        public string GetFastSeekCommandLineParameter(EncodingJobInfo state, EncodingOptions options, string segmentContainer)
        {
            var time = state.BaseRequest.StartTimeTicks ?? 0;
            var maxTime = state.RunTimeTicks ?? 0;
            var seekParam = string.Empty;

            if (time > 0)
            {
                // For direct streaming/remuxing, HLS segments start at keyframes.
                // However, ffmpeg will seek to previous keyframe when the exact frame time is the input
                // Workaround this by adding 0.5s offset to the seeking time to get the exact keyframe on most videos.
                // This will help subtitle syncing.
                var isHlsRemuxing = state.IsVideoRequest && state.TranscodingType is TranscodingJobType.Hls && IsCopyCodec(state.OutputVideoCodec);
                var seekTick = isHlsRemuxing ? time + 5000000L : time;

                // Seeking beyond EOF makes no sense in transcoding. Clamp the seekTick value to
                // [0, RuntimeTicks - 5.0s], so that the muxer gets packets and avoid error codes.
                if (maxTime > 0)
                {
                    seekTick = Math.Clamp(seekTick, 0, Math.Max(maxTime - 50000000L, 0));
                }

                seekParam += string.Format(CultureInfo.InvariantCulture, "-ss {0}", _mediaEncoder.GetTimeParameter(seekTick));
            }

            return seekParam;
        }

        private string GetFfmpegAnalyzeDurationArg(EncodingJobInfo state)
        {
            var analyzeDurationArgument = string.Empty;

            // Apply -analyzeduration as per the environment variable,
            // otherwise ffmpeg will break on certain files due to default value is 0.
            var ffmpegAnalyzeDuration = _config.GetFFmpegAnalyzeDuration() ?? string.Empty;

            if (state.MediaSource.AnalyzeDurationMs > 0)
            {
                analyzeDurationArgument = "-analyzeduration " + (state.MediaSource.AnalyzeDurationMs.Value * 1000).ToString(CultureInfo.InvariantCulture);
            }
            else if (!string.IsNullOrEmpty(ffmpegAnalyzeDuration))
            {
                analyzeDurationArgument = "-analyzeduration " + ffmpegAnalyzeDuration;
            }

            return analyzeDurationArgument;
        }

        private string GetFfmpegProbesizeArg()
        {
            var ffmpegProbeSize = _config.GetFFmpegProbeSize();

            if (!string.IsNullOrEmpty(ffmpegProbeSize))
            {
                return $"-probesize {ffmpegProbeSize}";
            }

            return string.Empty;
        }

        public string GetInputModifier(EncodingJobInfo state, EncodingOptions encodingOptions, string segmentContainer)
        {
            var inputModifier = GetInputProbeAndHeaderParams(state);

            inputModifier += " " + GetFastSeekCommandLineParameter(state, encodingOptions, segmentContainer);
            inputModifier = inputModifier.Trim();

            inputModifier = AppendRtspAndAudioSyncOptions(inputModifier, state);

            // The -fps_mode option cannot be applied to input
            if (NeedsInputVideoSync(state))
            {
                inputModifier += GetVideoSyncOption(state.InputVideoSync, _mediaEncoder.EncoderVersion);
            }

            int readrate = 0;
            if (ShouldReadAtNativeFramerate(state))
            {
                readrate = 1;
                inputModifier += " -re";
            }
            else if (NeedsSegmentDeletionReadrate(state, encodingOptions))
            {
                // Set an input read rate limit 10x for using SegmentDeletion with stream-copy
                // to prevent ffmpeg from exiting prematurely (due to fast drive)
                readrate = 10;
                inputModifier += $" -readrate {readrate}";
            }

            // Set a larger catchup value to revert to the old behavior,
            // otherwise, remuxing might stall due to this new option
            if (NeedsReadrateCatchup(readrate))
            {
                inputModifier += $" -readrate_catchup {readrate * 100}";
            }

            inputModifier = AppendInputFormatFlags(inputModifier, state);

            if (state.IsVideoRequest)
            {
                inputModifier = AppendExplicitInputFormat(inputModifier, state, encodingOptions);
            }

            if (state.MediaSource.RequiresLooping)
            {
                inputModifier += " -stream_loop -1 -reconnect_at_eof 1 -reconnect_streamed 1 -reconnect_delay_max 2";
            }

            return inputModifier;
        }

        private static string AppendRtspAndAudioSyncOptions(string inputModifier, EncodingJobInfo state)
        {
            if (state.InputProtocol == MediaProtocol.Rtsp)
            {
                inputModifier += " -rtsp_transport tcp+udp -rtsp_flags prefer_tcp";
            }

            if (!string.IsNullOrEmpty(state.InputAudioSync))
            {
                inputModifier += " -async " + state.InputAudioSync;
            }

            return inputModifier;
        }

        private bool NeedsInputVideoSync(EncodingJobInfo state)
        {
            return !string.IsNullOrEmpty(state.InputVideoSync) && _mediaEncoder.EncoderVersion < new Version(5, 1);
        }

        private static bool ShouldReadAtNativeFramerate(EncodingJobInfo state)
        {
            return state.ReadInputAtNativeFramerate && state.InputProtocol != MediaProtocol.Rtsp;
        }

        private bool NeedsReadrateCatchup(int readrate)
        {
            return readrate > 0 && _mediaEncoder.EncoderVersion >= _minFFmpegReadrateCatchupOption;
        }

        private static string AppendExplicitInputFormat(string inputModifier, EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            if (NeedsExplicitInputFormat(state, encodingOptions))
            {
                var inputFormat = GetInputFormat(state.InputContainer);
                if (!string.IsNullOrEmpty(inputFormat))
                {
                    inputModifier += " -f " + inputFormat;
                }
            }

            return inputModifier;
        }

        private static string AppendInputFormatFlags(string inputModifier, EncodingJobInfo state)
        {
            var flags = new List<string>();
            if (state.IgnoreInputDts)
            {
                flags.Add("+igndts");
            }

            if (state.IgnoreInputIndex)
            {
                flags.Add("+ignidx");
            }

            if (state.GenPtsInput || IsCopyCodec(state.OutputVideoCodec))
            {
                flags.Add("+genpts");
            }

            if (state.DiscardCorruptFramesInput)
            {
                flags.Add("+discardcorrupt");
            }

            if (state.EnableFastSeekInput)
            {
                flags.Add("+fastseek");
            }

            if (flags.Count > 0)
            {
                inputModifier += " -fflags " + string.Join(string.Empty, flags);
            }

            return inputModifier;
        }

        private bool NeedsSegmentDeletionReadrate(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            return encodingOptions.EnableSegmentDeletion
                && state.VideoStream is not null
                && state.TranscodingType == TranscodingJobType.Hls
                && IsCopyCodec(state.OutputVideoCodec)
                && _mediaEncoder.EncoderVersion >= _minFFmpegReadrateOption;
        }

        private static bool NeedsExplicitInputFormat(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            return !string.IsNullOrEmpty(state.InputContainer) && state.VideoType == VideoType.VideoFile && encodingOptions.HardwareAccelerationType != HardwareAccelerationType.none;
        }

        public static string GetVideoSyncOption(string videoSync, Version encoderVersion)
        {
            if (string.IsNullOrEmpty(videoSync))
            {
                return string.Empty;
            }

            return GetVersionedVideoSyncOption(videoSync, encoderVersion);
        }

        private static string GetVersionedVideoSyncOption(string videoSync, Version encoderVersion)
        {
            if (encoderVersion >= new Version(5, 1))
            {
                return GetFpsModeFromVideoSync(videoSync);
            }

            // -vsync is deprecated in FFmpeg 5.1 and will be removed in the future.
            return $" -vsync {videoSync}";
        }

        private static string GetFpsModeFromVideoSync(string videoSync)
        {
            if (int.TryParse(videoSync, CultureInfo.InvariantCulture, out var vsync))
            {
                return vsync switch
                {
                    -1 => " -fps_mode auto",
                    0 => " -fps_mode passthrough",
                    1 => " -fps_mode cfr",
                    2 => " -fps_mode vfr",
                    _ => string.Empty
                };
            }

            return string.Empty;
        }
    }
}
