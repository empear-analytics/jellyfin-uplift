using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        private static int GetAudioBitrate(long maxTotalBitrate, IReadOnlyList<string> targetAudioCodecs, MediaStream? audioStream, StreamInfo item)
        {
            string? targetAudioCodec = targetAudioCodecs.Count == 0 ? null : targetAudioCodecs[0];

            int? targetAudioChannels = item.GetTargetAudioChannels(targetAudioCodec);

            int defaultBitrate;
            int encoderAudioBitrateLimit = int.MaxValue;

            if (audioStream is null)
            {
                defaultBitrate = 192000;
            }
            else
            {
                if (IsDownmixing(targetAudioChannels, audioStream))
                {
                    // Reduce the bitrate if we're down mixing.
                    defaultBitrate = GetDefaultAudioBitrate(targetAudioCodec, targetAudioChannels);
                }
                else if (IsAtMostTargetChannels(targetAudioChannels, audioStream.Channels)
                         && IsNotAnyTargetAudioCodec(audioStream, targetAudioCodecs))
                {
                    // Shift the bitrate if we're transcoding to a different audio codec.
                    defaultBitrate = GetDefaultAudioBitrate(targetAudioCodec, audioStream.Channels.Value);
                }
                else
                {
                    defaultBitrate = audioStream.BitRate ?? GetDefaultAudioBitrate(targetAudioCodec, targetAudioChannels);
                }

                // Seeing webm encoding failures when source has 1 audio channel and 22k bitrate.
                // Any attempts to transcode over 64k will fail
                if (audioStream.Channels == 1
                    && (audioStream.BitRate ?? 0) < 64000)
                {
                    encoderAudioBitrateLimit = 64000;
                }
            }

            if (maxTotalBitrate > 0)
            {
                defaultBitrate = Math.Min(GetMaxAudioBitrateForTotalBitrate(maxTotalBitrate), defaultBitrate);
            }

            return Math.Min(defaultBitrate, encoderAudioBitrateLimit);
        }

        private static bool IsNotAnyTargetAudioCodec(MediaStream audioStream, IReadOnlyList<string> targetAudioCodecs)
        {
            return !string.IsNullOrEmpty(audioStream.Codec)
                && targetAudioCodecs is not null
                && targetAudioCodecs.Count > 0
                && !targetAudioCodecs.Any(elem => string.Equals(audioStream.Codec, elem, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsAtMostTargetChannels(int? targetAudioChannels, [NotNullWhen(true)] int? audioStreamChannels)
        {
            return targetAudioChannels.HasValue
                && audioStreamChannels.HasValue
                && audioStreamChannels.Value <= targetAudioChannels.Value;
        }

        private static bool IsDownmixing(int? targetAudioChannels, MediaStream audioStream)
        {
            return targetAudioChannels.HasValue
                && audioStream.Channels.HasValue
                && audioStream.Channels.Value > targetAudioChannels.Value;
        }
    }
}
