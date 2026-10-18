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
        private void ApplyVideoCodecProfileConditions(StreamInfo playlistItem, IEnumerable<CodecProfile> appliedVideoConditions, string? container, bool useSubContainer)
        {
            foreach (var condition in appliedVideoConditions)
            {
                foreach (var transcodingVideoCodec in playlistItem.VideoCodecs)
                {
                    if (condition.ContainsAnyCodec(transcodingVideoCodec, container, useSubContainer))
                    {
                        ApplyTranscodingConditions(playlistItem, condition.Conditions, transcodingVideoCodec, true, true);
                        continue;
                    }
                }
            }
        }

        private void ApplyAudioCodecProfileConditions(StreamInfo playlistItem, IEnumerable<CodecProfile> appliedAudioConditions, string? container)
        {
            foreach (var codecProfile in appliedAudioConditions)
            {
                foreach (var transcodingAudioCodec in playlistItem.AudioCodecs)
                {
                    if (codecProfile.ContainsAnyCodec(transcodingAudioCodec, container))
                    {
                        ApplyTranscodingConditions(playlistItem, codecProfile.Conditions, transcodingAudioCodec, true, true);
                        break;
                    }
                }
            }
        }

        private static void ApplyMaxVideoBitrate(StreamInfo playlistItem, int? maxBitrateSetting)
        {
            if (maxBitrateSetting.HasValue)
            {
                var availableBitrateForVideo = maxBitrateSetting.Value;

                if (playlistItem.AudioBitrate.HasValue)
                {
                    availableBitrateForVideo -= playlistItem.AudioBitrate.Value;
                }

                // Make sure the video bitrate is lower than bitrate settings but at least 64k
                // Don't use Math.Clamp as availableBitrateForVideo can be lower then 64k.
                var currentValue = playlistItem.VideoBitrate ?? availableBitrateForVideo;
                playlistItem.VideoBitrate = Math.Max(Math.Min(availableBitrateForVideo, currentValue), 64_000);
            }
        }
    }
}
