#nullable disable

#pragma warning disable CS1591
// We need lowercase normalized string for ffmpeg
#pragma warning disable CA1308

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
        private string GetVideoBitrateParam(EncodingJobInfo state, string videoCodec)
        {
            if (state.OutputVideoBitrate is null)
            {
                return string.Empty;
            }

            int bitrate = state.OutputVideoBitrate.Value;

            // Bit rate under 1000k is not allowed in h264_qsv.
            if (string.Equals(videoCodec, "h264_qsv", StringComparison.OrdinalIgnoreCase))
            {
                bitrate = Math.Max(bitrate, 1000);
            }

            // Currently use the same buffer size for all non-QSV encoders.
            // Use long arithmetic to prevent int32 overflow for very high bitrate values.
            int bufsize = (int)Math.Min((long)bitrate * 2, int.MaxValue);

            return GetVideoEncoderRateControlParam(state, videoCodec, bitrate, bufsize);
        }

        private string GetVideoEncoderRateControlParam(EncodingJobInfo state, string videoCodec, int bitrate, int bufsize)
        {
            if (string.Equals(videoCodec, "libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                return FormattableString.Invariant($" -b:v {bitrate} -bufsize {bufsize}");
            }

            if (IsLibX26xEncoder(videoCodec))
            {
                return FormattableString.Invariant($" -maxrate {bitrate} -bufsize {bufsize}");
            }

            if (IsQsvVideoEncoder(videoCodec))
            {
                return GetQsvVideoBitrateParam(state, bitrate);
            }

            if (IsAmfH26xEncoder(videoCodec))
            {
                // Override the too high default qmin 18 in transcoding preset in legacy h26x_amf
                return FormattableString.Invariant($" -rc cbr -qmin 0 -qmax 32 -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
            }

            if (IsVaapiVideoEncoder(videoCodec))
            {
                return GetVaapiVideoBitrateParam(bitrate, bufsize);
            }

            if (IsVideoToolboxH26xCodec(videoCodec))
            {
                // The `maxrate` and `bufsize` options can potentially lead to performance regression
                // and even encoder hangs, especially when the value is very high.
                return FormattableString.Invariant($" -b:v {bitrate} -qmin -1 -qmax -1");
            }

            return FormattableString.Invariant($" -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
        }

        private static bool IsLibX26xEncoder(string videoCodec)
        {
            return string.Equals(videoCodec, "libx264", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "libx265", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVideoToolboxH26xCodec(string videoCodec)
        {
            return string.Equals(videoCodec, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "hevc_videotoolbox", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetQsvVideoBitrateParam(EncodingJobInfo state, int bitrate)
        {
            // TODO: probe QSV encoders' capabilities and enable more tuning options
            // See also https://github.com/intel/media-delivery/blob/master/doc/quality.rst

            // Some less powerful H.264 HW decoders require strict CPB size
            // So bufsize optimizations should not be applied to them
            int factor = 2;
            var codec = state.ActualOutputVideoCodec;
            var level = state.GetRequestedLevel(codec);
            if (IsH264BelowLevel51(new EncoderParamCodecLevel(codec, level)))
            {
                factor = 1;
            }

            // Set (maxrate == bitrate + 1) to trigger VBR for better bitrate allocation
            // Set (rc_init_occupancy == 2 * bitrate) and (bufsize == 4 * bitrate) to deal with drastic scene changes
            // Use long arithmetic and clamp to int.MaxValue to prevent int32 overflow
            // (e.g. bitrate * 4 wraps to a negative value for bitrates above ~537 million)
            int qsvMaxrate = (int)Math.Min((long)bitrate + 1, int.MaxValue);
            int qsvInitOcc = (int)Math.Min((long)bitrate * 1 * factor, int.MaxValue);
            int qsvBufsize = (int)Math.Min((long)bitrate * 2 * factor, int.MaxValue);

            return string.Create(CultureInfo.InvariantCulture, $" -b:v {bitrate} -maxrate {qsvMaxrate} -rc_init_occupancy {qsvInitOcc} -bufsize {qsvBufsize}");
        }

        private string GetVaapiVideoBitrateParam(int bitrate, int bufsize)
        {
            // VBR in i965 driver may result in pixelated output.
            if (_mediaEncoder.IsVaapiDeviceInteli965)
            {
                return FormattableString.Invariant($" -rc_mode CBR -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
            }

            return FormattableString.Invariant($" -rc_mode VBR -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
        }
    }
}
