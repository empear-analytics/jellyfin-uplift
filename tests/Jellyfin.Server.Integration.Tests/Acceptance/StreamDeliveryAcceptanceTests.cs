using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// Acceptance tests for how the server delivers streams over HTTP: remote media passed through
/// from an upstream server, live HLS playlists, and progressive transcodes that are only asked
/// for their headers or have already been written.
/// </summary>
/// <remarks>
/// These protect the refactoring campaign (docs/refactoring/). The upstream server is
/// <see cref="UpstreamStub"/>; a live transcode is answered by <see cref="RecordingTranscodeManager"/>
/// writing the playlist ffmpeg would write, from the command line the server built. Responses are
/// recorded per OS with machine-specific folders and transcode file names masked.
/// Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public sealed partial class StreamDeliveryAcceptanceTests : IClassFixture<AcceptanceApplicationFactory>
{
    private static readonly Dictionary<string, string?> _ranges = new(StringComparer.Ordinal)
    {
        ["no-range"] = null,
        ["range-0-9"] = "bytes=0-9",
        ["range-5-open"] = "bytes=5-",
        ["range-empty"] = string.Empty
    };

    private readonly AcceptanceApplicationFactory _factory;

    public StreamDeliveryAcceptanceTests(AcceptanceApplicationFactory factory)
    {
        _factory = factory;
        _factory.Transcodes.ProduceLivePlaylists = true;
    }

    public static TheoryData<string, string> RemoteCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var item in TestItem.RemoteFlac)
        {
            foreach (var range in _ranges.Keys)
            {
                data.Add(item.Name, range);
            }
        }

        return data;
    }

    public static TheoryData<string> LiveCases()
    {
        var data = new TheoryData<string>();
        foreach (var id in LiveQueries().Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RemoteCases))]
    public async Task RemoteStaticStream_MatchesRecording(string itemName, string rangeName)
    {
        var item = TestItem.RemoteFlac.Single(i => i.Name == itemName);
        var client = await _factory.CreateAuthenticatedClientAsync();
        var itemId = await _factory.EnsureItemAsync(item);
        _factory.Upstream.Requests.Clear();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"Audio/{itemId:N}/stream?Static=true&DeviceId=acceptance");
        if (_ranges[rangeName] is { } range)
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var lines = new List<string>
        {
            $"{(int)response.StatusCode} {response.StatusCode}",
            "accept-ranges: " + string.Join(", ", response.Headers.AcceptRanges),
            "content-range: " + (response.Content.Headers.ContentRange?.ToString() ?? "-"),
            "content-length: " + (response.Content.Headers.ContentLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"),
            "content-type: " + (response.Content.Headers.ContentType?.ToString() ?? "-"),
            "body: " + body
        };
        lines.AddRange(_factory.Upstream.Requests.Select(r => "upstream: " + r));
        RecordedOutput.Verify("StreamDelivery", $"{itemName}.{rangeName}", string.Join('\n', lines));
    }

    [Theory]
    [MemberData(nameof(LiveCases))]
    public async Task LivePlaylist_MatchesRecording(string caseId)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var itemId = await _factory.EnsureItemAsync(TestItem.H264Sdr);
        var url = $"Videos/{itemId:N}/live.m3u8?MediaSourceId={itemId:N}&PlaySessionId=live-{caseId}&DeviceId=acceptance&{LiveQueries()[caseId]}";

        // The second request finds the playlist already written and does not start a transcode.
        var output = new StringBuilder();
        foreach (var attempt in new[] { "first", "again" })
        {
            var endRequests = _factory.Transcodes.LiveEndRequests;
            var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            output.Append("== ").Append(attempt).Append(' ').Append((int)response.StatusCode).Append(' ').Append(response.StatusCode)
                .Append(" content-type=").Append(response.Content.Headers.ContentType?.ToString() ?? "-")
                .Append(" end-requests=").Append(_factory.Transcodes.LiveEndRequests - endRequests).Append('\n')
                .Append(Normalize(body)).Append('\n');
        }

        RecordedOutput.Verify("StreamDelivery", "live." + caseId, output.ToString().TrimEnd());
    }

    [Fact]
    public async Task TranscodedFileHeadAndExisting_MatchRecording()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var itemId = await _factory.EnsureItemAsync(TestItem.FlacTrack);
        var output = new StringBuilder();

        // A HEAD request answers with the headers of the transcode without starting it.
        using (var head = new HttpRequestMessage(HttpMethod.Head, $"Audio/{itemId:N}/stream.mp3?AudioCodec=mp3&DeviceId=acceptance&PlaySessionId=head"))
        {
            var response = await client.SendAsync(head, TestContext.Current.CancellationToken);
            output.Append("head: ").Append((int)response.StatusCode)
                .Append(" content-type=").Append(response.Content.Headers.ContentType?.ToString() ?? "-")
                .Append(" accept-ranges=").Append(string.Join(", ", response.Headers.AcceptRanges))
                .Append(" ffmpeg=").Append(_factory.Transcodes.CommandLines.ContainsKey("head")).Append('\n');
        }

        // A transcode whose output is already there is served from that file instead of being started again.
        var url = $"Audio/{itemId:N}/stream.opus?AudioCodec=opus&DeviceId=acceptance&PlaySessionId=existing";
        var first = await client.GetAsync(url, TestContext.Current.CancellationToken);
        output.Append("first: ").Append((int)first.StatusCode).Append('\n');
        var outputPath = OutputPath().Match(_factory.Transcodes.CommandLines["existing"]).Groups[1].Value;
        await File.WriteAllTextAsync(outputPath, "OggS already transcoded", TestContext.Current.CancellationToken);
        _factory.Transcodes.CommandLines.TryRemove("existing", out _);

        var again = await client.GetAsync(url, TestContext.Current.CancellationToken);
        var body = await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        output.Append("again: ").Append((int)again.StatusCode)
            .Append(" content-type=").Append(again.Content.Headers.ContentType?.ToString() ?? "-")
            .Append(" accept-ranges=").Append(string.Join(", ", again.Headers.AcceptRanges))
            .Append(" ffmpeg=").Append(_factory.Transcodes.CommandLines.ContainsKey("existing"))
            .Append(" body=").Append(body);

        RecordedOutput.Verify("StreamDelivery", "transcoded-file", output.ToString());
    }

    private static Dictionary<string, string> LiveQueries() => new(StringComparer.Ordinal)
    {
        ["ts-default"] = "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts",
        ["ts-min1"] = "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts&MinSegments=1",
        ["ts-min4"] = "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts&MinSegments=4",
        ["ts-min0"] = "VideoCodec=h264&AudioCodec=aac&SegmentContainer=ts&MinSegments=0",
        ["fmp4-default"] = "VideoCodec=h264&AudioCodec=aac&SegmentContainer=mp4",
        ["fmp4-min1"] = "VideoCodec=h264&AudioCodec=aac&SegmentContainer=mp4&MinSegments=1",
        ["fmp4-min4"] = "VideoCodec=h264&AudioCodec=aac&SegmentContainer=mp4&MinSegments=4",
        ["fmp4-copy-segment3"] = "VideoCodec=h264&AudioCodec=aac&SegmentContainer=mp4&AllowVideoStreamCopy=true&SegmentLength=3",
    };

    private string Normalize(string text)
        => TranscodeFileName().Replace(_factory.Mask(text), "<file>").Replace("\r", "\\r", StringComparison.Ordinal);

    [GeneratedRegex("[0-9a-f]{32}")]
    private static partial Regex TranscodeFileName();

    [GeneratedRegex("-y \"([^\"]+)\"$")]
    private static partial Regex OutputPath();
}
