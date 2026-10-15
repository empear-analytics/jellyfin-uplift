using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AutoFixture;
using AutoFixture.AutoMoq;
using Castle.Components.DictionaryAdapter;
using Emby.Server.Implementations.IO;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using Moq;
using Xunit;

using MediaSourceManager = Emby.Server.Implementations.Library.MediaSourceManager;

namespace Jellyfin.Server.Implementations.Tests.Acceptance;

/// <summary>
/// Acceptance tests for which audio and subtitle track the server picks by default for a user,
/// recorded per stream set and subtitle mode.
/// </summary>
/// <remarks>
/// These protect the refactoring campaign (docs/refactoring/). They call the plugin-facing
/// IMediaSourceManager.SetDefaultAudioAndSubtitleStreamIndices, which runs through
/// MediaSourceManager into MediaStreamSelector. MediaSourceManager is built by AutoFixture, as
/// in the existing tests, so changes to its constructor do not break these tests. Each case
/// records the chosen tracks for every combination of audio language preference, "play default
/// audio track", subtitle language preference, the item's original language and remembered
/// selections. Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public class TrackSelectionAcceptanceTests
{
    private static readonly string?[] _audioPreferences = [null, "eng", "swe", "jpn"];

    private static readonly string?[] _subtitlePreferences = [null, "eng", "swe"];

    private static readonly string?[] _originalLanguages = [null, "jpn"];

    public static TheoryData<string, SubtitlePlaybackMode> Cases()
    {
        var data = new TheoryData<string, SubtitlePlaybackMode>();
        foreach (var set in StreamSets().Keys)
        {
            foreach (var mode in Enum.GetValues<SubtitlePlaybackMode>())
            {
                data.Add(set, mode);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DefaultTracks_MatchRecording(string streamSet, SubtitlePlaybackMode mode)
    {
        var (manager, rememberedData) = CreateManager();
        var output = new StringBuilder();
        foreach (var audioPreference in _audioPreferences)
        {
            foreach (var playDefault in new[] { true, false })
            {
                foreach (var subtitlePreference in _subtitlePreferences)
                {
                    foreach (var original in _originalLanguages)
                    {
                        foreach (var remembered in new[] { false, true })
                        {
                            rememberedData.AudioStreamIndex = remembered ? 2 : null;
                            rememberedData.SubtitleStreamIndex = remembered ? 4 : null;
                            var (audio, subtitle) = Select(manager, streamSet, mode, audioPreference, playDefault, subtitlePreference, original, remembered);
                            output.Append(CultureInfo.InvariantCulture, $"audioPref={audioPreference ?? "-"} playDefault={playDefault} subPref={subtitlePreference ?? "-"} original={original ?? "-"} remembered={remembered}")
                                .Append(CultureInfo.InvariantCulture, $" -> audio={Show(audio)} subtitle={Show(subtitle)}\n");
                        }
                    }
                }
            }
        }

        RecordedOutput.Verify("TrackSelection", streamSet + "." + mode, output.ToString().TrimEnd());
    }

    private static string Show(int? index) => index?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static (IMediaSourceManager Manager, UserItemData Remembered) CreateManager()
    {
        IFixture fixture = new Fixture().Customize(new AutoMoqCustomization { ConfigureMembers = true });
        fixture.Inject<IFileSystem>(fixture.Create<ManagedFileSystem>());

        // The remembered selections are changed per combination through this one object.
        var remembered = new UserItemData { Key = "key" };
        var userData = fixture.Freeze<Mock<IUserDataManager>>();
        userData.Setup(m => m.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>())).Returns(remembered);

        var localization = fixture.Create<Mock<ILocalizationManager>>();
        localization.Setup(m => m.FindLanguageInfo(It.IsAny<string>()))
            .Returns((string s) => string.IsNullOrEmpty(s) ? null : new CultureDto(s, s, s, new EditableList<string> { s }));
        fixture.Inject(localization.Object);

        return (fixture.Create<MediaSourceManager>(), remembered);
    }

    private static (int? Audio, int? Subtitle) Select(
        IMediaSourceManager manager,
        string streamSet,
        SubtitlePlaybackMode mode,
        string? audioPreference,
        bool playDefault,
        string? subtitlePreference,
        string? original,
        bool remembered)
    {
        var user = new User("acceptance", "provider", "reset")
        {
            AudioLanguagePreference = audioPreference,
            PlayDefaultAudioTrack = playDefault,
            SubtitleLanguagePreference = subtitlePreference,
            SubtitleMode = mode,
            RememberAudioSelections = remembered,
            RememberSubtitleSelections = remembered
        };
        var item = new Video
        {
            Id = new Guid("5e1ec7ed-0000-4000-8000-000000000001"),
            OwnerId = Guid.Empty,
            ParentId = Guid.Empty,
            OriginalLanguage = original!
        };
        var source = new MediaSourceInfo { MediaStreams = StreamSets()[streamSet]() };

        manager.SetDefaultAudioAndSubtitleStreamIndices(item, source, user);
        return (source.DefaultAudioStreamIndex, source.DefaultSubtitleStreamIndex);
    }

    private static Dictionary<string, Func<List<MediaStream>>> StreamSets() => new(StringComparer.Ordinal)
    {
        ["anime"] = () =>
        [
            Video(),
            Audio(1, "jpn", isDefault: true, isOriginal: true),
            Audio(2, "eng"),
            Subtitle(3, "eng", "ass", isDefault: true),
            Subtitle(4, "eng", "ass", isForced: true, title: "Signs & Songs"),
            Subtitle(5, "swe", "subrip"),
        ],
        ["movie-multi"] = () =>
        [
            Video(),
            Audio(1, "eng", isDefault: true, isOriginal: true),
            Audio(2, "eng", title: "Commentary"),
            Audio(3, "swe", title: "Swedish dub"),
            Audio(4, "ger"),
            Subtitle(5, "eng", "subrip", isHearingImpaired: true, title: "SDH"),
            Subtitle(6, "eng", "subrip"),
            Subtitle(7, "swe", "subrip", isForced: true),
            Subtitle(8, "swe", "subrip", isExternal: true),
        ],
        ["no-defaults"] = () =>
        [
            Video(),
            Audio(1, "und"),
            Audio(2, "fre"),
            Subtitle(3, "fre", "subrip", isForced: true),
            Subtitle(4, "und", "PGSSUB"),
        ],
        ["single-audio"] = () =>
        [
            Video(),
            Audio(1, "eng", isDefault: true),
        ],
        ["external-subtitles"] = () =>
        [
            Video(),
            Audio(1, "eng", isDefault: true),
            Audio(2, "jpn"),
            Subtitle(3, "eng", "subrip", isExternal: true),
            Subtitle(4, "swe", "subrip", isExternal: true, isForced: true),
            Subtitle(5, "jpn", "ass"),
        ],
    };

    private static MediaStream Video() => new() { Index = 0, Type = MediaStreamType.Video, Codec = "h264", IsDefault = true };

    private static MediaStream Audio(int index, string language, bool isDefault = false, bool isOriginal = false, string? title = null)
        => new()
        {
            Index = index,
            Type = MediaStreamType.Audio,
            Codec = "aac",
            Language = language,
            IsDefault = isDefault,
            IsOriginal = isOriginal,
            Title = title
        };

    private static MediaStream Subtitle(
        int index,
        string language,
        string codec,
        bool isDefault = false,
        bool isForced = false,
        bool isHearingImpaired = false,
        bool isExternal = false,
        string? title = null)
        => new()
        {
            Index = index,
            Type = MediaStreamType.Subtitle,
            Codec = codec,
            Language = language,
            IsDefault = isDefault,
            IsForced = isForced,
            IsHearingImpaired = isHearingImpaired,
            IsExternal = isExternal,
            Path = isExternal ? "/media/movie." + language + ".srt" : null,
            Title = title
        };
}
