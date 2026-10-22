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
        public int GetVideoBitrateParamValue(BaseEncodingJobOptions request, MediaStream videoStream, string outputVideoCodec)
        {
            var bitrate = request.VideoBitRate;

            if (videoStream is not null)
            {
                var isUpscaling = IsBitrateRequestUpscaling(request, videoStream);

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

        private static bool IsBitrateRequestUpscaling(BaseEncodingJobOptions request, MediaStream videoStream)
        {
            return request.Height.HasValue
                && videoStream.Height.HasValue
                && request.Height.Value > videoStream.Height.Value
                && request.Width.HasValue
                && videoStream.Width.HasValue
                && request.Width.Value > videoStream.Width.Value;
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
    }
}
