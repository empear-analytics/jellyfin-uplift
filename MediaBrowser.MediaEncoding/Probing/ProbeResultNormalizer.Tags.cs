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
        private void SetAudioInfoFromTags(MediaInfo audio, Dictionary<string, string> tags)
        {
            var people = new List<BaseItemPerson>();
            if (tags.TryGetValue("composer", out var composer) && !string.IsNullOrWhiteSpace(composer))
            {
                foreach (var person in Split(composer, false))
                {
                    people.Add(new BaseItemPerson { Name = person, Type = PersonKind.Composer });
                }
            }

            if (tags.TryGetValue("conductor", out var conductor) && !string.IsNullOrWhiteSpace(conductor))
            {
                foreach (var person in Split(conductor, false))
                {
                    people.Add(new BaseItemPerson { Name = person, Type = PersonKind.Conductor });
                }
            }

            if (tags.TryGetValue("lyricist", out var lyricist) && !string.IsNullOrWhiteSpace(lyricist))
            {
                foreach (var person in Split(lyricist, false))
                {
                    people.Add(new BaseItemPerson { Name = person, Type = PersonKind.Lyricist });
                }
            }

            AddPerformers(people, tags);

            // In cases where there isn't sufficient information as to which role a writer performed on a recording, tagging software uses the "writer" tag.
            if (tags.TryGetValue("writer", out var writer) && !string.IsNullOrWhiteSpace(writer))
            {
                foreach (var person in Split(writer, false))
                {
                    people.Add(new BaseItemPerson { Name = person, Type = PersonKind.Writer });
                }
            }

            if (tags.TryGetValue("arranger", out var arranger) && !string.IsNullOrWhiteSpace(arranger))
            {
                foreach (var person in Split(arranger, false))
                {
                    people.Add(new BaseItemPerson { Name = person, Type = PersonKind.Arranger });
                }
            }

            if (tags.TryGetValue("engineer", out var engineer) && !string.IsNullOrWhiteSpace(engineer))
            {
                foreach (var person in Split(engineer, false))
                {
                    people.Add(new BaseItemPerson { Name = person, Type = PersonKind.Engineer });
                }
            }

            if (tags.TryGetValue("mixer", out var mixer) && !string.IsNullOrWhiteSpace(mixer))
            {
                foreach (var person in Split(mixer, false))
                {
                    people.Add(new BaseItemPerson { Name = person, Type = PersonKind.Mixer });
                }
            }

            if (tags.TryGetValue("remixer", out var remixer) && !string.IsNullOrWhiteSpace(remixer))
            {
                foreach (var person in Split(remixer, false))
                {
                    people.Add(new BaseItemPerson { Name = person, Type = PersonKind.Remixer });
                }
            }

            audio.People = people.ToArray();

            SetAlbumArtists(audio, tags);

            // Track number
            audio.IndexNumber = GetDictionaryTrackOrDiscNumber(tags, "track");

            // Disc number
            audio.ParentIndexNumber = GetDictionaryTrackOrDiscNumber(tags, "disc");

            // There's several values in tags may or may not be present
            FetchStudios(audio, tags, "organization");
            FetchStudios(audio, tags, "ensemble");
            FetchStudios(audio, tags, "publisher");
            FetchStudios(audio, tags, "label");

            SetMusicBrainzIds(audio, tags);
        }

        private void AddPerformers(List<BaseItemPerson> people, Dictionary<string, string> tags)
        {
            if (tags.TryGetValue("performer", out var performer) && !string.IsNullOrWhiteSpace(performer))
            {
                foreach (var person in Split(performer, false))
                {
                    Match match = PerformerRegex().Match(person);

                    // If the performer doesn't have any instrument/role associated, it won't match. In that case, chances are it's simply a band name, so we skip it.
                    if (match.Success)
                    {
                        people.Add(new BaseItemPerson
                        {
                            Name = match.Groups["name"].Value,
                            Type = PersonKind.Actor,
                            Role = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Groups["instrument"].Value)
                        });
                    }
                }
            }
        }

        private void SetAlbumArtists(MediaInfo audio, Dictionary<string, string> tags)
        {
            // Set album artist
            var albumArtist = tags.GetFirstNotNullNorWhiteSpaceValue("albumartist", "album artist", "album_artist");
            audio.AlbumArtists = albumArtist is not null
                ? SplitDistinctArtists(albumArtist, _nameDelimiters, true).ToArray()
                : Array.Empty<string>();

            // Set album artist to artist if empty
            if (audio.AlbumArtists.Length == 0)
            {
                audio.AlbumArtists = audio.Artists;
            }
        }

        private static void SetMusicBrainzIds(MediaInfo audio, Dictionary<string, string> tags)
        {
            // These support multiple values, but for now we only store the first.
            var mb = GetMultipleMusicBrainzId(tags.GetValueOrDefault("MusicBrainz Album Artist Id"))
                ?? GetMultipleMusicBrainzId(tags.GetValueOrDefault("MUSICBRAINZ_ALBUMARTISTID"));
            audio.TrySetProviderId(MetadataProvider.MusicBrainzAlbumArtist, mb);

            mb = GetMultipleMusicBrainzId(tags.GetValueOrDefault("MusicBrainz Artist Id"))
                ?? GetMultipleMusicBrainzId(tags.GetValueOrDefault("MUSICBRAINZ_ARTISTID"));
            audio.TrySetProviderId(MetadataProvider.MusicBrainzArtist, mb);

            mb = GetMultipleMusicBrainzId(tags.GetValueOrDefault("MusicBrainz Album Id"))
                ?? GetMultipleMusicBrainzId(tags.GetValueOrDefault("MUSICBRAINZ_ALBUMID"));
            audio.TrySetProviderId(MetadataProvider.MusicBrainzAlbum, mb);

            mb = GetMultipleMusicBrainzId(tags.GetValueOrDefault("MusicBrainz Release Group Id"))
                 ?? GetMultipleMusicBrainzId(tags.GetValueOrDefault("MUSICBRAINZ_RELEASEGROUPID"));
            audio.TrySetProviderId(MetadataProvider.MusicBrainzReleaseGroup, mb);

            mb = GetMultipleMusicBrainzId(tags.GetValueOrDefault("MusicBrainz Release Track Id"))
                 ?? GetMultipleMusicBrainzId(tags.GetValueOrDefault("MUSICBRAINZ_RELEASETRACKID"));
            audio.TrySetProviderId(MetadataProvider.MusicBrainzTrack, mb);
        }

        private static string GetMultipleMusicBrainzId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        }

        /// <summary>
        /// Splits the specified val.
        /// </summary>
        /// <param name="val">The val.</param>
        /// <param name="allowCommaDelimiter">if set to <c>true</c> [allow comma delimiter].</param>
        /// <returns>System.String[][].</returns>
        private string[] Split(string val, bool allowCommaDelimiter)
        {
            // Only use the comma as a delimiter if there are no slashes or pipes.
            // We want to be careful not to split names that have commas in them
            return !allowCommaDelimiter || _nameDelimiters.Any(i => val.Contains(i, StringComparison.Ordinal)) ?
                val.Split(_nameDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) :
                val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private IEnumerable<string> SplitDistinctArtists(string val, char[] delimiters, bool splitFeaturing)
        {
            if (splitFeaturing)
            {
                val = val.Replace(" featuring ", ArtistReplaceValue, StringComparison.OrdinalIgnoreCase)
                    .Replace(" feat. ", ArtistReplaceValue, StringComparison.OrdinalIgnoreCase);
            }

            var artistsFound = new List<string>();

            foreach (var whitelistArtist in SplitWhitelist)
            {
                var originalVal = val;
                val = val.Replace(whitelistArtist, "|", StringComparison.OrdinalIgnoreCase);

                if (!string.Equals(originalVal, val, StringComparison.OrdinalIgnoreCase))
                {
                    artistsFound.Add(whitelistArtist);
                }
            }

            var artists = val.Split(delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            artistsFound.AddRange(artists);
            return artistsFound.DistinctNames();
        }

        /// <summary>
        /// Gets the studios from the tags collection.
        /// </summary>
        /// <param name="info">The info.</param>
        /// <param name="tags">The tags.</param>
        /// <param name="tagName">Name of the tag.</param>
        private void FetchStudios(MediaInfo info, IReadOnlyDictionary<string, string> tags, string tagName)
        {
            var val = tags.GetValueOrDefault(tagName);

            if (string.IsNullOrEmpty(val))
            {
                return;
            }

            var studios = Split(val, true);
            var studioList = new List<string>();

            foreach (var studio in studios)
            {
                if (string.IsNullOrWhiteSpace(studio))
                {
                    continue;
                }

                // Don't add artist/album artist name to studios, even if it's listed there
                if (info.Artists.Contains(studio, StringComparison.OrdinalIgnoreCase)
                    || info.AlbumArtists.Contains(studio, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                studioList.Add(studio);
            }

            info.Studios = studioList
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Gets the genres from the tags collection.
        /// </summary>
        /// <param name="info">The information.</param>
        /// <param name="tags">The tags.</param>
        private void FetchGenres(MediaInfo info, IReadOnlyDictionary<string, string> tags)
        {
            var genreVal = tags.GetValueOrDefault("genre");
            if (string.IsNullOrEmpty(genreVal))
            {
                return;
            }

            var genres = new List<string>(info.Genres);
            foreach (var genre in Split(genreVal, true))
            {
                if (string.IsNullOrEmpty(genre))
                {
                    continue;
                }

                genres.Add(genre);
            }

            info.Genres = genres
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Gets the track or disc number, which can be in the form of '1', or '1/3'.
        /// </summary>
        /// <param name="tags">The tags.</param>
        /// <param name="tagName">Name of the tag.</param>
        /// <returns>The track or disc number, or null, if missing or not parseable.</returns>
        private static int? GetDictionaryTrackOrDiscNumber(IReadOnlyDictionary<string, string> tags, string tagName)
        {
            var disc = tags.GetValueOrDefault(tagName);

            if (int.TryParse(disc.AsSpan().LeftPart('/'), out var discNum))
            {
                return discNum;
            }

            return null;
        }
    }
}
