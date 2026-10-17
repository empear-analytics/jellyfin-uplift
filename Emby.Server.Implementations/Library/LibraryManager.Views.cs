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
        public UserView GetNamedView(
            User user,
            string name,
            CollectionType? viewType,
            string sortName)
        {
            return GetNamedView(user, name, Guid.Empty, viewType, sortName);
        }

        public UserView GetNamedView(
            string name,
            CollectionType viewType,
            string sortName)
        {
            var path = Path.Combine(
                _configurationManager.ApplicationPaths.InternalMetadataPath,
                "views",
                _fileSystem.GetValidFilename(viewType.ToString()));

            // The display name is localized, so it must not take part in the id.
            var id = GetNewItemId(path + "_namedview_" + viewType.ToString(), typeof(UserView));

            var item = GetItemById(id) as UserView;

            var refresh = false;

            if (item is null || !string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                var info = Directory.CreateDirectory(path);
                item = new UserView
                {
                    Path = path,
                    Id = id,
                    DateCreated = info.CreationTimeUtc,
                    DateModified = info.LastWriteTimeUtc,
                    Name = name,
                    ViewType = viewType,
                    ForcedSortName = sortName
                };

                CreateItem(item, null);

                refresh = true;
            }
            else if (!string.Equals(item.Name, name, StringComparison.Ordinal))
            {
                item.Name = name;
                item.ForcedSortName = sortName;

                refresh = true;
            }

            if (refresh)
            {
                item.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, CancellationToken.None).GetAwaiter().GetResult();
                ProviderManager.QueueRefresh(item.Id, new MetadataRefreshOptions(new DirectoryService(_fileSystem)), RefreshPriority.Normal);
            }

            return item;
        }

        public UserView GetNamedView(
            User user,
            string name,
            Guid parentId,
            CollectionType? viewType,
            string sortName)
        {
            var id = GetNamedViewId(user, parentId, viewType);

            var path = Path.Combine(_configurationManager.ApplicationPaths.InternalMetadataPath, "views", id.ToString("N", CultureInfo.InvariantCulture));

            var item = GetItemById(id) as UserView;

            var isNew = false;

            if (item is null)
            {
                var info = Directory.CreateDirectory(path);
                item = new UserView
                {
                    Path = path,
                    Id = id,
                    DateCreated = info.CreationTimeUtc,
                    DateModified = info.LastWriteTimeUtc,
                    Name = name,
                    ViewType = viewType,
                    ForcedSortName = sortName,
                    UserId = user.Id,
                    DisplayParentId = parentId
                };

                CreateItem(item, null);

                isNew = true;
            }
            else if (!string.Equals(item.Name, name, StringComparison.Ordinal))
            {
                item.Name = name;
                item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).GetAwaiter().GetResult();
            }

            QueueViewRefreshIfStale(item, isNew);

            return item;
        }

        public Guid GetNamedViewId(User user, Guid parentId, CollectionType? viewType)
        {
            var parentIdString = parentId.IsEmpty()
                ? null
                : parentId.ToString("N", CultureInfo.InvariantCulture);

            // The name is either localized (grouped views) or the library folder's own name.
            var idValues = "38_namedview_" + user.Id.ToString("N", CultureInfo.InvariantCulture) + (parentIdString ?? string.Empty) + (viewType?.ToString() ?? string.Empty);

            return GetNewItemId(idValues, typeof(UserView));
        }

        public UserView GetShadowView(
            BaseItem parent,
            CollectionType? viewType,
            string sortName)
        {
            ArgumentNullException.ThrowIfNull(parent);

            var name = parent.Name;
            var parentId = parent.Id;
            var id = GetShadowViewId(name, parentId, viewType);

            var path = parent.Path;

            var item = GetItemById(id) as UserView;

            var isNew = false;

            if (item is null)
            {
                var info = Directory.CreateDirectory(path);
                item = new UserView
                {
                    Path = path,
                    Id = id,
                    DateCreated = info.CreationTimeUtc,
                    DateModified = info.LastWriteTimeUtc,
                    Name = name,
                    ViewType = viewType,
                    ForcedSortName = sortName,
                    DisplayParentId = parentId
                };

                CreateItem(item, null);

                isNew = true;
            }

            QueueViewRefreshIfStale(item, isNew);

            return item;
        }

        public Guid GetShadowViewId(string name, Guid parentId, CollectionType? viewType)
        {
            var idValues = "38_namedview_" + name + parentId + (viewType?.ToString() ?? string.Empty);

            return GetNewItemId(idValues, typeof(UserView));
        }

        public UserView GetNamedView(
            string name,
            Guid parentId,
            CollectionType? viewType,
            string sortName,
            string uniqueId)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            var parentIdString = parentId.IsEmpty()
                ? null
                : parentId.ToString("N", CultureInfo.InvariantCulture);
            var idValues = "37_namedview_" + (parentIdString ?? string.Empty) + (viewType?.ToString() ?? string.Empty);
            if (!string.IsNullOrEmpty(uniqueId))
            {
                idValues += uniqueId;
            }

            var id = GetNewItemId(idValues, typeof(UserView));

            var path = Path.Combine(_configurationManager.ApplicationPaths.InternalMetadataPath, "views", id.ToString("N", CultureInfo.InvariantCulture));

            var item = GetItemById(id) as UserView;

            var isNew = false;

            if (item is null)
            {
                var info = Directory.CreateDirectory(path);
                item = new UserView
                {
                    Path = path,
                    Id = id,
                    DateCreated = info.CreationTimeUtc,
                    DateModified = info.LastWriteTimeUtc,
                    Name = name,
                    ViewType = viewType,
                    ForcedSortName = sortName,
                    DisplayParentId = parentId
                };

                CreateItem(item, null);

                isNew = true;
            }

            if (viewType != item.ViewType || !string.Equals(item.Name, name, StringComparison.Ordinal))
            {
                item.ViewType = viewType;
                item.Name = name;
                item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).GetAwaiter().GetResult();
            }

            QueueViewRefreshIfStale(item, isNew);

            return item;
        }

        private void QueueViewRefreshIfStale(UserView item, bool isNew)
        {
            var lastRefreshedUtc = item.DateLastRefreshed;
            var refresh = isNew || DateTime.UtcNow - lastRefreshedUtc >= _viewRefreshInterval;

            if (!refresh && !item.DisplayParentId.IsEmpty())
            {
                var displayParent = GetItemById(item.DisplayParentId);
                refresh = displayParent is not null && displayParent.DateLastSaved > lastRefreshedUtc;
            }

            if (refresh)
            {
                ProviderManager.QueueRefresh(
                    item.Id,
                    new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                    {
                        // Need to force save to increment DateLastSaved
                        ForceSave = true
                    },
                    RefreshPriority.Normal);
            }
        }
    }
}
