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
        public IReadOnlyList<PersonInfo> GetPeople(InternalPeopleQuery query)
        {
            return _peopleRepository.GetPeople(query).Items;
        }

        public IReadOnlyList<PersonInfo> GetPeople(BaseItem item)
        {
            if (item.SupportsPeople)
            {
                var people = GetPeople(new InternalPeopleQuery
                {
                    ItemId = item.Id
                });

                if (people.Count > 0)
                {
                    return people;
                }
            }

            return [];
        }

        public QueryResult<BaseItem> GetPeopleItems(InternalPeopleQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);

            // This hands back by-name items, so the people without one are not ours to report.
            query.MustHaveItem = true;

            var queryResult = _peopleRepository.GetPeople(query);
            var baseItems = queryResult.Items.Select(i =>
                {
                    try
                    {
                        return GetPerson(i.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "error retrieving BaseItem for person: {0}", i.Name);
                        return null;
                    }
                })
                .Where(i => i is not null)
                .Where(i => query.User is null || i!.IsVisible(query.User))
                .OfType<BaseItem>()
                .ToList()
                .AsReadOnly();

            return new QueryResult<BaseItem>
            {
                StartIndex = queryResult.StartIndex,
                TotalRecordCount = queryResult.TotalRecordCount,
                Items = baseItems,
            };
        }

        public IReadOnlyList<string> GetPeopleNames(InternalPeopleQuery query)
        {
            return _peopleRepository.GetPeopleNames(query);
        }

        /// <inheritdoc/>
        public int DeleteOrphanedCredits()
        {
            return _peopleRepository.DeleteOrphanedCredits();
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<Guid, IReadOnlyList<string>> GetPeopleNamesByItems(IReadOnlyList<Guid> itemIds, IReadOnlyList<string> personTypes)
        {
            return _peopleRepository.GetPeopleNamesByItems(itemIds, personTypes);
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<Guid, IReadOnlyList<PersonInfo>> GetPeopleByItems(IReadOnlyList<Guid> itemIds)
        {
            return _peopleRepository.GetPeopleByItems(itemIds);
        }

        public void UpdatePeople(BaseItem item, List<PersonInfo> people)
        {
            UpdatePeopleAsync(item, people, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public async Task UpdatePeopleAsync(BaseItem item, IReadOnlyList<PersonInfo> people, CancellationToken cancellationToken)
        {
            if (!item.SupportsPeople)
            {
                return;
            }

            if (people is not null)
            {
                people = people.Where(e => e is not null).ToArray();
                _peopleRepository.UpdatePeople(item.Id, people);
                await SavePeopleMetadataAsync(people, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SavePeopleMetadataAsync(IEnumerable<PersonInfo> people, CancellationToken cancellationToken)
        {
            foreach (var person in people)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var itemUpdateType = ItemUpdateType.MetadataDownload;
                var saveEntity = false;
                var personEntity = GetPerson(person.Name);

                if (personEntity is null)
                {
                    try
                    {
                        personEntity = GetOrCreatePerson(person.Name);
                        saveEntity = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create person {Name}", person.Name);
                        continue;
                    }
                }

                saveEntity = UpdatePersonProviderIds(personEntity, person, saveEntity);

                if (!string.IsNullOrWhiteSpace(person.ImageUrl) && !personEntity.HasImage(ImageType.Primary))
                {
                    personEntity.SetImage(
                        new ItemImageInfo
                        {
                            Path = person.ImageUrl,
                            Type = ImageType.Primary
                        },
                        0);

                    saveEntity = true;
                    itemUpdateType = ItemUpdateType.ImageUpdate;
                }

                if (saveEntity)
                {
                    await RunMetadataSavers(personEntity, itemUpdateType).ConfigureAwait(false);
                    personEntity.DateLastSaved = DateTime.UtcNow;

                    CreateItems([personEntity], null, CancellationToken.None);
                }
            }
        }

        private static bool UpdatePersonProviderIds(Person personEntity, PersonInfo person, bool saveEntity)
        {
            foreach (var id in person.ProviderIds)
            {
                if (!string.Equals(personEntity.GetProviderId(id.Key), id.Value, StringComparison.OrdinalIgnoreCase))
                {
                    personEntity.SetProviderId(id.Key, id.Value);
                    saveEntity = true;
                }
            }

            return saveEntity;
        }
    }
}
