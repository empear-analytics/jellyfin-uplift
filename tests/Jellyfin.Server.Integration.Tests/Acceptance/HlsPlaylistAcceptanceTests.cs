using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using AudioItem = MediaBrowser.Controller.Entities.Audio.Audio;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// Acceptance tests for the HLS playlists the server returns over HTTP, recorded per OS.
/// </summary>
/// <remarks>
/// These protect the refactoring campaign (docs/refactoring/) at the HTTP boundary: library
/// items with known streams are created directly in the in-memory server (no media files or
/// ffprobe are needed for a playlist), and the master and variant playlists are requested the
/// way a client does. Responses are compared with recordings of the current code; the access
/// token, which differs per run, is masked, and carriage returns are written out as "\r".
/// Segment requests are covered too: the server builds the ffmpeg command line for the segment,
/// and <see cref="RecordingTranscodeManager"/> records it instead of starting ffmpeg; the server's
/// root folder is masked in it. Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public sealed class HlsPlaylistAcceptanceTests : IClassFixture<AcceptanceApplicationFactory>
{
    private static readonly SemaphoreSlim _setupLock = new(1, 1);
    private static string? _accessToken;

    private readonly AcceptanceApplicationFactory _factory;

    public HlsPlaylistAcceptanceTests(AcceptanceApplicationFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var id in AllCases().Keys)
        {
            data.Add(id);
        }

        return data;
    }

    public static TheoryData<string> SegmentCases()
    {
        var data = new TheoryData<string>();
        foreach (var id in AllSegmentCases().Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Playlist_MatchesRecording(string caseId)
    {
        var testCase = AllCases()[caseId];
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));
        var itemId = await EnsureItemAsync(testCase.Item);

        var url = $"{testCase.Kind}/{itemId:N}/{testCase.Playlist}?MediaSourceId={itemId:N}&PlaySessionId=acceptance&{testCase.Query}";
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Carriage returns are written out as "\r": the playlist's line endings follow the OS,
        // and git would otherwise normalise them away in the recordings.
        var playlist = MaskToken(body, _accessToken).Replace("\r", "\\r", StringComparison.Ordinal);
        var actual = $"{(int)response.StatusCode} {response.StatusCode}\n{playlist}";
        RecordedOutput.Verify("HlsPlaylist", caseId, actual.TrimEnd());
    }

    [Theory]
    [MemberData(nameof(SegmentCases))]
    public async Task SegmentCommandLine_MatchesRecording(string caseId)
    {
        var testCase = AllSegmentCases()[caseId];
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));
        var itemId = await EnsureItemAsync(testCase.Item);
        var session = "segment-" + caseId;

        var url = $"{testCase.Kind}/{itemId:N}/hls1/main/0.{testCase.Extension}?MediaSourceId={itemId:N}&PlaySessionId={session}"
            + $"&{testCase.Query}&runtimeTicks=0&actualSegmentLengthTicks=60000000";
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        var commandLine = _factory.Transcodes.CommandLines.TryGetValue(session, out var line)
            ? MaskRoot(MaskToken(line, _accessToken))
            : "no ffmpeg command line";
        RecordedOutput.Verify("HlsSegmentCommandLine", caseId, $"{(int)response.StatusCode} {response.StatusCode}\n{commandLine}");
    }

    // The root appears as is, with forward slashes, and in ffmpeg's filter escaping (C\:/...).
    private string MaskRoot(string text)
    {
        var forward = _factory.Root.Replace('\\', '/');
        return text.Replace(_factory.Root, "<root>", StringComparison.Ordinal)
            .Replace(forward.Replace(":", "\\:", StringComparison.Ordinal), "<root>", StringComparison.Ordinal)
            .Replace(forward, "<root>", StringComparison.Ordinal);
    }

    private static string MaskToken(string body, string token)
        => string.IsNullOrEmpty(token) ? body : body.Replace(token, "<token>", StringComparison.Ordinal);

    private static Dictionary<string, PlaylistCase> AllCases()
    {
        var cases = new Dictionary<string, PlaylistCase>(StringComparer.Ordinal);
        void Add(string name, TestItem item, string playlist, string query, string kind = "Videos")
            => cases.Add($"{item.Name}.{playlist.Replace(".m3u8", string.Empty, StringComparison.Ordinal)}.{name}", new PlaylistCase(item, kind, playlist, query));

        var queries = new (string Name, string Query)[]
        {
            ("h264-aac-ts", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts"),
            ("h264-aac-fmp4", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=mp4"),
            ("hevc-h264-copy", "VideoCodec=hevc,h264&AudioCodec=aac,eac3,ac3&SegmentContainer=mp4&AllowVideoStreamCopy=true&AllowAudioStreamCopy=true"),
            ("copy-disallowed", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=mp4&AllowVideoStreamCopy=false&AllowAudioStreamCopy=false"),
            ("maxwidth1280-4m", "VideoCodec=h264&AudioCodec=aac&MaxWidth=1280&VideoBitRate=4000000&AudioBitRate=128000&MaxAudioChannels=2"),
            ("adaptive", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=mp4&EnableAdaptiveBitrateStreaming=true&VideoBitRate=20000000"),
            ("sdr-only-h264-high", "VideoCodec=h264,hevc&AudioCodec=aac&SegmentContainer=mp4&h264-profile=high,main&hevc-rangetype=SDR&h264-level=41"),
            ("subtitles-hls", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts&SubtitleStreamIndex=2&SubtitleMethod=Hls"),
            ("subtitles-encode", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts&SubtitleStreamIndex=2&SubtitleMethod=Encode"),
            ("all-copy", "VideoCodec=h264,hevc,av1,vp9&AudioCodec=aac,ac3,eac3,flac,opus,mp3,dts,truehd&SegmentContainer=mp4&AllowVideoStreamCopy=true&AllowAudioStreamCopy=true"),
            ("dovi-copy", "VideoCodec=hevc,av1,h264&AudioCodec=aac,eac3,ac3&SegmentContainer=mp4&AllowVideoStreamCopy=true&AllowAudioStreamCopy=true"
                + "&hevc-rangetype=SDR,HDR10,HLG,DOVI,DOVIWithHDR10,DOVIWithHLG,DOVIWithSDR&av1-rangetype=SDR,HDR10,HLG,DOVI,DOVIWithHDR10"),
            ("hevc-transcode", "VideoCodec=hevc&AudioCodec=aac&SegmentContainer=mp4&AllowVideoStreamCopy=false&hevc-profile=main,main10&hevc-level=150"),
            ("av1-transcode", "VideoCodec=av1&AudioCodec=opus&SegmentContainer=mp4&AllowVideoStreamCopy=false"),
            ("vp9-copy", "VideoCodec=vp9,h264&AudioCodec=opus,aac&SegmentContainer=mp4&AllowVideoStreamCopy=true&AllowAudioStreamCopy=true"),
            ("adaptive-3m", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts&EnableAdaptiveBitrateStreaming=true&VideoBitRate=3000000&AudioBitRate=128000&MaxWidth=1920"),
            ("vtt-subs", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=mp4&SubtitleStreamIndex=2&SubtitleMethod=Hls&SubtitleCodec=vtt"),
            ("audio-ac3", "VideoCodec=h264&AudioCodec=ac3&SegmentContainer=mp4&AllowAudioStreamCopy=false&AudioBitRate=256000&MaxAudioChannels=6"),
            ("audio-eac3", "VideoCodec=h264&AudioCodec=eac3&SegmentContainer=mp4&AllowAudioStreamCopy=false&AudioBitRate=256000&MaxAudioChannels=6"),
            ("audio-flac", "VideoCodec=h264&AudioCodec=flac&SegmentContainer=mp4&AllowAudioStreamCopy=false&MaxAudioChannels=6"),
            ("audio-opus", "VideoCodec=h264&AudioCodec=opus&SegmentContainer=mp4&AllowAudioStreamCopy=false&AudioBitRate=192000&MaxAudioChannels=6"),
            ("audio-mp3", "VideoCodec=h264&AudioCodec=mp3&SegmentContainer=ts&AllowAudioStreamCopy=false&AudioBitRate=192000"),
            ("audio-alac", "VideoCodec=h264&AudioCodec=alac&SegmentContainer=mp4&AllowAudioStreamCopy=false"),
        };

        foreach (var item in TestItem.All.Concat(TestItem.More))
        {
            foreach (var (name, query) in queries)
            {
                Add(name, item, "master.m3u8", query);
            }

            Add("h264-aac-ts", item, "main.m3u8", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts");
            Add("copy-fmp4-segment3", item, "main.m3u8", "VideoCodec=hevc,h264&AudioCodec=aac&SegmentContainer=mp4&AllowVideoStreamCopy=true&SegmentLength=3");
        }

        foreach (var (name, query) in new[] { ("aac", "AudioCodec=aac&SegmentContainer=ts"), ("opus-fmp4", "AudioCodec=opus&SegmentContainer=mp4&MaxAudioChannels=2&AudioBitRate=96000") })
        {
            Add(name, TestItem.FlacTrack, "master.m3u8", query, "Audio");
            Add(name, TestItem.FlacTrack, "main.m3u8", query, "Audio");
        }

        return cases;
    }

    private static Dictionary<string, SegmentCase> AllSegmentCases()
    {
        var cases = new Dictionary<string, SegmentCase>(StringComparer.Ordinal);
        var queries = new (string Name, string Extension, string Query)[]
        {
            ("h264-aac-ts", "ts", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts"),
            ("copy-fmp4", "mp4", "VideoCodec=hevc,h264,av1,vp9&AudioCodec=aac,eac3,ac3,opus&SegmentContainer=mp4&AllowVideoStreamCopy=true&AllowAudioStreamCopy=true"),
            ("hevc-fmp4", "mp4", "VideoCodec=hevc&AudioCodec=aac&SegmentContainer=mp4&AllowVideoStreamCopy=false&VideoBitRate=8000000"),
            ("downscale-sdr", "mp4", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=mp4&AllowVideoStreamCopy=false&MaxWidth=1280&VideoBitRate=3000000&MaxAudioChannels=2&h264-rangetype=SDR"),
            ("burn-in", "ts", "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts&SubtitleStreamIndex=2&SubtitleMethod=Encode"),
        };

        foreach (var item in TestItem.All.Concat(TestItem.More))
        {
            foreach (var (name, extension, query) in queries)
            {
                cases.Add($"{item.Name}.{name}", new SegmentCase(item, "Videos", extension, query));
            }
        }

        cases.Add("flac-track.aac-ts", new SegmentCase(TestItem.FlacTrack, "Audio", "ts", "AudioCodec=aac&SegmentContainer=ts&AudioBitRate=192000"));
        cases.Add("flac-track.opus-fmp4", new SegmentCase(TestItem.FlacTrack, "Audio", "mp4", "AudioCodec=opus&SegmentContainer=mp4&MaxAudioChannels=2&AudioBitRate=96000"));
        cases.Add("flac-track.flac-copy", new SegmentCase(TestItem.FlacTrack, "Audio", "mp4", "AudioCodec=flac&SegmentContainer=mp4&AllowAudioStreamCopy=true"));
        return cases;
    }

    private async Task<Guid> EnsureItemAsync(TestItem testItem)
    {
        var services = _factory.Services;
        var libraryManager = services.GetRequiredService<ILibraryManager>();
        var path = "/acceptance/media/" + testItem.Name + (testItem.IsAudio ? ".flac" : ".mkv");
        var id = libraryManager.GetNewItemId(path, testItem.IsAudio ? typeof(AudioItem) : typeof(Movie));

        await _setupLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (libraryManager.GetItemById(id) is null)
            {
                var root = libraryManager.RootFolder;
                BaseItem item = testItem.IsAudio ? new AudioItem() : new Movie();
                item.Id = id;
                item.Name = testItem.Name;
                item.Path = path;
                item.Container = testItem.IsAudio ? "flac" : "mkv";
                item.RunTimeTicks = TimeSpan.FromSeconds(20).Ticks;
                item.ParentId = root.Id;
                item.DateCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                item.DateModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                libraryManager.CreateItem(item, root);
                services.GetRequiredService<IMediaStreamRepository>().SaveMediaStreams(id, testItem.Streams(), CancellationToken.None);
            }
        }
        finally
        {
            _setupLock.Release();
        }

        return id;
    }

    private sealed record PlaylistCase(TestItem Item, string Kind, string Playlist, string Query);

    private sealed record SegmentCase(TestItem Item, string Kind, string Extension, string Query);
}
