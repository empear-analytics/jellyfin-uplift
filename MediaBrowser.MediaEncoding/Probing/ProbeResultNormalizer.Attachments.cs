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
        private static string NormalizeSubtitleCodec(string codec)
        {
            if (string.Equals(codec, "dvb_subtitle", StringComparison.OrdinalIgnoreCase))
            {
                codec = "DVBSUB";
            }
            else if (string.Equals(codec, "dvb_teletext", StringComparison.OrdinalIgnoreCase))
            {
                codec = "DVBTXT";
            }
            else if (string.Equals(codec, "dvd_subtitle", StringComparison.OrdinalIgnoreCase))
            {
                codec = "DVDSUB"; // .sub+.idx
            }
            else if (string.Equals(codec, "hdmv_pgs_subtitle", StringComparison.OrdinalIgnoreCase))
            {
                codec = "PGSSUB"; // .sup
            }

            return codec;
        }

        /// <summary>
        /// Converts ffprobe stream info to our MediaAttachment class.
        /// </summary>
        /// <param name="streamInfo">The stream info.</param>
        /// <returns>MediaAttachments.</returns>
        private MediaAttachment GetMediaAttachment(MediaStreamInfo streamInfo)
        {
            if (streamInfo.CodecType != CodecType.Attachment
                && streamInfo.Disposition?.GetValueOrDefault("attached_pic") != 1)
            {
                return null;
            }

            var attachment = new MediaAttachment
            {
                Codec = streamInfo.CodecName,
                Index = streamInfo.Index
            };

            if (!string.IsNullOrWhiteSpace(streamInfo.CodecTagString))
            {
                attachment.CodecTag = streamInfo.CodecTagString;
            }

            if (streamInfo.Tags is not null)
            {
                attachment.FileName = GetDictionaryValue(streamInfo.Tags, "filename");
                attachment.MimeType = GetDictionaryValue(streamInfo.Tags, "mimetype");
                attachment.Comment = GetDictionaryValue(streamInfo.Tags, "comment");
            }

            return attachment;
        }

        /// <summary>
        /// Gets a string from an FFProbeResult tags dictionary.
        /// </summary>
        /// <param name="tags">The tags.</param>
        /// <param name="key">The key.</param>
        /// <returns>System.String.</returns>
        private static string GetDictionaryValue(IReadOnlyDictionary<string, string> tags, string key)
        {
            if (tags is null)
            {
                return null;
            }

            tags.TryGetValue(key, out var val);

            return val;
        }

        private static void SetAudioRuntimeTicks(InternalMediaInfoResult result, MediaInfo data)
        {
            // Get the first info stream
            var stream = result.Streams?.FirstOrDefault(s => s.CodecType == CodecType.Audio);
            if (stream is null)
            {
                return;
            }

            // Get duration from stream properties
            var duration = stream.Duration;

            // If it's not there go into format properties
            if (string.IsNullOrEmpty(duration))
            {
                duration = result.Format.Duration;
            }

            // If we got something, parse it
            if (!string.IsNullOrEmpty(duration))
            {
                data.RunTimeTicks = TimeSpan.FromSeconds(double.Parse(duration, CultureInfo.InvariantCulture)).Ticks;
            }
        }

        private static void SetSize(InternalMediaInfoResult data, MediaInfo info)
        {
            if (data.Format is null)
            {
                return;
            }

            info.Size = string.IsNullOrEmpty(data.Format.Size) ? null : long.Parse(data.Format.Size, CultureInfo.InvariantCulture);
        }

        private static ChapterInfo GetChapterInfo(MediaChapter chapter)
        {
            var info = new ChapterInfo();

            if (chapter.Tags is not null && chapter.Tags.TryGetValue("title", out string name))
            {
                info.Name = name;
            }

            // Limit accuracy to milliseconds to match xml saving
            var secondsString = chapter.StartTime;

            if (double.TryParse(secondsString, CultureInfo.InvariantCulture, out var seconds))
            {
                var ms = Math.Round(TimeSpan.FromSeconds(seconds).TotalMilliseconds);
                info.StartPositionTicks = TimeSpan.FromMilliseconds(ms).Ticks;
            }

            return info;
        }
    }
}
