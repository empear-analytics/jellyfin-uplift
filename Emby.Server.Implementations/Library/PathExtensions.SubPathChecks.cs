using System.Diagnostics.CodeAnalysis;

namespace Emby.Server.Implementations.Library
{
    public static partial class PathExtensions
    {
        private static bool EndsInsideSegment(string path, string subPath, bool oldSubPathEndsWithSeparator, char newDirectorySeparatorChar)
        {
            return path.Length > subPath.Length
                && !oldSubPathEndsWithSeparator
                && path[subPath.Length] != newDirectorySeparatorChar;
        }

        private static bool IsMissingOrLongerThanPath([NotNullWhen(false)] string? path, [NotNullWhen(false)] string? subPath, [NotNullWhen(false)] string? newSubPath)
        {
            return string.IsNullOrEmpty(path)
                || string.IsNullOrEmpty(subPath)
                || string.IsNullOrEmpty(newSubPath)
                || subPath.Length > path.Length;
        }
    }
}
