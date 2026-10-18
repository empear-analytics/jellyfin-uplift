#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    /// <summary>
    /// Class responsible for normalizing FFprobe output.
    /// </summary>
    public partial class ProbeResultNormalizer
    {
        // When extracting subtitles, the maximum length to consider (to avoid invalid filenames)
        private const int MaxSubtitleDescriptionExtractionLength = 100;

        private const string ArtistReplaceValue = " | ";

        private static readonly char[] _basicDelimiters = ['/', ';'];
        private static readonly char[] _nameDelimiters = [.. _basicDelimiters, '|', '\\'];
        private static readonly char[] _genreDelimiters = [.. _basicDelimiters, ','];
        private static readonly string[] _webmVideoCodecs = ["av1", "vp8", "vp9"];
        private static readonly string[] _webmAudioCodecs = ["opus", "vorbis"];

        private readonly ILogger _logger;
        private readonly ILocalizationManager _localization;

        private string[] _splitWhiteList;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProbeResultNormalizer"/> class.
        /// </summary>
        /// <param name="logger">The <see cref="ILogger{ProbeResultNormalizer}"/> for use with the <see cref="ProbeResultNormalizer"/> instance.</param>
        /// <param name="localization">The <see cref="ILocalizationManager"/> for use with the <see cref="ProbeResultNormalizer"/> instance.</param>
        public ProbeResultNormalizer(ILogger logger, ILocalizationManager localization)
        {
            _logger = logger;
            _localization = localization;
        }

        private IReadOnlyList<string> SplitWhitelist => _splitWhiteList ??= new string[]
        {
            "AC/DC",
            "A/T/O/S",
            "As/Hi Soundworks",
            "Au/Ra",
            "Bremer/McCoy",
            "b/bqスタヂオ",
            "DOV/S",
            "DJ'TEKINA//SOMETHING",
            "IX/ON",
            "J-CORE SLi//CER",
            "M(a/u)SH",
            "Kaoru/Brilliance",
            "signum/ii",
            "Richiter(LORB/DUGEM DI BARAT)",
            "이달의 소녀 1/3",
            "R!N / Gemie",
            "LOONA 1/3",
            "LOONA / yyxy",
            "LOONA / ODD EYE CIRCLE",
            "K/DA",
            "22/7",
            "諭吉佳作/men",
            "//dARTH nULL",
            "Phantom/Ghost",
            "She/Her/Hers",
            "5/8erl in Ehr'n",
            "Smith/Kotzen",
            "We;Na",
            "LSR/CITY",
            "Kairon; IRSE!",
        };

        /// <summary>
        /// Transforms a FFprobe response into its <see cref="MediaInfo"/> equivalent.
        /// </summary>
        /// <param name="data">The <see cref="InternalMediaInfoResult"/>.</param>
        /// <param name="videoType">The <see cref="VideoType"/>.</param>
        /// <param name="isAudio">A boolean indicating whether the media is audio.</param>
        /// <param name="path">Path to media file.</param>
        /// <param name="protocol">Path media protocol.</param>
        /// <returns>The <see cref="MediaInfo"/>.</returns>
        public MediaInfo GetMediaInfo(InternalMediaInfoResult data, VideoType? videoType, bool isAudio, string path, MediaProtocol protocol)
        {
            var info = new MediaInfo
            {
                Path = path,
                Protocol = protocol,
                VideoType = videoType
            };

            FFProbeHelpers.NormalizeFFProbeResult(data);
            SetSize(data, info);

            var internalStreams = data.Streams ?? Array.Empty<MediaStreamInfo>();
            var internalFrames = data.Frames ?? Array.Empty<MediaFrameInfo>();

            info.MediaStreams = internalStreams.Select(s => GetMediaStream(isAudio, s, data.Format, internalFrames))
                .Where(i => i is not null)
                // Drop subtitle streams if we don't know the codec because it will just cause failures if we don't know how to handle them
                .Where(i => i.Type != MediaStreamType.Subtitle || !string.IsNullOrWhiteSpace(i.Codec))
                .ToList();

            info.MediaAttachments = internalStreams.Select(GetMediaAttachment)
                .Where(i => i is not null)
                .ToList();

            SetContainerAndBitrate(info, data);

            var tags = GetCombinedTags(data, isAudio);

            FetchGenres(info, tags);

            SetMetadataFromTags(info, tags);

            SetArtists(info, tags);

            // Guess ProductionYear from PremiereDate if missing
            if (info.ProductionYear is null && info.PremiereDate is not null)
            {
                info.ProductionYear = info.PremiereDate.Value.Year;
            }

            if (data.Chapters is not null)
            {
                info.Chapters = data.Chapters.Select(GetChapterInfo).ToArray();
            }

            // Set mediaType-specific metadata
            if (isAudio)
            {
                SetAudioRuntimeTicks(data, info);

                // tags are normally located under data.format, but we've seen some cases with ogg where they're part of the info stream
                // so let's create a combined list of both

                SetAudioInfoFromTags(info, tags);
            }
            else
            {
                SetVideoInfo(info, data, tags);
            }

            return info;
        }

        private static void SetMetadataFromTags(MediaInfo info, Dictionary<string, string> tags)
        {
            info.Name = tags.GetFirstNotNullNorWhiteSpaceValue("title", "title-eng");
            info.ForcedSortName = tags.GetFirstNotNullNorWhiteSpaceValue("sort_name", "title-sort", "titlesort");
            info.Overview = tags.GetFirstNotNullNorWhiteSpaceValue("synopsis", "description", "desc", "comment");

            info.ParentIndexNumber = FFProbeHelpers.GetDictionaryNumericValue(tags, "season_number");
            info.IndexNumber = FFProbeHelpers.GetDictionaryNumericValue(tags, "episode_sort") ??
                               FFProbeHelpers.GetDictionaryNumericValue(tags, "episode_id");
            info.ShowName = tags.GetValueOrDefault("show_name", "show");
            info.ProductionYear = FFProbeHelpers.GetDictionaryNumericValue(tags, "date");

            // Several different forms of retail/premiere date
            info.PremiereDate =
                FFProbeHelpers.GetDictionaryDateTime(tags, "originaldate") ??
                FFProbeHelpers.GetDictionaryDateTime(tags, "retaildate") ??
                FFProbeHelpers.GetDictionaryDateTime(tags, "retail date") ??
                FFProbeHelpers.GetDictionaryDateTime(tags, "retail_date") ??
                FFProbeHelpers.GetDictionaryDateTime(tags, "date_released") ??
                FFProbeHelpers.GetDictionaryDateTime(tags, "date") ??
                FFProbeHelpers.GetDictionaryDateTime(tags, "creation_time");

            // Set common metadata for music (audio) and music videos (video)
            info.Album = tags.GetValueOrDefault("album");
        }

        private void SetContainerAndBitrate(MediaInfo info, InternalMediaInfoResult data)
        {
            if (data.Format is not null)
            {
                info.Container = NormalizeFormat(data.Format.FormatName, info.MediaStreams);

                if (int.TryParse(data.Format.BitRate, CultureInfo.InvariantCulture, out var value))
                {
                    info.Bitrate = value;
                }
            }
        }

        private static Dictionary<string, string> GetCombinedTags(InternalMediaInfoResult data, bool isAudio)
        {
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tagStreamType = isAudio ? CodecType.Audio : CodecType.Video;

            var tagStream = data.Streams?.FirstOrDefault(i => i.CodecType == tagStreamType);

            if (tagStream?.Tags is not null)
            {
                CopyTags(tags, tagStream.Tags);
            }

            if (data.Format?.Tags is not null)
            {
                foreach (var (key, value) in data.Format.Tags)
                {
                    tags[key] = value;
                }
            }

            return tags;
        }

        private static void CopyTags(Dictionary<string, string> tags, IReadOnlyDictionary<string, string> source)
        {
            foreach (var (key, value) in source)
            {
                tags[key] = value;
            }
        }

        private void SetArtists(MediaInfo info, Dictionary<string, string> tags)
        {
            if (tags.TryGetValue("artists", out var artists) && !string.IsNullOrWhiteSpace(artists))
            {
                info.Artists = SplitDistinctArtists(artists, _basicDelimiters, false).ToArray();
            }
            else
            {
                var artist = tags.GetFirstNotNullNorWhiteSpaceValue("artist");
                info.Artists = artist is null
                    ? Array.Empty<string>()
                    : SplitDistinctArtists(artist, _nameDelimiters, true).ToArray();
            }
        }

        [GeneratedRegex("(?<name>.*) \\((?<instrument>.*)\\)")]
        private static partial Regex PerformerRegex();

        [GeneratedRegex(@"(\.\d{7})\d+")]
        private static partial Regex DurationOverPrecisionRegex();
    }
}
