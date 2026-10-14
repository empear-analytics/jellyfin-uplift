using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using MediaBrowser.Model.Session;
using Moq;
using Xunit;

using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Jellyfin.Controller.Tests.Acceptance;

/// <summary>
/// Acceptance tests for EncodingHelper's transcode decisions: whether a video or audio stream
/// may be copied unchanged for a client's constraints, and the audio-only ffmpeg command line.
/// </summary>
/// <remarks>
/// Like <see cref="EncodingCommandLineAcceptanceTests"/>, these only use public members, mock only
/// external boundaries and compare with outputs recorded from the current code, per OS.
/// </remarks>
public class TranscodeDecisionAcceptanceTests
{
    public static TheoryData<string> VideoCopyCases() => Ids(VideoCopy());

    public static TheoryData<string> AudioCopyCases() => Ids(AudioCopy());

    public static TheoryData<string> AudioCommandCases() => Ids(AudioCommands());

    [Theory]
    [MemberData(nameof(VideoCopyCases))]
    public void CanStreamCopyVideo_MatchesRecording(string caseId)
    {
        var (stream, constraints) = VideoCopy()[caseId];
        var state = VideoState(stream, constraints);

        var canCopy = CreateHelper().CanStreamCopyVideo(state, stream);

        RecordedOutput.Verify("StreamCopyVideo", caseId, "copy=" + canCopy.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(AudioCopyCases))]
    public void CanStreamCopyAudio_MatchesRecording(string caseId)
    {
        var (stream, constraints) = AudioCopy()[caseId];
        var state = AudioState(stream, constraints.Request());

        var canCopy = CreateHelper().CanStreamCopyAudio(state, stream, constraints.Codecs, out var reasons);

        RecordedOutput.Verify("StreamCopyAudio", caseId, "copy=" + canCopy.ToString(CultureInfo.InvariantCulture) + " reasons=" + reasons);
    }

    [Theory]
    [MemberData(nameof(AudioCommandCases))]
    public void ProgressiveAudioCommandLine_MatchesRecording(string caseId)
    {
        var c = AudioCommands()[caseId];
        var request = new StreamingRequestDto
        {
            AudioCodec = c.OutputCodec,
            AudioBitRate = c.Bitrate,
            MaxAudioChannels = c.Channels,
            AudioSampleRate = c.SampleRate,
            EnableAudioVbrEncoding = c.Vbr
        };
        var state = AudioState(c.Input, request);
        state.OutputAudioCodec = c.OutputCodec;
        state.OutputAudioBitrate = c.Bitrate;
        state.OutputAudioChannels = c.Channels;
        state.OutputContainer = c.Container;
        state.OutputFilePath = "/transcodes/output." + c.Container;
        var options = new EncodingOptions
        {
            EnableAudioVbr = c.Vbr,
            DownMixStereoAlgorithm = c.DownMix,
            DownMixAudioBoost = 2,
            EncodingThreadCount = -1
        };

        var actual = CreateHelper().GetProgressiveAudioFullCommandLine(state, options, state.OutputFilePath);

        RecordedOutput.Verify("AudioCommandLine", caseId, actual);
    }

    private static TheoryData<string> Ids<T>(Dictionary<string, T> cases)
    {
        var data = new TheoryData<string>();
        foreach (var id in cases.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    private static Dictionary<string, (MediaStream Stream, VideoConstraints Constraints)> VideoCopy()
    {
        var cases = new Dictionary<string, (MediaStream, VideoConstraints)>(StringComparer.Ordinal);
        foreach (var (name, stream) in VideoStreams())
        {
            foreach (var constraints in VideoConstraints.All)
            {
                cases.Add(name + "." + constraints.Name, (stream(), constraints));
            }
        }

        return cases;
    }

    private static Dictionary<string, (MediaStream Stream, AudioConstraints Constraints)> AudioCopy()
    {
        var cases = new Dictionary<string, (MediaStream, AudioConstraints)>(StringComparer.Ordinal);
        foreach (var (name, stream) in AudioStreams())
        {
            foreach (var constraints in AudioConstraints.All)
            {
                cases.Add(name + "." + constraints.Name, (stream(), constraints));
            }
        }

        return cases;
    }

    private static Dictionary<string, AudioCommand> AudioCommands()
    {
        var cases = new Dictionary<string, AudioCommand>(StringComparer.Ordinal);
        void Add(AudioCommand c) => cases.Add(c.Id, c);

        var outputs = new (string Codec, string Container)[]
        {
            ("aac", "m4a"), ("mp3", "mp3"), ("opus", "ogg"), ("vorbis", "ogg"),
            ("flac", "flac"), ("alac", "m4a"), ("ac3", "ac3"), ("pcm_s16le", "wav")
        };
        var inputs = AudioStreams().Where(s => s.Name is "aac-stereo" or "eac3-5.1" or "flac-24bit" or "truehd-7.1").ToList();

        foreach (var (codec, container) in outputs)
        {
            foreach (var (name, stream) in inputs)
            {
                Add(new AudioCommand(name, stream(), codec, container, 128_000, 2, null, false, DownMixStereoAlgorithms.None));
                Add(new AudioCommand(name, stream(), codec, container, 320_000, 2, 44_100, true, DownMixStereoAlgorithms.None));
                Add(new AudioCommand(name, stream(), codec, container, null, 6, 22_050, false, DownMixStereoAlgorithms.None));
            }
        }

        // Downmixing 5.1 to stereo with each algorithm, and raw pcm output.
        foreach (var algorithm in Enum.GetValues<DownMixStereoAlgorithms>())
        {
            Add(new AudioCommand("eac3-5.1", AudioStreams().First(s => s.Name == "eac3-5.1").Stream(), "aac", "mp4", 192_000, 2, null, false, algorithm));
        }

        Add(new AudioCommand("flac-24bit", AudioStreams().First(s => s.Name == "flac-24bit").Stream(), "pcm_s16le", "pcm", null, 2, 48_000, false, DownMixStereoAlgorithms.None));
        return cases;
    }

    private static EncodingJobInfo VideoState(MediaStream stream, VideoConstraints constraints)
    {
        var audio = AudioStreams().First().Stream();
        var subtitle = new MediaStream { Index = 2, Type = MediaStreamType.Subtitle, Codec = "PGSSUB" };
        var request = constraints.Request();
        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            BaseRequest = request,
            MediaSource = new MediaSourceInfo { Id = "source", Container = "mkv", MediaStreams = [stream, audio, subtitle] },
            InputContainer = "mkv",
            IsVideoRequest = true,
            IsInputVideo = true,
            VideoStream = stream,
            AudioStream = audio,
            SubtitleStream = request.SubtitleStreamIndex is null ? null : subtitle,
            SubtitleDeliveryMethod = request.SubtitleMethod,
            SupportedVideoCodecs = constraints.Codecs,
            SupportedAudioCodecs = ["aac"]
        };
    }

    private static EncodingJobInfo AudioState(MediaStream stream, StreamingRequestDto request)
        => new(TranscodingJobType.Progressive)
        {
            BaseRequest = request,
            MediaSource = new MediaSourceInfo { Id = "source", Path = "/media/audio", Protocol = MediaProtocol.File, Container = "mka", MediaStreams = [stream] },
            MediaPath = "/media/audio",
            InputProtocol = MediaProtocol.File,
            InputContainer = "mka",
            RunTimeTicks = TimeSpan.FromMinutes(4).Ticks,
            AudioStream = stream,
            SupportedAudioCodecs = request.AudioCodec is null ? [] : [request.AudioCodec]
        };

    private static (string Name, Func<MediaStream> Stream)[] VideoStreams() =>
    [
        ("h264-1080p", () => Video("h264", "High", 41, 1920, 1080, 8, 8_000_000, 4)),
        ("h264-4k-l51", () => Video("h264", "High", 51, 3840, 2160, 8, 40_000_000, 4)),
        ("h264-interlaced", () => Video("h264", "Main", 30, 720, 576, 8, 4_000_000, 2, interlaced: true)),
        ("h264-anamorphic-refs16", () => Video("h264", "High", 41, 1440, 1080, 8, 8_000_000, 16, anamorphicAnnexB: true)),
        ("hevc-hdr10", () => Video("hevc", "Main 10", 150, 3840, 2160, 10, 25_000_000, 1, "smpte2084")),
        ("hevc-dovi", () => Video("hevc", "Main 10", 153, 3840, 2160, 10, 30_000_000, 1, "smpte2084", dovi: true)),
        ("av1-10bit", () => Video("av1", "Main", 8, 1920, 1080, 10, 6_000_000, 1)),
        ("vp9-hlg", () => Video("vp9", "Profile 2", 51, 3840, 2160, 10, 20_000_000, 1, "arib-std-b67")),
        ("mpeg4-sd", () => Video("mpeg4", "Advanced Simple Profile", 5, 640, 480, 8, 1_500_000, 1)),
    ];

    private static (string Name, Func<MediaStream> Stream)[] AudioStreams() =>
    [
        ("aac-stereo", () => Audio("aac", "LC", 2, 48_000, 256_000, null)),
        ("ac3-5.1", () => Audio("ac3", null, 6, 48_000, 640_000, null)),
        ("eac3-5.1", () => Audio("eac3", null, 6, 48_000, 768_000, null)),
        ("truehd-7.1", () => Audio("truehd", null, 8, 48_000, 4_000_000, 24)),
        ("dts-5.1", () => Audio("dts", "DTS-HD MA", 6, 48_000, 3_000_000, 24)),
        ("flac-24bit", () => Audio("flac", null, 2, 96_000, 2_000_000, 24)),
        ("opus-stereo", () => Audio("opus", null, 2, 48_000, 160_000, null)),
        ("mp3-stereo", () => Audio("mp3", null, 2, 44_100, 320_000, null)),
    ];

    private static MediaStream Video(string codec, string profile, double level, int width, int height, int bitDepth, int bitrate, int refFrames, string transfer = "bt709", bool interlaced = false, bool anamorphicAnnexB = false, bool dovi = false)
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
            BitRate = bitrate,
            RefFrames = refFrames,
            ColorTransfer = transfer,
            ColorPrimaries = transfer == "bt709" ? "bt709" : "bt2020",
            ColorSpace = transfer == "bt709" ? "bt709" : "bt2020nc",
            RealFrameRate = 23.976f,
            AverageFrameRate = 23.976f,
            IsAVC = codec == "h264" ? !anamorphicAnnexB : null,
            IsAnamorphic = anamorphicAnnexB,
            IsInterlaced = interlaced,
            DvProfile = dovi ? 8 : null,
            DvBlSignalCompatibilityId = dovi ? 1 : null,
            RpuPresentFlag = dovi ? 1 : null,
            BlPresentFlag = dovi ? 1 : null,
            CodecTag = codec == "hevc" ? "hvc1" : null
        };

    private static MediaStream Audio(string codec, string? profile, int channels, int sampleRate, int bitrate, int? bitDepth)
        => new()
        {
            Index = 1,
            Type = MediaStreamType.Audio,
            Codec = codec,
            Profile = profile,
            Channels = channels,
            SampleRate = sampleRate,
            BitRate = bitrate,
            BitDepth = bitDepth,
            Language = "eng",
            IsDefault = true
        };

    private static EncodingHelper CreateHelper()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(x => x.SupportsEncoder(It.IsAny<string>())).Returns<string>(e => e != "aac_at" && e != "libfdk_aac");
        encoder.Setup(x => x.SupportsDecoder(It.IsAny<string>())).Returns(true);
        encoder.Setup(x => x.SupportsFilter(It.IsAny<string>())).Returns(true);
        encoder.Setup(x => x.CanEncodeToAudioCodec(It.IsAny<string>())).Returns(true);
        encoder.SetupGet(x => x.EncoderVersion).Returns(new Version(7, 1));
        encoder.Setup(x => x.GetInputPathArgument(It.IsAny<EncodingJobInfo>())).Returns<EncodingJobInfo>(s => "file:\"" + s.MediaPath + "\"");
        encoder.Setup(x => x.GetInputPathArgument(It.IsAny<string>(), It.IsAny<MediaSourceInfo>())).Returns<string, MediaSourceInfo>((p, _) => "file:\"" + p + "\"");
        encoder.Setup(x => x.GetInputArgument(It.IsAny<string>(), It.IsAny<MediaSourceInfo>())).Returns<string, MediaSourceInfo>((p, _) => "file:\"" + p + "\"");

        return new EncodingHelper(
            Mock.Of<IApplicationPaths>(),
            encoder.Object,
            Mock.Of<ISubtitleEncoder>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<IConfigurationManager>(),
            Mock.Of<IPathManager>());
    }

    private sealed record AudioCommand(
        string InputName,
        MediaStream Input,
        string OutputCodec,
        string Container,
        int? Bitrate,
        int Channels,
        int? SampleRate,
        bool Vbr,
        DownMixStereoAlgorithms DownMix)
    {
        public string Id => string.Join(
            '.',
            new[]
            {
                InputName,
                OutputCodec,
                Container,
                Bitrate is null ? "nobitrate" : "b" + Bitrate,
                "ch" + Channels,
                SampleRate is null ? null : "sr" + SampleRate,
                Vbr ? "vbr" : null,
                DownMix == DownMixStereoAlgorithms.None ? null : DownMix.ToString()
            }.Where(p => p is not null));
    }

    private sealed record VideoConstraints(string Name, string[] Codecs, Action<VideoRequestDto> Configure)
    {
        public static readonly VideoConstraints[] All =
        [
            new("open", ["h264", "hevc", "av1", "vp9", "mpeg4"], _ => { }),
            new("h264-only", ["h264"], _ => { }),
            new("maxwidth1280", ["h264", "hevc", "av1", "vp9"], r => r.MaxWidth = 1280),
            new("bitrate4m", ["h264", "hevc", "av1", "vp9"], r => r.VideoBitRate = 4_000_000),
            new("h264-high-l40-8bit", ["h264", "hevc"], r =>
            {
                r.StreamOptions["h264-profile"] = "high,main";
                r.StreamOptions["h264-level"] = "40";
                r.StreamOptions["h264-videobitdepth"] = "8";
                r.StreamOptions["hevc-videobitdepth"] = "8";
            }),
            new("sdr-hdr10-only", ["h264", "hevc", "av1", "vp9"], r =>
            {
                r.StreamOptions["hevc-rangetype"] = "SDR|HDR10";
                r.StreamOptions["av1-rangetype"] = "SDR";
                r.StreamOptions["vp9-rangetype"] = "SDR|HDR10";
            }),
            new("avc-nonanamorphic-refs4", ["h264", "hevc"], r =>
            {
                r.RequireAvc = true;
                r.RequireNonAnamorphic = true;
                r.StreamOptions["h264-maxrefframes"] = "4";
            }),
            new("framerate-limits", ["h264", "hevc", "av1", "vp9"], r =>
            {
                r.MaxFramerate = 23;
                r.Framerate = 30;
            }),
            new("deinterlace-burnin", ["h264", "hevc", "av1", "vp9"], r =>
            {
                r.DeInterlace = true;
                r.SubtitleStreamIndex = 2;
                r.SubtitleMethod = SubtitleDeliveryMethod.Encode;
            }),
            new("copy-disallowed", ["h264", "hevc", "av1", "vp9"], r => r.AllowVideoStreamCopy = false),
        ];

        public VideoRequestDto Request()
        {
            var request = new VideoRequestDto
            {
                AllowVideoStreamCopy = true,
                AllowAudioStreamCopy = true,
                VideoCodec = string.Join(',', Codecs),
                Context = EncodingContext.Streaming
            };
            Configure(request);
            return request;
        }
    }

    private sealed record AudioConstraints(string Name, string[] Codecs, Action<StreamingRequestDto> Configure)
    {
        public static readonly AudioConstraints[] All =
        [
            new("aac", ["aac"], _ => { }),
            new("surround", ["aac", "ac3", "eac3"], _ => { }),
            new("lossless", ["flac", "alac", "truehd"], _ => { }),
            new("mp3-opus", ["mp3", "opus"], _ => { }),
            new("stereo-max", ["aac", "ac3", "eac3", "flac", "opus", "mp3"], r => r.MaxAudioChannels = 2),
            new("16bit", ["aac", "flac", "truehd", "dts"], r => r.StreamOptions["flac-audiobitdepth"] = "16"),
            new("bitrate128k", ["aac", "mp3", "opus"], r => r.AudioBitRate = 128_000),
            new("copy-disallowed", ["aac", "ac3", "eac3", "flac", "opus", "mp3", "truehd", "dts"], r => r.AllowAudioStreamCopy = false),
        ];

        public StreamingRequestDto Request()
        {
            var request = new StreamingRequestDto { AllowAudioStreamCopy = true, AudioCodec = string.Join(',', Codecs) };
            Configure(request);
            return request;
        }
    }
}
