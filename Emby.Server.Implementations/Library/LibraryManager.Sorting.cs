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
        public IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, User? user, IEnumerable<ItemSortBy> sortBy, SortOrder sortOrder)
        {
            IOrderedEnumerable<BaseItem>? orderedItems = null;

            foreach (var orderBy in sortBy.Select(o => GetComparer(o, user)).Where(c => c is not null))
            {
                if (orderBy is RandomComparer)
                {
                    var randomItems = items.ToArray();
                    Random.Shared.Shuffle(randomItems);
                    items = randomItems;
                    // Items are no longer ordered at this point, so set orderedItems back to null
                    orderedItems = null;
                }
                else if (orderedItems is null)
                {
                    orderedItems = OrderItems(items, orderBy, sortOrder);
                }
                else
                {
                    orderedItems = ThenOrderItems(orderedItems, orderBy, sortOrder);
                }
            }

            return orderedItems ?? items;
        }

        /// <inheritdoc />
        public IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, User? user, IEnumerable<(ItemSortBy OrderBy, SortOrder SortOrder)> orderBy)
        {
            IOrderedEnumerable<BaseItem>? orderedItems = null;

            foreach (var (name, sortOrder) in orderBy)
            {
                var comparer = GetComparer(name, user);
                if (comparer is null)
                {
                    continue;
                }

                if (comparer is RandomComparer)
                {
                    var randomItems = items.ToArray();
                    Random.Shared.Shuffle(randomItems);
                    items = randomItems;
                    // Items are no longer ordered at this point, so set orderedItems back to null
                    orderedItems = null;
                }
                else if (orderedItems is null)
                {
                    orderedItems = OrderItems(items, comparer, sortOrder);
                }
                else
                {
                    orderedItems = ThenOrderItems(orderedItems, comparer, sortOrder);
                }
            }

            return orderedItems ?? items;
        }

        private static IOrderedEnumerable<BaseItem> OrderItems(IEnumerable<BaseItem> items, IBaseItemComparer? comparer, SortOrder sortOrder)
        {
            return sortOrder == SortOrder.Descending
                ? items.OrderByDescending(i => i, comparer)
                : items.OrderBy(i => i, comparer);
        }

        private static IOrderedEnumerable<BaseItem> ThenOrderItems(IOrderedEnumerable<BaseItem> orderedItems, IBaseItemComparer? comparer, SortOrder sortOrder)
        {
            return sortOrder == SortOrder.Descending
                ? orderedItems!.ThenByDescending(i => i, comparer)
                : orderedItems!.ThenBy(i => i, comparer); // orderedItems is set during the first iteration
        }

        /// <summary>
        /// Gets the comparer.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="user">The user.</param>
        /// <returns>IBaseItemComparer.</returns>
        private IBaseItemComparer? GetComparer(ItemSortBy name, User? user)
        {
            var comparer = Comparers.FirstOrDefault(c => name == c.Type);

            if (comparer is IUserBaseItemComparer)
            {
                if (user is null)
                {
                    throw new ArgumentException($"Sort key '{name}' requires a user, but none was provided.");
                }

                var userComparer = (IUserBaseItemComparer)Activator.CreateInstance(comparer.GetType())!; // only null for Nullable<T> instances

                userComparer.User = user;
                userComparer.UserManager = _userManager;
                userComparer.UserDataManager = _userDataManager;

                return userComparer;
            }

            return comparer;
        }
    }
}
