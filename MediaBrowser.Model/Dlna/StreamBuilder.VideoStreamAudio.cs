using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        private static bool IsDirectAudioStreamSatisfied(MediaStream? audioStreamWithSupportedCodec, bool channelsExceedsLimit, TranscodeReason directAudioFailures, StreamInfo playlistItem)
        {
            var directAudioStreamSatisfied = audioStreamWithSupportedCodec is not null && !channelsExceedsLimit
                && directAudioFailures == 0;

            directAudioStreamSatisfied = directAudioStreamSatisfied && !playlistItem.TranscodeReasons.HasFlag(TranscodeReason.ContainerBitrateExceedsLimit);
            return directAudioStreamSatisfied;
        }

        private static List<string> GetPreferredAudioCodecs(StreamInfo playlistItem, MediaStream? audioStream, string? audioCodec)
        {
            // Prefer matching audio codecs, could do better here
            var audioCodecs = ContainerHelper.Split(audioCodec).ToList();

            if (audioCodecs.Count == 0 && audioStream is not null)
            {
                // Add the original codec if no codec is specified
                audioCodecs.Add(audioStream.Codec);
            }

            // Enforce HLS audio codec restrictions
            audioCodecs = FilterHlsAudioCodecs(playlistItem, audioCodecs);

            return audioCodecs;
        }

        private static bool IsAudioChannelLimitExceeded(MediaStream? audioStreamWithSupportedCodec, StreamInfo playlistItem)
        {
            return audioStreamWithSupportedCodec is not null && audioStreamWithSupportedCodec.Channels > (playlistItem.TranscodingMaxAudioChannels ?? int.MaxValue);
        }

        private TranscodeReason GetDirectAudioFailures(MediaOptions options, MediaSourceInfo item, string? container, MediaStream? audioStreamWithSupportedCodec)
        {
            return audioStreamWithSupportedCodec is null ? default : GetCompatibilityAudioCodec(new GetCompatibilityAudioCodecParts(options, item, container ?? string.Empty, audioStreamWithSupportedCodec), null, true, false);
        }
    }
}
