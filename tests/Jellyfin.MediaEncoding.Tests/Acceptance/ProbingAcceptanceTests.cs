using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Jellyfin.Extensions.Json;
using Jellyfin.Extensions.Json.Converters;
using MediaBrowser.MediaEncoding.Probing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.MediaEncoding.Tests.Acceptance;

/// <summary>
/// Acceptance tests for how ffprobe output is normalised into a <see cref="MediaInfo"/>, recorded
/// per sample and variant.
/// </summary>
/// <remarks>
/// These protect the refactoring campaign (docs/refactoring/). Every ffprobe sample in
/// Test Data/Probing goes through ProbeResultNormalizer.GetMediaInfo (which the existing tests
/// already call), unchanged and with variants applied to its JSON: stream dispositions, language
/// and title tags, HDR10, HLG and Dolby Vision colour data, rotation, interlacing, anamorphic
/// aspect, channel layouts, missing bitrates and music tags. The whole resulting MediaInfo is
/// recorded as JSON, so a change to any normalised field shows. Only localisation is mocked, and
/// it returns its keys. Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public class ProbingAcceptanceTests
{
    private static readonly JsonSerializerOptions _readOptions = CreateReadOptions();

    private static readonly JsonSerializerOptions _writeOptions = new(JsonDefaults.Options)
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { HideExperimentProperties } }
    };

    private static readonly string[] _languages = ["eng", "swe", "jpn", "und", "ger"];

    private static readonly string[] _titles = ["Commentary", "SDH", "Forced", "Director's Cut", "Audio Description"];

    private static readonly string[] _dispositions = ["default", "forced", "hearing_impaired", "visual_impaired", "comment", "original", "dub", "attached_pic"];

    private static readonly Dictionary<string, Action<JsonObject>> _variants = new(StringComparer.Ordinal)
    {
        ["base"] = _ => { },
        ["dispositions"] = SetDispositions,
        ["tags"] = SetTags,
        ["hdr10"] = root => SetVideoColour(root, "smpte2084", "bt2020", "bt2020nc", "yuv420p10le", 10),
        ["hlg"] = root => SetVideoColour(root, "arib-std-b67", "bt2020", "bt2020nc", "yuv420p10le", 10),
        ["dovi8"] = SetDolbyVision,
        ["rotation"] = root => AddVideoSideData(root, new JsonObject { ["side_data_type"] = "Display Matrix", ["rotation"] = -90 }),
        ["interlaced"] = root => SetOnStreams(root, "video", s => s["field_order"] = "tt"),
        ["anamorphic"] = SetAnamorphic,
        ["channels"] = SetChannelLayouts,
        ["no-bitrates"] = RemoveBitrates,
        ["music-tags"] = SetMusicTags,
    };

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var sample in Samples())
        {
            foreach (var variant in _variants.Keys)
            {
                data.Add(sample, variant);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GetMediaInfo_MatchesRecording(string sample, string variant)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Join("Test Data", "Probing", sample + ".json")))!.AsObject();
        _variants[variant](root);
        var probe = root.Deserialize<InternalMediaInfoResult>(_readOptions)!;
        var isAudio = sample.StartsWith("music", StringComparison.Ordinal) && !sample.Contains("video", StringComparison.Ordinal);

        string actual;
        try
        {
            var info = CreateNormalizer().GetMediaInfo(probe, isAudio ? null : VideoType.VideoFile, isAudio, "/media/" + sample + ".mkv", MediaProtocol.File);
            actual = JsonSerializer.Serialize(info, _writeOptions);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or NullReferenceException)
        {
            actual = "exception: " + ex.GetType().Name;
        }

        RecordedOutput.Verify("Probing", sample + "." + variant, actual.ReplaceLineEndings("\n"));
    }

    private static IEnumerable<string> Samples()
        => Directory.GetFiles(Path.Join("Test Data", "Probing"), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .Order(StringComparer.Ordinal);

    private static ProbeResultNormalizer CreateNormalizer()
    {
        var localization = new Mock<ILocalizationManager>();
        localization.Setup(l => l.GetLocalizedString(It.IsAny<string>())).Returns<string>(key => "loc:" + key);
        localization.Setup(l => l.FindLanguageInfo(It.IsAny<string>()))
            .Returns<string>(code => string.IsNullOrEmpty(code) ? null : new CultureDto("lang-" + code, "Lang " + code, code, [code]));
        return new ProbeResultNormalizer(NullLogger<ProbeResultNormalizer>.Instance, localization.Object);
    }

    // The track flags of the feature experiment (docs/experiment/feature-spec.md) are left out, so
    // these recordings neither show nor pin how the feature behaves; it is graded elsewhere.
    private static void HideExperimentProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(MediaStream))
        {
            return;
        }

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (typeInfo.Properties[i].Name is "IsAudioDescription" or "IsCommentary")
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Options);
        options.Converters.Add(new JsonBoolStringConverter());
        return options;
    }

    private static IEnumerable<JsonObject> Streams(JsonObject root, string? type = null)
        => (root["streams"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(s => type is null || (string?)s["codec_type"] == type);

    private static void SetOnStreams(JsonObject root, string type, Action<JsonObject> change)
    {
        foreach (var stream in Streams(root, type))
        {
            change(stream);
        }
    }

    private static JsonObject Tags(JsonObject node)
    {
        if (node["tags"] is not JsonObject tags)
        {
            tags = new JsonObject();
            node["tags"] = tags;
        }

        return tags;
    }

    private static void SetDispositions(JsonObject root)
    {
        var i = 0;
        foreach (var stream in Streams(root).Where(s => (string?)s["codec_type"] is "audio" or "subtitle" or "video"))
        {
            var disposition = new JsonObject();
            foreach (var name in _dispositions)
            {
                disposition[name] = 0;
            }

            disposition[_dispositions[i % _dispositions.Length]] = 1;
            disposition[_dispositions[(i + 3) % _dispositions.Length]] = 1;
            stream["disposition"] = disposition;
            i++;
        }
    }

    private static void SetTags(JsonObject root)
    {
        var i = 0;
        foreach (var stream in Streams(root))
        {
            var tags = Tags(stream);
            tags["language"] = _languages[i % _languages.Length];
            tags["title"] = _titles[i % _titles.Length];
            tags["BPS"] = (1_000_000 + (i * 128_000)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            tags["DURATION"] = "01:30:00.000000000";
            tags["NUMBER_OF_FRAMES"] = "129600";
            tags["handler_name"] = "Handler " + i;
            i++;
        }
    }

    private static void SetVideoColour(JsonObject root, string transfer, string primaries, string space, string pixelFormat, int bits)
        => SetOnStreams(root, "video", s =>
        {
            s["color_transfer"] = transfer;
            s["color_primaries"] = primaries;
            s["color_space"] = space;
            s["pix_fmt"] = pixelFormat;
            s["bits_per_raw_sample"] = bits.ToString(System.Globalization.CultureInfo.InvariantCulture);
        });

    private static void AddVideoSideData(JsonObject root, JsonObject sideData)
        => SetOnStreams(root, "video", s =>
        {
            if (s["side_data_list"] is not JsonArray list)
            {
                list = [];
                s["side_data_list"] = list;
            }

            list.Add(sideData.DeepClone());
        });

    private static void SetDolbyVision(JsonObject root)
    {
        SetVideoColour(root, "smpte2084", "bt2020", "bt2020nc", "yuv420p10le", 10);
        AddVideoSideData(root, new JsonObject
        {
            ["side_data_type"] = "DOVI configuration record",
            ["dv_version_major"] = 1,
            ["dv_version_minor"] = 0,
            ["dv_profile"] = 8,
            ["dv_level"] = 6,
            ["rpu_present_flag"] = 1,
            ["el_present_flag"] = 0,
            ["bl_present_flag"] = 1,
            ["dv_bl_signal_compatibility_id"] = 1
        });
    }

    private static void SetAnamorphic(JsonObject root)
        => SetOnStreams(root, "video", s =>
        {
            s["sample_aspect_ratio"] = "4:3";
            s["display_aspect_ratio"] = "16:9";
        });

    private static void SetChannelLayouts(JsonObject root)
    {
        var layouts = new (string Layout, int Channels)[] { ("7.1", 8), ("5.1(side)", 6), ("mono", 1), ("stereo", 2), ("2.1", 3) };
        var i = 0;
        foreach (var stream in Streams(root, "audio"))
        {
            stream["channel_layout"] = layouts[i % layouts.Length].Layout;
            stream["channels"] = layouts[i % layouts.Length].Channels;
            i++;
        }
    }

    private static void RemoveBitrates(JsonObject root)
    {
        foreach (var stream in Streams(root))
        {
            stream.Remove("bit_rate");
            Tags(stream).Remove("BPS");
            Tags(stream).Remove("BPS-eng");
        }

        (root["format"] as JsonObject)?.Remove("bit_rate");
    }

    private static void SetMusicTags(JsonObject root)
    {
        if (root["format"] is not JsonObject format)
        {
            return;
        }

        var tags = Tags(format);
        tags["title"] = "Track Title";
        tags["artist"] = "Artist One; Artist Two";
        tags["album_artist"] = "Album Artist";
        tags["album"] = "The Album";
        tags["genre"] = "Rock;Pop/Indie";
        tags["date"] = "2019-05-17";
        tags["track"] = "3/12";
        tags["disc"] = "1/2";
        tags["composer"] = "Composer A / Composer B";
        tags["lyricist"] = "Lyricist";
        tags["publisher"] = "Label";
        tags["MusicBrainz Album Id"] = "11111111-2222-3333-4444-555555555555";
        tags["MusicBrainz Artist Id"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        tags["comment"] = "A comment";
        tags["description"] = "A description";
    }
}
