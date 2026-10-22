using System;
using System.Globalization;
using System.Text;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// Video codec strings.
/// </summary>
public static partial class HlsCodecStringHelpers
{
    /// <summary>
    /// Gets a H.264 codec string.
    /// </summary>
    /// <param name="profile">H.264 profile.</param>
    /// <param name="level">H.264 level.</param>
    /// <returns>H.264 string.</returns>
    public static string GetH264String(string? profile, int level)
    {
        StringBuilder result = new StringBuilder("avc1", 11);

        if (string.Equals(profile, "high", StringComparison.OrdinalIgnoreCase))
        {
            result.Append(".6400");
        }
        else if (string.Equals(profile, "main", StringComparison.OrdinalIgnoreCase))
        {
            result.Append(".4D40");
        }
        else if (string.Equals(profile, "baseline", StringComparison.OrdinalIgnoreCase))
        {
            result.Append(".42E0");
        }
        else
        {
            // Default to constrained baseline if profile is invalid
            result.Append(".4240");
        }

        string levelHex = level.ToString("X2", CultureInfo.InvariantCulture);
        result.Append(levelHex);

        return result.ToString();
    }

    /// <summary>
    /// Gets a H.265 codec string.
    /// </summary>
    /// <param name="profile">H.265 profile.</param>
    /// <param name="level">H.265 level.</param>
    /// <returns>H.265 string.</returns>
    public static string GetH265String(string? profile, int level)
    {
        // The h265 syntax is a bit of a mystery at the time this comment was written.
        // This is what I've found through various sources:
        // FORMAT: [codecTag].[profile].[constraint?].L[level * 30].[UNKNOWN]
        StringBuilder result = new StringBuilder("hvc1", 16);

        if (string.Equals(profile, "main10", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile, "main 10", StringComparison.OrdinalIgnoreCase))
        {
            result.Append(".2.4");
        }
        else
        {
            // Default to main if profile is invalid
            result.Append(".1.4");
        }

        result.Append(".L")
            .Append(level)
            .Append(".B0");

        return result.ToString();
    }

    /// <summary>
    /// Gets an AV1 codec string.
    /// </summary>
    /// <param name="profile">AV1 profile.</param>
    /// <param name="level">AV1 level.</param>
    /// <param name="tierFlag">AV1 tier flag.</param>
    /// <param name="bitDepth">AV1 bit depth.</param>
    /// <returns>The AV1 codec string.</returns>
    public static string GetAv1String(string? profile, int level, bool tierFlag, int bitDepth)
    {
        // https://aomediacodec.github.io/av1-isobmff/#codecsparam
        // FORMAT: [codecTag].[profile].[level][tier].[bitDepth]
        StringBuilder result = new StringBuilder("av01", 13);

        if (string.Equals(profile, "Main", StringComparison.OrdinalIgnoreCase))
        {
            result.Append(".0");
        }
        else if (string.Equals(profile, "High", StringComparison.OrdinalIgnoreCase))
        {
            result.Append(".1");
        }
        else if (string.Equals(profile, "Professional", StringComparison.OrdinalIgnoreCase))
        {
            result.Append(".2");
        }
        else
        {
            // Default to Main
            result.Append(".0");
        }

        if (level is <= 0 or > 31)
        {
            // Default to the maximum defined level 6.3
            level = 19;
        }

        if (IsUnsupportedAv1BitDepth(bitDepth))
        {
            // Default to 8 bits
            bitDepth = 8;
        }

        result.Append('.')
            // Needed to pad it double digits; otherwise, browsers will reject the stream.
            .AppendFormat(CultureInfo.InvariantCulture, "{0:D2}", level)
            .Append(tierFlag ? 'H' : 'M');

        string bitDepthD2 = bitDepth.ToString("D2", CultureInfo.InvariantCulture);
        result.Append('.')
            .Append(bitDepthD2);

        return result.ToString();
    }

    /// <summary>
    /// Gets a Dolby Vision codec string for profiles without a compatible base layer.
    /// </summary>
    /// <param name="dvProfile">Dolby Vision profile number.</param>
    /// <param name="dvLevel">Dolby Vision level number.</param>
    /// <param name="codec">Video codec name (e.g. "hevc", "av1") to determine the DoVi FourCC.</param>
    /// <returns>Dolby Vision codec string.</returns>
    public static string GetDoviString(int dvProfile, int dvLevel, string codec)
    {
        // HEVC DoVi uses dvh1, AV1 DoVi uses dav1 (out-of-band parameter sets, recommended by Apple HLS spec Rule 1.10)
        var fourCc = string.Equals(codec, "av1", StringComparison.OrdinalIgnoreCase) ? "dav1" : "dvh1";
        StringBuilder result = new StringBuilder(fourCc, 12);

        result.Append('.')
            .AppendFormat(CultureInfo.InvariantCulture, "{0:D2}", dvProfile)
            .Append('.')
            .AppendFormat(CultureInfo.InvariantCulture, "{0:D2}", dvLevel);

        return result.ToString();
    }

    private static bool IsUnsupportedAv1BitDepth(int bitDepth)
    {
        return bitDepth != 8
            && bitDepth != 10
            && bitDepth != 12;
    }
}
