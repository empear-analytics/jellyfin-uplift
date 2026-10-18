using System;
using System.Globalization;
using System.Text;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// Helpers to generate HLS codec strings according to
/// <a href="https://datatracker.ietf.org/doc/html/rfc6381#section-3.3">RFC 6381 section 3.3</a>
/// and the <a href="https://mp4ra.org">MP4 Registration Authority</a>.
/// </summary>
public static partial class HlsCodecStringHelpers
{
    /// <summary>
    /// Codec name for MP3.
    /// </summary>
    public const string MP3 = "mp4a.40.34";

    /// <summary>
    /// Codec name for AC-3.
    /// </summary>
    public const string AC3 = "ac-3";

    /// <summary>
    /// Codec name for E-AC-3.
    /// </summary>
    public const string EAC3 = "ec-3";

    /// <summary>
    /// Codec name for FLAC.
    /// </summary>
    public const string FLAC = "fLaC";

    /// <summary>
    /// Codec name for ALAC.
    /// </summary>
    public const string ALAC = "alac";

    /// <summary>
    /// Codec name for OPUS.
    /// </summary>
    public const string OPUS = "Opus";

    /// <summary>
    /// Codec name for TRUEHD.
    /// </summary>
    public const string TRUEHD = "mlpa";

    /// <summary>
    /// Gets a MP3 codec string.
    /// </summary>
    /// <returns>MP3 codec string.</returns>
    public static string GetMP3String()
    {
        return MP3;
    }

    /// <summary>
    /// Gets an AAC codec string.
    /// </summary>
    /// <param name="profile">AAC profile.</param>
    /// <returns>AAC codec string.</returns>
    public static string GetAACString(string? profile)
    {
        StringBuilder result = new StringBuilder("mp4a", 9);

        if (string.Equals(profile, "HE-AAC", StringComparison.OrdinalIgnoreCase))
        {
            result.Append(".40.5");
        }
        else
        {
            // Default to LC if profile is invalid
            result.Append(".40.2");
        }

        return result.ToString();
    }

    /// <summary>
    /// Gets an AC-3 codec string.
    /// </summary>
    /// <returns>AC-3 codec string.</returns>
    public static string GetAC3String()
    {
        return AC3;
    }

    /// <summary>
    /// Gets an E-AC-3 codec string.
    /// </summary>
    /// <returns>E-AC-3 codec string.</returns>
    public static string GetEAC3String()
    {
        return EAC3;
    }

    /// <summary>
    /// Gets an FLAC codec string.
    /// </summary>
    /// <returns>FLAC codec string.</returns>
    public static string GetFLACString()
    {
        return FLAC;
    }

    /// <summary>
    /// Gets an ALAC codec string.
    /// </summary>
    /// <returns>ALAC codec string.</returns>
    public static string GetALACString()
    {
        return ALAC;
    }

    /// <summary>
    /// Gets an OPUS codec string.
    /// </summary>
    /// <returns>OPUS codec string.</returns>
    public static string GetOPUSString()
    {
        return OPUS;
    }

    /// <summary>
    /// Gets an TRUEHD codec string.
    /// </summary>
    /// <returns>TRUEHD codec string.</returns>
    public static string GetTRUEHDString()
    {
        return TRUEHD;
    }

    /// <summary>
    /// Gets an DTS codec string.
    /// </summary>
    /// <param name="profile">DTS profile.</param>
    /// <returns>DTS codec string.</returns>
    public static string GetDTSString(string? profile)
    {
        if (IsDtsCoreProfile(profile))
        {
            return "dtsc";
        }

        if (IsDtsHdProfile(profile))
        {
            return "dtsh";
        }

        if (string.Equals(profile, "DTS Express", StringComparison.OrdinalIgnoreCase))
        {
            return "dtse";
        }

        // Default to DTS core if profile is invalid
        return "dtsc";
    }

    private static bool IsDtsCoreProfile(string? profile)
    {
        return string.Equals(profile, "DTS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile, "DTS-ES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile, "DTS 96/24", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDtsHdProfile(string? profile)
    {
        return string.Equals(profile, "DTS-HD HRA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile, "DTS-HD MA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile, "DTS-HD MA + DTS:X", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile, "DTS-HD MA + DTS:X IMAX", StringComparison.OrdinalIgnoreCase);
    }
}
