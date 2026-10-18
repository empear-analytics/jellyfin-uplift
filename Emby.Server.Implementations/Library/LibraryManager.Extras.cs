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
        public IEnumerable<BaseItem> FindExtras(BaseItem owner, IReadOnlyList<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService)
        {
            // Apply .ignore rules
            var filtered = fileSystemChildren.Where(c => !_dotIgnoreIgnoreRule.ShouldIgnore(c, owner)).ToList();
            var isFolder = IsFolderOrDiscVideo(owner);
            var ownerVideoInfo = VideoResolver.Resolve(owner.Path, isFolder, _namingOptions, libraryRoot: owner.ContainingFolderPath);
            if (ownerVideoInfo is null)
            {
                return [];
            }

            var candidates = FindExtraCandidates(filtered, ownerVideoInfo, directoryService);

            var extras = new List<BaseItem>();
            var typeCounters = new Dictionary<ExtraType, int>();
            var generatedNames = new Dictionary<ExtraType, HashSet<string>>();

            // Order by path so that the numbering handed out below does not depend on the
            // order the file system happened to list the folder in
            foreach (var candidate in candidates.OrderBy(c => c.Extra.Path, StringComparer.Ordinal))
            {
                var extra = PrepareExtra(candidate);
                if (extra is not null)
                {
                    extras.Add(extra);
                }
            }

            return extras;

            BaseItem? PrepareExtra(ExtraCandidate candidate)
            {
                var resolved = candidate.Extra;
                var extra = resolved;
                var name = GetExtraName(candidate, ownerVideoInfo, typeCounters);

                // Try to retrieve it from the db. If we don't find it, use the resolved version
                var itemById = GetItemById(extra.Id);
                if (itemById is not null)
                {
                    extra = itemById;
                }

                // The name derived from the file is only a default. A name that came from anywhere else,
                // such as a local metadata file, is the user's and has to survive the scan, so only a
                // name this method handed out itself is renewed; RefreshExtras persists the change.
                if (IsUnlockedNewName(name, extra)
                    && (itemById is null || IsGeneratedExtraName(extra.Name, candidate)))
                {
                    extra.Name = name;
                }

                // Only update extra type if it is more specific then the currently known extra type
                if (IsMoreSpecificExtraType(extra, candidate))
                {
                    extra.ExtraType = candidate.ExtraType;
                }

                // Only return items that are actual extras (have ExtraType set)
                // Note: OwnerId and ParentId are set by RefreshExtras, not here,
                // so that RefreshExtras can detect when they need updating and set ForceSave.
                if (extra.ExtraType is not null)
                {
                    extra.IsInMixedFolder = candidate.IsInMixedFolder;
                    return extra;
                }

                return null;
            }

            bool IsGeneratedExtraName(string currentName, ExtraCandidate candidate)
            {
                // The file name is what an extra was called before it was given a name of its type
                if (string.Equals(currentName, candidate.Extra.Name, StringComparison.Ordinal))
                {
                    return true;
                }

                if (!generatedNames.TryGetValue(candidate.ExtraType, out var names))
                {
                    // Any of the numbers of this type may have been handed out, as the order the extras
                    // of a type are numbered in shifts as files appear beside them or are taken away
                    names = new HashSet<string>(StringComparer.Ordinal);
                    var count = candidates.Count(c => c.ExtraType == candidate.ExtraType);
                    for (var seen = 0; seen < count; seen++)
                    {
                        names.Add(GetNumberedExtraName(candidate.ExtraType, seen));
                    }

                    generatedNames[candidate.ExtraType] = names;
                }

                return names.Contains(currentName);
            }
        }

        private List<ExtraCandidate> FindExtraCandidates(List<FileSystemMetadata> filtered, VideoFileInfo ownerVideoInfo, IDirectoryService directoryService)
        {
            var candidates = new List<ExtraCandidate>();

            var count = filtered.Count;
            for (var i = 0; i < count; i++)
            {
                var current = filtered[i];
                if (current.IsDirectory && _namingOptions.AllExtrasTypesFolderNames.ContainsKey(current.Name))
                {
                    var filesInSubFolder = _fileSystem.GetFiles(current.FullName, null, false, false);
                    var filesInSubFolderList = filesInSubFolder.ToList();

                    bool subFolderIsMixedFolder = filesInSubFolderList.Count > 1;

                    foreach (var file in filesInSubFolderList)
                    {
                        if (!_extraResolver.TryGetExtraTypeForOwner(file.FullName, ownerVideoInfo, out var extraType, out var extraRule))
                        {
                            continue;
                        }

                        AddCandidate(file, extraType.Value, extraRule, subFolderIsMixedFolder);
                    }
                }
                else if (!current.IsDirectory && _extraResolver.TryGetExtraTypeForOwner(current.FullName, ownerVideoInfo, out var extraType, out var extraRule))
                {
                    AddCandidate(current, extraType.Value, extraRule, false);
                }
            }

            return candidates;

            void AddCandidate(FileSystemMetadata file, ExtraType extraType, ExtraRule extraRule, bool isInMixedFolder)
            {
                var extra = ResolvePath(_fileSystem.GetFileInfo(file.FullName), directoryService, _extraResolver.GetResolversForExtraType(extraType));
                if (extra is Video or Audio)
                {
                    candidates.Add(new ExtraCandidate(extra, extraType, extraRule, isInMixedFolder));
                }
            }
        }

        private static bool IsFolderOrDiscVideo(BaseItem owner)
        {
            return owner.IsFolder || (owner is Video video && (video.VideoType == VideoType.BluRay || video.VideoType == VideoType.Dvd));
        }

        private static bool IsUnlockedNewName(string name, BaseItem extra)
        {
            return !string.IsNullOrEmpty(name)
                && extra.LockedFields?.Contains(MetadataField.Name) != true;
        }

        private static bool IsMoreSpecificExtraType(BaseItem extra, ExtraCandidate candidate)
        {
            return extra.ExtraType is null or ExtraType.Unknown || candidate.ExtraType != ExtraType.Unknown;
        }

        /// <summary>
        /// Gets the name to give an extra.
        /// </summary>
        /// <param name="candidate">The resolved extra.</param>
        /// <param name="ownerVideoInfo">The naming info of the owner.</param>
        /// <param name="typeCounters">Number of extras named after their type so far, per type.</param>
        /// <returns>The name.</returns>
        private string GetExtraName(ExtraCandidate candidate, VideoFileInfo ownerVideoInfo, Dictionary<ExtraType, int> typeCounters)
        {
            var isNamedAfterOwner = candidate.ExtraRule.RuleType switch
            {
                ExtraRuleType.Filename => true,
                ExtraRuleType.Suffix => string.Equals(candidate.Extra.Name, ownerVideoInfo.Name, StringComparison.OrdinalIgnoreCase),
                _ => false
            };

            if (!isNamedAfterOwner)
            {
                return candidate.Extra.Name;
            }

            typeCounters.TryGetValue(candidate.ExtraType, out var seen);
            typeCounters[candidate.ExtraType] = seen + 1;

            return GetNumberedExtraName(candidate.ExtraType, seen);
        }

        /// <summary>
        /// Gets the name given to the n-th extra of a type that is named after its type.
        /// </summary>
        /// <param name="extraType">The extra type.</param>
        /// <param name="seen">Number of extras of the type named before this one.</param>
        /// <returns>The name.</returns>
        private string GetNumberedExtraName(ExtraType extraType, int seen)
        {
            var typeName = _localization.GetServerLocalizedString(GetExtraTypeNameKey(extraType));

            return seen == 0
                ? typeName
                : string.Format(
                    CultureInfo.InvariantCulture,
                    _localization.GetServerLocalizedString("NameExtraNumbered"),
                    typeName,
                    seen + 1);
        }

        private static string GetExtraTypeNameKey(ExtraType extraType) => extraType switch
        {
            ExtraType.Clip => "NameExtraClip",
            ExtraType.Trailer => "NameExtraTrailer",
            ExtraType.BehindTheScenes => "NameExtraBehindTheScenes",
            ExtraType.DeletedScene => "NameExtraDeletedScene",
            _ => GetExtraTypeNameKeyFromInterview(extraType)
        };

        private static string GetExtraTypeNameKeyFromInterview(ExtraType extraType) => extraType switch
        {
            ExtraType.Interview => "NameExtraInterview",
            ExtraType.Scene => "NameExtraScene",
            ExtraType.Sample => "NameExtraSample",
            ExtraType.ThemeSong => "NameExtraThemeSong",
            _ => GetExtraTypeNameKeyFromThemeVideo(extraType)
        };

        private static string GetExtraTypeNameKeyFromThemeVideo(ExtraType extraType) => extraType switch
        {
            ExtraType.ThemeVideo => "NameExtraThemeVideo",
            ExtraType.Featurette => "NameExtraFeaturette",
            ExtraType.Short => "NameExtraShort",
            _ => "NameExtraUnknown"
        };

        private sealed record ExtraCandidate(BaseItem Extra, ExtraType ExtraType, ExtraRule ExtraRule, bool IsInMixedFolder);
    }
}
