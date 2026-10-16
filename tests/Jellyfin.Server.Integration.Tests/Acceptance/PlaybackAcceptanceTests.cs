using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// Acceptance tests for the playback negotiation clients do over HTTP: the PlaybackInfo endpoint
/// with real device profiles, and the universal and progressive audio streams.
/// </summary>
/// <remarks>
/// These protect the refactoring campaign (docs/refactoring/): PlaybackInfo runs MediaInfoHelper
/// and StreamBuilder on the server; the audio endpoints run AudioHelper and
/// FileStreamResponseHelpers. Device profiles are the ones in Jellyfin.Model.Tests' Test Data.
/// Responses are recorded with the access token, play session ids and machine-specific folders
/// masked; for transcodes, the ffmpeg command line the server builds is recorded too.
/// Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public sealed partial class PlaybackAcceptanceTests : IClassFixture<AcceptanceApplicationFactory>
{
    private static readonly string[] _profiles =
    [
        "Chrome", "Firefox", "SafariNext", "AndroidTVExoPlayer", "Tizen4-4K-5.1",
        "WebOS-23", "RokuSSPlus", "LowBandwidth", "TranscodeMedia", "DirectMedia"
    ];

    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    private readonly AcceptanceApplicationFactory _factory;

    public PlaybackAcceptanceTests(AcceptanceApplicationFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string> PlaybackInfoCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var item in Items())
        {
            foreach (var variant in new[] { "default", "bitrate3m-subtitle", "nodirect-nocopy", "audio2-burnin" })
            {
                data.Add(item.Name, variant);
            }
        }

        return data;
    }

    public static TheoryData<string> AudioCases()
    {
        var data = new TheoryData<string>();
        foreach (var id in AudioRequests().Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PlaybackInfoCases))]
    public async Task PlaybackInfo_MatchesRecording(string itemName, string variant)
    {
        var item = Items().Single(i => i.Name == itemName);
        var client = await _factory.CreateAuthenticatedClientAsync();
        var itemId = await _factory.EnsureItemAsync(item);
        var streams = item.Streams();
        var subtitle = streams.FirstOrDefault(s => s.Type == MediaStreamType.Subtitle)?.Index;
        var secondAudio = streams.Where(s => s.Type == MediaStreamType.Audio).Skip(1).FirstOrDefault()?.Index;

        var output = new StringBuilder();
        foreach (var profile in _profiles)
        {
            var body = new JsonObject { ["DeviceProfile"] = JsonNode.Parse(await File.ReadAllTextAsync(ProfilePath(profile), TestContext.Current.CancellationToken)) };
            switch (variant)
            {
                case "bitrate3m-subtitle":
                    body["MaxStreamingBitrate"] = 3_000_000;
                    body["SubtitleStreamIndex"] = subtitle;
                    break;
                case "nodirect-nocopy":
                    body["EnableDirectPlay"] = false;
                    body["EnableDirectStream"] = false;
                    body["AllowVideoStreamCopy"] = false;
                    body["AllowAudioStreamCopy"] = false;
                    body["MaxAudioChannels"] = 2;
                    break;
                case "audio2-burnin":
                    body["AudioStreamIndex"] = secondAudio;
                    body["SubtitleStreamIndex"] = subtitle;
                    body["AlwaysBurnInSubtitleWhenTranscoding"] = true;
                    break;
            }

            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"Items/{itemId:N}/PlaybackInfo", content, TestContext.Current.CancellationToken);
            var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            output.Append("== ").Append(profile).Append(' ').Append((int)response.StatusCode).Append('\n');
            output.Append(Normalize(text)).Append('\n');
        }

        RecordedOutput.Verify("PlaybackInfo", itemName + "." + variant, output.ToString().TrimEnd());
    }

    [Theory]
    [MemberData(nameof(AudioCases))]
    public async Task AudioStream_MatchesRecording(string caseId)
    {
        var (item, path) = AudioRequests()[caseId];
        var client = await _factory.CreateAuthenticatedClientAsync();
        var itemId = await _factory.EnsureItemAsync(item);
        _factory.Transcodes.LastCommandLine = null;

        var url = path.Replace("{id}", itemId.ToString("N"), StringComparison.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var lines = new List<string>
        {
            $"{(int)response.StatusCode} {response.StatusCode}",
            "content-type: " + response.Content.Headers.ContentType,
            "location: " + Normalize(response.Headers.Location?.ToString() ?? "-"),
            "body: " + bytes.Length + " bytes" + (response.Content.Headers.ContentType?.MediaType?.StartsWith("audio", StringComparison.Ordinal) == true ? " " + Encoding.ASCII.GetString(bytes) : string.Empty),
            "ffmpeg: " + Normalize(_factory.Transcodes.LastCommandLine ?? "-")
        };
        RecordedOutput.Verify("AudioStream", caseId, string.Join('\n', lines));
    }

    private static TestItem[] Items() => [.. TestItem.All, .. TestItem.More, TestItem.FlacTrack, TestItem.FlacFile];

    private static Dictionary<string, (TestItem Item, string Path)> AudioRequests()
    {
        var cases = new Dictionary<string, (TestItem, string)>(StringComparer.Ordinal);
        foreach (var item in new[] { TestItem.FlacTrack, TestItem.FlacFile })
        {
            cases.Add(item.Name + ".universal-direct", (item, "Audio/{id}/universal?Container=flac,mp3&DeviceId=acceptance"));
            cases.Add(item.Name + ".universal-mp3-http", (item, "Audio/{id}/universal?Container=mp3&TranscodingContainer=mp3&TranscodingProtocol=http&AudioCodec=mp3&MaxStreamingBitrate=192000&DeviceId=acceptance"));
            cases.Add(item.Name + ".universal-aac-hls", (item, "Audio/{id}/universal?Container=mp3&TranscodingContainer=ts&TranscodingProtocol=hls&AudioCodec=aac&MaxAudioChannels=2&DeviceId=acceptance"));
            cases.Add(item.Name + ".universal-opus-lowrate", (item, "Audio/{id}/universal?Container=opus&TranscodingContainer=ogg&TranscodingProtocol=http&AudioCodec=opus&MaxStreamingBitrate=64000&MaxAudioSampleRate=24000&DeviceId=acceptance"));
            cases.Add(item.Name + ".stream-static", (item, "Audio/{id}/stream?Static=true&DeviceId=acceptance"));
            cases.Add(item.Name + ".stream-mp3", (item, "Audio/{id}/stream.mp3?AudioCodec=mp3&AudioBitRate=128000&DeviceId=acceptance&PlaySessionId=acceptance"));
            cases.Add(item.Name + ".stream-opus-stereo", (item, "Audio/{id}/stream.opus?AudioCodec=opus&MaxAudioChannels=2&AudioSampleRate=48000&DeviceId=acceptance"));
            cases.Add(item.Name + ".stream-flac-copy", (item, "Audio/{id}/stream.flac?AudioCodec=flac&AllowAudioStreamCopy=true&DeviceId=acceptance"));
        }

        return cases;
    }

    private string Normalize(string text)
    {
        text = _factory.Mask(text);
        text = PlaySessionIdJson().Replace(text, "\"PlaySessionId\":\"<session>\"");
        text = PlaySessionIdQuery().Replace(text, "PlaySessionId=<session>");

        // Without a play session id from the client, the server makes a random one, and the
        // transcode's output file is named after it.
        text = TranscodeFileName().Replace(text, "transcodes$1<file>");
        if (text.StartsWith('{'))
        {
            var json = JsonNode.Parse(text)!;
            HideExperimentProperties(json);
            text = json.ToJsonString(_indented);
        }

        return text.ReplaceLineEndings("\n");
    }

    // The track flags of the feature experiment (docs/experiment/feature-spec.md) are left out, so
    // these recordings neither show nor pin how the feature behaves; it is graded elsewhere.
    private static void HideExperimentProperties(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("IsAudioDescription");
            obj.Remove("IsCommentary");
            foreach (var (_, child) in obj)
            {
                HideExperimentProperties(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                HideExperimentProperties(child);
            }
        }
    }

    private static string ProfilePath(string profile, [CallerFilePath] string callerPath = "")
        => Path.Join(Path.GetDirectoryName(callerPath), "..", "..", "Jellyfin.Model.Tests", "Test Data", "DeviceProfile-" + profile + ".json");

    [GeneratedRegex("\"PlaySessionId\":\"[0-9a-f]{32}\"")]
    private static partial Regex PlaySessionIdJson();

    [GeneratedRegex("PlaySessionId=[0-9a-f]{32}")]
    private static partial Regex PlaySessionIdQuery();

    [GeneratedRegex(@"transcodes([\\/]+)[0-9a-f]{32}")]
    private static partial Regex TranscodeFileName();
}
