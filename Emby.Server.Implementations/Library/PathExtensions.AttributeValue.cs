using System;
using MediaBrowser.Common.Providers;

namespace Emby.Server.Implementations.Library
{
    public static partial class PathExtensions
    {
        /// <summary>
        /// Gets the attribute value.
        /// </summary>
        /// <param name="str">The STR.</param>
        /// <param name="attribute">The attrib.</param>
        /// <returns>System.String.</returns>
        /// <exception cref="ArgumentException"><paramref name="str" /> or <paramref name="attribute" /> is empty.</exception>
        public static string? GetAttributeValue(this ReadOnlySpan<char> str, ReadOnlySpan<char> attribute)
        {
            if (str.Length == 0)
            {
                throw new ArgumentException("String can't be empty.", nameof(str));
            }

            if (attribute.Length == 0)
            {
                throw new ArgumentException("String can't be empty.", nameof(attribute));
            }

            // Allow tmdb as an alias for tmdbid, tvdb for tvdbid, etc.
            // The code below only supports aliases for attributes in the form of "<alias>id".
            ReadOnlySpan<char> shortAttr = GetShortAlias(attribute);

            for (int strIndex = 0, attributeIndex = 0; attributeIndex > -1;)
            {
                // We may want to use imdbid pattern matching later, so we don't want to modify the original 'str'.
                var subStr = str[strIndex..];
                int attributeEnd = 0;

                if (shortAttr.Length > 0)
                {
                    // If we are using an alias it should be shorter (and a prefix), so let's search for that.
                    attributeIndex = subStr.IndexOf(shortAttr, StringComparison.OrdinalIgnoreCase);
                    attributeEnd = attributeIndex + shortAttr.Length;
                }
                else
                {
                    attributeIndex = subStr.IndexOf(attribute, StringComparison.OrdinalIgnoreCase);
                    attributeEnd = attributeIndex + attribute.Length;
                }

                // The next iteration should start at the end of the attribute we just found.
                // If attributeIndex < 0, the loop will end and strIndex won't be used again.
                strIndex += attributeEnd;

                if (attributeIndex <= 0)
                {
                    continue;
                }

                var attributeOpener = subStr[attributeIndex - 1];
                var attributeCloser = GetAttributeCloser(attributeOpener);

                if (attributeCloser == '\0')
                {
                    continue;
                }

                if (IsAliasFollowedById(shortAttr, subStr, attributeEnd))
                {
                    // We were searching for a shortened attribute, but it's followed by "id" - let's skip it.
                    attributeEnd += 2;
                }

                // attributeEnd points at '='.
                // We need at least 1 more character and the closing bracket after that.
                if (!HasAssignmentAfterAttribute(subStr, attributeEnd))
                {
                    continue;
                }

                var closingIndex = subStr[attributeEnd..].IndexOf(attributeCloser);

                // Must be at least 1 character before the closing bracket.
                if (closingIndex <= 1)
                {
                    continue;
                }

                var trimmed = subStr[(attributeEnd + 1)..(attributeEnd + closingIndex)].Trim();

                if (trimmed.Length > 0)
                {
                    return trimmed.ToString();
                }
            }

            return FindImdbIdByPattern(str, attribute);
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
