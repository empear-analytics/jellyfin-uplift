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
        private void ApplyAudioBitrateLimit(MediaOptions options, StreamInfo playlistItem, MediaStream? audioStream)
        {
            int audioBitrate = GetAudioBitrate(options.GetMaxBitrate(true) ?? 0, playlistItem.TargetAudioCodec, audioStream, playlistItem);
            playlistItem.AudioBitrate = Math.Min(playlistItem.AudioBitrate ?? audioBitrate, audioBitrate);
        }

        private static void ApplyAudioChannelLimit(StreamInfo playlistItem, bool channelsExceedsLimit)
        {
            if (channelsExceedsLimit && playlistItem.TargetAudioStream is not null)
            {
                playlistItem.TranscodeReasons |= TranscodeReason.AudioChannelsNotSupported;
                playlistItem.TargetAudioStream.Channels = playlistItem.TranscodingMaxAudioChannels;
            }
        }

        private static void MarkUnsupportedVideoCodec(StreamInfo playlistItem, MediaStream? videoStream, List<string> videoCodecs)
        {
            if (videoStream is not null && !ContainerHelper.ContainsContainer(videoCodecs, false, videoStream.Codec))
            {
                playlistItem.TranscodeReasons |= TranscodeReason.VideoCodecNotSupported;
            }
        }

        private static void MarkUnsupportedAudioCodec(StreamInfo playlistItem, MediaStream? audioStream, MediaStream? audioStreamWithSupportedCodec)
        {
            if (audioStream is not null && audioStreamWithSupportedCodec is null)
            {
                playlistItem.TranscodeReasons |= TranscodeReason.AudioCodecNotSupported;
            }
        }
    }
}
