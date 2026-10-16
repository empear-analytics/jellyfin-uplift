using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
/// Acceptance tests for the ffmpeg command line that <see cref="EncodingHelper"/> builds for a
/// progressive transcode, recorded per operating system.
/// </summary>
/// <remarks>
/// These tests exist to protect the refactoring campaign (docs/refactoring/). They only call
/// public, plugin-facing members and only mock external boundaries (ffmpeg's capabilities,
/// paths, configuration), so restructuring EncodingHelper's internals must leave them green.
/// The expected command lines are recorded from the current code: a difference means behaviour
/// changed, not that the old output was right. Several branches depend on the operating system,
/// so each OS has its own recordings under Recorded/EncodingCommandLine/&lt;os&gt;/; a case with no
/// recording for the current OS is skipped. Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public class EncodingCommandLineAcceptanceTests
{
    private static readonly HardwareAccelerationType[] _hardware = Enum.GetValues<HardwareAccelerationType>();

    private static readonly string[] _outputCodecs = ["h264", "hevc", "av1"];

    // Option profiles change EncodingOptions; device profiles change what the VAAPI device reports.
    private static readonly string[] _optionProfiles = ["nodecode", "native", "no10bit", "lowpower", "swencode", "slow-crf"];

    private static readonly string[] _deviceProfiles = ["i965", "amd", "novulkan", "limited"];

    private static readonly Scenario[] _profileScenarios = [Scenarios.H264Sdr, Scenarios.Hdr10, Scenarios.Dovi, Scenarios.Interlaced, Scenarios.PgsBurnIn];

    // EncodingHelper names the VAAPI render node in the command line only if the file exists, so
    // the real /dev/dri/renderD128 would make the output depend on the machine's GPU. A fixed
    // file the tests create themselves keeps it the same everywhere.
    private static readonly string _renderNode = CreateRenderNode();

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
    public void ProgressiveVideoCommandLine_MatchesRecording(string caseId)
    {
        var testCase = AllCases()[caseId];
        var helper = CreateHelper(testCase.Profile);
        var state = testCase.BuildState();

        var actual = helper.GetProgressiveVideoFullCommandLine(state, testCase.Options, EncoderPreset.veryfast);

        RecordedOutput.Verify("EncodingCommandLine", caseId, actual);
    }

    private static Dictionary<string, EncodingCase> AllCases()
    {
        var cases = new Dictionary<string, EncodingCase>(StringComparer.Ordinal);
        void Add(EncodingCase c) => cases.Add(c.Id, c);

        foreach (var hw in _hardware)
        {
            foreach (var scenario in Scenarios.All)
            {
                foreach (var codec in _outputCodecs)
                {
                    Add(new EncodingCase(hw, scenario, codec, MaxWidth: null, Container: "mkv"));
                }

                Add(new EncodingCase(hw, scenario, "copy", MaxWidth: null, Container: "mkv"));
            }

            // Downscaling and a streaming mp4 container exercise scale filters and output flags.
            Add(new EncodingCase(hw, Scenarios.H264Sdr, "h264", MaxWidth: 1280, Container: "mp4"));
            Add(new EncodingCase(hw, Scenarios.Hdr10, "h264", MaxWidth: 1280, Container: "mp4"));
            Add(new EncodingCase(hw, Scenarios.Hdr10, "hevc", MaxWidth: 1920, Container: "ts"));
            Add(new EncodingCase(hw, Scenarios.Hdr10, "h264", MaxWidth: null, Container: "mkv", Tonemapping: false));
            Add(new EncodingCase(hw, Scenarios.Interlaced, "h264", MaxWidth: null, Container: "mkv", DoubleRateDeinterlace: true));

            // Option profiles: how the hardware decoding, encoding and quality settings change the line.
            foreach (var profile in _optionProfiles)
            {
                foreach (var scenario in _profileScenarios)
                {
                    Add(new EncodingCase(hw, scenario, "h264", MaxWidth: null, Container: "mkv", Profile: profile));
                    Add(new EncodingCase(hw, scenario, "hevc", MaxWidth: null, Container: "mkv", Profile: profile));
                }
            }
        }

        // Device profiles only matter where a VAAPI device is involved.
        foreach (var hw in new[] { HardwareAccelerationType.vaapi, HardwareAccelerationType.qsv })
        {
            foreach (var profile in _deviceProfiles)
            {
                foreach (var scenario in _profileScenarios)
                {
                    Add(new EncodingCase(hw, scenario, "h264", MaxWidth: null, Container: "mkv", Profile: profile));
                    Add(new EncodingCase(hw, scenario, "hevc", MaxWidth: 1280, Container: "mkv", Profile: profile));
                }
            }
        }

        return cases;
    }

    private static string CreateRenderNode()
    {
        const string Path = "/tmp/jellyfin-acceptance-renderD128";
        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path, string.Empty);
        }

        return Path;
    }

    private static EncodingHelper CreateHelper(string profile)
    {
        // A fully capable ffmpeg: every encoder, decoder, filter and hardware path is available.
        // Device profiles change which kind of VAAPI device it reports.
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(x => x.SupportsEncoder(It.IsAny<string>())).Returns(true);
        encoder.Setup(x => x.SupportsDecoder(It.IsAny<string>())).Returns(true);
        encoder.Setup(x => x.SupportsHwaccel(It.IsAny<string>())).Returns(true);
        encoder.Setup(x => x.SupportsFilter(It.IsAny<string>())).Returns(true);
        encoder.Setup(x => x.SupportsFilterWithOption(It.IsAny<FilterOptionType>())).Returns(true);
        encoder.Setup(x => x.SupportsBitStreamFilterWithOption(It.IsAny<BitStreamFilterOptionType>())).Returns(true);
        encoder.SetupGet(x => x.EncoderVersion).Returns(new Version(7, 1));
        encoder.SetupGet(x => x.IsVaapiDeviceInteliHD).Returns(profile is "" or "novulkan");
        encoder.SetupGet(x => x.IsVaapiDeviceInteli965).Returns(profile == "i965");
        encoder.SetupGet(x => x.IsVaapiDeviceAmd).Returns(profile == "amd");
        encoder.SetupGet(x => x.IsVaapiDeviceSupportVulkanDrmInterop).Returns(profile is not ("novulkan" or "limited"));
        encoder.Setup(x => x.EscapeSubtitleFilterPath(It.IsAny<string>())).Returns<string>(p => p);
        encoder.Setup(x => x.GetInputPathArgument(It.IsAny<EncodingJobInfo>())).Returns<EncodingJobInfo>(s => "file:\"" + s.MediaPath + "\"");
        encoder.Setup(x => x.GetInputPathArgument(It.IsAny<string>(), It.IsAny<MediaSourceInfo>())).Returns<string, MediaSourceInfo>((p, _) => "file:\"" + p + "\"");
        encoder.Setup(x => x.GetInputArgument(It.IsAny<string>(), It.IsAny<MediaSourceInfo>())).Returns<string, MediaSourceInfo>((p, _) => "file:\"" + p + "\"");
        encoder.Setup(x => x.GetExternalSubtitleInputArgument(It.IsAny<string>())).Returns<string>(p => "file:\"" + p + "\"");

        var subtitles = new Mock<ISubtitleEncoder>();
        subtitles.Setup(x => x.GetSubtitleFilePath(It.IsAny<MediaStream>(), It.IsAny<MediaSourceInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult("/cache/subtitles/extracted.ass"));
        subtitles.Setup(x => x.GetSubtitleFileCharacterSet(It.IsAny<MediaStream>(), It.IsAny<string>(), It.IsAny<MediaSourceInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult("UTF-8"));

        var pathManager = new Mock<IPathManager>();
        pathManager.Setup(x => x.GetAttachmentFolderPath(It.IsAny<string>())).Returns("/cache/attachments/source");

        return new EncodingHelper(
            Mock.Of<IApplicationPaths>(),
            encoder.Object,
            subtitles.Object,
            Mock.Of<IConfiguration>(),
            Mock.Of<IConfigurationManager>(),
            pathManager.Object);
    }

    private sealed record EncodingCase(
        HardwareAccelerationType Hardware,
        Scenario Scenario,
        string OutputCodec,
        int? MaxWidth,
        string Container,
        bool Tonemapping = true,
        bool DoubleRateDeinterlace = false,
        string Profile = "")
    {
        public string Id => string.Join(
            '.',
            new[]
            {
                Hardware.ToString(),
                Scenario.Name,
                OutputCodec,
                Container,
                MaxWidth is null ? null : "w" + MaxWidth,
                Tonemapping ? null : "notonemap",
                DoubleRateDeinterlace ? "doublerate" : null,
                Profile.Length == 0 ? null : Profile
            }.Where(p => p is not null));

        public EncodingOptions Options => ApplyProfile(new()
        {
            HardwareAccelerationType = Hardware,
            EnableHardwareEncoding = true,
            AllowHevcEncoding = true,
            AllowAv1Encoding = true,
            HardwareDecodingCodecs = ["h264", "hevc", "mpeg2video", "vc1", "vp8", "vp9", "av1"],
            EnableDecodingColorDepth10Hevc = true,
            EnableDecodingColorDepth10Vp9 = true,
            EnableTonemapping = Tonemapping,
            EnableVppTonemapping = Tonemapping,
            EnableVideoToolboxTonemapping = Tonemapping,
            DeinterlaceDoubleRate = DoubleRateDeinterlace,
            EncodingThreadCount = -1,
            VaapiDevice = _renderNode,
            QsvDevice = string.Empty
        });

        private EncodingOptions ApplyProfile(EncodingOptions options)
        {
            switch (Profile)
            {
                case "nodecode":
                    options.HardwareDecodingCodecs = [];
                    break;
                case "native":
                    options.PreferSystemNativeHwDecoder = true;
                    options.EnableEnhancedNvdecDecoder = true;
                    break;
                case "no10bit":
                    options.EnableDecodingColorDepth10Hevc = false;
                    options.EnableDecodingColorDepth10Vp9 = false;
                    options.EnableDecodingColorDepth10HevcRext = true;
                    options.EnableDecodingColorDepth12HevcRext = true;
                    break;
                case "lowpower":
                    options.EnableIntelLowPowerH264HwEncoder = true;
                    options.EnableIntelLowPowerHevcHwEncoder = true;
                    break;
                case "swencode":
                    options.EnableHardwareEncoding = false;
                    break;
                case "slow-crf":
                    options.EncoderPreset = EncoderPreset.slow;
                    options.H264Crf = 18;
                    options.H265Crf = 22;
                    options.TonemappingAlgorithm = TonemappingAlgorithm.hable;
                    options.TonemappingMode = TonemappingMode.max;
                    options.TonemappingRange = TonemappingRange.tv;
                    break;
            }

            return options;
        }

        public EncodingJobInfo BuildState()
        {
            var streams = Scenario.Streams();
            var video = streams.First(s => s.Type == MediaStreamType.Video);
            var audio = streams.First(s => s.Type == MediaStreamType.Audio);
            var subtitle = streams.FirstOrDefault(s => s.Type == MediaStreamType.Subtitle);

            var source = new MediaSourceInfo
            {
                Id = "0123456789abcdef0123456789abcdef",
                Path = "/media/" + Scenario.Name + "." + Scenario.Container,
                Protocol = MediaProtocol.File,
                Container = Scenario.Container,
                RunTimeTicks = TimeSpan.FromMinutes(90).Ticks,
                MediaStreams = streams
            };

            var request = new VideoRequestDto
            {
                Context = EncodingContext.Streaming,
                VideoCodec = OutputCodec,
                AudioCodec = "aac",
                MaxWidth = MaxWidth,
                VideoBitRate = 8_000_000,
                AudioBitRate = 192_000,
                MaxAudioChannels = 2,
                SubtitleMethod = subtitle is null ? SubtitleDeliveryMethod.Drop : SubtitleDeliveryMethod.Encode
            };

            return new EncodingJobInfo(TranscodingJobType.Progressive)
            {
                BaseRequest = request,
                MediaSource = source,
                MediaPath = source.Path,
                InputProtocol = MediaProtocol.File,
                InputContainer = Scenario.Container,
                RunTimeTicks = source.RunTimeTicks,
                IsInputVideo = true,
                IsVideoRequest = true,
                VideoStream = video,
                AudioStream = audio,
                SubtitleStream = subtitle,
                SubtitleDeliveryMethod = request.SubtitleMethod,
                OutputVideoCodec = OutputCodec,
                OutputAudioCodec = "aac",
                OutputAudioChannels = 2,
                OutputAudioBitrate = 192_000,
                OutputVideoBitrate = 8_000_000,
                OutputContainer = Container,
                OutputFilePath = "/transcodes/output." + Container,
                SupportedAudioCodecs = ["aac"],
                SupportedVideoCodecs = [OutputCodec],
                SupportedSubtitleCodecs = []
            };
        }
    }

    private sealed record Scenario(string Name, string Container, Func<List<MediaStream>> Streams);

    private static class Scenarios
    {
        public static readonly Scenario H264Sdr = new("h264-sdr", "mkv", () =>
        [
            Video(0, "h264", "High", 1920, 1080, 8, "yuv420p", "bt709", "bt709", "bt709"),
            Audio(1, "aac", 2, "stereo")
        ]);

        public static readonly Scenario Hdr10 = new("hevc-hdr10", "mkv", () =>
        [
            Video(0, "hevc", "Main 10", 3840, 2160, 10, "yuv420p10le", "smpte2084", "bt2020", "bt2020nc"),
            Audio(1, "eac3", 6, "5.1")
        ]);

        public static readonly Scenario Dovi = new("hevc-dovi", "mp4", () =>
        {
            var video = Video(0, "hevc", "Main 10", 3840, 2160, 10, "yuv420p10le", "smpte2084", "bt2020", "bt2020nc");
            video.DvProfile = 8;
            video.DvLevel = 6;
            video.RpuPresentFlag = 1;
            video.BlPresentFlag = 1;
            video.DvBlSignalCompatibilityId = 1;
            return [video, Audio(1, "truehd", 8, "7.1")];
        });

        public static readonly Scenario Hlg = new("vp9-hlg", "webm", () =>
        [
            Video(0, "vp9", "Profile 2", 3840, 2160, 10, "yuv420p10le", "arib-std-b67", "bt2020", "bt2020nc"),
            Audio(1, "opus", 2, "stereo")
        ]);

        public static readonly Scenario Av1 = new("av1-sdr", "mkv", () =>
        [
            Video(0, "av1", "Main", 1920, 1080, 10, "yuv420p10le", "bt709", "bt709", "bt709"),
            Audio(1, "opus", 2, "stereo")
        ]);

        public static readonly Scenario Interlaced = new("mpeg2-interlaced", "ts", () =>
        {
            var video = Video(0, "mpeg2video", "Main", 720, 576, 8, "yuv420p", "bt470bg", "bt470bg", "bt470bg");
            video.IsInterlaced = true;
            video.RealFrameRate = 25;
            video.AverageFrameRate = 25;
            return [video, Audio(1, "ac3", 6, "5.1")];
        });

        public static readonly Scenario PgsBurnIn = new("h264-pgs", "mkv", () =>
        [
            Video(0, "h264", "High", 1920, 1080, 8, "yuv420p", "bt709", "bt709", "bt709"),
            Audio(1, "dts", 6, "5.1"),
            new MediaStream { Index = 2, Type = MediaStreamType.Subtitle, Codec = "PGSSUB", Language = "eng" }
        ]);

        public static readonly Scenario SrtBurnIn = new("h264-srt", "mkv", () =>
        [
            Video(0, "h264", "High", 1920, 1080, 8, "yuv420p", "bt709", "bt709", "bt709"),
            Audio(1, "aac", 2, "stereo"),
            new MediaStream
            {
                Index = 2,
                Type = MediaStreamType.Subtitle,
                Codec = "srt",
                Language = "swe",
                IsExternal = true,
                SupportsExternalStream = true,
                Path = "/media/h264-srt.sv.srt"
            }
        ]);

        public static readonly Scenario[] All = [H264Sdr, Hdr10, Dovi, Hlg, Av1, Interlaced, PgsBurnIn, SrtBurnIn];

        private static MediaStream Video(int index, string codec, string profile, int width, int height, int bitDepth, string pixelFormat, string transfer, string primaries, string space)
            => new()
            {
                Index = index,
                Type = MediaStreamType.Video,
                Codec = codec,
                Profile = profile,
                Width = width,
                Height = height,
                BitDepth = bitDepth,
                PixelFormat = pixelFormat,
                ColorTransfer = transfer,
                ColorPrimaries = primaries,
                ColorSpace = space,
                RealFrameRate = 23.976f,
                AverageFrameRate = 23.976f,
                BitRate = 20_000_000,
                IsDefault = true
            };

        private static MediaStream Audio(int index, string codec, int channels, string layout)
            => new()
            {
                Index = index,
                Type = MediaStreamType.Audio,
                Codec = codec,
                Channels = channels,
                ChannelLayout = layout,
                SampleRate = 48000,
                BitRate = 640_000,
                Language = "eng",
                IsDefault = true
            };
    }
}
