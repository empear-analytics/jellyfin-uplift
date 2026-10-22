#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Dto;

namespace Emby.Server.Implementations.Library
{
    public partial class LibraryManager
    {
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
    }
}
