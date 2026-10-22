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
        /// <summary>
        /// Converts ffprobe stream info to our MediaStream class.
        /// </summary>
        /// <param name="isAudio">if set to <c>true</c> [is info].</param>
        /// <param name="streamInfo">The stream info.</param>
        /// <param name="formatInfo">The format info.</param>
        /// <param name="frameInfoList">The frame info.</param>
        /// <returns>MediaStream.</returns>
        private MediaStream GetMediaStream(bool isAudio, MediaStreamInfo streamInfo, MediaFormatInfo formatInfo, IReadOnlyList<MediaFrameInfo> frameInfoList)
        {
            var stream = new MediaStream
            {
                Codec = streamInfo.CodecName,
                Profile = streamInfo.Profile,
                Width = streamInfo.Width,
                Height = streamInfo.Height,
                Level = streamInfo.Level,
                Index = streamInfo.Index,
                PixelFormat = streamInfo.PixelFormat,
                NalLengthSize = streamInfo.NalLengthSize,
                TimeBase = streamInfo.TimeBase,
                CodecTimeBase = streamInfo.CodecTimeBase
            };

            // Filter out junk
            if (!string.IsNullOrWhiteSpace(streamInfo.CodecTagString) && !streamInfo.CodecTagString.Contains("[0]", StringComparison.OrdinalIgnoreCase))
            {
                stream.CodecTag = streamInfo.CodecTagString;
            }

            if (streamInfo.Tags is not null)
            {
                stream.Language = GetDictionaryValue(streamInfo.Tags, "language");
                stream.Comment = GetDictionaryValue(streamInfo.Tags, "comment");
                stream.Title = GetDictionaryValue(streamInfo.Tags, "title");
            }

            if (streamInfo.CodecType == CodecType.Audio)
            {
                SetAudioStreamInfo(stream, streamInfo);
            }
            else if (streamInfo.CodecType == CodecType.Subtitle)
            {
                SetSubtitleStreamInfo(stream, streamInfo);
            }
            else if (streamInfo.CodecType == CodecType.Video)
            {
                SetVideoStreamInfo(isAudio, stream, streamInfo, frameInfoList);
            }
            else if (streamInfo.CodecType == CodecType.Data)
            {
                stream.Type = MediaStreamType.Data;
            }
            else
            {
                return null;
            }

            SetStreamBitRate(isAudio, stream, streamInfo, formatInfo);

            SetBitRateFromTags(stream, streamInfo);

            ApplyDisposition(stream, streamInfo);

            NormalizeStreamTitle(stream);

            return stream;
        }

        private static void SetStreamBitRate(bool isAudio, MediaStream stream, MediaStreamInfo streamInfo, MediaFormatInfo formatInfo)
        {
            // Get stream bitrate
            var bitrate = 0;

            if (int.TryParse(streamInfo.BitRate, CultureInfo.InvariantCulture, out var value))
            {
                bitrate = value;
            }

            // The bitrate info of FLAC audio is included in formatInfo.
            // Don't do this for video streams: formatInfo.BitRate is the overall container
            // bitrate (video + audio + subtitles + overhead), not the video bitrate.
            if (ShouldUseFormatBitRate(bitrate, formatInfo, isAudio, stream))
            {
                // If the stream info doesn't have a bitrate get the value from the media format info
                if (int.TryParse(formatInfo.BitRate, CultureInfo.InvariantCulture, out value))
                {
                    bitrate = value;
                }
            }

            if (bitrate > 0)
            {
                stream.BitRate = bitrate;
            }
        }

        private static bool ShouldUseFormatBitRate(int bitrate, MediaFormatInfo formatInfo, bool isAudio, MediaStream stream)
        {
            return bitrate == 0
                && formatInfo is not null
                && isAudio && stream.Type == MediaStreamType.Audio;
        }

        private static void ApplyDisposition(MediaStream stream, MediaStreamInfo streamInfo)
        {
            var disposition = streamInfo.Disposition;
            if (disposition is not null)
            {
                if (disposition.GetValueOrDefault("default") == 1)
                {
                    stream.IsDefault = true;
                }

                if (disposition.GetValueOrDefault("forced") == 1)
                {
                    stream.IsForced = true;
                }

                if (disposition.GetValueOrDefault("hearing_impaired") == 1)
                {
                    stream.IsHearingImpaired = true;
                }

                if (disposition.GetValueOrDefault("original") == 1)
                {
                    stream.IsOriginal = true;
                }
            }
        }

        private static void NormalizeStreamTitle(MediaStream stream)
        {
            if (string.Equals(stream.Title, "cc", StringComparison.OrdinalIgnoreCase)
                || stream.Type == MediaStreamType.EmbeddedImage)
            {
                stream.Title = null;
            }
        }
    }
}
