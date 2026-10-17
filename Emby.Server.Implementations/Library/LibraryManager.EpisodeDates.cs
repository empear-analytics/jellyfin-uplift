#pragma warning disable CS1591

using System;
using System.Diagnostics.CodeAnalysis;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using EpisodeInfo = Emby.Naming.TV.EpisodeInfo;

namespace Emby.Server.Implementations.Library
{
    public partial class LibraryManager
    {
        private static bool UpdateDateBasedEpisodeNumbers(Episode episode, EpisodeInfo episodeInfo)
        {
            var changed = false;

            if (episode.IndexNumber.HasValue)
            {
                episode.IndexNumber = null;
                changed = true;
            }

            if (episode.IndexNumberEnd.HasValue)
            {
                episode.IndexNumberEnd = null;
                changed = true;
            }

            changed = FillPremiereDate(episode, episodeInfo, changed);
            changed = FillProductionYear(episode, episodeInfo, changed);

            return changed;
        }

        private static bool HasFullDate([NotNullWhen(true)] int? year, [NotNullWhen(true)] int? month, [NotNullWhen(true)] int? day)
        {
            return year.HasValue && month.HasValue && day.HasValue;
        }

        private static bool FillPremiereDate(Episode episode, EpisodeInfo episodeInfo, bool changed)
        {
            if (!episode.PremiereDate.HasValue)
            {
                if (HasFullDate(episodeInfo.Year, episodeInfo.Month, episodeInfo.Day))
                {
                    episode.PremiereDate = new DateTime(episodeInfo.Year.Value, episodeInfo.Month.Value, episodeInfo.Day.Value).ToUniversalTime();
                }

                if (episode.PremiereDate.HasValue)
                {
                    changed = true;
                }
            }

            return changed;
        }

        private static bool FillProductionYear(Episode episode, EpisodeInfo episodeInfo, bool changed)
        {
            if (episode.ProductionYear is null)
            {
                episode.ProductionYear = episodeInfo.Year;

                if (episode.ProductionYear is not null)
                {
                    changed = true;
                }
            }

            return changed;
        }
    }
}
