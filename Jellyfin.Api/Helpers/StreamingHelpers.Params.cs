using System;
using System.Globalization;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Dlna;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// The streaming helpers.
/// </summary>
public static partial class StreamingHelpers
{
    /// <summary>
    /// Parses the parameters.
    /// </summary>
    /// <param name="request">The request.</param>
    private static void ParseParams(StreamingRequestDto request)
    {
        if (string.IsNullOrEmpty(request.Params))
        {
            return;
        }

        var vals = request.Params.Split(';');

        var videoRequest = request as VideoRequestDto;

        for (var i = 0; i < vals.Length; i++)
        {
            var val = vals[i];

            if (string.IsNullOrWhiteSpace(val))
            {
                continue;
            }

            ApplyParam(request, videoRequest, i, val);
        }
    }

    private static void ApplyParam(StreamingRequestDto request, VideoRequestDto? videoRequest, int i, string val)
    {
        switch (i)
        {
            case 0:
                // DeviceProfileId
                break;
            case 1:
                request.DeviceId = val;
                break;
            case 2:
                request.MediaSourceId = val;
                break;
            case 3:
                request.Static = string.Equals("true", val, StringComparison.OrdinalIgnoreCase);
                break;
            case 4:
                ApplyVideoCodec(videoRequest, val);
                break;
            case 5:
                ApplyAudioCodec(request, val);
                break;
            default:
                StreamingParamApplyRest1(request, videoRequest, i, val);
                break;
        }
    }

    private static void StreamingParamApplyRest1(StreamingRequestDto request, VideoRequestDto? videoRequest, int i, string val)
    {
        switch (i)
        {
            case 6:
                ApplyAudioStreamIndex(videoRequest, val);
                break;
            case 7:
                ApplySubtitleStreamIndex(videoRequest, val);
                break;
            case 8:
                ApplyVideoBitRate(videoRequest, val);
                break;
            case 9:
                request.AudioBitRate = int.Parse(val, CultureInfo.InvariantCulture);
                break;
            case 10:
                request.MaxAudioChannels = int.Parse(val, CultureInfo.InvariantCulture);
                break;
            case 11:
                ApplyMaxFramerate(videoRequest, val);
                break;
            default:
                StreamingParamApplyRest2(request, videoRequest, i, val);
                break;
        }
    }

    private static void StreamingParamApplyRest2(StreamingRequestDto request, VideoRequestDto? videoRequest, int i, string val)
    {
        switch (i)
        {
            case 12:
                ApplyMaxWidth(videoRequest, val);
                break;
            case 13:
                ApplyMaxHeight(videoRequest, val);
                break;
            case 14:
                request.StartTimeTicks = long.Parse(val, CultureInfo.InvariantCulture);
                break;
            case 15:
                ApplyLevel(videoRequest, val);
                break;
            case 16:
                ApplyMaxRefFrames(videoRequest, val);
                break;
            case 17:
                ApplyMaxVideoBitDepth(videoRequest, val);
                break;
            default:
                StreamingParamApplyRest3(request, videoRequest, i, val);
                break;
        }
    }

    private static void StreamingParamApplyRest3(StreamingRequestDto request, VideoRequestDto? videoRequest, int i, string val)
    {
        switch (i)
        {
            case 18:
                ApplyProfile(videoRequest, val);
                break;
            case 19:
                // cabac no longer used
                break;
            case 20:
                request.PlaySessionId = val;
                break;
            case 21:
                // api_key
                break;
            case 22:
                request.LiveStreamId = val;
                break;
            case 23:
                // Duplicating ItemId because of MediaMonkey
                break;
            case 24:
                ApplyCopyTimestamps(videoRequest, val);
                break;
            case 25:
                ApplySubtitleMethod(videoRequest, val);
                break;
            case 26:
                request.TranscodingMaxAudioChannels = int.Parse(val, CultureInfo.InvariantCulture);
                break;
            case 27:
                ApplyEnableSubtitlesInManifest(videoRequest, val);
                break;
            case 28:
                request.Tag = val;
                break;
            case 29:
                ApplyRequireAvc(videoRequest, val);
                break;
            case 30:
                ApplySubtitleCodec(request, val);
                break;
            case 31:
                ApplyRequireNonAnamorphic(videoRequest, val);
                break;
            case 32:
                ApplyDeInterlace(videoRequest, val);
                break;
            case 33:
                request.TranscodeReasons = val;
                break;
        }
    }

    private static void ApplyVideoCodec(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null && IsValidCodecName(val))
        {
            videoRequest.VideoCodec = val;
        }
    }

    private static void ApplyAudioCodec(StreamingRequestDto request, string val)
    {
        if (IsValidCodecName(val))
        {
            request.AudioCodec = val;
        }
    }

    private static void ApplyAudioStreamIndex(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.AudioStreamIndex = int.Parse(val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplySubtitleStreamIndex(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.SubtitleStreamIndex = int.Parse(val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyVideoBitRate(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.VideoBitRate = int.Parse(val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyMaxFramerate(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.MaxFramerate = float.Parse(val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyMaxWidth(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.MaxWidth = int.Parse(val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyMaxHeight(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.MaxHeight = int.Parse(val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyLevel(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null && EncodingHelper.LevelValidationRegex().IsMatch(val))
        {
            videoRequest.Level = val;
        }
    }

    private static void ApplyMaxRefFrames(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.MaxRefFrames = int.Parse(val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyMaxVideoBitDepth(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.MaxVideoBitDepth = int.Parse(val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyProfile(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null && IsValidCodecName(val))
        {
            videoRequest.Profile = val;
        }
    }

    private static void ApplyCopyTimestamps(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.CopyTimestamps = string.Equals("true", val, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void ApplyEnableSubtitlesInManifest(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.EnableSubtitlesInManifest = string.Equals("true", val, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void ApplyRequireAvc(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.RequireAvc = string.Equals("true", val, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void ApplySubtitleCodec(StreamingRequestDto request, string val)
    {
        if (IsValidCodecName(val))
        {
            request.SubtitleCodec = val;
        }
    }

    private static void ApplyRequireNonAnamorphic(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.RequireNonAnamorphic = string.Equals("true", val, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void ApplyDeInterlace(VideoRequestDto? videoRequest, string val)
    {
        if (videoRequest is not null)
        {
            videoRequest.DeInterlace = string.Equals("true", val, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void ApplySubtitleMethod(VideoRequestDto? videoRequest, string val)
    {
        if (!string.IsNullOrWhiteSpace(val) && videoRequest is not null)
        {
            if (Enum.TryParse(val, out SubtitleDeliveryMethod method))
            {
                videoRequest.SubtitleMethod = method;
            }
        }
    }

    private static bool IsValidCodecName(string val)
    {
        return EncodingHelper.ContainerValidationRegex().IsMatch(val);
    }
}
