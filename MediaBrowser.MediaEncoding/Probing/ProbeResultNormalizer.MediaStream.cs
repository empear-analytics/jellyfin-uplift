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
        /// <summary>
        /// Converts ffprobe stream info to our MediaStream class.
        /// </summary>
        /// <param name="isAudio">if set to <c>true</c> [is info].</param>
        /// <param name="streamInfo">The stream info.</param>
        /// <param name="formatInfo">The format info.</param>
        /// <param name="frameInfoList">The frame info.</param>
        /// <returns>MediaStream.</returns>
        private MediaStream GetMediaStream(bool isAudio, MediaStreamInfo streamInfo, MediaFormatInfo formatInfo, IReadOnlyList<MediaFrameInfo> frameInfoList)
        {
            var stream = new MediaStream
            {
                Codec = streamInfo.CodecName,
                Profile = streamInfo.Profile,
                Width = streamInfo.Width,
                Height = streamInfo.Height,
                Level = streamInfo.Level,
                Index = streamInfo.Index,
                PixelFormat = streamInfo.PixelFormat,
                NalLengthSize = streamInfo.NalLengthSize,
                TimeBase = streamInfo.TimeBase,
                CodecTimeBase = streamInfo.CodecTimeBase
            };

            // Filter out junk
            if (!string.IsNullOrWhiteSpace(streamInfo.CodecTagString) && !streamInfo.CodecTagString.Contains("[0]", StringComparison.OrdinalIgnoreCase))
            {
                stream.CodecTag = streamInfo.CodecTagString;
            }

            if (streamInfo.Tags is not null)
            {
                stream.Language = GetDictionaryValue(streamInfo.Tags, "language");
                stream.Comment = GetDictionaryValue(streamInfo.Tags, "comment");
                stream.Title = GetDictionaryValue(streamInfo.Tags, "title");
            }

            if (streamInfo.CodecType == CodecType.Audio)
            {
                SetAudioStreamInfo(stream, streamInfo);
            }
            else if (streamInfo.CodecType == CodecType.Subtitle)
            {
                SetSubtitleStreamInfo(stream, streamInfo);
            }
            else if (streamInfo.CodecType == CodecType.Video)
            {
                SetVideoStreamInfo(isAudio, stream, streamInfo, frameInfoList);
            }
            else if (streamInfo.CodecType == CodecType.Data)
            {
                stream.Type = MediaStreamType.Data;
            }
            else
            {
                return null;
            }

            SetStreamBitRate(isAudio, stream, streamInfo, formatInfo);

            SetBitRateFromTags(stream, streamInfo);

            ApplyDisposition(stream, streamInfo);

            NormalizeStreamTitle(stream);

            return stream;
        }

        private void SetAudioStreamInfo(MediaStream stream, MediaStreamInfo streamInfo)
        {
            stream.Type = MediaStreamType.Audio;
            stream.LocalizedDefault = _localization.GetLocalizedString("Default");
            stream.LocalizedExternal = _localization.GetLocalizedString("External");
            stream.LocalizedOriginal = _localization.GetLocalizedString("Original");
            if (!string.IsNullOrEmpty(stream.Language))
            {
                stream.LocalizedLanguage = _localization.GetLanguageDisplayName(stream.Language);
            }

            stream.Channels = streamInfo.Channels;

            if (int.TryParse(streamInfo.SampleRate, CultureInfo.InvariantCulture, out var sampleRate))
            {
                stream.SampleRate = sampleRate;
            }

            stream.ChannelLayout = ParseChannelLayout(streamInfo.ChannelLayout);

            if (streamInfo.BitsPerSample > 0)
            {
                stream.BitDepth = streamInfo.BitsPerSample;
            }
            else if (streamInfo.BitsPerRawSample > 0)
            {
                stream.BitDepth = streamInfo.BitsPerRawSample;
            }

            SetAudioTitleFromTags(stream, streamInfo);
        }

        private static void SetAudioTitleFromTags(MediaStream stream, MediaStreamInfo streamInfo)
        {
            if (string.IsNullOrEmpty(stream.Title))
            {
                // FFprobe exposes MP4 track names via the name tag rather than title
                stream.Title = GetDictionaryValue(streamInfo.Tags, "name");

                if (string.IsNullOrEmpty(stream.Title))
                {
                    // fall back to handler_name if populated and not the default "SoundHandler"
                    string handlerName = GetDictionaryValue(streamInfo.Tags, "handler_name");
                    if (!string.IsNullOrEmpty(handlerName) && !string.Equals(handlerName, "SoundHandler", StringComparison.OrdinalIgnoreCase))
                    {
                        stream.Title = handlerName;
                    }
                }
            }
        }

        private void SetSubtitleStreamInfo(MediaStream stream, MediaStreamInfo streamInfo)
        {
            stream.Type = MediaStreamType.Subtitle;
            stream.Codec = NormalizeSubtitleCodec(stream.Codec);
            stream.LocalizedUndefined = _localization.GetLocalizedString("Undefined");
            stream.LocalizedDefault = _localization.GetLocalizedString("Default");
            stream.LocalizedForced = _localization.GetLocalizedString("Forced");
            stream.LocalizedExternal = _localization.GetLocalizedString("External");
            stream.LocalizedHearingImpaired = _localization.GetLocalizedString("HearingImpaired");
            if (!string.IsNullOrEmpty(stream.Language))
            {
                stream.LocalizedLanguage = _localization.GetLanguageDisplayName(stream.Language);
            }

            if (string.IsNullOrEmpty(stream.Title))
            {
                // FFprobe exposes MP4 track names via the name tag rather than title
                stream.Title = GetDictionaryValue(streamInfo.Tags, "name");

                if (string.IsNullOrEmpty(stream.Title))
                {
                    // fall back to handler_name if populated and not the default "SubtitleHandler"
                    string handlerName = GetDictionaryValue(streamInfo.Tags, "handler_name");
                    if (!string.IsNullOrEmpty(handlerName) && !string.Equals(handlerName, "SubtitleHandler", StringComparison.OrdinalIgnoreCase))
                    {
                        stream.Title = handlerName;
                    }
                }
            }
        }

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

        private static void SetColorInfo(MediaStream stream, MediaStreamInfo streamInfo)
        {
            if (!string.IsNullOrEmpty(streamInfo.ColorRange))
            {
                stream.ColorRange = streamInfo.ColorRange;
            }

            if (!string.IsNullOrEmpty(streamInfo.ColorSpace))
            {
                stream.ColorSpace = streamInfo.ColorSpace;
            }

            if (!string.IsNullOrEmpty(streamInfo.ColorTransfer))
            {
                stream.ColorTransfer = streamInfo.ColorTransfer;
            }

            if (!string.IsNullOrEmpty(streamInfo.ColorPrimaries))
            {
                stream.ColorPrimaries = streamInfo.ColorPrimaries;
            }
        }

        private static void ApplySideData(MediaStream stream, MediaStreamInfo streamInfo)
        {
            if (streamInfo.SideDataList is not null)
            {
                foreach (var data in streamInfo.SideDataList)
                {
                    // Parse Dolby Vision metadata from side_data
                    if (string.Equals(data.SideDataType, "DOVI configuration record", StringComparison.OrdinalIgnoreCase))
                    {
                        stream.DvVersionMajor = data.DvVersionMajor;
                        stream.DvVersionMinor = data.DvVersionMinor;
                        stream.DvProfile = data.DvProfile;
                        stream.DvLevel = data.DvLevel;
                        stream.RpuPresentFlag = data.RpuPresentFlag;
                        stream.ElPresentFlag = data.ElPresentFlag;
                        stream.BlPresentFlag = data.BlPresentFlag;
                        stream.DvBlSignalCompatibilityId = data.DvBlSignalCompatibilityId;
                    }

                    // Parse video rotation metadata from side_data
                    else if (string.Equals(data.SideDataType, "Display Matrix", StringComparison.OrdinalIgnoreCase))
                    {
                        stream.Rotation = data.Rotation;
                    }

                    // Parse video frame cropping metadata from side_data
                    // TODO: save them and make HW filters to apply them in HWA pipelines
                    else if (string.Equals(data.SideDataType, "Frame Cropping", StringComparison.OrdinalIgnoreCase))
                    {
                        // Streams containing artificially added frame cropping
                        // metadata should not be marked as anamorphic.
                        stream.IsAnamorphic = false;
                    }
                }
            }
        }

        private static void SetHdr10PlusFlag(MediaStream stream, IReadOnlyList<MediaFrameInfo> frameInfoList)
        {
            var frameInfo = frameInfoList?.FirstOrDefault(i => i.StreamIndex == stream.Index);
            if (frameInfo?.SideDataList is not null
                && frameInfo.SideDataList.Any(data => string.Equals(data.SideDataType, "HDR Dynamic Metadata SMPTE2094-40 (HDR10+)", StringComparison.OrdinalIgnoreCase)))
            {
                stream.Hdr10PlusPresentFlag = true;
            }
        }

        private static void SetStreamBitRate(bool isAudio, MediaStream stream, MediaStreamInfo streamInfo, MediaFormatInfo formatInfo)
        {
            // Get stream bitrate
            var bitrate = 0;

            if (int.TryParse(streamInfo.BitRate, CultureInfo.InvariantCulture, out var value))
            {
                bitrate = value;
            }

            // The bitrate info of FLAC audio is included in formatInfo.
            // Don't do this for video streams: formatInfo.BitRate is the overall container
            // bitrate (video + audio + subtitles + overhead), not the video bitrate.
            if (ShouldUseFormatBitRate(bitrate, formatInfo, isAudio, stream))
            {
                // If the stream info doesn't have a bitrate get the value from the media format info
                if (int.TryParse(formatInfo.BitRate, CultureInfo.InvariantCulture, out value))
                {
                    bitrate = value;
                }
            }

            if (bitrate > 0)
            {
                stream.BitRate = bitrate;
            }
        }

        private static bool ShouldUseFormatBitRate(int bitrate, MediaFormatInfo formatInfo, bool isAudio, MediaStream stream)
        {
            return bitrate == 0
                && formatInfo is not null
                && isAudio && stream.Type == MediaStreamType.Audio;
        }

        private static void SetBitRateFromTags(MediaStream stream, MediaStreamInfo streamInfo)
        {
            // Extract bitrate info from tag "BPS" if possible.
            if (stream.BitRate.HasValue)
            {
                return;
            }

            if (streamInfo.CodecType != CodecType.Audio
                && streamInfo.CodecType != CodecType.Video)
            {
                return;
            }

            var bps = GetBPSFromTags(streamInfo);
            if (bps > 0)
            {
                stream.BitRate = bps;
                return;
            }

            // Get average bitrate info from tag "NUMBER_OF_BYTES" and "DURATION" if possible.
            var durationInSeconds = GetRuntimeSecondsFromTags(streamInfo);
            var bytes = GetNumberOfBytesFromTags(streamInfo);
            if (!HasDurationAndSize(durationInSeconds, bytes))
            {
                return;
            }

            bps = Convert.ToInt32(bytes * 8 / durationInSeconds, CultureInfo.InvariantCulture);
            if (bps > 0)
            {
                stream.BitRate = bps;
            }
        }

        private static bool HasDurationAndSize(double? durationInSeconds, long? bytes)
        {
            return durationInSeconds is not null && durationInSeconds.Value >= 1 && bytes is not null;
        }

        private static void ApplyDisposition(MediaStream stream, MediaStreamInfo streamInfo)
        {
            var disposition = streamInfo.Disposition;
            if (disposition is not null)
            {
                if (disposition.GetValueOrDefault("default") == 1)
                {
                    stream.IsDefault = true;
                }

                if (disposition.GetValueOrDefault("forced") == 1)
                {
                    stream.IsForced = true;
                }

                if (disposition.GetValueOrDefault("hearing_impaired") == 1)
                {
                    stream.IsHearingImpaired = true;
                }

                if (disposition.GetValueOrDefault("original") == 1)
                {
                    stream.IsOriginal = true;
                }
            }
        }

        private static void NormalizeStreamTitle(MediaStream stream)
        {
            if (string.Equals(stream.Title, "cc", StringComparison.OrdinalIgnoreCase)
                || stream.Type == MediaStreamType.EmbeddedImage)
            {
                stream.Title = null;
            }
        }

        private static string ParseChannelLayout(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            return input.AsSpan().LeftPart('(').ToString();
        }

        private static string GetAspectRatio(MediaStreamInfo info)
        {
            var original = info.DisplayAspectRatio;

            var parts = (original ?? string.Empty).Split(':');
            if (!TryParseDisplayAspectRatioParts(parts, out var width, out var height))
            {
                width = info.Width.Value;
                height = info.Height.Value;
            }

            if (width > 0 && height > 0)
            {
                double ratio = width;
                ratio /= height;

                if (IsClose(ratio, 1.777777778, .03))
                {
                    return "16:9";
                }

                if (IsClose(ratio, 1.3333333333, .05))
                {
                    return "4:3";
                }

                if (IsClose(ratio, 1.41))
                {
                    return "1.41:1";
                }

                if (IsClose(ratio, 1.5))
                {
                    return "1.5:1";
                }

                if (IsClose(ratio, 1.6))
                {
                    return "1.6:1";
                }

                if (IsClose(ratio, 1.66666666667))
                {
                    return "5:3";
                }

                if (IsClose(ratio, 1.85, .02))
                {
                    return "1.85:1";
                }

                if (IsClose(ratio, 2.35, .025))
                {
                    return "2.35:1";
                }

                if (IsClose(ratio, 2.4, .025))
                {
                    return "2.40:1";
                }
            }

            return original;
        }

        private static bool TryParseDisplayAspectRatioParts(string[] parts, out int width, out int height)
        {
            width = default;
            height = default;
            return parts.Length == 2
                    && int.TryParse(parts[0], CultureInfo.InvariantCulture, out width)
                    && int.TryParse(parts[1], CultureInfo.InvariantCulture, out height)
                    && width > 0
                    && height > 0;
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
            if (parts.Length == 2
                && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var num)
                && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var den)
                && den > 0)
            {
                return IsClose(num / den, 1.0, 0.01);
            }

            return string.Equals(sar, "1:1", StringComparison.Ordinal);
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

        private static int? GetBPSFromTags(MediaStreamInfo streamInfo)
        {
            if (streamInfo?.Tags is null)
            {
                return null;
            }

            var bps = GetDictionaryValue(streamInfo.Tags, "BPS-eng") ?? GetDictionaryValue(streamInfo.Tags, "BPS");
            if (int.TryParse(bps, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBps))
            {
                return parsedBps;
            }

            return null;
        }

        private static double? GetRuntimeSecondsFromTags(MediaStreamInfo streamInfo)
        {
            if (streamInfo?.Tags is null)
            {
                return null;
            }

            var duration = GetDictionaryValue(streamInfo.Tags, "DURATION-eng") ?? GetDictionaryValue(streamInfo.Tags, "DURATION");
            if (!string.IsNullOrEmpty(duration))
            {
                // Matroska DURATION tags use nanosecond precision (e.g. "00:00:05.023000000"), but
                // TimeSpan only supports up to 7 fractional digits (ticks). Trim the surplus digits so
                // these durations parse instead of being silently dropped.
                duration = DurationOverPrecisionRegex().Replace(duration, "$1");
                if (TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out var parsedDuration))
                {
                    return parsedDuration.TotalSeconds;
                }
            }

            return null;
        }

        private static long? GetNumberOfBytesFromTags(MediaStreamInfo streamInfo)
        {
            if (streamInfo?.Tags is null)
            {
                return null;
            }

            var numberOfBytes = GetDictionaryValue(streamInfo.Tags, "NUMBER_OF_BYTES-eng")
                                ?? GetDictionaryValue(streamInfo.Tags, "NUMBER_OF_BYTES");
            if (long.TryParse(numberOfBytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBytes))
            {
                return parsedBytes;
            }

            return null;
        }
    }
}
