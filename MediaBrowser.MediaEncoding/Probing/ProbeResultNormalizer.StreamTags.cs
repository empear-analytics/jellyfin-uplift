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
        private static void SetBitRateFromTags(MediaStream stream, MediaStreamInfo streamInfo)
        {
            // Extract bitrate info from tag "BPS" if possible.
            if (stream.BitRate.HasValue)
            {
                return;
            }

            if (streamInfo.CodecType != CodecType.Audio
                && streamInfo.CodecType != CodecType.Video)
            {
                return;
            }

            var bps = GetBPSFromTags(streamInfo);
            if (bps > 0)
            {
                stream.BitRate = bps;
                return;
            }

            // Get average bitrate info from tag "NUMBER_OF_BYTES" and "DURATION" if possible.
            var durationInSeconds = GetRuntimeSecondsFromTags(streamInfo);
            var bytes = GetNumberOfBytesFromTags(streamInfo);
            if (!HasDurationAndSize(durationInSeconds, bytes))
            {
                return;
            }

            bps = Convert.ToInt32(bytes * 8 / durationInSeconds, CultureInfo.InvariantCulture);
            if (bps > 0)
            {
                stream.BitRate = bps;
            }
        }

        private static bool HasDurationAndSize(double? durationInSeconds, long? bytes)
        {
            return durationInSeconds is not null && durationInSeconds.Value >= 1 && bytes is not null;
        }

        private static int? GetBPSFromTags(MediaStreamInfo streamInfo)
        {
            if (streamInfo?.Tags is null)
            {
                return null;
            }

            var bps = GetDictionaryValue(streamInfo.Tags, "BPS-eng") ?? GetDictionaryValue(streamInfo.Tags, "BPS");
            if (int.TryParse(bps, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBps))
            {
                return parsedBps;
            }

            return null;
        }

        private static double? GetRuntimeSecondsFromTags(MediaStreamInfo streamInfo)
        {
            if (streamInfo?.Tags is null)
            {
                return null;
            }

            var duration = GetDictionaryValue(streamInfo.Tags, "DURATION-eng") ?? GetDictionaryValue(streamInfo.Tags, "DURATION");
            if (!string.IsNullOrEmpty(duration))
            {
                // Matroska DURATION tags use nanosecond precision (e.g. "00:00:05.023000000"), but
                // TimeSpan only supports up to 7 fractional digits (ticks). Trim the surplus digits so
                // these durations parse instead of being silently dropped.
                duration = DurationOverPrecisionRegex().Replace(duration, "$1");
                if (TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out var parsedDuration))
                {
                    return parsedDuration.TotalSeconds;
                }
            }

            return null;
        }

        private static long? GetNumberOfBytesFromTags(MediaStreamInfo streamInfo)
        {
            if (streamInfo?.Tags is null)
            {
                return null;
            }

            var numberOfBytes = GetDictionaryValue(streamInfo.Tags, "NUMBER_OF_BYTES-eng")
                                ?? GetDictionaryValue(streamInfo.Tags, "NUMBER_OF_BYTES");
            if (long.TryParse(numberOfBytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBytes))
            {
                return parsedBytes;
            }

            return null;
        }
    }
}
