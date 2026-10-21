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

            return AttributeValueSearch(str, attribute, shortAttr);
        }

        private static string? AttributeValueSearch(ReadOnlySpan<char> str, ReadOnlySpan<char> attribute, ReadOnlySpan<char> shortAttr)
        {
            for (int strIndex = 0, attributeIndex = 0; attributeIndex > -1;)
            {
                // We may want to use imdbid pattern matching later, so we don't want to modify the original 'str'.
                var subStr = str[strIndex..];
                int attributeEnd = 0;

                var searched = SelectSearchedAttribute(attribute, shortAttr);
                attributeIndex = subStr.IndexOf(searched, StringComparison.OrdinalIgnoreCase);
                attributeEnd = attributeIndex + searched.Length;

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

                attributeEnd = AttributeValueSkipId(shortAttr, subStr, attributeEnd);

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
    }
}
