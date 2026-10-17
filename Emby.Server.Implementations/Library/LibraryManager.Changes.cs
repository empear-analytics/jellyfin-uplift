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
        public IEnumerable<Guid> GetLocalAlternateVersionIds(Video video)
        {
            ArgumentNullException.ThrowIfNull(video);

            var linkedIds = _linkedChildrenService.GetLinkedChildrenIds(video.Id, (int)MediaBrowser.Controller.Entities.LinkedChildType.LocalAlternateVersion);
            if (linkedIds.Count > 0)
            {
                return linkedIds;
            }

            return [];
        }

        /// <inheritdoc />
        public IEnumerable<Video> GetLinkedAlternateVersions(Video video)
        {
            ArgumentNullException.ThrowIfNull(video);

            var linkedIds = _linkedChildrenService.GetLinkedChildrenIds(video.Id, (int)MediaBrowser.Controller.Entities.LinkedChildType.LinkedAlternateVersion);
            if (linkedIds.Count > 0)
            {
                return linkedIds
                    .Select(id => GetItemById(id))
                    .Where(i => i is not null)
                    .OfType<Video>()
                    .OrderBy(i => i.SortName);
            }

            return [];
        }

        /// <inheritdoc />
        public IReadOnlySet<Guid> GetItemIdsWithAlternateVersions(IReadOnlyList<Guid> itemIds)
        {
            return _linkedChildrenService.GetItemIdsWithAlternateVersions(itemIds);
        }

        /// <inheritdoc />
        public void UpsertLinkedChild(Guid parentId, Guid childId, MediaBrowser.Controller.Entities.LinkedChildType childType)
        {
            _linkedChildrenService.UpsertLinkedChild(parentId, childId, childType);
        }

        /// <inheritdoc />
        public void CreateItem(BaseItem item, BaseItem? parent)
        {
            CreateItems([item], parent, CancellationToken.None);
        }

        /// <inheritdoc />
        public void CreateItems(IReadOnlyList<BaseItem> items, BaseItem? parent, CancellationToken cancellationToken)
        {
            // Resolve and add any local alternate version items that don't exist yet
            // This ensures they exist in the database when LinkedChildren are processed
            var allItems = new List<BaseItem>(items);
            var parentFolder = parent as Folder;
            var parentCollectionType = parent is not null ? GetTopFolderContentType(parent) : null;
            foreach (var item in items)
            {
                // Parental controls filter on the stored score, so it has to match the ratings being saved.
                item.OnMetadataChanged();

                AddMissingLocalAlternateVersions(item, allItems, parentFolder, parentCollectionType);
            }

            _persistenceService.SaveItems(allItems, cancellationToken);

            foreach (var item in allItems)
            {
                RegisterItem(item);
            }

            if (parent is Folder folder)
            {
                folder.Children = null;
                folder.UserData = null;
            }

            NotifyItemsAdded(items, parent);
        }

        private void AddMissingLocalAlternateVersions(BaseItem item, List<BaseItem> allItems, Folder? parentFolder, CollectionType? parentCollectionType)
        {
            if (item is Video video && video.LocalAlternateVersions.Length > 0)
            {
                AddLocalAlternateVersions(video, allItems, parentFolder, parentCollectionType);
            }
        }

        private void AddLocalAlternateVersions(Video video, List<BaseItem> allItems, Folder? parentFolder, CollectionType? parentCollectionType)
        {
            var videoType = video.GetType();
            foreach (var path in video.LocalAlternateVersions)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                // Use the primary video's type for ID calculation to ensure consistency
                var altId = GetNewItemId(path, videoType);
                if (GetItemById(altId) is null && !allItems.Any(i => i.Id.Equals(altId)))
                {
                    // Alternate version doesn't exist, resolve and create it
                    // ensuring it has the same type as the primary video
                    var altVideo = ResolveAlternateVersion(path, videoType, parentFolder, parentCollectionType);
                    AddResolvedAlternateVersion(video, altVideo, path, allItems);
                }
            }
        }

        private void AddResolvedAlternateVersion(Video video, Video? altVideo, string path, List<BaseItem> allItems)
        {
            if (altVideo is not null)
            {
                altVideo.OwnerId = video.Id;
                altVideo.SetPrimaryVersionId(video.Id);
                altVideo.IsInMixedFolder = video.IsInMixedFolder;
                // ResolveAlternateVersion only sees the alternate's primary file.
                // If the alternate is itself a stack (e.g. 1080p part1 + part2),
                // detect its parts from sibling files so its AdditionalParts persist.
                SetAdditionalPartsFromStack(altVideo, path);
                allItems.Add(altVideo);
            }
        }

        private void NotifyItemsAdded(IReadOnlyList<BaseItem> items, BaseItem? parent)
        {
            if (ItemAdded is not null)
            {
                foreach (var item in items)
                {
                    // With the live tv guide this just creates too much noise
                    if (item.SourceType != SourceType.Library)
                    {
                        continue;
                    }

                    try
                    {
                        ItemAdded(
                            this,
                            new ItemChangeEventArgs
                            {
                                Item = item,
                                Parent = parent ?? item.GetParent()
                            });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in ItemAdded event handler");
                    }
                }
            }
        }

        /// <inheritdoc />
        public async Task UpdateItemsAsync(IReadOnlyList<BaseItem> items, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken)
        {
            foreach (var item in items)
            {
                // Parental controls filter on the stored score, so it has to match the ratings being saved.
                item.OnMetadataChanged();

                item.DateLastSaved = DateTime.UtcNow;
                await RunMetadataSavers(item, updateReason).ConfigureAwait(false);

                // Modify again, so saved value is after write time of externally saved metadata
                item.DateLastSaved = DateTime.UtcNow;
            }

            ForgetDroppedLocalAlternateVersions(items);

            // Resolve and add any local alternate version items that don't exist yet
            // This ensures they exist in the database when LinkedChildren are processed
            var allItems = new List<BaseItem>(items);
            var parentFolder = parent as Folder;
            var parentCollectionType = GetTopFolderContentType(parent);
            foreach (var item in items)
            {
                AddMissingLocalAlternateVersions(item, allItems, parentFolder, parentCollectionType);
            }

            _persistenceService.SaveItems(allItems, cancellationToken);

            RegisterAddedItems(items, allItems);

            if (parent is Folder folder)
            {
                folder.Children = null;
                folder.UserData = null;
            }

            NotifyItemsUpdated(items, parent, updateReason);
        }

        private void NotifyItemsUpdated(IReadOnlyList<BaseItem> items, BaseItem parent, ItemUpdateType updateReason)
        {
            if (ItemUpdated is not null)
            {
                foreach (var item in items)
                {
                    // With the live tv guide this just creates too much noise
                    if (item.SourceType != SourceType.Library)
                    {
                        continue;
                    }

                    try
                    {
                        ItemUpdated(
                            this,
                            new ItemChangeEventArgs
                            {
                                Item = item,
                                Parent = parent,
                                UpdateReason = updateReason
                            });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in ItemUpdated event handler");
                    }
                }
            }
        }

        private void RegisterAddedItems(IReadOnlyList<BaseItem> items, List<BaseItem> allItems)
        {
            foreach (var item in allItems)
            {
                if (!items.Contains(item))
                {
                    RegisterItem(item);
                }
            }
        }

        /// <inheritdoc />
        public Task UpdateItemAsync(BaseItem item, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken)
            => UpdateItemsAsync([item], parent, updateReason, cancellationToken);

        /// <summary>
        /// Forgets the cached local alternate versions of the supplied items that they no longer list.
        /// </summary>
        /// <param name="items">The items about to be saved.</param>
        private void ForgetDroppedLocalAlternateVersions(IReadOnlyList<BaseItem> items)
        {
            foreach (var video in items.OfType<Video>())
            {
                var videoType = video.GetType();
                var keptIds = video.LocalAlternateVersions
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Select(path => GetNewItemId(path, videoType))
                    .ToHashSet();

                foreach (var versionId in GetLocalAlternateVersionIds(video))
                {
                    if (!keptIds.Contains(versionId))
                    {
                        _cache.TryRemove(versionId, out _);
                    }
                }
            }
        }

        /// <inheritdoc />
        public async Task ReattachUserDataAsync(BaseItem item, CancellationToken cancellationToken)
        {
            await _persistenceService.ReattachUserDataAsync(item, cancellationToken).ConfigureAwait(false);
        }

        public async Task RunMetadataSavers(BaseItem item, ItemUpdateType updateReason)
        {
            if (item.IsFileProtocol)
            {
                await ProviderManager.SaveMetadataAsync(item, updateReason).ConfigureAwait(false);
            }

            await UpdateImagesAsync(item, updateReason >= ItemUpdateType.ImageUpdate).ConfigureAwait(false);
        }

        /// <summary>
        /// Reports the item removed.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="parent">The parent item.</param>
        public void ReportItemRemoved(BaseItem item, BaseItem parent)
        {
            if (ItemRemoved is not null)
            {
                try
                {
                    ItemRemoved(
                        this,
                        new ItemChangeEventArgs
                        {
                            Item = item,
                            Parent = parent
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ItemRemoved event handler");
                }
            }
        }
    }
}
