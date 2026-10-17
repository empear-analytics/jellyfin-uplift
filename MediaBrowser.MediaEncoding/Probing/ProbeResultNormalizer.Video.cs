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

        private void FetchWtvInfo(MediaInfo video, InternalMediaInfoResult data)
        {
            var tags = data.Format?.Tags;

            if (tags is null)
            {
                return;
            }

            SetWtvGenres(video, tags);

            if (TryGetParentalRating(tags, out var officialRating))
            {
                video.OfficialRating = officialRating;
            }

            if (TryGetMediaCredits(tags, out var people))
            {
                video.People = Array.ConvertAll(
                    people.Split(_basicDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    i => new BaseItemPerson { Name = i, Type = PersonKind.Actor });
            }

            SetWtvDates(video, tags);

            var description = tags.GetValueOrDefault("WM/SubTitleDescription");

            var subTitle = tags.GetValueOrDefault("WM/SubTitle");

            // For below code, credit to MCEBuddy: https://mcebuddy2x.codeplex.com/

            // Sometimes for TV Shows the Subtitle field is empty and the subtitle description contains the subtitle, extract if possible. See ticket https://mcebuddy2x.codeplex.com/workitem/1910
            // The format is -> EPISODE/TOTAL_EPISODES_IN_SEASON. SUBTITLE: DESCRIPTION
            // OR -> COMMENT. SUBTITLE: DESCRIPTION
            // e.g. -> 4/13. The Doctor's Wife: Science fiction drama. When he follows a Time Lord distress signal, the Doctor puts Amy, Rory and his beloved TARDIS in grave danger. Also in HD. [AD,S]
            // e.g. -> CBeebies Bedtime Hour. The Mystery: Animated adventures of two friends who live on an island in the middle of the big city. Some of Abney and Teal's favourite objects are missing. [S]
            description = ParseDescriptionFromSubtitle(video, subTitle, description);

            if (!string.IsNullOrWhiteSpace(description))
            {
                video.Overview = description;
            }
        }

        private static bool TryGetParentalRating(IReadOnlyDictionary<string, string> tags, out string officialRating)
        {
            return tags.TryGetValue("WM/ParentalRating", out officialRating) && !string.IsNullOrWhiteSpace(officialRating);
        }

        private static bool TryGetMediaCredits(IReadOnlyDictionary<string, string> tags, out string people)
        {
            return tags.TryGetValue("WM/MediaCredits", out people) && !string.IsNullOrEmpty(people);
        }

        private static void SetWtvDates(MediaInfo video, IReadOnlyDictionary<string, string> tags)
        {
            if (tags.TryGetValue("WM/OriginalReleaseTime", out var year) && int.TryParse(year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear))
            {
                video.ProductionYear = parsedYear;
            }

            // Credit to MCEBuddy: https://mcebuddy2x.codeplex.com/
            // DateTime is reported along with timezone info (typically Z i.e. UTC hence assume None)
            if (tags.TryGetValue("WM/MediaOriginalBroadcastDateTime", out var premiereDateString) && DateTime.TryParse(year, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedDate))
            {
                video.PremiereDate = parsedDate;
            }
        }

        private static void SetWtvGenres(MediaInfo video, IReadOnlyDictionary<string, string> tags)
        {
            if (tags.TryGetValue("WM/Genre", out var genres) && !string.IsNullOrWhiteSpace(genres))
            {
                var genreList = genres.Split(_genreDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                // If this is empty then don't overwrite genres that might have been fetched earlier
                if (genreList.Length > 0)
                {
                    video.Genres = genreList;
                }
            }
        }

        private string ParseDescriptionFromSubtitle(MediaInfo video, string subTitle, string description)
        {
            if (DescriptionContainsSubtitle(subTitle, description)) // Check within the Subtitle size limit, otherwise from description it can get too long creating an invalid filename
            {
                string[] descriptionParts = description.Split(':');
                if (descriptionParts.Length > 0)
                {
                    string subtitle = descriptionParts[0];
                    try
                    {
                        // Check if it contains a episode number and season number
                        if (subtitle.Contains('/', StringComparison.Ordinal))
                        {
                            string[] subtitleParts = subtitle.Split(' ');
                            string[] numbers = subtitleParts[0].Replace(".", string.Empty, StringComparison.Ordinal).Split('/');
                            video.IndexNumber = int.Parse(numbers[0], CultureInfo.InvariantCulture);
                            // int totalEpisodesInSeason = int.Parse(numbers[1], CultureInfo.InvariantCulture);

                            // Skip the numbers, concatenate the rest, trim and set as new description
                            description = string.Join(' ', subtitleParts, 1, subtitleParts.Length - 1).Trim();
                        }
                        else if (subtitle.Contains('.', StringComparison.Ordinal))
                        {
                            var subtitleParts = subtitle.Split('.');
                            description = string.Join('.', subtitleParts, 1, subtitleParts.Length - 1).Trim();
                        }
                        else
                        {
                            description = subtitle.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while parsing subtitle field");

                        // Fallback to default parsing
                        if (subtitle.Contains('.', StringComparison.Ordinal))
                        {
                            var subtitleParts = subtitle.Split('.');
                            description = string.Join('.', subtitleParts, 1, subtitleParts.Length - 1).Trim();
                        }
                        else
                        {
                            description = subtitle.Trim();
                        }
                    }
                }
            }

            return description;
        }

        private static bool DescriptionContainsSubtitle(string subTitle, string description)
        {
            return string.IsNullOrWhiteSpace(subTitle)
                && !string.IsNullOrWhiteSpace(description)
                && description.AsSpan()[..Math.Min(description.Length, MaxSubtitleDescriptionExtractionLength)].Contains(':');
        }

        private void ExtractTimestamp(MediaInfo video)
        {
            if (video.VideoType != VideoType.VideoFile)
            {
                return;
            }

            // Skip timestamp extration for remote resource (http, rtsp, etc.)
            // as they cannot be opened with FileStream
            if (video.Protocol != MediaProtocol.File)
            {
                return;
            }

            if (IsNotMpegTransportStreamContainer(video))
            {
                return;
            }

            try
            {
                video.Timestamp = GetMpegTimestamp(video.Path);
                _logger.LogDebug("Video has {Timestamp} timestamp", video.Timestamp);
            }
            catch (Exception ex)
            {
                video.Timestamp = null;
                _logger.LogError(ex, "Error extracting timestamp info from {Path}", video.Path);
            }
        }

        private static bool IsNotMpegTransportStreamContainer(MediaInfo video)
        {
            return !string.Equals(video.Container, "mpeg2ts", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(video.Container, "m2ts", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(video.Container, "ts", StringComparison.OrdinalIgnoreCase);
        }

        // REVIEW: find out why the byte array needs to be 197 bytes long and comment the reason
        private static TransportStreamTimestamp GetMpegTimestamp(string path)
        {
            var packetBuffer = new byte[197];

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1))
            {
                fs.ReadExactly(packetBuffer);
            }

            if (packetBuffer[0] == 71)
            {
                return TransportStreamTimestamp.None;
            }

            if ((packetBuffer[4] != 71) || (packetBuffer[196] != 71))
            {
                return TransportStreamTimestamp.None;
            }

            if ((packetBuffer[0] == 0) && (packetBuffer[1] == 0) && (packetBuffer[2] == 0) && (packetBuffer[3] == 0))
            {
                return TransportStreamTimestamp.Zero;
            }

            return TransportStreamTimestamp.Valid;
        }
    }
}
