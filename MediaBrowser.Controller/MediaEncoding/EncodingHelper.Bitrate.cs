#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Configuration;
using IConfigurationManager = MediaBrowser.Common.Configuration.IConfigurationManager;

namespace MediaBrowser.Controller.MediaEncoding
{
    public partial class EncodingHelper
    {
        private static int GetBitDepthCompensatedBitrate(int bitrate, MediaStream videoStream, int targetBitDepth)
        {
            if (IsBitrateCompensationUnneededForBitDepth(videoStream, targetBitDepth)
                || videoStream.BitRate is not int sourceBitrate)
            {
                return bitrate;
            }

            // Bit depth reduction requires bit rate compensation because
            // 10-bit encoded video is significantly more efficient.
            var factor = sourceBitrate switch
            {
                <= 4_000_000 => 1.5,
                <= 8_000_000 => 1.2,
                <= 12_000_000 => 1.1,
                _ => 1.05
            };

            return Convert.ToInt32(bitrate * factor);
        }

        private static bool IsBitrateCompensationUnneededForBitDepth(MediaStream videoStream, int targetBitDepth)
        {
            return targetBitDepth >= 10
                || (videoStream.BitDepth is int bitDepth && bitDepth <= targetBitDepth);
        }

        public int GetVideoBitrateParamValue(BaseEncodingJobOptions request, MediaStream videoStream, string outputVideoCodec)
        {
            var bitrate = request.VideoBitRate;

            if (videoStream is not null)
            {
                var isUpscaling = request.Height.HasValue
                    && videoStream.Height.HasValue
                    && request.Height.Value > videoStream.Height.Value
                    && request.Width.HasValue
                    && videoStream.Width.HasValue
                    && request.Width.Value > videoStream.Width.Value;

                // Don't allow bitrate increases unless upscaling
                if (ShouldCapToSourceBitrate(isUpscaling, bitrate, videoStream))
                {
                    bitrate = GetMinBitrate(videoStream.BitRate.Value, bitrate.Value);
                }

                if (bitrate.HasValue)
                {
                    bitrate = GetCompensatedBitrate(request, videoStream, outputVideoCodec, bitrate);
                }
            }

            // Cap the max target bitrate to 400 Mbps.
            // No consumer or professional hardware transcode target exceeds this value
            // (Intel QSV tops out at ~300 Mbps for H.264; HEVC High Tier Level 5.x is ~240 Mbps).
            // Without this cap, plugin-provided MPEG-TS streams with no usable bitrate metadata
            // can produce unreasonably large -bufsize/-maxrate values for the encoder.
            // Note: the existing FallbackMaxStreamingBitrate mechanism (default 30 Mbps) only
            // applies when a LiveStreamId is set (M3U/HDHR sources). Plugin streams and other
            // sources that bypass the LiveTV pipeline are not covered by it.
            const int MaxSaneBitrate = 400_000_000; // 400 Mbps
            return Math.Min(bitrate ?? 0, MaxSaneBitrate);
        }

        private static int? GetCompensatedBitrate(BaseEncodingJobOptions request, MediaStream videoStream, string outputVideoCodec, int? bitrate)
        {
            var inputVideoCodec = videoStream.Codec;
            bitrate = GetBitDepthCompensatedBitrate(bitrate.Value, videoStream, 8);
            bitrate = ScaleBitrate(bitrate.Value, inputVideoCodec, outputVideoCodec);

            // If a max bitrate was requested, don't let the scaled bitrate exceed it
            if (request.VideoBitRate.HasValue)
            {
                bitrate = Math.Min(bitrate.Value, request.VideoBitRate.Value);
            }

            return bitrate;
        }

        private static bool ShouldCapToSourceBitrate(bool isUpscaling, int? bitrate, MediaStream videoStream)
        {
            return !isUpscaling && bitrate.HasValue && videoStream.BitRate.HasValue;
        }

        private int GetMinBitrate(int sourceBitrate, int requestedBitrate)
        {
            // these values were chosen from testing to improve low bitrate streams
            if (sourceBitrate <= 2000000)
            {
                sourceBitrate = Convert.ToInt32(sourceBitrate * 2.5);
            }
            else if (sourceBitrate <= 3000000)
            {
                sourceBitrate *= 2;
            }

            var bitrate = Math.Min(sourceBitrate, requestedBitrate);

            return bitrate;
        }

        private static double GetVideoBitrateScaleFactor(string codec)
        {
            // hevc & vp9 - 40% more efficient than h.264
            if (IsHevcOrVp9Codec(codec))
            {
                return .6;
            }

            // av1 - 50% more efficient than h.264
            if (string.Equals(codec, "av1", StringComparison.OrdinalIgnoreCase))
            {
                return .5;
            }

            return 1;
        }

        private static bool IsHevcOrVp9Codec(string codec)
        {
            return string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "vp9", StringComparison.OrdinalIgnoreCase);
        }

        public static int ScaleBitrate(int bitrate, string inputVideoCodec, string outputVideoCodec)
        {
            var inputScaleFactor = GetVideoBitrateScaleFactor(inputVideoCodec);
            var outputScaleFactor = GetVideoBitrateScaleFactor(outputVideoCodec);

            // Don't scale the real bitrate lower than the requested bitrate
            var scaleFactor = Math.Max(outputScaleFactor / inputScaleFactor, 1);

            if (bitrate <= 500000)
            {
                scaleFactor = Math.Max(scaleFactor, 4);
            }
            else if (bitrate <= 1000000)
            {
                scaleFactor = Math.Max(scaleFactor, 3);
            }
            else if (bitrate <= 2000000)
            {
                scaleFactor = Math.Max(scaleFactor, 2.5);
            }
            else if (bitrate <= 3000000)
            {
                scaleFactor = Math.Max(scaleFactor, 2);
            }
            else if (bitrate >= 30000000)
            {
                // Don't scale beyond 30Mbps, it is hardly visually noticeable for most codecs with our prefer speed encoding
                // and will cause extremely high bitrate to be used for av1->h264 transcoding that will overload clients and encoders
                scaleFactor = 1;
            }

            return Convert.ToInt32(scaleFactor * bitrate);
        }
    }
}
