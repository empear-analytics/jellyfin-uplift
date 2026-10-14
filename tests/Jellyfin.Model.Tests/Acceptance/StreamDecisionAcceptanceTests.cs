using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Model.Tests.Acceptance;

/// <summary>
/// Acceptance tests for the playback decision <see cref="StreamBuilder"/> makes for a client,
/// recorded per device profile and option variant.
/// </summary>
/// <remarks>
/// These protect the refactoring campaign (docs/refactoring/). Each case runs every media source
/// in Test Data through the public GetOptimalVideoStream for one real client profile, and records
/// what a client receives: play method, container, codecs, transcode reasons, selected tracks, the
/// streaming URL and the subtitle delivery plan. Only public, plugin-facing members are used and
/// only the transcoder's subtitle-extraction capability is mocked. Expected outputs are recorded
/// from the current code; set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public class StreamDecisionAcceptanceTests
{
    private static readonly Guid _itemId = new("11D229B7-2D48-4B95-9F9B-49F6AB75E613");

    private static Dictionary<string, Variant> Variants { get; } = new(StringComparer.Ordinal)
    {
        ["default"] = new(EnableExtraction: false, MaxBitrate: null, SelectSubtitle: false, SecondAudio: false, AllowCopy: true),
        ["bitrate3m-subtitle"] = new(EnableExtraction: true, MaxBitrate: 3_000_000, SelectSubtitle: true, SecondAudio: false, AllowCopy: true),
        ["nocopy-audio2"] = new(EnableExtraction: false, MaxBitrate: null, SelectSubtitle: false, SecondAudio: true, AllowCopy: false),
    };

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var profile in Directory.GetFiles("Test Data", "DeviceProfile-*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal))
        {
            foreach (var variant in Variants.Keys)
            {
                data.Add(profile!["DeviceProfile-".Length..], variant);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void OptimalVideoStream_MatchesRecording(string profileName, string variantName)
    {
        var profile = Load<DeviceProfile>("DeviceProfile-" + profileName);
        var variant = Variants[variantName];
        var transcoder = new Mock<ITranscoderSupport>();
        transcoder.Setup(t => t.CanExtractSubtitles(It.IsAny<string>())).Returns(variant.EnableExtraction);
        var builder = new StreamBuilder(transcoder.Object, NullLogger<StreamBuilder>.Instance);

        var output = new StringBuilder();
        foreach (var file in Directory.GetFiles("Test Data", "MediaSourceInfo-*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file)["MediaSourceInfo-".Length..];
            var source = Load<MediaSourceInfo>(Path.GetFileNameWithoutExtension(file));
            var options = new MediaOptions
            {
                ItemId = _itemId,
                MediaSourceId = source.Id,
                MediaSources = [source],
                DeviceId = "acceptance-device",
                Profile = profile,
                AllowAudioStreamCopy = variant.AllowCopy,
                AllowVideoStreamCopy = variant.AllowCopy,
                EnableDirectStream = false,
                MaxBitrate = variant.MaxBitrate,
                SubtitleStreamIndex = variant.SelectSubtitle ? source.MediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Subtitle)?.Index : null,
                AudioStreamIndex = variant.SecondAudio ? source.MediaStreams?.Where(s => s.Type == MediaStreamType.Audio).Skip(1).FirstOrDefault()?.Index : null
            };

            output.Append("== ").Append(name).Append('\n');
            output.Append(Describe(builder, transcoder.Object, options)).Append('\n');
        }

        RecordedOutput.Verify("StreamDecision", profileName + "." + variantName, output.ToString().TrimEnd());
    }

    private static string Describe(StreamBuilder builder, ITranscoderSupport transcoder, MediaOptions options)
    {
        StreamInfo? info;
        try
        {
            info = builder.GetOptimalVideoStream(options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "exception: " + ex.GetType().Name;
        }

        if (info is null)
        {
            return "no stream";
        }

        var lines = new List<string>
        {
            $"method={info.PlayMethod} container={info.Container} protocol={info.SubProtocol}",
            $"video={string.Join(',', info.VideoCodecs)} audio={string.Join(',', info.AudioCodecs)} reasons={info.TranscodeReasons}",
            $"audioIndex={info.AudioStreamIndex} subtitleIndex={info.SubtitleStreamIndex} subtitleMethod={info.SubtitleDeliveryMethod}",
            "url=" + info.ToUrl("media:", "TOKEN", null)
        };
        foreach (var subtitle in info.GetSubtitleProfiles(transcoder, false, "media:", "TOKEN"))
        {
            lines.Add($"subtitle {subtitle.Index} {subtitle.Format} {subtitle.DeliveryMethod} {subtitle.Url}");
        }

        return string.Join('\n', lines);
    }

    private static T Load<T>(string name)
    {
        using var stream = File.OpenRead(Path.Join("Test Data", name + ".json"));
        return JsonSerializer.Deserialize<T>(stream, JsonDefaults.Options)
            ?? throw new InvalidDataException("Invalid test data: " + name);
    }

    private sealed record Variant(bool EnableExtraction, int? MaxBitrate, bool SelectSubtitle, bool SecondAudio, bool AllowCopy);
}
