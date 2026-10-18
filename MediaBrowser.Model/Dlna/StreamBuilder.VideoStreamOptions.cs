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
        private static void CopyVideoStreamOptions(StreamInfo playlistItem, MediaStream? videoStream, string? qualifier)
        {
            if (videoStream?.Level is not null)
            {
                playlistItem.SetOption(qualifier, "level", videoStream.Level.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (videoStream?.BitDepth is not null)
            {
                playlistItem.SetOption(qualifier, "videobitdepth", videoStream.BitDepth.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(videoStream?.Profile))
            {
                playlistItem.SetOption(qualifier, "profile", videoStream.Profile.ToLowerInvariant());
            }
        }

        private static List<string> GetPreferredVideoCodecs(StreamInfo playlistItem, MediaStream? videoStream, string? videoCodec)
        {
            // Prefer matching video codecs
            var videoCodecs = ContainerHelper.Split(videoCodec).ToList();

            if (videoCodecs.Count == 0 && videoStream is not null)
            {
                // Add the original codec if no codec is specified
                videoCodecs.Add(videoStream.Codec);
            }

            // Enforce HLS video codec restrictions
            if (playlistItem.SubProtocol == MediaStreamProtocol.hls)
            {
                videoCodecs = videoCodecs.Where(codec => _supportedHlsVideoCodecs.Contains(codec)).ToList();
            }

            return videoCodecs;
        }

        private static void CopyAudioProfileAndLevel(StreamInfo playlistItem, MediaStream audioStream)
        {
            if (!string.IsNullOrEmpty(audioStream.Profile))
            {
                playlistItem.SetOption(audioStream.Codec, "profile", audioStream.Profile.ToLowerInvariant());
            }

            if (audioStream.Level.HasValue && audioStream.Level.Value != 0)
            {
                playlistItem.SetOption(audioStream.Codec, "level", audioStream.Level.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static List<string> FilterHlsAudioCodecs(StreamInfo playlistItem, List<string> audioCodecs)
        {
            if (playlistItem.SubProtocol == MediaStreamProtocol.hls)
            {
                if (string.Equals(playlistItem.Container, "mp4", StringComparison.OrdinalIgnoreCase))
                {
                    audioCodecs = audioCodecs.Where(codec => _supportedHlsAudioCodecsMp4.Contains(codec)).ToList();
                }
                else
                {
                    audioCodecs = audioCodecs.Where(codec => _supportedHlsAudioCodecsTs.Contains(codec)).ToList();
                }
            }

            return audioCodecs;
        }
    }
}
