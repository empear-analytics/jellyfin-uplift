#nullable disable

#pragma warning disable CS1591

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Controller.MediaEncoding
{
    public partial class JobLogger
    {
        private readonly ILogger _logger;

        public JobLogger(ILogger logger)
        {
            _logger = logger;
        }

        public async Task StartStreamingLog(EncodingJobInfo state, StreamReader reader, Stream target)
        {
            try
            {
                using (target)
                using (reader)
                {
                    string line = await reader.ReadLineAsync().ConfigureAwait(false);
                    while (line is not null && reader.BaseStream.CanRead)
                    {
                        ParseLogLine(line, state);

                        var bytes = Encoding.UTF8.GetBytes(Environment.NewLine + line);

                        // If ffmpeg process is closed, the state is disposed, so don't write to target in that case
                        if (!target.CanWrite)
                        {
                            break;
                        }

                        await target.WriteAsync(bytes).ConfigureAwait(false);

                        // Check again, the stream could have been closed
                        if (!target.CanWrite)
                        {
                            break;
                        }

                        await target.FlushAsync().ConfigureAwait(false);
                        line = await reader.ReadLineAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading ffmpeg log");
            }
        }

        private void ParseLogLine(string line, EncodingJobInfo state)
        {
            float? framerate = null;
            double? percent = null;
            TimeSpan? transcodingPosition = null;
            long? bytesTranscoded = null;
            int? bitRate = null;

            var parts = line.Split(' ');

            var totalMs = GetTotalMilliseconds(state);

            var startMs = GetStartMilliseconds(state);

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];

                if (IsFpsLabelWithValue(part, i, parts))
                {
                    var rate = parts[i + 1];

                    framerate = ParseFramerate(rate, framerate);
                }
                else if (part.StartsWith("fps=", StringComparison.OrdinalIgnoreCase))
                {
                    var rate = part.Split('=', 2)[^1];

                    framerate = ParseFramerate(rate, framerate);
                }
                else if (IsTimeFieldWithRuntime(state, part))
                {
                    var context = new TimeProgressInput(part, startMs, totalMs);
                    var result = ParseTimeProgress(context, new TimeProgress(percent, transcodingPosition));
                    percent = result.Percent;
                    transcodingPosition = result.TranscodingPosition;
                }
                else if (part.StartsWith("size=", StringComparison.OrdinalIgnoreCase))
                {
                    bytesTranscoded = ParseBytesTranscoded(part, bytesTranscoded);
                }
                else
                {
                    bitRate = ParseLogLineBitRate(part, bitRate);
                }
            }

            if (ParseLogLineHasProgress(framerate, percent))
            {
                state.ReportTranscodingProgress(transcodingPosition, framerate, percent, bytesTranscoded, bitRate);
            }
        }

        private static TimeProgress ParseTimeProgress(TimeProgressInput context, TimeProgress current)
        {
            var percent = current.Percent;
            var transcodingPosition = current.TranscodingPosition;

            var time = context.Part.Split('=', 2)[^1];

            if (TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out var val))
            {
                var currentMs = context.StartMs + val.TotalMilliseconds;

                percent = 100.0 * currentMs / context.TotalMs;

                transcodingPosition = TimeSpan.FromMilliseconds(currentMs);
            }

            return new TimeProgress(percent, transcodingPosition);
        }

        private static bool ParseLogLineHasProgress(float? framerate, double? percent)
        {
            return framerate.HasValue || percent.HasValue;
        }

        private static int? ParseLogLineBitRate(string part, int? bitRate)
        {
            if (part.StartsWith("bitrate=", StringComparison.OrdinalIgnoreCase))
            {
                bitRate = ParseBitRate(part, bitRate);
            }

            return bitRate;
        }

        private static double GetTotalMilliseconds(EncodingJobInfo state)
        {
            return state.RunTimeTicks.HasValue
                ? TimeSpan.FromTicks(state.RunTimeTicks.Value).TotalMilliseconds
                : 0;
        }

        private static double GetStartMilliseconds(EncodingJobInfo state)
        {
            return state.BaseRequest.StartTimeTicks.HasValue
                ? TimeSpan.FromTicks(state.BaseRequest.StartTimeTicks.Value).TotalMilliseconds
                : 0;
        }

        private static bool IsFpsLabelWithValue(string part, int i, string[] parts)
        {
            return string.Equals(part, "fps=", StringComparison.OrdinalIgnoreCase) &&
                    (i + 1 < parts.Length);
        }

        private static bool IsTimeFieldWithRuntime(EncodingJobInfo state, string part)
        {
            return state.RunTimeTicks.HasValue &&
                    part.StartsWith("time=", StringComparison.OrdinalIgnoreCase);
        }

        private readonly record struct TimeProgressInput(string Part, double StartMs, double TotalMs);

        private readonly record struct TimeProgress(double? Percent, TimeSpan? TranscodingPosition);
    }
}
