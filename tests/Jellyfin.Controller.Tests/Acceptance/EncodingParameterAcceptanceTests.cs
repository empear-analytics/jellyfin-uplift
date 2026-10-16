using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Moq;
using Xunit;

using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Jellyfin.Controller.Tests.Acceptance;

/// <summary>
/// Acceptance tests for two of EncodingHelper's parameter decisions: the audio bitrate it targets,
/// and how it attaches a media source to a transcoding job.
/// </summary>
/// <remarks>
/// Like the other acceptance suites, these call public, plugin-facing members only, mock only
/// ffmpeg's capabilities, and compare with outputs recorded from the current code, per OS.
/// </remarks>
public class EncodingParameterAcceptanceTests
{
    private static readonly string[] _audioCodecs = ["aac", "mp3", "opus", "vorbis", "ac3", "eac3", "dts", "truehd", "flac", "alac", "pcm_s16le", "none"];

    private static readonly int?[] _channels = [null, 2, 6, 8];

    private static readonly int?[] _bitrates = [null, 128_000, 640_000];

    public static TheoryData<string> AudioCodecCases()
    {
        var data = new TheoryData<string>();
        foreach (var codec in _audioCodecs)
        {
            data.Add(codec);
        }

        return data;
    }

    public static TheoryData<string, string> AttachCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var source in Sources().Keys)
        {
            foreach (var request in Requests().Keys)
            {
                data.Add(source, request);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AudioCodecCases))]
    public void AudioBitrate_MatchesRecording(string codec)
    {
        var helper = CreateHelper();
        var output = new StringBuilder();
        foreach (var (name, stream) in AudioInputs())
        {
            foreach (var channels in _channels)
            {
                foreach (var bitrate in _bitrates)
                {
                    var result = helper.GetAudioBitrateParam(bitrate, codec == "none" ? string.Empty : codec, stream, channels);
                    output.Append(CultureInfo.InvariantCulture, $"{name} channels={Show(channels)} requested={Show(bitrate)} -> {Show(result)}\n");
                }
            }
        }

        RecordedOutput.Verify("AudioBitrate", codec, output.ToString().TrimEnd());
    }

    [Theory]
    [MemberData(nameof(AttachCases))]
    public void AttachMediaSourceInfo_MatchesRecording(string sourceName, string requestName)
    {
        var (request, url, supportedAudio, supportedVideo, isVideo) = Requests()[requestName];
        var state = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            BaseRequest = request(),
            IsVideoRequest = isVideo,
            SupportedAudioCodecs = supportedAudio,
            SupportedVideoCodecs = supportedVideo
        };
        var options = new EncodingOptions { AllowHevcEncoding = requestName != "hevc-disallowed", AllowAv1Encoding = false };

        CreateHelper().AttachMediaSourceInfo(state, options, Sources()[sourceName](), url);

        var lines = new[]
        {
            $"mediaPath={state.MediaPath} protocol={state.InputProtocol} container={state.InputContainer} runtime={Show(state.RunTimeTicks)}",
            $"videoSync={state.InputVideoSync ?? "-"} audioSync={state.InputAudioSync ?? "-"} nativeFramerate={state.ReadInputAtNativeFramerate} iso={state.IsoType?.ToString() ?? "-"} timestamp={state.InputTimestamp}",
            $"video={Show(state.VideoStream?.Index)} audio={Show(state.AudioStream?.Index)} subtitle={Show(state.SubtitleStream?.Index)} subtitleMethod={state.SubtitleDeliveryMethod} subtitleOffset={state.InternalSubtitleStreamOffset}",
            $"requestVideoCodec={state.BaseRequest.VideoCodec ?? "-"} requestAudioCodec={state.BaseRequest.AudioCodec ?? "-"} maxWidth={Show(state.BaseRequest.MaxWidth)} maxHeight={Show(state.BaseRequest.MaxHeight)}",
            $"supportedAudio={string.Join(',', state.SupportedAudioCodecs ?? [])} supportedVideo={string.Join(',', state.SupportedVideoCodecs ?? [])}",
            $"headers={string.Join(',', (state.RemoteHttpHeaders ?? []).Select(h => h.Key + ":" + h.Value))}"
        };
        RecordedOutput.Verify("AttachMediaSource", sourceName + "." + requestName, string.Join('\n', lines));
    }

    private static string Show(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static (string Name, MediaStream? Stream)[] AudioInputs() =>
    [
        ("aac-stereo-256k", Audio(1, "aac", 2, 256_000)),
        ("eac3-5.1-768k", Audio(1, "eac3", 6, 768_000)),
        ("truehd-7.1", Audio(1, "truehd", 8, 4_000_000)),
        ("flac-stereo-24bit", Audio(1, "flac", 2, 2_000_000)),
        ("mp3-mono-64k", Audio(1, "mp3", 1, 64_000)),
        ("no-stream", null),
    ];

    private static Dictionary<string, Func<MediaSourceInfo>> Sources() => new(StringComparer.Ordinal)
    {
        ["file-mkv"] = () => Source("mkv", MediaProtocol.File, "/media/movie.mkv", Movie()),
        ["encoder-path"] = () =>
        {
            var source = Source("mkv", MediaProtocol.Http, "http://example.com/movie", Movie());
            source.EncoderPath = "/media/cached/movie.mkv";
            source.EncoderProtocol = MediaProtocol.File;
            return source;
        },
        ["wtv"] = () => Source("wtv", MediaProtocol.File, "/media/recording.wtv", Movie()),
        ["wma"] = () => Source("wma", MediaProtocol.File, "/media/song.wma", [Audio(0, "wmav2", 2, 192_000)!]),
        ["live-native"] = () =>
        {
            var source = Source("ts", MediaProtocol.Http, "http://tuner/stream.ts", Movie());
            source.ReadAtNativeFramerate = true;
            source.Timestamp = TransportStreamTimestamp.Valid;
            source.RequiredHttpHeaders = new Dictionary<string, string>(StringComparer.Ordinal) { ["User-Agent"] = "tuner" };
            return source;
        },
        ["iso-dvd"] = () =>
        {
            var source = Source("iso", MediaProtocol.File, "/media/disc.iso", Movie());
            source.IsoType = IsoType.Dvd;
            return source;
        },
        ["many-streams"] = () => Source(
            "mkv",
            MediaProtocol.File,
            "/media/multi.mkv",
            [
                Video(0, 3840, 2160),
                Audio(1, "truehd", 8, 4_000_000)!,
                Audio(2, "dts", 6, 1_536_000)!,
                Audio(3, "aac", 2, 256_000)!,
                Subtitle(4, "PGSSUB", isExternal: false),
                Subtitle(5, "ass", isExternal: false),
                Subtitle(6, "subrip", isExternal: true),
                Video(7, 1920, 1080)
            ]),
    };

    private static Dictionary<string, (Func<BaseEncodingJobOptions> Request, string? Url, string[] Audio, string[] Video, bool IsVideo)> Requests()
        => new(StringComparer.Ordinal)
        {
            ["defaults"] = (() => new VideoRequestDto(), null, [], [], true),
            ["inferred-from-url"] = (() => new VideoRequestDto { Container = "mkv" }, "/videos/x/stream.webm", ["dts", "aac", "truehd", "opus"], [], true),
            ["indices-and-burn-in"] = (() => new VideoRequestDto
            {
                VideoCodec = "h264",
                AudioStreamIndex = 2,
                SubtitleStreamIndex = 5,
                SubtitleMethod = SubtitleDeliveryMethod.Encode,
                MaxWidth = 4096,
                MaxHeight = 4096
            }, null, ["aac", "ac3"], ["h264"], true),
            ["external-subtitle-embed"] = (() => new VideoRequestDto
            {
                VideoCodec = "hevc",
                SubtitleStreamIndex = 6,
                SubtitleMethod = SubtitleDeliveryMethod.Embed
            }, null, ["truehd", "dts", "eac3", "aac"], ["hevc", "h264", "av1"], true),
            ["hevc-disallowed"] = (() => new VideoRequestDto { VideoCodec = "hevc,h264" }, null, ["aac"], ["hevc", "av1", "h264"], true),
            ["audio-only"] = (() => new StreamingRequestDto { AudioCodec = "mp3" }, null, ["flac", "mp3", "aac"], [], false),
        };

    private static MediaSourceInfo Source(string container, MediaProtocol protocol, string path, List<MediaStream> streams)
        => new()
        {
            Id = "source",
            Container = container,
            Protocol = protocol,
            Path = path,
            RunTimeTicks = TimeSpan.FromMinutes(100).Ticks,
            MediaStreams = streams
        };

    private static List<MediaStream> Movie() =>
    [
        Video(0, 1920, 1080),
        Audio(1, "eac3", 6, 768_000)!,
        Audio(2, "aac", 2, 256_000)!,
        Subtitle(3, "subrip", isExternal: false)
    ];

    private static MediaStream Video(int index, int width, int height)
        => new() { Index = index, Type = MediaStreamType.Video, Codec = "h264", Width = width, Height = height, IsDefault = index == 0 };

    private static MediaStream? Audio(int index, string codec, int channels, int bitrate)
        => new() { Index = index, Type = MediaStreamType.Audio, Codec = codec, Channels = channels, BitRate = bitrate, SampleRate = 48_000, IsDefault = index == 1 };

    private static MediaStream Subtitle(int index, string codec, bool isExternal)
        => new() { Index = index, Type = MediaStreamType.Subtitle, Codec = codec, IsExternal = isExternal, Path = isExternal ? "/media/multi.srt" : null };

    private static EncodingHelper CreateHelper()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(x => x.SupportsEncoder(It.IsAny<string>())).Returns(true);
        encoder.Setup(x => x.SupportsDecoder(It.IsAny<string>())).Returns(true);
        encoder.Setup(x => x.CanEncodeToAudioCodec(It.IsAny<string>())).Returns<string>(c => c is not ("dts" or "truehd"));
        encoder.SetupGet(x => x.EncoderVersion).Returns(new Version(7, 1));

        return new EncodingHelper(
            Mock.Of<IApplicationPaths>(),
            encoder.Object,
            Mock.Of<ISubtitleEncoder>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<IConfigurationManager>(),
            Mock.Of<IPathManager>());
    }
}
