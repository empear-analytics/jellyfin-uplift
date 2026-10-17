#pragma warning disable CS1591
#pragma warning disable CA5394

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BitFaster.Caching.Lru;
using Emby.Naming.Common;
using Emby.Naming.TV;
using Emby.Naming.Video;
using Emby.Server.Implementations.Library.Resolvers;
using Emby.Server.Implementations.Playlists;
using Emby.Server.Implementations.ScheduledTasks.Tasks;
using Emby.Server.Implementations.Sorting;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using EpisodeInfo = Emby.Naming.TV.EpisodeInfo;
using Genre = MediaBrowser.Controller.Entities.Genre;
using Person = MediaBrowser.Controller.Entities.Person;
using VideoResolver = Emby.Naming.Video.VideoResolver;

namespace Emby.Server.Implementations.Library
{
    public partial class LibraryManager
    {
        /// <inheritdoc />
        public int? GetSeasonNumberFromPath(string path, Guid? parentId)
        {
            var parentPath = parentId.HasValue ? GetItemById(parentId.Value)?.ContainingFolderPath : null;
            return SeasonPathParser.Parse(path, parentPath, true, true).SeasonNumber;
        }

        /// <inheritdoc />
        public bool FillMissingEpisodeNumbersFromPath(Episode episode, bool forceRefresh)
        {
            var series = episode.Series;
            bool? isAbsoluteNaming = series is not null && string.Equals(series.DisplayOrder, "absolute", StringComparison.OrdinalIgnoreCase);
            if (!isAbsoluteNaming.Value)
            {
                // In other words, no filter applied
                isAbsoluteNaming = null;
            }

            var resolver = new EpisodeResolver(_namingOptions);

            var isFolder = episode.VideoType == VideoType.BluRay || episode.VideoType == VideoType.Dvd;

            EpisodeInfo? episodeInfo = null;
            if (episode.IsFileProtocol)
            {
                episodeInfo = ResolveEpisodeInfo(episode, resolver, isFolder, isAbsoluteNaming);
            }

            var changed = false;
            if (episodeInfo is null)
            {
                return changed;
            }

            if (episodeInfo.IsByDate)
            {
                changed = UpdateDateBasedEpisodeNumbers(episode, episodeInfo);
            }
            else
            {
                changed = UpdateEpisodeNumbers(episode, episodeInfo, forceRefresh);
            }

            if (!episode.ParentIndexNumber.HasValue)
            {
                changed = FillParentIndexNumberFromSeason(episode, changed);
            }

            return changed;
        }

        private static bool FillParentIndexNumberFromSeason(Episode episode, bool changed)
        {
            var season = episode.Season;

            if (season is not null)
            {
                episode.ParentIndexNumber = season.IndexNumber;
            }

            if (episode.ParentIndexNumber.HasValue)
            {
                changed = true;
            }

            return changed;
        }

        private static EpisodeInfo? ResolveEpisodeInfo(Episode episode, EpisodeResolver resolver, bool isFolder, bool? isAbsoluteNaming)
        {
            EpisodeInfo? episodeInfo = resolver.Resolve(episode.Path, isFolder, null, null, isAbsoluteNaming);
            // Resolve from parent folder if it's not the Season folder
            var parent = episode.GetParent();
            if (episodeInfo is null && parent.GetType() == typeof(Folder))
            {
                episodeInfo = resolver.Resolve(parent.Path, true, null, null, isAbsoluteNaming);
                if (episodeInfo is not null)
                {
                    // add the container
                    episodeInfo.Container = Path.GetExtension(episode.Path)?.TrimStart('.');
                }
            }

            return episodeInfo;
        }

        private static bool UpdateEpisodeNumbers(Episode episode, EpisodeInfo episodeInfo, bool forceRefresh)
        {
            var changed = false;

            if (!episode.IndexNumber.HasValue || forceRefresh)
            {
                if (episode.IndexNumber != episodeInfo.EpisodeNumber)
                {
                    changed = true;
                }

                episode.IndexNumber = episodeInfo.EpisodeNumber;
            }

            if (!episode.IndexNumberEnd.HasValue || forceRefresh)
            {
                if (episode.IndexNumberEnd != episodeInfo.EndingEpisodeNumber)
                {
                    changed = true;
                }

                episode.IndexNumberEnd = episodeInfo.EndingEpisodeNumber;
            }

            if (!episode.ParentIndexNumber.HasValue || forceRefresh)
            {
                if (episode.ParentIndexNumber != episodeInfo.SeasonNumber)
                {
                    changed = true;
                }

                episode.ParentIndexNumber = episodeInfo.SeasonNumber;
            }

            return changed;
        }

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

            if (!episode.PremiereDate.HasValue)
            {
                if (episodeInfo.Year.HasValue && episodeInfo.Month.HasValue && episodeInfo.Day.HasValue)
                {
                    episode.PremiereDate = new DateTime(episodeInfo.Year.Value, episodeInfo.Month.Value, episodeInfo.Day.Value).ToUniversalTime();
                }

                if (episode.PremiereDate.HasValue)
                {
                    changed = true;
                }
            }

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
