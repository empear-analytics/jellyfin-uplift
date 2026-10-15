using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.MediaEncoding.Transcoding;

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

    public Task<TranscodingJob> StartFfMpeg(
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
        throw new InvalidOperationException("Acceptance tests do not start ffmpeg.");
    }

    public TranscodingJob? GetTranscodingJob(string playSessionId) => _inner.GetTranscodingJob(playSessionId);

    public TranscodingJob? GetTranscodingJob(string path, TranscodingJobType type) => _inner.GetTranscodingJob(path, type);

    public void PingTranscodingJob(string playSessionId, bool? isUserPaused) => _inner.PingTranscodingJob(playSessionId, isUserPaused);

    public Task KillTranscodingJobs(string deviceId, string? playSessionId, Func<string, bool> deleteFiles)
        => _inner.KillTranscodingJobs(deviceId, playSessionId, deleteFiles);

    public void ReportTranscodingProgress(TranscodingJob job, StreamState state, TimeSpan? transcodingPosition, float? framerate, double? percentComplete, long? bytesTranscoded, int? bitRate)
        => _inner.ReportTranscodingProgress(job, state, transcodingPosition, framerate, percentComplete, bytesTranscoded, bitRate);

    public TranscodingJob? OnTranscodeBeginRequest(string path, TranscodingJobType type) => _inner.OnTranscodeBeginRequest(path, type);

    public void OnTranscodeEndRequest(TranscodingJob job) => _inner.OnTranscodeEndRequest(job);

    public ValueTask<IDisposable> LockAsync(string outputPath, CancellationToken cancellationToken) => _inner.LockAsync(outputPath, cancellationToken);
}
