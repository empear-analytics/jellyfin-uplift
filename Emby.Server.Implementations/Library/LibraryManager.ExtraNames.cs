#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using Emby.Naming.Video;
using MediaBrowser.Model.Entities;

namespace Emby.Server.Implementations.Library
{
    public partial class LibraryManager
    {
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
    }
}
