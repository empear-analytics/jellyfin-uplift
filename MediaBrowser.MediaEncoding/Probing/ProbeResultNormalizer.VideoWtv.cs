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
    }
}
