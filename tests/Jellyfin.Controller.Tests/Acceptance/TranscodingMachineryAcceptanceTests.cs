using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Controller.Tests.Acceptance;

/// <summary>
/// Acceptance tests for the transcoding machinery around the command line: what a job derives from
/// its request and streams, how ffmpeg's progress output is parsed, segment bookkeeping, and the
/// downmix tables.
/// </summary>
/// <remarks>
/// These protect the refactoring campaign (docs/refactoring/). They use public members of the
/// plugin-facing MediaBrowser.Controller package and compare tables with recordings of the current
/// behaviour. Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public class TranscodingMachineryAcceptanceTests
{
    private static readonly string[] _progressLines =
    [
        "ffmpeg version 7.1 Copyright (c) 2000-2024 the FFmpeg developers",
        "Input #0, matroska,webm, from 'movie.mkv':",
        "frame=   48 fps=0.0 q=28.0 size=     256kB time=00:00:02.00 bitrate=1048.6kbits/s speed=3.99x",
        "frame=  480 fps= 96 q=28.0 size=    5120KiB time=00:00:20.02 bitrate=2095.1kbits/s dup=1 drop=2 speed=4.01x",
        "frame= 1440 fps= 95 q=-1.0 size=   20480kB time=00:01:00.05 bitrate=2793.9kbits/s speed=3.8x",
        "size=N/A time=00:02:00.00 bitrate=N/A speed=6.0x",
        "frame= 2880 fps= 94 q=-1.0 Lsize=   41943kB time=00:02:00.10 bitrate=2860.0kbits/s speed=3.9x",
        "frame=3000 fps=90 size=-1kB time=-00:00:00.50 bitrate=-0.0kbits/s speed=N/A",
        "[hls @ 0x55d] Opening 'segment5.ts' for writing",
        "frame=  100 fps= 25 q=28.0 size=1048576 time=00:00:04.00 bitrate=2097.2kbits/s speed=1x",
        "video:1000kB audio:200kB subtitle:0kB other streams:0kB global headers:0kB muxing overhead: 1.5%",
    ];

    public static TheoryData<string, string> JobInfoCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var stream in Streams().Keys)
        {
            foreach (var request in Requests().Keys)
            {
                data.Add(stream, request);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(JobInfoCases))]
    public void EncodingJobInfo_MatchesRecording(string streamName, string requestName)
    {
        var (video, audio) = Streams()[streamName]();
        var (configure, videoCodec, audioCodec, type) = Requests()[requestName];
        var request = new VideoRequestDto { VideoCodec = videoCodec, AudioCodec = audioCodec };
        configure(request);
        var state = new EncodingJobInfo(type)
        {
            BaseRequest = request,
            IsVideoRequest = true,
            VideoStream = video,
            AudioStream = audio,
            OutputVideoCodec = videoCodec,
            OutputAudioCodec = audioCodec,
            MediaSource = new MediaSourceInfo { Container = "mkv", MediaStreams = [video, audio] },
            RunTimeTicks = TimeSpan.FromMinutes(90).Ticks,
            OutputContainer = type == TranscodingJobType.Hls ? "ts" : "mkv"
        };
        state.AddTranscodeReason(TranscodeReason.VideoCodecNotSupported);
        state.AddTranscodeReason(TranscodeReason.AudioChannelsNotSupported);

        var lines = new[]
        {
            Invariant($"output {state.OutputWidth}x{state.OutputHeight} sampleRate={state.OutputAudioSampleRate} audioBitDepth={state.OutputAudioBitDepth}"),
            Invariant($"target level={state.TargetVideoLevel} bitDepth={state.TargetVideoBitDepth} refs={state.TargetRefFrames} fps={state.TargetFramerate} profile={state.TargetVideoProfile} range={state.TargetVideoRangeType} tag={state.TargetVideoCodecTag}"),
            Invariant($"target anamorphic={state.IsTargetAnamorphic} interlaced={state.IsTargetInterlaced} avc={state.IsTargetAVC} timestamp={state.TargetTimestamp} packet={state.TargetPacketLength} videoStreams={state.TargetVideoStreamCount} audioStreams={state.TargetAudioStreamCount}"),
            Invariant($"actual video={state.ActualOutputVideoCodec} audio={state.ActualOutputAudioCodec} segmentedLive={state.IsSegmentedLiveStream} reasons={state.TranscodeReasons}"),
            Invariant($"deinterlace h264={state.DeInterlace("h264", false)} forced={state.DeInterlace("h264", true)} mpeg2={state.DeInterlace("mpeg2video", false)}"),
            Invariant($"requested level={state.GetRequestedLevel("h264")} refs={state.GetRequestedMaxRefFrames("h264")} bitDepth={state.GetRequestedVideoBitDepth("hevc")} audioBitDepth={state.GetRequestedAudioBitDepth("flac")} channels={state.GetRequestedAudioChannels("aac")}")
        };
        RecordedOutput.Verify("EncodingJobInfo", streamName + "." + requestName, string.Join('\n', lines));
    }

    [Fact]
    public void MimeTypes_MatchRecording()
    {
        var output = new StringBuilder();
        foreach (var isVideo in new[] { true, false })
        {
            foreach (var path in new[] { "out.mp4", "out.mkv", "out.ts", "out.m3u8", "out.webm", "out.mp3", "out.flac", "out.aac", "out.ogg", "out.opus", "out.wav", "out.m4a", "out.unknown", "out" })
            {
                foreach (var enableDefault in new[] { true, false })
                {
                    var state = new EncodingJobInfo(TranscodingJobType.Progressive) { IsVideoRequest = isVideo, BaseRequest = new VideoRequestDto() };
                    output.Append(CultureInfo.InvariantCulture, $"video={isVideo} {path} default={enableDefault} -> {state.GetMimeType(path, enableDefault) ?? "null"}\n");
                }
            }
        }

        RecordedOutput.Verify("EncodingJobInfo", "mime-types", output.ToString().TrimEnd());
    }

    [Fact]
    public void DownMix_MatchesRecording()
    {
        var output = new StringBuilder();
        foreach (var ((algorithm, layout), filter) in DownMixAlgorithmsHelper.AlgorithmFilterStrings.OrderBy(e => e.Key.Item1).ThenBy(e => e.Key.Item2, StringComparer.Ordinal))
        {
            output.Append(CultureInfo.InvariantCulture, $"filter {algorithm} {layout} -> {filter}\n");
        }

        foreach (var channels in new int?[] { null, 1, 2, 3, 4, 5, 6, 7, 8, 10 })
        {
            foreach (var layout in new[] { null, "stereo", "5.1(side)", "7.1", "quad", "unknown" })
            {
                var stream = new MediaStream { Type = MediaStreamType.Audio, Codec = "ac3", Channels = channels, ChannelLayout = layout! };
                output.Append(CultureInfo.InvariantCulture, $"infer channels={Show(channels)} layout={layout ?? "null"} -> {DownMixAlgorithmsHelper.InferChannelLayout(stream) ?? "null"}\n");
            }
        }

        RecordedOutput.Verify("TranscodingMachinery", "downmix", output.ToString().TrimEnd());
    }

    [Fact]
    public void TranscodingJobBookkeeping_MatchesRecording()
    {
        var job = new TranscodingJob(NullLogger<TranscodingJob>.Instance)
        {
            Type = TranscodingJobType.Hls,
            PlaySessionId = "session",
            Path = "/transcodes/session.m3u8"
        };
        var output = new StringBuilder();
        output.Append(Invariant($"start requests={job.ActiveRequestCount} highest={Show(job.GetHighestServedSegmentIndexEndingAtOrBefore(0))}\n"));

        var segment = TimeSpan.FromSeconds(6).Ticks;
        foreach (var index in new[] { 0, 1, 2, 4, 3, 7, 2 })
        {
            job.ReportSegmentDownloaded(index, (index + 1) * segment);
            output.Append(Invariant($"downloaded {index} -> download={job.DownloadPositionTicks}"));
            foreach (var at in new long[] { -1, 0, segment - 1, segment, 3 * segment, 5 * segment, 8 * segment, long.MaxValue })
            {
                output.Append(Invariant($" {at}:{Show(job.GetHighestServedSegmentIndexEndingAtOrBefore(at))}"));
            }

            output.Append('\n');
        }

        output.Append(Invariant($"requests +{job.IncrementActiveRequestCount()} +{job.IncrementActiveRequestCount()} -{job.DecrementActiveRequestCount()} -{job.DecrementActiveRequestCount()} -{job.DecrementActiveRequestCount()}\n"));
        job.StartKillTimer(_ => { });
        job.StopKillTimer();
        job.DisposeKillTimer();
        output.Append(Invariant($"after timers requests={job.ActiveRequestCount} exited={job.HasExited}"));

        RecordedOutput.Verify("TranscodingMachinery", "job-bookkeeping", output.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JobLogger_MatchesRecording(bool withRuntime)
    {
        var state = new ProgressRecordingJobInfo { RunTimeTicks = withRuntime ? TimeSpan.FromMinutes(4).Ticks : null };
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', _progressLines) + "\n"));
        var target = new MemoryStream();

        await new JobLogger(NullLogger.Instance).StartStreamingLog(state, new StreamReader(source), target);

        var output = string.Join('\n', state.Reports) + "\nlog: " + Encoding.UTF8.GetString(target.ToArray()).ReplaceLineEndings("|");
        RecordedOutput.Verify("TranscodingMachinery", "job-logger-" + (withRuntime ? "runtime" : "no-runtime"), output);
    }

    private static string Show(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private static Dictionary<string, Func<(MediaStream Video, MediaStream Audio)>> Streams() => new(StringComparer.Ordinal)
    {
        ["h264-1080p"] = () => (Video("h264", "High", 41, 1920, 1080, 8, "bt709", 23.976f), Audio("aac", 2, 48_000, null)),
        ["hevc-hdr10-4k"] = () => (Video("hevc", "Main 10", 153, 3840, 2160, 10, "smpte2084", 23.976f), Audio("eac3", 6, 48_000, null)),
        ["mpeg2-interlaced"] = () =>
        {
            var video = Video("mpeg2video", "Main", 8, 720, 576, 8, "bt709", 25f);
            video.IsInterlaced = true;
            return (video, Audio("ac3", 6, 48_000, null));
        },
        ["h264-anamorphic-60fps"] = () =>
        {
            var video = Video("h264", "Main", 42, 1440, 1080, 8, "bt709", 59.94f);
            video.IsAnamorphic = true;
            video.IsAVC = false;
            video.RefFrames = 16;
            return (video, Audio("flac", 2, 96_000, 24));
        },
        ["hevc-dovi"] = () =>
        {
            var video = Video("hevc", "Main 10", 150, 3840, 2160, 10, "smpte2084", 24f);
            video.DvProfile = 8;
            video.DvBlSignalCompatibilityId = 1;
            video.RpuPresentFlag = 1;
            video.BlPresentFlag = 1;
            video.CodecTag = "hvc1";
            return (video, Audio("truehd", 8, 48_000, 24));
        },
    };

    private static Dictionary<string, (Action<VideoRequestDto> Configure, string Video, string Audio, TranscodingJobType Type)> Requests() => new(StringComparer.Ordinal)
    {
        ["copy-both"] = (_ => { }, "copy", "copy", TranscodingJobType.Progressive),
        ["h264-defaults"] = (_ => { }, "h264", "aac", TranscodingJobType.Progressive),
        ["hls-limits"] = (r =>
        {
            r.MaxWidth = 1280;
            r.MaxHeight = 720;
            r.MaxFramerate = 30;
            r.VideoBitRate = 4_000_000;
            r.AudioSampleRate = 44_100;
            r.MaxAudioBitDepth = 16;
            r.MaxAudioChannels = 2;
        }, "h264", "aac", TranscodingJobType.Hls),
        ["fixed-size-options"] = (r =>
        {
            r.Width = 1024;
            r.Height = 576;
            r.Framerate = 24;
            r.StreamOptions["h264-level"] = "40";
            r.StreamOptions["h264-maxrefframes"] = "4";
            r.StreamOptions["hevc-videobitdepth"] = "8";
            r.StreamOptions["flac-audiobitdepth"] = "16";
            r.StreamOptions["aac-audiochannels"] = "6";
            r.StreamOptions["h264-deinterlace"] = "true";
            r.StreamOptions["h264-profile"] = "main";
        }, "h264", "aac", TranscodingJobType.Progressive),
        ["hevc-copy-audio"] = (r =>
        {
            r.Level = "150";
            r.Profile = "main10";
            r.MaxVideoBitDepth = 10;
            r.CopyTimestamps = true;
        }, "hevc", "copy", TranscodingJobType.Hls),
    };

    private static MediaStream Video(string codec, string profile, double level, int width, int height, int bitDepth, string transfer, float fps)
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
            ColorTransfer = transfer,
            ColorPrimaries = transfer == "bt709" ? "bt709" : "bt2020",
            ColorSpace = transfer == "bt709" ? "bt709" : "bt2020nc",
            RealFrameRate = fps,
            AverageFrameRate = fps,
            RefFrames = 4,
            IsAVC = codec == "h264" ? true : null
        };

    private static MediaStream Audio(string codec, int channels, int sampleRate, int? bitDepth)
        => new() { Index = 1, Type = MediaStreamType.Audio, Codec = codec, Channels = channels, SampleRate = sampleRate, BitDepth = bitDepth };

    private sealed class ProgressRecordingJobInfo : EncodingJobInfo
    {
        public ProgressRecordingJobInfo()
            : base(TranscodingJobType.Progressive)
        {
            BaseRequest = new VideoRequestDto();
        }

        public List<string> Reports { get; } = [];

        public override void ReportTranscodingProgress(TimeSpan? transcodingPosition, float? framerate, double? percentComplete, long? bytesTranscoded, int? bitRate)
            => Reports.Add(FormattableString.Invariant($"progress position={transcodingPosition?.ToString() ?? "-"} fps={framerate?.ToString(CultureInfo.InvariantCulture) ?? "-"} percent={percentComplete?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-"} bytes={bytesTranscoded?.ToString(CultureInfo.InvariantCulture) ?? "-"} bitrate={bitRate?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));
    }
}
