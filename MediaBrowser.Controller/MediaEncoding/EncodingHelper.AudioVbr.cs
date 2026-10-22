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
        private static string GetLameVbrModeParam(int bitrate, int bitratePerChannel)
        {
            // lame's VBR is only good for a certain bitrate range
            // For very low and very high bitrate, use abr mode
            if (bitratePerChannel is < 122500 and > 48000)
            {
                return " -qscale:a " + bitratePerChannel switch
                {
                    < 64000 => "6",
                    < 88000 => "4",
                    < 112000 => "2",
                    _ => "0"
                };
            }

            return " -abr:a 1" + " -b:a " + bitrate;
        }

        private static string GetAudioNonFdkVbrModeParam(string encoder, int bitrate, int bitratePerChannel)
        {
            if (string.Equals(encoder, "libmp3lame", StringComparison.OrdinalIgnoreCase))
            {
                return GetLameVbrModeParam(bitrate, bitratePerChannel);
            }

            if (string.Equals(encoder, "aac_at", StringComparison.OrdinalIgnoreCase))
            {
                // aac_at's CVBR mode
                return " -aac_at_mode:a 2" + " -b:a " + bitrate;
            }

            if (string.Equals(encoder, "libvorbis", StringComparison.OrdinalIgnoreCase))
            {
                return GetAudioVorbisVbrModeParam(bitratePerChannel);
            }

            return null;
        }

        private static string GetAudioVorbisVbrModeParam(int bitratePerChannel)
        {
            return " -qscale:a " + bitratePerChannel switch
            {
                < 40000 => "0",
                < 56000 => "2",
                < 80000 => "4",
                < 112000 => "6",
                _ => "8"
            };
        }
    }
}
