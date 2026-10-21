using System;
using MediaBrowser.Common.Providers;

namespace Emby.Server.Implementations.Library
{
    public static partial class PathExtensions
    {
        private static int AttributeValueSkipId(ReadOnlySpan<char> shortAttr, ReadOnlySpan<char> subStr, int attributeEnd)
        {
            if (IsAliasFollowedById(shortAttr, subStr, attributeEnd))
            {
                // We were searching for a shortened attribute, but it's followed by "id" - let's skip it.
                attributeEnd += 2;
            }

            return attributeEnd;
        }

        private static char GetAttributeCloser(char attributeOpener)
        {
            return attributeOpener switch
            {
                '[' => ']',
                '(' => ')',
                '{' => '}',
                _ => '\0'
            };
        }

        private static bool HasAssignmentAfterAttribute(ReadOnlySpan<char> subStr, int attributeEnd)
        {
            return attributeEnd + 2 < subStr.Length && (subStr[attributeEnd] is '=' or '-');
        }

        private static string? FindImdbIdByPattern(ReadOnlySpan<char> str, ReadOnlySpan<char> attribute)
        {
            // for imdbid we also accept pattern matching
            if (attribute.Equals("imdbid", StringComparison.OrdinalIgnoreCase))
            {
                var match = ProviderIdParsers.TryFindImdbId(str, out var imdbId);
                return match ? imdbId.ToString() : null;
            }

            return null;
        }
    }
}
