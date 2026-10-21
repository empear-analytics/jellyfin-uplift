using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Transcoding segment cleaner.
/// </summary>
public partial class TranscodingSegmentCleaner : IDisposable
{
    private readonly TranscodingJob _job;
    private readonly ILogger<TranscodingSegmentCleaner> _logger;
    private readonly IConfigurationManager _config;
    private readonly IFileSystem _fileSystem;
    private readonly IMediaEncoder _mediaEncoder;
    private Timer? _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranscodingSegmentCleaner"/> class.
    /// </summary>
    /// <param name="job">Transcoding job dto.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TranscodingSegmentCleaner}"/> interface.</param>
    /// <param name="config">Instance of the <see cref="IConfigurationManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="segmentLength">The segment length of this transcoding job. Unused.</param>
    public TranscodingSegmentCleaner(TranscodingJob job, ILogger<TranscodingSegmentCleaner> logger, IConfigurationManager config, IFileSystem fileSystem, IMediaEncoder mediaEncoder, int segmentLength)
    {
        _job = job;
        _logger = logger;
        _config = config;
        _fileSystem = fileSystem;
        _mediaEncoder = mediaEncoder;
    }

    /// <summary>
    /// Start timer.
    /// </summary>
    public void Start()
    {
        _timer = new Timer(TimerCallback, null, 20000, 20000);
    }

    /// <summary>
    /// Stop cleaner.
    /// </summary>
    public void Stop()
    {
        DisposeTimer();
    }

    /// <summary>
    /// Dispose cleaner.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Dispose cleaner.
    /// </summary>
    /// <param name="disposing">Disposing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeTimer();
        }
    }

    private EncodingOptions GetOptions()
    {
        return _config.GetEncodingOptions();
    }

    private async void TimerCallback(object? state)
    {
        if (_job.HasExited)
        {
            DisposeTimer();
            return;
        }

        var options = GetOptions();
        var enableSegmentDeletion = options.EnableSegmentDeletion;
        var segmentKeepSeconds = Math.Max(options.SegmentKeepSeconds, 20);

        if (enableSegmentDeletion)
        {
            var downloadPositionTicks = _job.DownloadPositionTicks ?? 0;
            var downloadPositionSeconds = Convert.ToInt64(TimeSpan.FromTicks(downloadPositionTicks).TotalSeconds);

            if (IsPastSegmentKeepWindow(downloadPositionSeconds, segmentKeepSeconds))
            {
                var idxMaxToDelete = _job.GetHighestServedSegmentIndexEndingAtOrBefore(downloadPositionTicks - TimeSpan.FromSeconds(segmentKeepSeconds).Ticks);

                if (idxMaxToDelete > 0)
                {
                    await DeleteSegmentFiles(_job, 0, idxMaxToDelete.Value, 1500).ConfigureAwait(false);
                }
            }
        }
    }

    private static bool IsPastSegmentKeepWindow(long downloadPositionSeconds, int segmentKeepSeconds)
    {
        return downloadPositionSeconds > 0 && segmentKeepSeconds > 0 && downloadPositionSeconds > segmentKeepSeconds;
    }

    private void DisposeTimer()
    {
        if (_timer is not null)
        {
            _timer.Dispose();
            _timer = null;
        }
    }
}
