using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.MediaEncoding.Transcoding;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// An <see cref="ITranscodeManager"/> that records the ffmpeg command line it is asked to start,
/// per play session, and starts nothing. Everything else is delegated to the real manager.
/// </summary>
/// <remarks>
/// Launching ffmpeg is the boundary the acceptance tests stop at: everything that builds the
/// command (the HLS controller, the streaming helpers, EncodingHelper) runs for real.
/// </remarks>
public sealed class RecordingTranscodeManager : ITranscodeManager
{
    private readonly TranscodeManager _inner;
    private readonly ConcurrentDictionary<TranscodingJob, byte> _fakeJobs = new();
    private int _liveEndRequests;

    public RecordingTranscodeManager(TranscodeManager inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// Gets the command lines requested so far, keyed by play session id.
    /// </summary>
    public ConcurrentDictionary<string, string> CommandLines { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the most recent command line, for requests without a play session id. The tests
    /// of one class run one after another, and each class has its own server.
    /// </summary>
    public string? LastCommandLine { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a live HLS transcode is answered the way ffmpeg
    /// would start it: by writing an event playlist with <see cref="LiveSegmentCount"/> segments,
    /// named as the command line tells ffmpeg to name them.
    /// </summary>
    public bool ProduceLivePlaylists { get; set; }

    /// <summary>
    /// Gets or sets the number of segments in a produced live playlist.
    /// </summary>
    public int LiveSegmentCount { get; set; } = 4;

    /// <summary>
    /// Gets the number of times the server ended a request on a produced live transcode.
    /// </summary>
    public int LiveEndRequests => _liveEndRequests;

    public async Task<TranscodingJob> StartFfMpeg(
        StreamState state,
        string outputPath,
        string commandLineArguments,
        Guid userId,
        TranscodingJobType transcodingJobType,
        CancellationTokenSource cancellationTokenSource,
        string? workingDirectory = null)
    {
        CommandLines[state.Request.PlaySessionId ?? string.Empty] = commandLineArguments;
        LastCommandLine = commandLineArguments;
        if (!ProduceLivePlaylists || !outputPath.EndsWith(".m3u8", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Acceptance tests do not start ffmpeg.");
        }

        await File.WriteAllTextAsync(outputPath, LivePlaylist(commandLineArguments, LiveSegmentCount), cancellationTokenSource.Token).ConfigureAwait(false);
        var job = new TranscodingJob(NullLogger<TranscodingJob>.Instance)
        {
            Path = outputPath,
            Type = transcodingJobType,
            PlaySessionId = state.Request.PlaySessionId,
            DeviceId = state.Request.DeviceId,
            HasExited = true,
            ActiveRequestCount = 1
        };
        _fakeJobs[job] = 0;
        return job;
    }

    public TranscodingJob? GetTranscodingJob(string playSessionId) => _inner.GetTranscodingJob(playSessionId);

    public TranscodingJob? GetTranscodingJob(string path, TranscodingJobType type) => _inner.GetTranscodingJob(path, type);

    public void PingTranscodingJob(string playSessionId, bool? isUserPaused) => _inner.PingTranscodingJob(playSessionId, isUserPaused);

    public Task KillTranscodingJobs(string deviceId, string? playSessionId, Func<string, bool> deleteFiles)
        => _inner.KillTranscodingJobs(deviceId, playSessionId, deleteFiles);

    public void ReportTranscodingProgress(TranscodingJob job, StreamState state, TimeSpan? transcodingPosition, float? framerate, double? percentComplete, long? bytesTranscoded, int? bitRate)
        => _inner.ReportTranscodingProgress(job, state, transcodingPosition, framerate, percentComplete, bytesTranscoded, bitRate);

    public TranscodingJob? OnTranscodeBeginRequest(string path, TranscodingJobType type) => _inner.OnTranscodeBeginRequest(path, type);

    public void OnTranscodeEndRequest(TranscodingJob job)
    {
        if (_fakeJobs.ContainsKey(job))
        {
            Interlocked.Increment(ref _liveEndRequests);
            return;
        }

        _inner.OnTranscodeEndRequest(job);
    }

    public ValueTask<IDisposable> LockAsync(string outputPath, CancellationToken cancellationToken) => _inner.LockAsync(outputPath, cancellationToken);

    private static string LivePlaylist(string commandLine, int segmentCount)
    {
        var segmentFile = Argument(commandLine, "-hls_segment_filename") ?? throw new InvalidOperationException("No segment file name: " + commandLine);
        var baseUrl = Argument(commandLine, "-hls_base_url") ?? string.Empty;
        var playlist = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:6\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:EVENT\n");
        if (Argument(commandLine, "-hls_fmp4_init_filename") is { } initFile)
        {
            playlist.Append("#EXT-X-MAP:URI=\"").Append(initFile).Append("\"\n");
        }

        for (var i = 0; i < segmentCount; i++)
        {
            var segment = Path.GetFileName(segmentFile.Replace("%d", i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            playlist.Append("#EXTINF:6.000000,\n").Append(baseUrl).Append(segment).Append('\n');
        }

        return playlist.ToString();
    }

    private static string? Argument(string commandLine, string option)
    {
        var match = Regex.Match(commandLine, Regex.Escape(option) + " \"([^\"]*)\"");
        return match.Success ? match.Groups[1].Value : null;
    }
}
