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

            if (IsZeroPacketHeader(packetBuffer))
            {
                return TransportStreamTimestamp.Zero;
            }

            return TransportStreamTimestamp.Valid;
        }

        private static bool IsZeroPacketHeader(byte[] packetBuffer)
        {
            return (packetBuffer[0] == 0) && (packetBuffer[1] == 0) && (packetBuffer[2] == 0) && (packetBuffer[3] == 0);
        }
    }
}
