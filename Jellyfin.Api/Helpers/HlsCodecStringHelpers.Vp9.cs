using System.Globalization;
using System.Text;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// VP9 codec strings.
/// </summary>
public static partial class HlsCodecStringHelpers
{
    /// <summary>
    /// Gets a VP9 codec string.
    /// </summary>
    /// <param name="width">Video width.</param>
    /// <param name="height">Video height.</param>
    /// <param name="pixelFormat">Video pixel format.</param>
    /// <param name="framerate">Video framerate.</param>
    /// <param name="bitDepth">Video bitDepth.</param>
    /// <returns>The VP9 codec string.</returns>
    public static string GetVp9String(int width, int height, string pixelFormat, float framerate, int bitDepth)
    {
        // refer: https://www.webmproject.org/vp9/mp4/
        StringBuilder result = new StringBuilder("vp09", 13);

        var profileString = GetVp9ProfileString(pixelFormat);

        var lumaPictureSize = width * height;
        var lumaSampleRate = lumaPictureSize * framerate;
        var levelString = GetVp9LevelString(lumaPictureSize, lumaSampleRate);

        if (IsUnsupportedBitDepth(bitDepth))
        {
            // Default to 8 bits
            bitDepth = 8;
        }

        result.Append('.').Append(profileString).Append('.').Append(levelString);
        var bitDepthD2 = bitDepth.ToString("D2", CultureInfo.InvariantCulture);
        result.Append('.')
            .Append(bitDepthD2);

        return result.ToString();
    }

    private static string GetVp9ProfileString(string pixelFormat)
    {
        return pixelFormat switch
        {
            "yuv420p" => "00",
            "yuvj420p" => "00",
            "yuv422p" => "01",
            "yuv444p" => "01",
            _ => GetVp9ExtendedProfileString(pixelFormat)
        };
    }

    private static string GetVp9ExtendedProfileString(string pixelFormat)
    {
        return pixelFormat switch
        {
            "yuv420p10le" or "yuv420p12le" => "02",
            "yuv422p10le" or "yuv422p12le" or "yuv444p10le" or "yuv444p12le" => "03",
            _ => "00"
        };
    }

    private static string GetVp9LevelString(int lumaPictureSize, float lumaSampleRate)
    {
        return lumaPictureSize switch
        {
            <= 0 => "00",
            <= 36864 => "10",
            <= 73728 => "11",
            <= 122880 => "20",
            <= 245760 => "21",
            _ => GetVp9HighLevelString(lumaPictureSize, lumaSampleRate)
        };
    }

    private static string GetVp9HighLevelString(int lumaPictureSize, float lumaSampleRate)
    {
        return lumaPictureSize switch
        {
            <= 552960 => "30",
            <= 983040 => "31",
            <= 2228224 => lumaSampleRate <= 83558400 ? "40" : "41",
            <= 8912896 => lumaSampleRate <= 311951360 ? "50" : (lumaSampleRate <= 588251136 ? "51" : "52"),
            <= 35651584 => lumaSampleRate <= 1176502272 ? "60" : (lumaSampleRate <= 4706009088 ? "61" : "62"),
            _ => "00" // This should not happen
        };
    }

    private static bool IsUnsupportedBitDepth(int bitDepth)
    {
        return bitDepth != 8
            && bitDepth != 10
            && bitDepth != 12;
    }
}
