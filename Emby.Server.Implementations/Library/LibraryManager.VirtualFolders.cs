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
        /// Gets the default view.
        /// </summary>
        /// <returns>IEnumerable{VirtualFolderInfo}.</returns>
        public List<VirtualFolderInfo> GetVirtualFolders()
        {
            return GetVirtualFolders(false);
        }

        public List<VirtualFolderInfo> GetVirtualFolders(bool includeRefreshState)
        {
            _logger.LogDebug("Getting topLibraryFolders");
            var topLibraryFolders = GetUserRootFolder().Children.ToList();

            _logger.LogDebug("Getting refreshQueue");
            var refreshQueue = includeRefreshState ? ProviderManager.GetRefreshQueue() : null;

            return _fileSystem.GetDirectoryPaths(_configurationManager.ApplicationPaths.DefaultUserViewsPath)
                .Select(dir => GetVirtualFolderInfo(dir, topLibraryFolders, refreshQueue))
                .ToList();
        }

        private VirtualFolderInfo GetVirtualFolderInfo(string dir, List<BaseItem> allCollectionFolders, HashSet<Guid>? refreshQueue)
        {
            var info = new VirtualFolderInfo
            {
                Name = Path.GetFileName(dir),

                Locations = _fileSystem.GetFilePaths(dir, false)
                .Where(i => Path.GetExtension(i.AsSpan()).Equals(ShortcutFileExtension, StringComparison.OrdinalIgnoreCase))
                    .Select(i =>
                    {
                        try
                        {
                            return _appHost.ExpandVirtualPath(_fileSystem.ResolveShortcut(i));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error resolving shortcut file {File}", i);
                            return null;
                        }
                    })
                    .Where(i => i is not null)
                    .Order()
                    .ToArray(),

                CollectionType = GetCollectionType(dir)
            };

            var libraryFolder = allCollectionFolders.FirstOrDefault(i => string.Equals(i.Path, dir, StringComparison.OrdinalIgnoreCase));
            if (libraryFolder is not null)
            {
                var libraryFolderId = libraryFolder.Id.ToString("N", CultureInfo.InvariantCulture);
                info.ItemId = libraryFolderId;
                if (libraryFolder.HasImage(ImageType.Primary))
                {
                    info.PrimaryImageItemId = libraryFolderId;
                }

                info.LibraryOptions = GetLibraryOptions(libraryFolder);

                if (refreshQueue is not null)
                {
                    info.RefreshProgress = libraryFolder.GetRefreshProgress();

                    info.RefreshStatus = info.RefreshProgress.HasValue ? "Active" : refreshQueue.Contains(libraryFolder.Id) ? "Queued" : "Idle";
                }
            }

            return info;
        }

        private CollectionTypeOptions? GetCollectionType(string path)
        {
            var files = _fileSystem.GetFilePaths(path, [".collection"], true, false);
            foreach (ReadOnlySpan<char> file in files)
            {
                if (Enum.TryParse<CollectionTypeOptions>(Path.GetFileNameWithoutExtension(file), true, out var res))
                {
                    return res;
                }
            }

            return null;
        }

        public async Task AddVirtualFolder(string name, CollectionTypeOptions? collectionType, LibraryOptions options, bool refreshLibrary)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            name = _fileSystem.GetValidFilename(name.Trim());

            var rootFolderPath = _configurationManager.ApplicationPaths.DefaultUserViewsPath;

            var existingNameCount = 1; // first numbered name will be 2
            var virtualFolderPath = Path.Combine(rootFolderPath, name);
            var originalName = name;
            while (Directory.Exists(virtualFolderPath))
            {
                existingNameCount++;
                name = originalName + existingNameCount;
                virtualFolderPath = Path.Combine(rootFolderPath, name);
            }

            var mediaPathInfos = options.PathInfos;
            ValidateMediaPathsExist(mediaPathInfos);

            LibraryMonitor.Stop();

            try
            {
                Directory.CreateDirectory(virtualFolderPath);

                if (collectionType is not null)
                {
                    var path = Path.Combine(virtualFolderPath, collectionType.ToString()!.ToLowerInvariant() + ".collection"); // Can't be null with legal values?

                    FileHelper.CreateEmpty(path);
                }

                CollectionFolder.SaveLibraryOptions(virtualFolderPath, options);

                AddMediaPaths(name, mediaPathInfos);

                // The libraries root was listed before this folder existed, so drop that listing:
                // anything still reading it resolves the library set without the new folder.
                _directoryService.Invalidate(virtualFolderPath);
            }
            finally
            {
                await ValidateTopLibraryFolders(CancellationToken.None).ConfigureAwait(false);

                if (refreshLibrary)
                {
                    _ = StartScanInBackground();
                }
                else
                {
                    // Need to add a delay here or directory watchers may still pick up the changes
                    await Task.Delay(1000).ConfigureAwait(false);
                    LibraryMonitor.Start();
                }
            }
        }

        private void AddMediaPaths(string name, MediaPathInfo[] mediaPathInfos)
        {
            if (mediaPathInfos is not null)
            {
                foreach (var path in mediaPathInfos)
                {
                    AddMediaPathInternal(name, path, false);
                }
            }
        }

        private static void ValidateMediaPathsExist(MediaPathInfo[] mediaPathInfos)
        {
            if (mediaPathInfos is not null)
            {
                var invalidpath = mediaPathInfos.FirstOrDefault(i => !Directory.Exists(i.Path));
                if (invalidpath is not null)
                {
                    throw new ArgumentException("The specified path does not exist: " + invalidpath.Path + ".");
                }
            }
        }

        internal Task StartScanInBackground()
        {
            // An active scan already handles library structure changes, so this request can be dropped.
            if (IsScanRunning)
            {
                return Task.CompletedTask;
            }

            // Queue instead of restarting so a scan that starts after the check is allowed to finish.
            return Task.Run(QueueLibraryScan);
        }

        public void AddMediaPath(string virtualFolderName, MediaPathInfo mediaPath)
        {
            AddMediaPathInternal(virtualFolderName, mediaPath, true);
        }

        private void AddMediaPathInternal(string virtualFolderName, MediaPathInfo pathInfo, bool saveLibraryOptions)
        {
            ArgumentNullException.ThrowIfNull(pathInfo);

            var path = pathInfo.Path;

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(nameof(path));
            }

            if (!Directory.Exists(path))
            {
                throw new FileNotFoundException("The path does not exist.");
            }

            var rootFolderPath = _configurationManager.ApplicationPaths.DefaultUserViewsPath;
            var virtualFolderPath = FileSystemHelper.GetChildPath(rootFolderPath, virtualFolderName)
                ?? throw new FileNotFoundException(
                    string.Format(CultureInfo.InvariantCulture, "The media collection {0} does not exist", virtualFolderName));

            CreateShortcut(virtualFolderPath, pathInfo);

            if (saveLibraryOptions)
            {
                var libraryOptions = CollectionFolder.GetLibraryOptions(virtualFolderPath);

                libraryOptions.PathInfos = [.. libraryOptions.PathInfos, pathInfo];

                SyncLibraryOptionsToLocations(virtualFolderPath, libraryOptions);

                CollectionFolder.SaveLibraryOptions(virtualFolderPath, libraryOptions);
            }
        }

        public void UpdateMediaPath(string virtualFolderName, MediaPathInfo mediaPath)
        {
            ArgumentNullException.ThrowIfNull(mediaPath);

            var rootFolderPath = _configurationManager.ApplicationPaths.DefaultUserViewsPath;
            var virtualFolderPath = FileSystemHelper.GetChildPath(rootFolderPath, virtualFolderName)
                ?? throw new FileNotFoundException(
                    string.Format(CultureInfo.InvariantCulture, "The media collection {0} does not exist", virtualFolderName));

            var libraryOptions = CollectionFolder.GetLibraryOptions(virtualFolderPath);

            SyncLibraryOptionsToLocations(virtualFolderPath, libraryOptions);

            CollectionFolder.SaveLibraryOptions(virtualFolderPath, libraryOptions);
        }

        private void SyncLibraryOptionsToLocations(string virtualFolderPath, LibraryOptions options)
        {
            var topLibraryFolders = GetUserRootFolder().Children.ToList();
            var info = GetVirtualFolderInfo(virtualFolderPath, topLibraryFolders, null);

            if (info.Locations.Length > 0 && info.Locations.Length != options.PathInfos.Length)
            {
                var list = options.PathInfos.ToList();

                foreach (var location in info.Locations)
                {
                    if (!list.Any(i => string.Equals(i.Path, location, StringComparison.Ordinal)))
                    {
                        list.Add(new MediaPathInfo(location));
                    }
                }

                options.PathInfos = list.ToArray();
            }
        }

        public async Task RemoveVirtualFolder(string name, bool refreshLibrary)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var rootFolderPath = _configurationManager.ApplicationPaths.DefaultUserViewsPath;

            var path = FileSystemHelper.GetChildPath(rootFolderPath, name);

            if (path is null || !Directory.Exists(path))
            {
                throw new FileNotFoundException("The media folder does not exist");
            }

            LibraryMonitor.Stop();

            try
            {
                Directory.Delete(path, true);
                _directoryService.Invalidate(path);
            }
            finally
            {
                CollectionFolder.OnCollectionFolderChange();

                if (refreshLibrary)
                {
                    await ValidateTopLibraryFolders(CancellationToken.None, true).ConfigureAwait(false);

                    _ = StartScanInBackground();
                }
                else
                {
                    // Need to add a delay here or directory watchers may still pick up the changes
                    await Task.Delay(1000).ConfigureAwait(false);
                    LibraryMonitor.Start();
                }
            }
        }

        private void RemoveContentTypeOverrides(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            List<NameValuePair>? removeList = null;

            foreach (var contentType in _configurationManager.Configuration.ContentTypes)
            {
                if (IsContentTypeOverrideOf(path, contentType))
                {
                    (removeList ??= new()).Add(contentType);
                }
            }

            if (removeList is not null)
            {
                _configurationManager.Configuration.ContentTypes = _configurationManager.Configuration.ContentTypes
                    .Except(removeList)
                    .ToArray();

                _configurationManager.SaveConfiguration();
            }
        }

        private bool IsContentTypeOverrideOf(string path, NameValuePair contentType)
        {
            return string.IsNullOrWhiteSpace(contentType.Name)
                    || _fileSystem.AreEqual(path, contentType.Name)
                    || _fileSystem.ContainsSubPath(path, contentType.Name);
        }

        public void RemoveMediaPath(string virtualFolderName, string mediaPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(mediaPath);

            var rootFolderPath = _configurationManager.ApplicationPaths.DefaultUserViewsPath;
            var virtualFolderPath = FileSystemHelper.GetChildPath(rootFolderPath, virtualFolderName);

            if (virtualFolderPath is null || !Directory.Exists(virtualFolderPath))
            {
                throw new FileNotFoundException(
                    string.Format(CultureInfo.InvariantCulture, "The media collection {0} does not exist", virtualFolderName));
            }

            var shortcut = _fileSystem.GetFilePaths(virtualFolderPath, true)
                .Where(i => Path.GetExtension(i.AsSpan()).Equals(ShortcutFileExtension, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(f => _appHost.ExpandVirtualPath(_fileSystem.ResolveShortcut(f)).Equals(mediaPath, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(shortcut))
            {
                _fileSystem.DeleteFile(shortcut);
                _directoryService.Invalidate(shortcut);
            }

            var libraryOptions = CollectionFolder.GetLibraryOptions(virtualFolderPath);

            libraryOptions.PathInfos = libraryOptions
                .PathInfos
                .Where(i => !string.Equals(i.Path, mediaPath, StringComparison.Ordinal))
                .ToArray();

            CollectionFolder.SaveLibraryOptions(virtualFolderPath, libraryOptions);
        }

        public void CreateShortcut(string virtualFolderPath, MediaPathInfo pathInfo)
        {
            var path = pathInfo.Path;
            var rootFolderPath = _configurationManager.ApplicationPaths.DefaultUserViewsPath;

            var shortcutFilename = Path.GetFileNameWithoutExtension(path);

            var lnk = Path.Combine(virtualFolderPath, shortcutFilename + ShortcutFileExtension);

            while (File.Exists(lnk))
            {
                shortcutFilename += "1";
                lnk = Path.Combine(virtualFolderPath, shortcutFilename + ShortcutFileExtension);
            }

            _fileSystem.CreateShortcut(lnk, _appHost.ReverseVirtualPath(path));
            _directoryService.Invalidate(lnk);
            RemoveContentTypeOverrides(path);
        }
    }
}
