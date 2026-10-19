using System;

namespace Emby.Server.Implementations.Library
{
    public static partial class PathExtensions
    {
        private static bool IsAliasFollowedById(ReadOnlySpan<char> shortAttr, ReadOnlySpan<char> subStr, int attributeEnd)
        {
            return shortAttr.Length > 0
                && attributeEnd + 1 < subStr.Length
                && (subStr[attributeEnd] is 'i' or 'I')
                && (subStr[attributeEnd + 1] is 'd' or 'D');
        }

        private static ReadOnlySpan<char> GetShortAlias(ReadOnlySpan<char> attribute)
        {
            return attribute switch
            {
                _ when attribute.Equals("tmdbid", StringComparison.OrdinalIgnoreCase) => "tmdb",
                _ when attribute.Equals("tvdbid", StringComparison.OrdinalIgnoreCase) => "tvdb",
                _ when attribute.Equals("imdbid", StringComparison.OrdinalIgnoreCase) => "imdb",
                _ => ReadOnlySpan<char>.Empty
            };
        }
    }
}
