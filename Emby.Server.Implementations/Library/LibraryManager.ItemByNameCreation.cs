#pragma warning disable CS1591

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;

namespace Emby.Server.Implementations.Library
{
    public partial class LibraryManager
    {
        /// <summary>
        /// Gets the studio.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>Task{Studio}.</returns>
        public Studio GetStudio(string name)
        {
            return CreateItemByName<Studio>(Studio.GetPath, name, new DtoOptions(true));
        }

        /// <summary>
        /// Gets the genre.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>Task{Genre}.</returns>
        public Genre GetGenre(string name)
        {
            return CreateItemByName<Genre>(Genre.GetPath, name, new DtoOptions(true));
        }

        /// <summary>
        /// Gets the music genre.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>Task{MusicGenre}.</returns>
        public MusicGenre GetMusicGenre(string name)
        {
            return CreateItemByName<MusicGenre>(MusicGenre.GetPath, name, new DtoOptions(true));
        }

        /// <summary>
        /// Gets the year.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>Task{Year}.</returns>
        public Year GetYear(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Years less than or equal to 0 are invalid.");
            }

            var name = value.ToString(CultureInfo.InvariantCulture);

            return CreateItemByName<Year>(Year.GetPath, name, new DtoOptions(true));
        }

        private T CreateItemByName<T>(Func<string, string> getPathFn, string name, DtoOptions options)
            where T : BaseItem, new()
        {
            if (typeof(T) == typeof(MusicArtist))
            {
                var existing = GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.MusicArtist],
                    Name = name,
                    DtoOptions = options
                }).Cast<MusicArtist>()
                .OrderBy(i => i.IsAccessedByName ? 1 : 0)
                .Cast<T>()
                .FirstOrDefault();

                if (existing is not null)
                {
                    return existing;
                }
            }

            var path = getPathFn(name);
            var id = GetItemByNameId<T>(path);
            var item = GetItemById(id) as T;
            if (item is null)
            {
                var info = Directory.CreateDirectory(path);
                item = new T
                {
                    Name = name,
                    Id = id,
                    DateCreated = info.CreationTimeUtc,
                    DateModified = info.LastWriteTimeUtc,
                    Path = path
                };

                CreateItem(item, null);
            }

            return item;
        }

        private Guid GetItemByNameId<T>(string path)
              where T : BaseItem, new()
        {
            var forceCaseInsensitiveId = _configurationManager.Configuration.EnableNormalizedItemByNameIds;
            return GetNewItemIdInternal(path, typeof(T), forceCaseInsensitiveId);
        }
    }
}
