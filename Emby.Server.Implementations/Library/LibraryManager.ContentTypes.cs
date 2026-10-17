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
        public LibraryOptions GetLibraryOptions(BaseItem item)
        {
            if (item is CollectionFolder collectionFolder)
            {
                return collectionFolder.GetLibraryOptions();
            }

            // List.Find is more performant than FirstOrDefault due to enumerator allocation
            return GetCollectionFolders(item)
                .Find(folder => folder is CollectionFolder) is CollectionFolder collectionFolder2
                ? collectionFolder2.GetLibraryOptions()
                : new LibraryOptions();
        }

        public CollectionType? GetContentType(BaseItem item)
        {
            var configuredContentType = GetConfiguredContentType(item, false);
            if (configuredContentType is not null)
            {
                return configuredContentType;
            }

            configuredContentType = GetConfiguredContentType(item, true);
            if (configuredContentType is not null)
            {
                return configuredContentType;
            }

            return GetInheritedContentType(item);
        }

        public CollectionType? GetInheritedContentType(BaseItem item)
        {
            var type = GetTopFolderContentType(item);

            if (type is not null)
            {
                return type;
            }

            return item.GetParents()
                .Select(GetConfiguredContentType)
                .LastOrDefault(i => i is not null);
        }

        public CollectionType? GetConfiguredContentType(BaseItem item)
        {
            return GetConfiguredContentType(item, false);
        }

        public CollectionType? GetConfiguredContentType(string path)
        {
            return GetContentTypeOverride(path, false);
        }

        public CollectionType? GetConfiguredContentType(BaseItem item, bool inheritConfiguredPath)
        {
            if (item is ICollectionFolder collectionFolder)
            {
                return collectionFolder.CollectionType;
            }

            return GetContentTypeOverride(item.ContainingFolderPath, inheritConfiguredPath);
        }

        private CollectionType? GetContentTypeOverride(string path, bool inherit)
        {
            var nameValuePair = _configurationManager.Configuration.ContentTypes
                                    .FirstOrDefault(i => _fileSystem.AreEqual(i.Name, path)
                                                         || (inherit && !string.IsNullOrEmpty(i.Name)
                                                                     && _fileSystem.ContainsSubPath(i.Name, path)));
            if (Enum.TryParse<CollectionType>(nameValuePair?.Value, out var collectionType))
            {
                return collectionType;
            }

            return null;
        }

        private CollectionType? GetTopFolderContentType(BaseItem item)
        {
            if (item is null)
            {
                return null;
            }

            while (!item.ParentId.IsEmpty())
            {
                var parent = item.GetParent();
                if (parent is null || parent is AggregateFolder)
                {
                    break;
                }

                item = parent;
            }

            return GetUserRootFolder().Children
                .OfType<ICollectionFolder>()
                .Where(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase) || i.PhysicalLocations.Contains(item.Path))
                .Select(i => i.CollectionType)
                .FirstOrDefault(i => i is not null);
        }
    }
}
