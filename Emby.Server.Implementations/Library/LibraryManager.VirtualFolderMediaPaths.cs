#pragma warning disable CS1591

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;

namespace Emby.Server.Implementations.Library
{
    public partial class LibraryManager
    {
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
