using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Api.Helpers;

public partial class DynamicHlsHelper
{
    /// <summary>
    /// Get the H.26X level of the output video stream.
    /// </summary>
    /// <param name="state">StreamState of the current stream.</param>
    /// <returns>H.26X level of the output video stream.</returns>
    private int? GetOutputVideoCodecLevel(StreamState state)
    {
        string levelString = string.Empty;
        if (IsCopyCodecWithVideoStream(state)
            && HasLevel(state.VideoStream.Level))
        {
            levelString = state.VideoStream.Level.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            levelString = GetTranscodingLevelString(state, levelString);
        }

        if (int.TryParse(levelString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLevel))
        {
            return parsedLevel;
        }

        return null;
    }

    private static string GetTranscodingLevelString(StreamState state, string levelString)
    {
        if (string.Equals(state.ActualOutputVideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            levelString = GetH264TranscodingLevel(state);
        }

        if (string.Equals(state.ActualOutputVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.ActualOutputVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase))
        {
            levelString = GetHevcTranscodingLevel(state);
        }

        if (string.Equals(state.ActualOutputVideoCodec, "av1", StringComparison.OrdinalIgnoreCase))
        {
            levelString = GetAv1TranscodingLevel(state);
        }

        return levelString;
    }

    private static string GetH264TranscodingLevel(StreamState state)
    {
        var levelString = state.GetRequestedLevel(state.ActualOutputVideoCodec) ?? "41";
        levelString = EncodingHelper.NormalizeTranscodingLevel(state, levelString);
        return levelString;
    }

    private static string GetHevcTranscodingLevel(StreamState state)
    {
        var levelString = state.GetRequestedLevel("h265") ?? state.GetRequestedLevel("hevc") ?? "120";
        levelString = EncodingHelper.NormalizeTranscodingLevel(state, levelString);
        return levelString;
    }

    private static string GetAv1TranscodingLevel(StreamState state)
    {
        var levelString = state.GetRequestedLevel("av1") ?? "19";
        levelString = EncodingHelper.NormalizeTranscodingLevel(state, levelString);
        return levelString;
    }

    /// <summary>
    /// Get the profile of the output video stream.
    /// </summary>
    /// <param name="state">StreamState of the current stream.</param>
    /// <param name="codec">Video codec.</param>
    /// <returns>Profile of the output video stream.</returns>
    private string GetOutputVideoCodecProfile(StreamState state, string codec)
    {
        string profileString = string.Empty;
        if (IsCopyWithKnownProfile(state))
        {
            profileString = state.VideoStream.Profile;
        }
        else if (!string.IsNullOrEmpty(codec))
        {
            profileString = state.GetRequestedProfiles(codec).FirstOrDefault() ?? string.Empty;
            if (string.Equals(state.ActualOutputVideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
            {
                profileString ??= "high";
            }

            if (IsHevcOrAv1OutputCodec(state))
            {
                profileString ??= "main";
            }
        }

        return profileString;
    }

    private static bool IsCopyWithKnownProfile(StreamState state)
    {
        return EncodingHelper.IsCopyCodec(state.OutputVideoCodec)
            && !string.IsNullOrEmpty(state.VideoStream.Profile);
    }

    private static bool IsHevcOrAv1OutputCodec(StreamState state)
    {
        return string.Equals(state.ActualOutputVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.ActualOutputVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.ActualOutputVideoCodec, "av1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a formatted string of the output audio codec, for use in the CODECS field.
    /// </summary>
    /// <seealso cref="AppendPlaylistCodecsField(StringBuilder, StreamState)"/>
    /// <seealso cref="GetPlaylistVideoCodecs(StreamState, string, int)"/>
    /// <param name="state">StreamState of the current stream.</param>
    /// <returns>Formatted audio codec string.</returns>
    private string GetPlaylistAudioCodecs(StreamState state)
    {
        if (string.Equals(state.ActualOutputAudioCodec, "aac", StringComparison.OrdinalIgnoreCase))
        {
            return GetAacCodecString(state);
        }

        if (string.Equals(state.ActualOutputAudioCodec, "mp3", StringComparison.OrdinalIgnoreCase))
        {
            return HlsCodecStringHelpers.GetMP3String();
        }

        if (string.Equals(state.ActualOutputAudioCodec, "ac3", StringComparison.OrdinalIgnoreCase))
        {
            return HlsCodecStringHelpers.GetAC3String();
        }

        if (string.Equals(state.ActualOutputAudioCodec, "eac3", StringComparison.OrdinalIgnoreCase))
        {
            return HlsCodecStringHelpers.GetEAC3String();
        }

        if (string.Equals(state.ActualOutputAudioCodec, "flac", StringComparison.OrdinalIgnoreCase))
        {
            return HlsCodecStringHelpers.GetFLACString();
        }

        return GetRemainingPlaylistAudioCodecs(state);
    }

    private static string GetRemainingPlaylistAudioCodecs(StreamState state)
    {
        if (string.Equals(state.ActualOutputAudioCodec, "alac", StringComparison.OrdinalIgnoreCase))
        {
            return HlsCodecStringHelpers.GetALACString();
        }

        if (string.Equals(state.ActualOutputAudioCodec, "opus", StringComparison.OrdinalIgnoreCase))
        {
            return HlsCodecStringHelpers.GetOPUSString();
        }

        if (string.Equals(state.ActualOutputAudioCodec, "truehd", StringComparison.OrdinalIgnoreCase))
        {
            return HlsCodecStringHelpers.GetTRUEHDString();
        }

        if (string.Equals(state.ActualOutputAudioCodec, "dts", StringComparison.OrdinalIgnoreCase))
        {
            return GetDtsCodecString(state);
        }

        return string.Empty;
    }

    private static string GetAacCodecString(StreamState state)
    {
        string? profile = EncodingHelper.IsCopyCodec(state.OutputAudioCodec)
            ? state.AudioStream?.Profile : state.GetRequestedProfiles("aac").FirstOrDefault();

        return HlsCodecStringHelpers.GetAACString(profile);
    }

    private static string GetDtsCodecString(StreamState state)
    {
        // lavc only support encoding DTS core profile
        string? profile = EncodingHelper.IsCopyCodec(state.OutputAudioCodec) ? state.AudioStream?.Profile : "DTS";

        return HlsCodecStringHelpers.GetDTSString(profile);
    }

    /// <summary>
    /// Gets a formatted string of the output video codec, for use in the CODECS field.
    /// </summary>
    /// <seealso cref="AppendPlaylistCodecsField(StringBuilder, StreamState)"/>
    /// <seealso cref="GetPlaylistAudioCodecs(StreamState)"/>
    /// <param name="state">StreamState of the current stream.</param>
    /// <param name="codec">Video codec.</param>
    /// <param name="level">Video level.</param>
    /// <returns>Formatted video codec string.</returns>
    private string GetPlaylistVideoCodecs(StreamState state, string codec, int level)
    {
        if (level == 0)
        {
            // This is 0 when there's no requested level in the device profile
            // and the source is not encoded in H.26X or AV1
            _logger.LogError("Got invalid level when building CODECS field for HLS master playlist");
            return string.Empty;
        }

        if (string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            string profile = GetOutputVideoCodecProfile(state, "h264");
            return HlsCodecStringHelpers.GetH264String(profile, level);
        }

        if (IsHevcCodec(codec))
        {
            string profile = GetOutputVideoCodecProfile(state, "hevc");
            return HlsCodecStringHelpers.GetH265String(profile, level);
        }

        if (string.Equals(codec, "av1", StringComparison.OrdinalIgnoreCase))
        {
            string profile = GetOutputVideoCodecProfile(state, "av1");

            int bitDepth = GetOutputAv1BitDepth(state);

            return HlsCodecStringHelpers.GetAv1String(profile, level, false, bitDepth);
        }

        // VP9 HLS is for video remuxing only, everything is probed from the original video
        if (string.Equals(codec, "vp9", StringComparison.OrdinalIgnoreCase))
        {
            return GetVp9CodecString(state);
        }

        return string.Empty;
    }

    private static int GetOutputAv1BitDepth(StreamState state)
    {
        // Currently we only transcode to 8 bits AV1
        int bitDepth = 8;
        if (IsCopyCodecWithVideoStream(state)
            && HasBitDepth(state.VideoStream.BitDepth))
        {
            bitDepth = state.VideoStream.BitDepth.Value;
        }

        return bitDepth;
    }

    private static bool IsCopyCodecWithVideoStream(StreamState state)
    {
        return EncodingHelper.IsCopyCodec(state.OutputVideoCodec)
            && state.VideoStream is not null;
    }

    private static bool HasLevel([NotNullWhen(true)] double? level)
    {
        return level.HasValue;
    }

    private static bool HasBitDepth([NotNullWhen(true)] int? bitDepth)
    {
        return bitDepth.HasValue;
    }

    private static string GetVp9CodecString(StreamState state)
    {
        var width = state.VideoStream.Width ?? 0;
        var height = state.VideoStream.Height ?? 0;
        var framerate = state.VideoStream.ReferenceFrameRate ?? 30;
        var bitDepth = state.VideoStream.BitDepth ?? 8;
        return HlsCodecStringHelpers.GetVp9String(
            width,
            height,
            state.VideoStream.PixelFormat,
            framerate,
            bitDepth);
    }

    private static bool IsHevcCodec(string codec)
    {
        return string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase);
    }
}
