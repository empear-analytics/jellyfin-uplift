#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Probing
{
    public partial class ProbeResultNormalizer
    {
        private void SetVideoInfo(MediaInfo info, InternalMediaInfoResult data, Dictionary<string, string> tags)
        {
            FetchStudios(info, tags, "copyright");

            SetOfficialRatingFromItunExtc(info, tags);

            var iTunXml = tags.GetFirstNotNullNorWhiteSpaceValue("iTunMOVI");
            if (iTunXml is not null)
            {
                FetchFromItunesInfo(iTunXml, info);
            }

            if (data.Format is not null && !string.IsNullOrEmpty(data.Format.Duration))
            {
                info.RunTimeTicks = TimeSpan.FromSeconds(double.Parse(data.Format.Duration, CultureInfo.InvariantCulture)).Ticks;
            }

            FetchWtvInfo(info, data);

            ExtractTimestamp(info);

            if (tags.TryGetValue("stereo_mode", out var stereoMode) && string.Equals(stereoMode, "left_right", StringComparison.OrdinalIgnoreCase))
            {
                info.Video3DFormat = Video3DFormat.FullSideBySide;
            }

            EstimateAudioBitrates(info);

            EstimateVideoBitrate(info);

            // If the container bitrate is still unknown, infer it from the sum of the streams.
            info.InferTotalBitrate();
        }

        private static void SetOfficialRatingFromItunExtc(MediaInfo info, Dictionary<string, string> tags)
        {
            var iTunExtc = tags.GetFirstNotNullNorWhiteSpaceValue("iTunEXTC");
            if (iTunExtc is not null)
            {
                var parts = iTunExtc.Split('|', StringSplitOptions.RemoveEmptyEntries);
                // Example
                // mpaa|G|100|For crude humor
                if (parts.Length > 1)
                {
                    info.OfficialRating = parts[1];

                    if (parts.Length > 3)
                    {
                        info.OfficialRatingDescription = parts[3];
                    }
                }
            }
        }

        private static void EstimateAudioBitrates(MediaInfo info)
        {
            foreach (var mediaStream in info.MediaStreams)
            {
                if (mediaStream.Type == MediaStreamType.Audio && !mediaStream.BitRate.HasValue)
                {
                    mediaStream.BitRate = GetEstimatedAudioBitrate(mediaStream.Codec, mediaStream.Profile, mediaStream.Channels);
                }
            }
        }

        private static void EstimateVideoBitrate(MediaInfo info)
        {
            // ffprobe frequently omits the per-stream video bitrate (common in MP4/MKV containers).
            // Estimate the missing video bitrate as the container bitrate minus the combined stream bitrates.
            var videoStreams = info.MediaStreams.Where(i => i.Type == MediaStreamType.Video).ToList();
            if (CanEstimateVideoBitrate(info, videoStreams))
            {
                var otherStreams = info.MediaStreams
                    .Where(i => i.Type != MediaStreamType.Video && !i.IsExternal)
                    .ToList();

                // Only attribute the leftover bitrate to the video stream if every audio stream's bitrate is known.
                var audioBitratesKnown = otherStreams
                    .Where(i => i.Type == MediaStreamType.Audio)
                    .All(i => i.BitRate.HasValue);

                if (audioBitratesKnown)
                {
                    var estimatedVideoBitrate = info.Bitrate.Value - otherStreams.Sum(i => i.BitRate ?? 0);
                    if (estimatedVideoBitrate > 0)
                    {
                        videoStreams[0].BitRate = estimatedVideoBitrate;
                    }
                }
            }
        }

        private static bool CanEstimateVideoBitrate(MediaInfo info, List<MediaStream> videoStreams)
        {
            return info.Bitrate.HasValue
                && videoStreams.Count == 1
                && !videoStreams[0].BitRate.HasValue;
        }

        // REVIEW: find out why the byte array needs to be 197 bytes long and comment the reason
    }
}
