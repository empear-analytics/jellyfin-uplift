using System;
using System.Collections.Generic;
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
/// token, which differs per run, is masked. Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public sealed class HlsPlaylistAcceptanceTests : IClassFixture<JellyfinApplicationFactory>
{
    private static readonly SemaphoreSlim _setupLock = new(1, 1);
    private static string? _accessToken;

    private readonly JellyfinApplicationFactory _factory;

    public HlsPlaylistAcceptanceTests(JellyfinApplicationFactory factory)
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

        var actual = $"{(int)response.StatusCode} {response.StatusCode}\n{MaskToken(body, _accessToken)}";
        RecordedOutput.Verify("HlsPlaylist", caseId, actual.TrimEnd());
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
        };

        foreach (var item in TestItem.All)
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
}
