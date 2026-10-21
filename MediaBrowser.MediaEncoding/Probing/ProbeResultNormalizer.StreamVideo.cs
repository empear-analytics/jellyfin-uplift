#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Probing
{
    public partial class ProbeResultNormalizer
    {
        private static void SetVideoStreamInfo(bool isAudio, MediaStream stream, MediaStreamInfo streamInfo, IReadOnlyList<MediaFrameInfo> frameInfoList)
        {
            stream.IsAVC = streamInfo.IsAvc;
            stream.AverageFrameRate = GetFrameRate(streamInfo.AverageFrameRate);
            stream.RealFrameRate = GetFrameRate(streamInfo.RFrameRate);

            stream.IsInterlaced = !string.IsNullOrWhiteSpace(streamInfo.FieldOrder)
                && !string.Equals(streamInfo.FieldOrder, "progressive", StringComparison.OrdinalIgnoreCase);

            SetVideoStreamType(isAudio, stream);

            stream.AspectRatio = GetAspectRatio(streamInfo);

            if (streamInfo.BitsPerSample > 0)
            {
                stream.BitDepth = streamInfo.BitsPerSample;
            }
            else if (streamInfo.BitsPerRawSample > 0)
            {
                stream.BitDepth = streamInfo.BitsPerRawSample;
            }

            SetBitDepthFromPixelFormat(stream, streamInfo);

            SetIsAnamorphic(stream, streamInfo);

            if (streamInfo.Refs > 0)
            {
                stream.RefFrames = streamInfo.Refs;
            }

            SetColorInfo(stream, streamInfo);

            ApplySideData(stream, streamInfo);

            SetHdr10PlusFlag(stream, frameInfoList);
        }

        private static void SetVideoStreamType(bool isAudio, MediaStream stream)
        {
            if (IsEmbeddedImageCodec(isAudio, stream))
            {
                stream.Type = MediaStreamType.EmbeddedImage;
            }
            else if (string.Equals(stream.Codec, "mjpeg", StringComparison.OrdinalIgnoreCase))
            {
                // How to differentiate between video and embedded image?
                // The only difference I've seen thus far is presence of codec tag, also embedded images have high (unusual) framerates
                if (!string.IsNullOrWhiteSpace(stream.CodecTag))
                {
                    stream.Type = MediaStreamType.Video;
                }
                else
                {
                    stream.Type = MediaStreamType.EmbeddedImage;
                }
            }
            else
            {
                stream.Type = MediaStreamType.Video;
            }
        }

        private static bool IsEmbeddedImageCodec(bool isAudio, MediaStream stream)
        {
            return isAudio
                || string.Equals(stream.Codec, "bmp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stream.Codec, "gif", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stream.Codec, "png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stream.Codec, "webp", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetBitDepthFromPixelFormat(MediaStream stream, MediaStreamInfo streamInfo)
        {
            if (!stream.BitDepth.HasValue)
            {
                if (!string.IsNullOrEmpty(streamInfo.PixelFormat))
                {
                    if (Is8BitPixelFormat(streamInfo))
                    {
                        stream.BitDepth = 8;
                    }
                    else if (Is10BitPixelFormat(streamInfo))
                    {
                        stream.BitDepth = 10;
                    }
                    else if (Is12BitPixelFormat(streamInfo))
                    {
                        stream.BitDepth = 12;
                    }
                }
            }
        }

        private static bool Is8BitPixelFormat(MediaStreamInfo streamInfo)
        {
            return string.Equals(streamInfo.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase)
                || string.Equals(streamInfo.PixelFormat, "yuv444p", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Is10BitPixelFormat(MediaStreamInfo streamInfo)
        {
            return string.Equals(streamInfo.PixelFormat, "yuv420p10le", StringComparison.OrdinalIgnoreCase)
                || string.Equals(streamInfo.PixelFormat, "yuv444p10le", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Is12BitPixelFormat(MediaStreamInfo streamInfo)
        {
            return string.Equals(streamInfo.PixelFormat, "yuv420p12le", StringComparison.OrdinalIgnoreCase)
                || string.Equals(streamInfo.PixelFormat, "yuv444p12le", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetIsAnamorphic(MediaStream stream, MediaStreamInfo streamInfo)
        {
            // http://stackoverflow.com/questions/17353387/how-to-detect-anamorphic-video-with-ffprobe
            if (string.IsNullOrEmpty(streamInfo.SampleAspectRatio)
                && string.IsNullOrEmpty(streamInfo.DisplayAspectRatio))
            {
                stream.IsAnamorphic = false;
            }
            else if (IsNearSquarePixelSar(streamInfo.SampleAspectRatio))
            {
                stream.IsAnamorphic = false;
            }
            else if (!string.Equals(streamInfo.SampleAspectRatio, "0:1", StringComparison.Ordinal))
            {
                stream.IsAnamorphic = true;
            }
            else if (string.Equals(streamInfo.DisplayAspectRatio, "0:1", StringComparison.Ordinal))
            {
                stream.IsAnamorphic = false;
            }
            else if (!string.Equals(
                         streamInfo.DisplayAspectRatio,
                         // Force GetAspectRatio() to derive ratio from Width/Height directly by using null DAR
                         GetAspectRatio(new MediaStreamInfo
                         {
                             Width = streamInfo.Width,
                             Height = streamInfo.Height,
                             DisplayAspectRatio = null
                         }),
                         StringComparison.Ordinal))
            {
                stream.IsAnamorphic = true;
            }
            else
            {
                stream.IsAnamorphic = false;
            }
        }

        private static bool IsClose(double d1, double d2, double variance = .005)
        {
            return Math.Abs(d1 - d2) <= variance;
        }

        /// <summary>
        /// Determines whether a sample aspect ratio represents square (or near-square) pixels.
        /// Some encoders produce SARs like 3201:3200 for content that is effectively 1:1,
        /// which would be falsely classified as anamorphic by an exact string comparison.
        /// A 1% tolerance safely covers encoder rounding artifacts while preserving detection
        /// of genuine anamorphic content (closest standard is PAL 4:3 at 16:15 = 6.67% off).
        /// </summary>
        /// <param name="sar">The sample aspect ratio string in "N:D" format.</param>
        /// <returns><c>true</c> if the SAR is within 1% of 1:1; otherwise <c>false</c>.</returns>
        internal static bool IsNearSquarePixelSar(string sar)
        {
            if (string.IsNullOrEmpty(sar))
            {
                return false;
            }

            var parts = sar.Split(':');
            if (TryParseSarRatioParts(parts, out var num, out var den))
            {
                return IsClose(num / den, 1.0, 0.01);
            }

            return string.Equals(sar, "1:1", StringComparison.Ordinal);
        }

        private static bool TryParseSarRatioParts(string[] parts, out double num, out double den)
        {
            num = default;
            den = default;
            return parts.Length == 2
                && double.TryParse(parts[0], CultureInfo.InvariantCulture, out num)
                && double.TryParse(parts[1], CultureInfo.InvariantCulture, out den)
                && den > 0;
        }

        /// <summary>
        /// Gets a frame rate from a string value in ffprobe output
        /// This could be a number or in the format of 2997/125.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>System.Nullable{System.Single}.</returns>
        internal static float? GetFrameRate(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty)
            {
                return null;
            }

            int index = value.IndexOf('/');
            if (index == -1)
            {
                return null;
            }

            if (!float.TryParse(value[..index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dividend)
                || !float.TryParse(value[(index + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var divisor))
            {
                return null;
            }

            return divisor == 0f ? null : dividend / divisor;
        }
    }
}
