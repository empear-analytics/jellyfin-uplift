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
        public void DeleteItem(BaseItem item, DeleteOptions options)
        {
            DeleteItem(item, options, false);
        }

        public void DeleteItem(BaseItem item, DeleteOptions options, bool notifyParentItem)
        {
            ArgumentNullException.ThrowIfNull(item);

            var parent = item.GetOwner() ?? item.GetParent();

            DeleteItem(item, options, parent, notifyParentItem);
        }

        public void DeleteItemsUnsafeFast(IReadOnlyCollection<BaseItem> items, bool deleteSourceFiles = false)
        {
            if (items.Count == 0)
            {
                return;
            }

            var pathMaps = items.Select(e =>
                (Item: e,
                InternalPath: GetInternalMetadataPaths(e),
                DeletePaths: deleteSourceFiles ? e.GetDeletePaths() : [])).ToArray();

            foreach (var (item, internalPaths, pathsToDelete) in pathMaps)
            {
                DeleteMetadataPaths(item, internalPaths);

                foreach (var fileSystemInfo in pathsToDelete)
                {
                    DeleteItemPath(item, false, fileSystemInfo);
                }
            }

            var externalDataManager = _externalDataManagerFactory.Value;
            foreach (var (item, _, _) in pathMaps)
            {
                externalDataManager.DeleteExternalItemFiles(item);
            }

            _persistenceService.DeleteItem([.. pathMaps.Select(f => f.Item.Id)]);

            // Evict the deleted items from the cache and announce each removal.
            foreach (var (item, _, _) in pathMaps)
            {
                _cache.TryRemove(item.Id, out _);
                ReportItemRemoved(item, item.GetOwner() ?? item.GetParent());
            }
        }

        public void DeleteItem(BaseItem item, DeleteOptions options, BaseItem parent, bool notifyParentItem)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (item.SourceType == SourceType.Channel)
            {
                PrepareChannelItemDeletion(item, options);
            }

            LogItemRemoval(item);

            // If deleting a primary version video, clear PrimaryVersionId from alternate versions
            // OwnerId check: items with OwnerId set are alternate versions or extras, not primaries
            UpdateVersionsOfDeletedVideo(item);

            var children = item.IsFolder
                ? ((Folder)item).GetRecursiveChildren(false)
                : [];

            DeleteMetadataPaths(item, GetMetadataPaths(item, children));

            if (ShouldDeleteItemFiles(item, options))
            {
                DeleteItemPaths(item);
            }

            item.SetParent(null);

            var externalDataManager = _externalDataManagerFactory.Value;
            externalDataManager.DeleteExternalItemFiles(item);
            foreach (var child in children)
            {
                externalDataManager.DeleteExternalItemFiles(child);
            }

            _persistenceService.DeleteItem([item.Id, .. children.Select(f => f.Id)]);
            _cache.TryRemove(item.Id, out _);
            foreach (var child in children)
            {
                _cache.TryRemove(child.Id, out _);
            }

            if (parent is Folder folder)
            {
                folder.Children = null;
                folder.UserData = null;
            }

            // Announce the descendants before the item itself.
            foreach (var child in children)
            {
                ReportItemRemoved(child, item);
            }

            ReportItemRemoved(item, parent);
        }

        private void UpdateVersionsOfDeletedVideo(BaseItem item)
        {
            if (item is Video video && IsPrimaryVersion(video))
            {
                PromoteAlternateVersionOfDeletedPrimary(item, video);
            }
            else if (item is Video alternateVideo && alternateVideo.PrimaryVersionId.HasValue)
            {
                // If deleting an alternate version, re-route references to its primary
                RerouteLinkedChildReferencesAsync(alternateVideo.Id, alternateVideo.PrimaryVersionId.Value).GetAwaiter().GetResult();

                // Remove deleted alternate from primary's LinkedAlternateVersions
                if (GetItemById(alternateVideo.PrimaryVersionId.Value) is Video primaryVideo)
                {
                    primaryVideo.LinkedAlternateVersions = primaryVideo.LinkedAlternateVersions
                        .Where(lc => !lc.ItemId.HasValue || !lc.ItemId.Value.Equals(alternateVideo.Id))
                        .ToArray();
                    primaryVideo.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).GetAwaiter().GetResult();
                }
            }
        }

        private void LogItemRemoval(BaseItem item)
        {
            if (item is LiveTvProgram)
            {
                _logger.LogDebug(
                    "Removing item, Type: {Type}, Name: {Name}, Path: {Path}, Id: {Id}",
                    item.GetType().Name,
                    item.Name ?? "Unknown name",
                    item.Path ?? string.Empty,
                    item.Id);
            }
            else
            {
                _logger.LogInformation(
                    "Removing item, Type: {Type}, Name: {Name}, Path: {Path}, Id: {Id}",
                    item.GetType().Name,
                    item.Name ?? "Unknown name",
                    item.Path ?? string.Empty,
                    item.Id);
            }
        }

        private static bool IsPrimaryVersion(Video video)
        {
            return !video.PrimaryVersionId.HasValue && video.OwnerId.IsEmpty();
        }

        private bool ShouldDeleteItemFiles(BaseItem item, DeleteOptions options)
        {
            return (options.DeleteFileLocation && item.IsFileProtocol) || IsInternalItem(item);
        }

        private void DeleteMetadataPaths(BaseItem item, IEnumerable<string> metadataPaths)
        {
            foreach (var metadataPath in metadataPaths)
            {
                if (!Directory.Exists(metadataPath))
                {
                    continue;
                }

                _logger.LogDebug(
                    "Deleting metadata path, Type: {Type}, Name: {Name}, Path: {Path}, Id: {Id}",
                    item.GetType().Name,
                    item.Name ?? "Unknown name",
                    metadataPath,
                    item.Id);

                try
                {
                    Directory.Delete(metadataPath, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting {MetadataPath}", metadataPath);
                }
            }
        }

        private void DeleteItemPaths(BaseItem item)
        {
            // Assume only the first is required
            // Add this flag to GetDeletePaths if required in the future
            var isRequiredForDelete = true;

            foreach (var fileSystemInfo in item.GetDeletePaths())
            {
                DeleteItemPath(item, isRequiredForDelete, fileSystemInfo);

                isRequiredForDelete = false;
            }
        }

        private void PromoteAlternateVersionOfDeletedPrimary(BaseItem item, Video video)
        {
            var localAlternateIds = GetLocalAlternateVersionIds(video).ToHashSet();
            var allAlternateVersions = localAlternateIds
                .Concat(GetLinkedAlternateVersions(video).Select(v => v.Id))
                .Distinct()
                .Select(id => GetItemById(id))
                .OfType<Video>()
                .ToList();

            // Partition alternates by whether their files still exist on disk
            var alternateVersions = new List<Video>();
            var missingAlternates = new List<Video>();
            foreach (var alt in allAlternateVersions)
            {
                if (!string.IsNullOrEmpty(alt.Path) && !_fileSystem.FileExists(alt.Path))
                {
                    missingAlternates.Add(alt);
                }
                else
                {
                    alternateVersions.Add(alt);
                }
            }

            // Delete alternates whose files no longer exist to avoid ghost items.
            // Clear PrimaryVersionId first so DeleteItem doesn't try to update the primary being deleted.
            DeleteMissingAlternateVersions(missingAlternates);

            if (alternateVersions.Count > 0)
            {
                PromoteFirstAlternateVersion(item, video, alternateVersions, localAlternateIds);
            }
        }

        private void DeleteMissingAlternateVersions(List<Video> missingAlternates)
        {
            foreach (var missing in missingAlternates)
            {
                _logger.LogInformation(
                    "Deleting missing alternate version {Name} ({Path})",
                    missing.Name ?? "Unknown name",
                    missing.Path ?? string.Empty);
                missing.SetPrimaryVersionId(null);
                missing.OwnerId = Guid.Empty;
                missing.LocalAlternateVersions = [];
                missing.LinkedAlternateVersions = [];
                DeleteItem(missing, new DeleteOptions { DeleteFileLocation = false }, false);
            }
        }

        private void PromoteFirstAlternateVersion(BaseItem item, Video video, List<Video> alternateVersions, HashSet<Guid> localAlternateIds)
        {
            _logger.LogInformation(
                "Clearing PrimaryVersionId from {Count} alternate versions of {Name}",
                alternateVersions.Count,
                item.Name ?? "Unknown name");

            // Promote the first alternate version to be the new primary
            var newPrimary = alternateVersions[0];
            newPrimary.SetPrimaryVersionId(null);
            newPrimary.OwnerId = Guid.Empty;

            // Transfer alternate version arrays from old primary to new primary
            // so UpdateToRepositoryAsync creates correct LinkedChildren entries
            newPrimary.LocalAlternateVersions = video.LocalAlternateVersions
                .Where(p => !string.Equals(p, newPrimary.Path, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            newPrimary.LinkedAlternateVersions = video.LinkedAlternateVersions
                .Where(lc => !lc.ItemId.HasValue || !lc.ItemId.Value.Equals(newPrimary.Id))
                .ToArray();

            newPrimary.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).GetAwaiter().GetResult();

            // Re-route playlist/collection references from deleted primary to new primary
            RerouteLinkedChildReferencesAsync(video.Id, newPrimary.Id).GetAwaiter().GetResult();

            // Update remaining alternates to point to new primary
            foreach (var alternate in alternateVersions.Skip(1))
            {
                alternate.SetPrimaryVersionId(newPrimary.Id);
                // Only set OwnerId for local alternates; linked alternates are independent items
                alternate.OwnerId = localAlternateIds.Contains(alternate.Id) ? newPrimary.Id : Guid.Empty;
                alternate.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        private static void PrepareChannelItemDeletion(BaseItem item, DeleteOptions options)
        {
            if (options.DeleteFromExternalProvider)
            {
                try
                {
                    BaseItem.ChannelManager.DeleteItem(item).GetAwaiter().GetResult();
                }
                catch (ArgumentException)
                {
                    // channel no longer installed
                }
            }

            options.DeleteFileLocation = false;
        }

        private void DeleteItemPath(BaseItem item, bool isRequiredForDelete, FileSystemMetadata fileSystemInfo)
        {
            if (FileSystemEntryExists(fileSystemInfo))
            {
                try
                {
                    LogDeletingItemPath(item, fileSystemInfo);

                    DeleteFileSystemEntry(fileSystemInfo);
                }
                catch (DirectoryNotFoundException)
                {
                    LogItemDirectoryNotFound(item, fileSystemInfo);
                }
                catch (FileNotFoundException)
                {
                    LogItemFileNotFound(item, fileSystemInfo);
                }
                catch (IOException)
                {
                    if (isRequiredForDelete)
                    {
                        throw;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    if (isRequiredForDelete)
                    {
                        throw;
                    }
                }
            }
        }

        private void LogDeletingItemPath(BaseItem item, FileSystemMetadata fileSystemInfo)
        {
            _logger.LogInformation(
                "Deleting item path, Type: {Type}, Name: {Name}, Path: {Path}, Id: {Id}",
                item.GetType().Name,
                item.Name ?? "Unknown name",
                fileSystemInfo.FullName,
                item.Id);
        }

        private void LogItemDirectoryNotFound(BaseItem item, FileSystemMetadata fileSystemInfo)
        {
            _logger.LogInformation(
                "Directory not found, only removing from database, Type: {Type}, Name: {Name}, Path: {Path}, Id: {Id}",
                item.GetType().Name,
                item.Name ?? "Unknown name",
                fileSystemInfo.FullName,
                item.Id);
        }

        private void LogItemFileNotFound(BaseItem item, FileSystemMetadata fileSystemInfo)
        {
            _logger.LogInformation(
                "File not found, only removing from database, Type: {Type}, Name: {Name}, Path: {Path}, Id: {Id}",
                item.GetType().Name,
                item.Name ?? "Unknown name",
                fileSystemInfo.FullName,
                item.Id);
        }

        private static bool FileSystemEntryExists(FileSystemMetadata fileSystemInfo)
        {
            return Directory.Exists(fileSystemInfo.FullName) || File.Exists(fileSystemInfo.FullName);
        }

        private static void DeleteFileSystemEntry(FileSystemMetadata fileSystemInfo)
        {
            if (fileSystemInfo.IsDirectory)
            {
                Directory.Delete(fileSystemInfo.FullName, true);
            }
            else
            {
                File.Delete(fileSystemInfo.FullName);
            }
        }

        private bool IsInternalItem(BaseItem item)
        {
            if (!item.IsFileProtocol)
            {
                return false;
            }

            var pathToCheck = GetInternalItemsPath(item);

            var itemPath = item.Path;
            if (!string.IsNullOrEmpty(pathToCheck) && !string.IsNullOrEmpty(itemPath))
            {
                var cleanPath = _fileSystem.GetValidFilename(itemPath);
                var cleanCheckPath = _fileSystem.GetValidFilename(pathToCheck);

                return cleanPath.StartsWith(cleanCheckPath, StringComparison.Ordinal);
            }

            return false;
        }

        private string? GetInternalItemsPath(BaseItem item)
        {
            return item switch
            {
                Genre => _configurationManager.ApplicationPaths.GenrePath,
                MusicArtist => _configurationManager.ApplicationPaths.ArtistsPath,
                MusicGenre => _configurationManager.ApplicationPaths.MusicGenrePath,
                Person => _configurationManager.ApplicationPaths.PeoplePath,
                Studio => _configurationManager.ApplicationPaths.StudioPath,
                Year => _configurationManager.ApplicationPaths.YearPath,
                _ => null
            };
        }

        private List<string> GetMetadataPaths(BaseItem item, IEnumerable<BaseItem> children)
        {
            var list = GetInternalMetadataPaths(item);
            foreach (var child in children)
            {
                list.AddRange(GetInternalMetadataPaths(child));
            }

            return list;
        }

        private List<string> GetInternalMetadataPaths(BaseItem item)
        {
            var list = new List<string>
            {
                item.GetInternalMetadataPath()
            };

            if (item is Video video)
            {
                // Trickplay
                list.Add(_pathManager.GetTrickplayDirectory(video));

                // Chapter Images
                list.Add(_pathManager.GetChapterImageFolderPath(video));

                // Subtitles and attachments
                foreach (var mediaSource in item.GetMediaSources(false))
                {
                    var subtitleFolder = _pathManager.GetSubtitleFolderPath(mediaSource.Id);
                    if (subtitleFolder is not null)
                    {
                        list.Add(subtitleFolder);
                    }

                    var attachmentFolder = _pathManager.GetAttachmentFolderPath(mediaSource.Id);
                    if (attachmentFolder is not null)
                    {
                        list.Add(attachmentFolder);
                    }
                }
            }

            return list;
        }
    }
}
