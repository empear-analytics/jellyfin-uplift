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
        private static int GetAudioCommonLossyBitrate(int inputChannels, int outputChannels, int bitrate)
        {
#pragma warning disable SA1008
            return (inputChannels, outputChannels) switch
            {
                ( >= 6, >= 6 or 0) => Math.Min(640000, bitrate),
                ( > 0, > 0) => Math.Min(outputChannels * 128000, bitrate),
                ( > 0, _) => Math.Min(inputChannels * 128000, bitrate),
                (_, _) => Math.Min(384000, bitrate)
            };
#pragma warning restore SA1008
        }

        private static bool IsDtsAudioCodec(string audioCodec)
        {
            return string.Equals(audioCodec, "dts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "dca", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDefaultOrCommonLossyAudioCodec(string audioCodec)
        {
            return string.IsNullOrEmpty(audioCodec)
                || string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "mp3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "opus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "vorbis", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "ac3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "eac3", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLameVbrModeParam(int bitrate, int bitratePerChannel)
        {
            // lame's VBR is only good for a certain bitrate range
            // For very low and very high bitrate, use abr mode
            if (bitratePerChannel is < 122500 and > 48000)
            {
                return " -qscale:a " + bitratePerChannel switch
                {
                    < 64000 => "6",
                    < 88000 => "4",
                    < 112000 => "2",
                    _ => "0"
                };
            }

            return " -abr:a 1" + " -b:a " + bitrate;
        }

        private static string GetAudioNonFdkVbrModeParam(string encoder, int bitrate, int bitratePerChannel)
        {
            if (string.Equals(encoder, "libmp3lame", StringComparison.OrdinalIgnoreCase))
            {
                return GetLameVbrModeParam(bitrate, bitratePerChannel);
            }

            if (string.Equals(encoder, "aac_at", StringComparison.OrdinalIgnoreCase))
            {
                // aac_at's CVBR mode
                return " -aac_at_mode:a 2" + " -b:a " + bitrate;
            }

            if (string.Equals(encoder, "libvorbis", StringComparison.OrdinalIgnoreCase))
            {
                return " -qscale:a " + bitratePerChannel switch
                {
                    < 40000 => "0",
                    < 56000 => "2",
                    < 80000 => "4",
                    < 112000 => "6",
                    _ => "8"
                };
            }

            return null;
        }

        private static void AddStereoDownMixFilters(EncodingJobInfo state, EncodingOptions encodingOptions, List<string> filters)
        {
            var hasDownMixFilter = DownMixAlgorithmsHelper.AlgorithmFilterStrings.TryGetValue((encodingOptions.DownMixStereoAlgorithm, DownMixAlgorithmsHelper.InferChannelLayout(state.AudioStream)), out var downMixFilterString);
            if (hasDownMixFilter)
            {
                filters.Add(downMixFilterString);
            }

            if (!encodingOptions.DownMixAudioBoost.Equals(1))
            {
                filters.Add("volume=" + encodingOptions.DownMixAudioBoost.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static bool NeedsSubtitleAudioTimestampShift(EncodingJobInfo state, bool isCopyingTimestamps)
        {
            return state.SubtitleStream is not null && state.SubtitleStream.IsTextSubtitleStream && ShouldEncodeSubtitle(state) && !isCopyingTimestamps;
        }

        private static int? GetHlsCompatibleChannelCount(int? resultChannels)
        {
            // We can let FFMpeg supply an extra LFE channel for 5ch and 7ch to make them 5.1 and 7.1
            if (resultChannels == 5)
            {
                resultChannels = 6;
            }
            else if (resultChannels == 7)
            {
                resultChannels = 8;
            }
            else
            {
                // For other weird layout, just downmix to stereo for compatibility
                resultChannels = 2;
            }

            return resultChannels;
        }

        private static bool HasUnsupportedHlsChannelLayout(EncodingJobInfo state, int? resultChannels)
        {
            return state.TranscodingType != TranscodingJobType.Progressive
                && ((resultChannels > 2 && resultChannels < 6) || resultChannels == 7);
        }

        private int? GetAudioTranscodeChannels(EncodingJobInfo state, BaseEncodingJobOptions request, string codec, int? resultChannels)
        {
            var isTranscodingAudio = !IsCopyCodec(codec);

            if (isTranscodingAudio)
            {
                var audioEncoder = GetAudioEncoder(state);
                if (!_audioTranscodeChannelLookup.TryGetValue(audioEncoder, out var transcoderChannelLimit))
                {
                    // Set default max transcoding channels to 8 to prevent encoding errors due to asking for too many channels.
                    transcoderChannelLimit = 8;
                }

                // Set resultChannels to minimum between resultChannels, TranscodingMaxAudioChannels, transcoderChannelLimit
                resultChannels = transcoderChannelLimit < resultChannels ? transcoderChannelLimit : resultChannels ?? transcoderChannelLimit;

                if (request.TranscodingMaxAudioChannels < resultChannels)
                {
                    resultChannels = request.TranscodingMaxAudioChannels;
                }

                // Avoid transcoding to audio channels other than 1ch, 2ch, 6ch (5.1 layout) and 8ch (7.1 layout).
                // https://developer.apple.com/documentation/http_live_streaming/hls_authoring_specification_for_apple_devices
                if (HasUnsupportedHlsChannelLayout(state, resultChannels))
                {
                    resultChannels = GetHlsCompatibleChannelCount(resultChannels);
                }
            }

            return resultChannels;
        }

        private static int GetAudioDefaultBitrate(MediaStream audioStream, int? outputAudioChannels)
        {
            return 128000 * (outputAudioChannels ?? audioStream.Channels ?? 2);
        }
    }
}
