#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;

namespace MediaBrowser.MediaEncoding.Probing
{
    public partial class ProbeResultNormalizer
    {
        internal static int? GetEstimatedAudioBitrate(string codec, string profile, int? channels)
        {
            if (IsAudioBitrateNotEstimable(codec, channels))
            {
                return null;
            }

            // Rough typical bitrates used only as a fallback when ffprobe doesn't report a stream bitrate.
            var channelCount = channels.Value;
            var isMultichannel = channelCount > 2;

            return codec.ToLowerInvariant() switch
            {
                "aac" or "mp3" or "mp2" => isMultichannel ? 320000 : 192000,
                "ac3" or "eac3" => isMultichannel ? 640000 : 192000,
                "dts" or "dca" => IsDtsLossless(profile) ? channelCount * 700000 : (isMultichannel ? 1509000 : 768000),
                "opus" => isMultichannel ? 256000 : 128000,
                "vorbis" => isMultichannel ? 320000 : 160000,
                "wmav1" or "wmav2" or "wmapro" => isMultichannel ? 384000 : 192000,
                "flac" or "alac" => channelCount * 480000,
                "truehd" or "mlp" => channelCount * 700000,
                _ => null
            };
        }

        private static bool IsAudioBitrateNotEstimable(string codec, [NotNullWhen(false)] int? channels)
        {
            return !channels.HasValue || channels.Value < 1 || string.IsNullOrEmpty(codec);
        }

        private static bool IsDtsLossless(string profile)
            => profile is not null && profile.Contains("HD MA", StringComparison.OrdinalIgnoreCase);
    }
}
