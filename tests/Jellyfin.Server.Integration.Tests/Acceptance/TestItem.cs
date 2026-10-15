using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// A library item for the acceptance tests, described by the streams ffprobe would have reported.
/// </summary>
/// <param name="Name">The item name, which also determines its path and id.</param>
/// <param name="Streams">Creates the item's media streams.</param>
/// <param name="IsAudio">Whether the item is an audio track rather than a movie.</param>
/// <param name="RealFile">Whether the item is backed by a real (tiny) file, so it can be streamed directly.</param>
public sealed record TestItem(string Name, Func<List<MediaStream>> Streams, bool IsAudio = false, bool RealFile = false)
{
    public static readonly TestItem H264Sdr = new("h264-sdr", () =>
    [
        Video("h264", "High", 41, 1920, 1080, 8, "bt709", 8_000_000),
        Audio(1, "aac", 2, 256_000),
        Subtitle(2, "subrip", "eng")
    ]);

    public static readonly TestItem Hdr10 = new("hevc-hdr10", () =>
    [
        Video("hevc", "Main 10", 150, 3840, 2160, 10, "smpte2084", 25_000_000),
        Audio(1, "eac3", 6, 768_000),
        Subtitle(2, "PGSSUB", "swe")
    ]);

    public static readonly TestItem Dovi = new("hevc-dovi", () =>
    {
        var video = Video("hevc", "Main 10", 153, 3840, 2160, 10, "smpte2084", 30_000_000);
        video.DvProfile = 8;
        video.DvLevel = 6;
        video.DvBlSignalCompatibilityId = 1;
        video.RpuPresentFlag = 1;
        video.BlPresentFlag = 1;
        return [video, Audio(1, "truehd", 8, 4_000_000), Subtitle(2, "ass", "jpn")];
    });

    public static readonly TestItem Av1 = new("av1-sdr", () =>
    [
        Video("av1", "Main", 8, 1920, 1080, 10, "bt709", 6_000_000),
        Audio(1, "opus", 2, 160_000),
        Subtitle(2, "webvtt", "fra")
    ]);

    public static readonly TestItem FlacTrack = new(
        "flac-track",
        () =>
        [
            new MediaStream { Index = 0, Type = MediaStreamType.Audio, Codec = "flac", Channels = 2, SampleRate = 96_000, BitDepth = 24, BitRate = 2_000_000, IsDefault = true }
        ],
        IsAudio: true);

    public static readonly TestItem Vp9Hlg = new("vp9-hlg", () =>
    [
        Video("vp9", "Profile 2", 51, 3840, 2160, 10, "arib-std-b67", 20_000_000),
        Audio(1, "opus", 6, 510_000),
        Subtitle(2, "webvtt", "eng")
    ]);

    public static readonly TestItem H264Surround = new("h264-surround", () =>
    [
        Video("h264", "High", 41, 1920, 1080, 8, "bt709", 12_000_000),
        Audio(1, "dts", 6, 1_536_000),
        Audio(2, "truehd", 8, 4_000_000),
        Audio(3, "ac3", 6, 640_000),
        Subtitle(4, "PGSSUB", "eng")
    ]);

    public static readonly TestItem DoviProfile5 = new("hevc-dovi-p5", () =>
    {
        var video = Video("hevc", "Main 10", 153, 3840, 2160, 10, "smpte2084", 30_000_000);
        video.DvProfile = 5;
        video.DvLevel = 6;
        video.DvBlSignalCompatibilityId = 0;
        video.RpuPresentFlag = 1;
        video.BlPresentFlag = 1;
        video.ColorPrimaries = null;
        video.ColorSpace = null;
        video.ColorTransfer = null;
        return [video, Audio(1, "eac3", 6, 768_000), Subtitle(2, "subrip", "eng")];
    });

    public static readonly TestItem FlacFile = new(
        "flac-file",
        () =>
        [
            new MediaStream { Index = 0, Type = MediaStreamType.Audio, Codec = "flac", Channels = 2, SampleRate = 44_100, BitDepth = 16, BitRate = 900_000, IsDefault = true }
        ],
        IsAudio: true,
        RealFile: true);

    public static readonly TestItem[] All = [H264Sdr, Hdr10, Dovi, Av1];

    /// <summary>
    /// Items added in the second round, kept apart so the first round's case ids stay stable.
    /// </summary>
    public static readonly TestItem[] More = [Vp9Hlg, H264Surround, DoviProfile5];

    private static MediaStream Video(string codec, string profile, double level, int width, int height, int bitDepth, string transfer, int bitrate)
        => new()
        {
            Index = 0,
            Type = MediaStreamType.Video,
            Codec = codec,
            Profile = profile,
            Level = level,
            Width = width,
            Height = height,
            BitDepth = bitDepth,
            PixelFormat = bitDepth == 8 ? "yuv420p" : "yuv420p10le",
            ColorTransfer = transfer,
            ColorPrimaries = transfer == "bt709" ? "bt709" : "bt2020",
            ColorSpace = transfer == "bt709" ? "bt709" : "bt2020nc",
            RealFrameRate = 23.976f,
            AverageFrameRate = 23.976f,
            BitRate = bitrate,
            RefFrames = 4,
            IsAVC = codec == "h264" ? true : null,
            IsDefault = true
        };

    private static MediaStream Audio(int index, string codec, int channels, int bitrate)
        => new()
        {
            Index = index,
            Type = MediaStreamType.Audio,
            Codec = codec,
            Channels = channels,
            SampleRate = 48_000,
            BitRate = bitrate,
            Language = "eng",
            IsDefault = true
        };

    private static MediaStream Subtitle(int index, string codec, string language)
        => new()
        {
            Index = index,
            Type = MediaStreamType.Subtitle,
            Codec = codec,
            Language = language
        };
}
