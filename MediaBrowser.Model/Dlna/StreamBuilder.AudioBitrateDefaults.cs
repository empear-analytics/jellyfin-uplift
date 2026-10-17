using System;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        private static int GetDefaultAudioBitrate(string? audioCodec, int? audioChannels)
        {
            if (!string.IsNullOrEmpty(audioCodec))
            {
                // Default to a higher bitrate for stream copy
                if (IsLossyStreamCopyCodec(audioCodec))
                {
                    return GetLossyCodecDefaultBitrate(audioChannels);
                }

                if (IsLosslessStreamCopyCodec(audioCodec))
                {
                    return GetLosslessCodecDefaultBitrate(audioChannels);
                }
            }

            return 192000;
        }

        private static bool IsLossyStreamCopyCodec(string? audioCodec)
        {
            return string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "mp3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "ac3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "eac3", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLosslessStreamCopyCodec(string? audioCodec)
        {
            return string.Equals(audioCodec, "flac", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "alac", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetLossyCodecDefaultBitrate(int? audioChannels)
        {
            if ((audioChannels ?? 0) < 2)
            {
                return 128000;
            }

            return (audioChannels ?? 0) >= 6 ? 640000 : 384000;
        }

        private static int GetLosslessCodecDefaultBitrate(int? audioChannels)
        {
            if ((audioChannels ?? 0) < 2)
            {
                return 768000;
            }

            return (audioChannels ?? 0) >= 6 ? 3584000 : 1536000;
        }
    }
}
