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
        /// Resolves the item.
        /// </summary>
        /// <param name="args">The args.</param>
        /// <param name="resolvers">The resolvers.</param>
        /// <returns>BaseItem.</returns>
        private BaseItem? ResolveItem(ItemResolveArgs args, IItemResolver[]? resolvers)
        {
            var item = (resolvers ?? EntityResolvers).Select(r => Resolve(args, r))
                .FirstOrDefault(i => i is not null);

            if (item is not null)
            {
                ResolverHelper.SetInitialItemValues(item, args, _fileSystem, this);
            }

            return item;
        }

        private BaseItem? Resolve(ItemResolveArgs args, IItemResolver resolver)
        {
            try
            {
                return resolver.ResolvePath(args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in {Resolver} resolving {Path}", resolver.GetType().Name, args.Path);
                return null;
            }
        }

        public Guid GetNewItemId(string key, Type type)
        {
            return GetNewItemIdInternal(key, type, false);
        }

        private Guid GetNewItemIdInternal(string key, Type type, bool forceCaseInsensitive)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentNullException.ThrowIfNull(type);

            string programDataPath = _configurationManager.ApplicationPaths.ProgramDataPath;
            if (key.StartsWith(programDataPath, StringComparison.Ordinal))
            {
                // Try to normalize paths located underneath program-data in an attempt to make them more portable
                key = key.Substring(programDataPath.Length)
                    .TrimStart('/', '\\')
                    .Replace('/', '\\');
            }

            if (forceCaseInsensitive || !_configurationManager.Configuration.EnableCaseSensitiveItemIds)
            {
                key = key.ToLowerInvariant();
            }

            key = type.FullName + key;

            return key.GetMD5();
        }

        public BaseItem? ResolvePath(
            FileSystemMetadata fileInfo,
            Folder? parent = null,
            IDirectoryService? directoryService = null,
            CollectionType? collectionType = null)
            => ResolvePath(fileInfo, directoryService ?? new DirectoryService(_fileSystem), null, parent, collectionType);

        private void SetAdditionalPartsFromStack(Video altVideo, string path)
        {
            if (altVideo.AdditionalParts is { Length: > 0 })
            {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            IEnumerable<FileSystemMetadata> siblings;
            try
            {
                siblings = _fileSystem.GetFiles(directory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate siblings to detect stack for {Path}", path);
                return;
            }

            var stacks = StackResolver.Resolve(siblings, _namingOptions);
            foreach (var stack in stacks)
            {
                if (stack.Files.Count > 1
                    && string.Equals(stack.Files[0], path, StringComparison.OrdinalIgnoreCase))
                {
                    altVideo.AdditionalParts = stack.Files.Skip(1).ToArray();
                    return;
                }
            }
        }

        /// <inheritdoc />
        public Video? ResolveAlternateVersion(string path, Type expectedVideoType, Folder? parent, CollectionType? collectionType)
        {
            // Clean up any existing item saved with wrong type (e.g. Video instead of Movie).
            // This happens when items were previously resolved without proper type context
            // in mixed-content libraries where collectionType is null.
            var expectedId = GetNewItemId(path, expectedVideoType);
            if (expectedVideoType != typeof(Video))
            {
                var wrongTypeId = GetNewItemId(path, typeof(Video));
                if (!wrongTypeId.Equals(expectedId) && GetItemById(wrongTypeId) is Video wrongTypeItem)
                {
                    _logger.LogInformation(
                        "Removing alternate version with wrong type {WrongType}, expected {ExpectedType}: {Path}",
                        wrongTypeItem.GetType().Name,
                        expectedVideoType.Name,
                        path);

                    // A full DeleteItem would save the primary version, which resolves its
                    // alternates again and re-enters here before this row is gone.
                    DeleteItemsUnsafeFast([wrongTypeItem]);

                    // The fast path skips the parent bookkeeping, and the stale item is listed
                    // under its ParentId, so that folder's cached listing has to be dropped.
                    if (wrongTypeItem.GetParent() is Folder staleParent)
                    {
                        staleParent.Children = null;
                        staleParent.UserData = null;
                    }
                }
            }

            var resolved = ResolvePath(
                _fileSystem.GetFileSystemInfo(path),
                parent,
                collectionType: collectionType) as Video;

            if (resolved is null)
            {
                return null;
            }

            // Ensure the alternate version has the same concrete type as the primary video.
            // ResolvePath may return a generic Video for files in mixed-content libraries
            // where collectionType is null, even though the primary is a Movie/Episode/etc.
            if (resolved.GetType() != expectedVideoType)
            {
                if (Activator.CreateInstance(expectedVideoType) is Video correctVideo)
                {
                    correctVideo.Path = resolved.Path;
                    correctVideo.Name = resolved.Name;
                    correctVideo.VideoType = resolved.VideoType;
                    correctVideo.ProductionYear = resolved.ProductionYear;
                    correctVideo.ExtraType = resolved.ExtraType;
                    resolved = correctVideo;
                }
            }

            resolved.Id = expectedId;
            return resolved;
        }

        private BaseItem? ResolvePath(
            FileSystemMetadata fileInfo,
            IDirectoryService directoryService,
            IItemResolver[]? resolvers,
            Folder? parent = null,
            CollectionType? collectionType = null,
            LibraryOptions? libraryOptions = null)
        {
            ArgumentNullException.ThrowIfNull(fileInfo);

            var fullPath = fileInfo.FullName;

            if (collectionType is null && parent is not null)
            {
                collectionType = GetContentTypeOverride(fullPath, true);
            }

            var args = new ItemResolveArgs(_configurationManager.ApplicationPaths, this)
            {
                Parent = parent,
                FileInfo = fileInfo,
                CollectionType = collectionType,
                LibraryOptions = libraryOptions
            };

            // Return null if ignore rules deem that we should do so
            if (IgnoreFile(args.FileInfo, args.Parent))
            {
                return null;
            }

            // Gather child folder and files
            if (args.IsDirectory)
            {
                GatherFileSystemChildren(args, directoryService, parent);
            }

            // Filter content based on ignore rules
            if (args.IsDirectory)
            {
                var filtered = args.GetActualFileSystemChildren().ToArray();
                args.FileSystemChildren = filtered ?? [];
            }

            return ResolveItem(args, resolvers);
        }

        private void GatherFileSystemChildren(ItemResolveArgs args, IDirectoryService directoryService, Folder? parent)
        {
            var isPhysicalRoot = args.IsPhysicalRoot;

            // When resolving the root, we need it's grandchildren (children of user views)
            var flattenFolderDepth = isPhysicalRoot ? 2 : 0;

            FileSystemMetadata[] files;
            var isVf = args.IsVf;

            try
            {
                files = FileData.GetFilteredFileSystemEntries(directoryService, args.Path, _fileSystem, _appHost, _logger, args, flattenFolderDepth: flattenFolderDepth, resolveShortcuts: isPhysicalRoot || isVf);
            }
            catch (Exception ex)
            {
                if (parent is not null && parent.IsPhysicalRoot)
                {
                    _logger.LogError(ex, "Error in GetFilteredFileSystemEntries isPhysicalRoot: {0} IsVf: {1}", isPhysicalRoot, isVf);

                    files = [];
                }
                else
                {
                    throw;
                }
            }

            // Need to remove sub-paths that may have been resolved from shortcuts
            // Example: if \\server\movies exists, then strip out \\server\movies\action
            if (isPhysicalRoot)
            {
                files = NormalizeRootPathList(files).ToArray();
            }

            args.FileSystemChildren = files;
        }

        public bool IgnoreFile(FileSystemMetadata file, BaseItem? parent)
            => EntityResolutionIgnoreRules.Any(r => r.ShouldIgnore(file, parent));

        public List<FileSystemMetadata> NormalizeRootPathList(IEnumerable<FileSystemMetadata> paths)
        {
            var originalList = paths.ToList();

            var list = originalList.Where(i => i.IsDirectory)
                .Select(i => Path.TrimEndingDirectorySeparator(i.FullName))
                .Distinct()
                .ToList();

            var dupes = list.Where(subPath => !subPath.EndsWith(":\\", StringComparison.Ordinal) && list.Any(i => _fileSystem.ContainsSubPath(i, subPath)))
                .ToList();

            foreach (var dupe in dupes)
            {
                _logger.LogInformation("Found duplicate path: {0}", dupe);
            }

            var newList = list.Except(dupes, StringComparer.Ordinal).Select(_fileSystem.GetDirectoryInfo).ToList();
            newList.AddRange(originalList.Where(i => !i.IsDirectory));
            return newList;
        }

        public IEnumerable<BaseItem> ResolvePaths(IEnumerable<FileSystemMetadata> files, IDirectoryService directoryService, Folder parent, LibraryOptions libraryOptions, CollectionType? collectionType = null)
        {
            return ResolvePaths(files, directoryService, parent, libraryOptions, collectionType, EntityResolvers);
        }

        public IEnumerable<BaseItem> ResolvePaths(
            IEnumerable<FileSystemMetadata> files,
            IDirectoryService directoryService,
            Folder parent,
            LibraryOptions libraryOptions,
            CollectionType? collectionType,
            IItemResolver[] resolvers)
        {
            var fileList = files.Where(i => !IgnoreFile(i, parent)).ToList();

            if (parent is not null)
            {
                var multiItemResolvers = resolvers is null ? MultiItemResolvers : resolvers.OfType<IMultiItemResolver>();

                foreach (var resolver in multiItemResolvers)
                {
                    var result = resolver.ResolveMultiple(parent, fileList, collectionType, directoryService);

                    if (result?.Items.Count > 0)
                    {
                        var items = result.Items;
                        items.RemoveAll(item => !ResolverHelper.SetInitialItemValues(item, parent, this, directoryService));
                        items.AddRange(ResolveFileList(result.ExtraFiles, directoryService, parent, collectionType, resolvers, libraryOptions));
                        return items;
                    }
                }
            }

            return ResolveFileList(fileList, directoryService, parent, collectionType, resolvers, libraryOptions);
        }

        private IEnumerable<BaseItem> ResolveFileList(
            IReadOnlyList<FileSystemMetadata> fileList,
            IDirectoryService directoryService,
            Folder? parent,
            CollectionType? collectionType,
            IItemResolver[]? resolvers,
            LibraryOptions libraryOptions)
        {
            // Given that fileList is a list we can save enumerator allocations by indexing
            for (var i = 0; i < fileList.Count; i++)
            {
                var file = fileList[i];
                BaseItem? result = null;
                try
                {
                    result = ResolvePath(file, directoryService, resolvers, parent, collectionType, libraryOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error resolving path {Path}", file.FullName);
                }

                if (result is not null)
                {
                    yield return result;
                }
            }
        }
    }
}
