using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Transcoding segment cleaner: deleting segment files.
/// </summary>
public partial class TranscodingSegmentCleaner
{
    private async Task DeleteSegmentFiles(TranscodingJob job, long idxMin, long idxMax, int delayMs)
    {
        var path = job.Path ?? throw new ArgumentException("Path can't be null.");

        _logger.LogDebug("Deleting segment file(s) index {Min} to {Max} from {Path}", idxMin, idxMax, path);

        await Task.Delay(delayMs).ConfigureAwait(false);

        try
        {
            if (job.Type == TranscodingJobType.Hls)
            {
                DeleteHlsSegmentFiles(path, idxMin, idxMax);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error deleting segment file(s) {Path}", path);
        }
    }

    private void DeleteHlsSegmentFiles(string outputFilePath, long idxMin, long idxMax)
    {
        var directory = Path.GetDirectoryName(outputFilePath)
                        ?? throw new ArgumentException("Path can't be a root directory.", nameof(outputFilePath));

        var name = Path.GetFileNameWithoutExtension(outputFilePath);

        var filesToDelete = _fileSystem.GetFilePaths(directory)
            .Where(f => long.TryParse(Path.GetFileNameWithoutExtension(f).Replace(name, string.Empty, StringComparison.Ordinal), out var idx)
                        && (idx >= idxMin && idx <= idxMax));

        List<Exception>? exs = null;
        foreach (var file in filesToDelete)
        {
            try
            {
                _logger.LogDebug("Deleting HLS segment file {0}", file);
                _fileSystem.DeleteFile(file);
            }
            catch (IOException ex)
            {
                (exs ??= new List<Exception>()).Add(ex);
                _logger.LogDebug(ex, "Error deleting HLS segment file {Path}", file);
            }
        }

        if (exs is not null)
        {
            throw new AggregateException("Error deleting HLS segment files", exs);
        }
    }
}
