#nullable disable

#pragma warning disable CS1591

using System;
using System.Globalization;

namespace MediaBrowser.Controller.MediaEncoding
{
    public partial class JobLogger
    {
        private static float? ParseFramerate(string rate, float? framerate)
        {
            if (float.TryParse(rate, CultureInfo.InvariantCulture, out var val))
            {
                framerate = val;
            }

            return framerate;
        }

        private static long? ParseBytesTranscoded(string part, long? bytesTranscoded)
        {
            var size = part.Split('=', 2)[^1];

            int? scale = null;
            if (size.Contains("kb", StringComparison.OrdinalIgnoreCase))
            {
                scale = 1024;
                size = size.Replace("kb", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (scale.HasValue)
            {
                if (long.TryParse(size, CultureInfo.InvariantCulture, out var val))
                {
                    bytesTranscoded = val * scale.Value;
                }
            }

            return bytesTranscoded;
        }

        private static int? ParseBitRate(string part, int? bitRate)
        {
            var rate = part.Split('=', 2)[^1];

            int? scale = null;
            if (rate.Contains("kbits/s", StringComparison.OrdinalIgnoreCase))
            {
                scale = 1024;
                rate = rate.Replace("kbits/s", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (scale.HasValue)
            {
                if (float.TryParse(rate, CultureInfo.InvariantCulture, out var val))
                {
                    bitRate = (int)Math.Ceiling(val * scale.Value);
                }
            }

            return bitRate;
        }
    }
}
