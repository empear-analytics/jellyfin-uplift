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
        public Guid GetPersonId(string name)
        {
            return GetItemByNameId<Person>(Person.GetPath(name));
        }

        /// <inheritdoc />
        public Person? GetPerson(string name)
        {
            var path = Person.GetPath(name);
            var id = GetItemByNameId<Person>(path);
            if (GetItemById(id) is Person item)
            {
                return item;
            }

            return null;
        }

        /// <inheritdoc />
        public Person GetOrCreatePerson(string name)
        {
            var existing = GetPerson(name);
            if (existing is not null)
            {
                return existing;
            }

            var path = Person.GetPath(name);
            var info = Directory.CreateDirectory(path);
            var item = new Person
            {
                Name = name,
                Id = GetItemByNameId<Person>(path),
                DateCreated = info.CreationTimeUtc,
                DateModified = info.LastWriteTimeUtc,
                Path = path
            };

            item.PresentationUniqueKey = item.CreatePresentationUniqueKey();

            CreateItem(item, null);

            return item;
        }

        public Guid GetStudioId(string name)
        {
            return GetItemByNameId<Studio>(Studio.GetPath(name));
        }

        public Guid GetGenreId(string name)
        {
            return GetItemByNameId<Genre>(Genre.GetPath(name));
        }

        public Guid GetMusicGenreId(string name)
        {
            return GetItemByNameId<MusicGenre>(MusicGenre.GetPath(name));
        }

        /// <summary>
        /// Gets a Genre.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>Task{Genre}.</returns>
        public MusicArtist GetArtist(string name)
        {
            return GetArtist(name, new DtoOptions(true));
        }

        public IReadOnlyDictionary<string, MusicArtist[]> GetArtists(IReadOnlyList<string> names)
        {
            return _linkedChildrenService.FindArtists(names);
        }

        public MusicArtist GetArtist(string name, DtoOptions options)
        {
            return CreateItemByName<MusicArtist>(MusicArtist.GetPath, name, options);
        }
    }
}
