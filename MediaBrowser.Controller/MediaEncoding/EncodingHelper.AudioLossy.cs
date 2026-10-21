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
        private static int GetAudioCommonLossyBitrate(int inputChannels, int outputChannels, int bitrate)
        {
#pragma warning disable SA1008
            return (inputChannels, outputChannels) switch
            {
                ( >= 6, >= 6 or 0) => Math.Min(640000, bitrate),
                ( > 0, > 0) => Math.Min(outputChannels * 128000, bitrate),
                ( > 0, _) => Math.Min(inputChannels * 128000, bitrate),
                (_, _) => Math.Min(384000, bitrate)
            };
#pragma warning restore SA1008
        }

        private static bool IsDtsAudioCodec(string audioCodec)
        {
            return string.Equals(audioCodec, "dts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "dca", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDefaultOrCommonLossyAudioCodec(string audioCodec)
        {
            return string.IsNullOrEmpty(audioCodec)
                || string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "mp3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "opus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "vorbis", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "ac3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "eac3", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetAudioDefaultBitrate(MediaStream audioStream, int? outputAudioChannels)
        {
            return 128000 * (outputAudioChannels ?? audioStream.Channels ?? 2);
        }
    }
}
