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
        private string NormalizeFormat(string format, IReadOnlyList<MediaStream> mediaStreams)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                return null;
            }

            // Input can be a list of multiple, comma-delimited formats - each of them needs to be checked
            var splitFormat = format.Split(',');
            for (var i = 0; i < splitFormat.Length; i++)
            {
                // Handle MPEG-1 container
                if (string.Equals(splitFormat[i], "mpegvideo", StringComparison.OrdinalIgnoreCase))
                {
                    splitFormat[i] = "mpeg";
                }

                // Handle MPEG-TS container
                else if (string.Equals(splitFormat[i], "mpegts", StringComparison.OrdinalIgnoreCase))
                {
                    splitFormat[i] = "ts";
                }

                // Handle matroska container
                else if (string.Equals(splitFormat[i], "matroska", StringComparison.OrdinalIgnoreCase))
                {
                    splitFormat[i] = "mkv";
                }

                // Handle WebM
                else if (string.Equals(splitFormat[i], "webm", StringComparison.OrdinalIgnoreCase))
                {
                    // Limit WebM to supported stream types and codecs.
                    // FFprobe can report "matroska,webm" for Matroska-like containers, so only keep "webm" if all streams are WebM-compatible.
                    // Any stream that is not video nor audio is not supported in WebM and should disqualify the webm container probe result.
                    if (HasWebmIncompatibleStream(mediaStreams))
                    {
                        splitFormat[i] = string.Empty;
                    }
                }
            }

            return string.Join(',', splitFormat.Where(s => !string.IsNullOrEmpty(s)));
        }

        private static bool HasWebmIncompatibleStream(IReadOnlyList<MediaStream> mediaStreams)
        {
            return mediaStreams.Any(stream => stream.Type is not MediaStreamType.Video and not MediaStreamType.Audio)
                || mediaStreams.Any(stream => (stream.Type == MediaStreamType.Video && !_webmVideoCodecs.Contains(stream.Codec, StringComparison.OrdinalIgnoreCase))
                    || (stream.Type == MediaStreamType.Audio && !_webmAudioCodecs.Contains(stream.Codec, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
