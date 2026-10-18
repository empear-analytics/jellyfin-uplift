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
        /// <summary>
        /// Gets the intros.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="user">The user.</param>
        /// <returns>IEnumerable{System.String}.</returns>
        public async Task<IEnumerable<Video>> GetIntros(BaseItem item, User user)
        {
            if (IntroProviders.Length == 0)
            {
                return [];
            }

            var tasks = IntroProviders
                .Select(i => GetIntros(i, item, user));

            var items = await Task.WhenAll(tasks).ConfigureAwait(false);

            return items
                .SelectMany(i => i)
                .Select(ResolveIntro)
                .Where(i => i is not null)!; // null values got filtered out
        }

        /// <summary>
        /// Gets the intros.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="item">The item.</param>
        /// <param name="user">The user.</param>
        /// <returns>Task&lt;IEnumerable&lt;IntroInfo&gt;&gt;.</returns>
        private async Task<IEnumerable<IntroInfo>> GetIntros(IIntroProvider provider, BaseItem item, User user)
        {
            try
            {
                return await provider.GetIntros(item, user).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting intros");

                return [];
            }
        }

        /// <summary>
        /// Resolves the intro.
        /// </summary>
        /// <param name="info">The info.</param>
        /// <returns>Video.</returns>
        private Video? ResolveIntro(IntroInfo info)
        {
            Video? video = null;

            if (info.ItemId.HasValue)
            {
                // Get an existing item by Id
                video = GetItemById(info.ItemId.Value) as Video;

                if (video is null)
                {
                    _logger.LogError("Unable to locate item with Id {ID}.", info.ItemId.Value);
                }
            }
            else if (!string.IsNullOrEmpty(info.Path))
            {
                try
                {
                    // Try to resolve the path into a video
                    video = IntrosResolvePathVideo(info);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error resolving path {Path}.", info.Path);
                }
            }
            else
            {
                _logger.LogError("IntroProvider returned an IntroInfo with null Path and ItemId.");
            }

            return video;
        }

        private Video? IntrosResolvePathVideo(IntroInfo info)
        {
            var video = ResolvePath(_fileSystem.GetFileSystemInfo(info.Path)) as Video;

            if (video is null)
            {
                _logger.LogError("Intro resolver returned null for {Path}.", info.Path);
            }
            else
            {
                // Pull the saved db item that will include metadata
                var dbItem = GetItemById(video.Id) as Video;

                if (dbItem is not null)
                {
                    video = dbItem;
                }
                else
                {
                    return null;
                }
            }

            return video;
        }
    }
}
