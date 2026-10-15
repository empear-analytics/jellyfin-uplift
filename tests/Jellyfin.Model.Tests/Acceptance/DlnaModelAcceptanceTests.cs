using System.Globalization;
using System.Text;
using System.Text.Json;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dlna;
using Xunit;

namespace Jellyfin.Model.Tests.Acceptance;

/// <summary>
/// Acceptance tests for the small public building blocks of the playback decision: resolution
/// normalisation, subtitle language and container matching, and profile conditions.
/// </summary>
/// <remarks>
/// These protect the refactoring campaign (docs/refactoring/). They call public members of the
/// plugin-facing MediaBrowser.Model package over a grid of inputs and compare the whole table with
/// a recording of the current behaviour. Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public class DlnaModelAcceptanceTests
{
    [Fact]
    public void ResolutionNormalizer_MatchesRecording()
    {
        var output = new StringBuilder();
        foreach (var input in new int?[] { null, 2_000_000, 10_000_000, 60_000_000 })
        {
            foreach (var target in new[] { 500_000, 1_000_000, 4_000_000, 9_000_000, 20_000_000, 50_000_000 })
            {
                foreach (var equivalentShare in new[] { 1.0, 0.5 })
                {
                    foreach (var (maxWidth, maxHeight) in new (int?, int?)[] { (null, null), (1280, null), (null, 720), (3840, 2160) })
                    {
                        foreach (var fps in new float?[] { null, 24f, 30f, 60f, 120f })
                        {
                            foreach (var hdr in new[] { false, true })
                            {
                                var result = ResolutionNormalizer.Normalize(input, target, (int)(target * equivalentShare), maxWidth, maxHeight, fps, hdr);
                                output.Append(CultureInfo.InvariantCulture, $"in={Show(input)} out={target} eq={equivalentShare} max={Show(maxWidth)}x{Show(maxHeight)} fps={Show(fps)} hdr={hdr}")
                                    .Append(CultureInfo.InvariantCulture, $" -> {Show(result.MaxWidth)}x{Show(result.MaxHeight)}\n");
                            }
                        }
                    }
                }
            }
        }

        RecordedOutput.Verify("DlnaModel", "resolution-normalizer", output.ToString().TrimEnd());
    }

    [Fact]
    public void SubtitleAndContainerMatching_MatchesRecording()
    {
        var output = new StringBuilder();
        foreach (var language in new[] { null, string.Empty, "eng", "eng,swe", "ENG,jpn" })
        {
            var profile = new SubtitleProfile { Format = "srt", Method = SubtitleDeliveryMethod.External, Language = language! };
            foreach (var subtitle in new[] { null, string.Empty, "eng", "swe", "jpn", "ger", "Eng" })
            {
                output.Append(CultureInfo.InvariantCulture, $"subtitle profile={language ?? "null"} language={subtitle ?? "null"} -> {profile.SupportsLanguage(subtitle!)}\n");
            }
        }

        foreach (var (container, subContainer) in new (string?, string?)[] { ("mkv", null), ("mp4,m4v", null), ("mov|mp4", null), ("ts", "hls"), (null, null), ("-mkv", null), ("mp4", "mkv") })
        {
            var profile = new ContainerProfile { Type = DlnaProfileType.Video, Container = container, SubContainer = subContainer };
            foreach (var asked in new[] { "mkv", "mp4", "m4v", "mov", "ts", "hls", "webm", "mkv,webm", "webm,mp4", string.Empty })
            {
                foreach (var useSub in new[] { false, true })
                {
                    output.Append(CultureInfo.InvariantCulture, $"container profile={container ?? "null"}/{subContainer ?? "null"} asked={asked} sub={useSub} -> {profile.ContainsContainer(asked, useSub)}\n");
                }
            }
        }

        foreach (var condition in new[]
        {
            new ProfileCondition(),
            new ProfileCondition(ProfileConditionType.LessThanEqual, ProfileConditionValue.Width, "1920"),
            new ProfileCondition(ProfileConditionType.EqualsAny, ProfileConditionValue.VideoProfile, "high|main", true),
            new ProfileCondition(ProfileConditionType.NotEquals, ProfileConditionValue.IsAnamorphic, "true", false)
        })
        {
            output.Append(CultureInfo.InvariantCulture, $"condition {condition.Condition} {condition.Property} {condition.Value ?? "null"} required={condition.IsRequired}\n");
        }

        var resolution = new ResolutionConfiguration(1280, 3_000_000);
        output.Append(CultureInfo.InvariantCulture, $"resolution {resolution.MaxWidth} {resolution.MaxBitrate}");

        RecordedOutput.Verify("DlnaModel", "matching", output.ToString());
    }

    [Fact]
    public void TranscodingProfileDefaultsAndCopy_MatchRecording()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var defaults = new TranscodingProfile();
        var populated = new TranscodingProfile
        {
            Container = "ts",
            Type = DlnaProfileType.Video,
            VideoCodec = "h264,hevc",
            AudioCodec = "aac,ac3",
            Protocol = MediaStreamProtocol.hls,
            EstimateContentLength = true,
            EnableMpegtsM2TsMode = true,
            TranscodeSeekInfo = TranscodeSeekInfo.Bytes,
            CopyTimestamps = true,
            Context = EncodingContext.Static,
            EnableSubtitlesInManifest = true,
            MaxAudioChannels = "6",
            MinSegments = 2,
            SegmentLength = 3,
            Conditions = [new ProfileCondition(ProfileConditionType.LessThanEqual, ProfileConditionValue.Width, "1920")],
            EnableAudioVbrEncoding = false
        };

        var output = "defaults:\n" + JsonSerializer.Serialize(defaults, options)
            + "\ncopy of defaults:\n" + JsonSerializer.Serialize(new TranscodingProfile(defaults), options)
            + "\ncopy of populated:\n" + JsonSerializer.Serialize(new TranscodingProfile(populated), options);
        RecordedOutput.Verify("DlnaModel", "transcoding-profile", output.ReplaceLineEndings("\n"));
    }

    private static string Show(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Show(float? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
