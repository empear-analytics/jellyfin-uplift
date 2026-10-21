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

            ApplyParam(new StreamingParam(request, videoRequest, i, val));
        }
    }

    private static void ApplyParam(StreamingParam parameter)
    {
        switch (parameter.I)
        {
            case 0:
                // DeviceProfileId
                break;
            case 1:
                parameter.Request.DeviceId = parameter.Val;
                break;
            case 2:
                parameter.Request.MediaSourceId = parameter.Val;
                break;
            case 3:
                parameter.Request.Static = string.Equals("true", parameter.Val, StringComparison.OrdinalIgnoreCase);
                break;
            case 4:
                ApplyVideoCodec(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            case 5:
                ApplyAudioCodec(parameter.Request, parameter.Val);
                break;
            default:
                StreamingParamApplyRest1(parameter);
                break;
        }
    }

    private static void StreamingParamApplyRest1(StreamingParam parameter)
    {
        switch (parameter.I)
        {
            case 6:
                ApplyAudioStreamIndex(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            case 7:
                ApplySubtitleStreamIndex(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            case 8:
                ApplyVideoBitRate(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            case 9:
                parameter.Request.AudioBitRate = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
                break;
            case 10:
                parameter.Request.MaxAudioChannels = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
                break;
            case 11:
                ApplyMaxFramerate(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            default:
                StreamingParamApplyRest2(parameter);
                break;
        }
    }

    private static void StreamingParamApplyRest2(StreamingParam parameter)
    {
        switch (parameter.I)
        {
            case 12:
                ApplyMaxWidth(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            case 13:
                ApplyMaxHeight(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            case 14:
                parameter.Request.StartTimeTicks = long.Parse(parameter.Val, CultureInfo.InvariantCulture);
                break;
            case 15:
                ApplyLevel(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            case 16:
                ApplyMaxRefFrames(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            case 17:
                ApplyMaxVideoBitDepth(new VideoRequestParam(parameter.VideoRequest, parameter.Val));
                break;
            default:
                StreamingParamApplyRest3(parameter);
                break;
        }
    }

    private static void StreamingParamApplyRest3(StreamingParam parameter)
    {
        switch (parameter.I)
        {
            case 18:
                ApplyProfile(parameter.VideoRequest, parameter.Val);
                break;
            case 19:
                // cabac no longer used
                break;
            case 20:
                parameter.Request.PlaySessionId = parameter.Val;
                break;
            case 21:
                // api_key
                break;
            case 22:
                parameter.Request.LiveStreamId = parameter.Val;
                break;
            case 23:
                // Duplicating ItemId because of MediaMonkey
                break;
            case 24:
                ApplyCopyTimestamps(parameter.VideoRequest, parameter.Val);
                break;
            case 25:
                ApplySubtitleMethod(parameter.VideoRequest, parameter.Val);
                break;
            case 26:
                parameter.Request.TranscodingMaxAudioChannels = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
                break;
            case 27:
                ApplyEnableSubtitlesInManifest(parameter.VideoRequest, parameter.Val);
                break;
            case 28:
                parameter.Request.Tag = parameter.Val;
                break;
            case 29:
                ApplyRequireAvc(parameter.VideoRequest, parameter.Val);
                break;
            case 30:
                ApplySubtitleCodec(parameter.Request, parameter.Val);
                break;
            case 31:
                ApplyRequireNonAnamorphic(parameter.VideoRequest, parameter.Val);
                break;
            case 32:
                ApplyDeInterlace(parameter.VideoRequest, parameter.Val);
                break;
            case 33:
                parameter.Request.TranscodeReasons = parameter.Val;
                break;
        }
    }

    private static void ApplyVideoCodec(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null && IsValidCodecName(parameter.Val))
        {
            parameter.VideoRequest.VideoCodec = parameter.Val;
        }
    }

    private static void ApplyAudioCodec(StreamingRequestDto request, string val)
    {
        if (IsValidCodecName(val))
        {
            request.AudioCodec = val;
        }
    }

    private static void ApplyAudioStreamIndex(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null)
        {
            parameter.VideoRequest.AudioStreamIndex = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplySubtitleStreamIndex(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null)
        {
            parameter.VideoRequest.SubtitleStreamIndex = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyVideoBitRate(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null)
        {
            parameter.VideoRequest.VideoBitRate = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyMaxFramerate(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null)
        {
            parameter.VideoRequest.MaxFramerate = float.Parse(parameter.Val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyMaxWidth(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null)
        {
            parameter.VideoRequest.MaxWidth = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyMaxHeight(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null)
        {
            parameter.VideoRequest.MaxHeight = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyLevel(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null && EncodingHelper.LevelValidationRegex().IsMatch(parameter.Val))
        {
            parameter.VideoRequest.Level = parameter.Val;
        }
    }

    private static void ApplyMaxRefFrames(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null)
        {
            parameter.VideoRequest.MaxRefFrames = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyMaxVideoBitDepth(VideoRequestParam parameter)
    {
        if (parameter.VideoRequest is not null)
        {
            parameter.VideoRequest.MaxVideoBitDepth = int.Parse(parameter.Val, CultureInfo.InvariantCulture);
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

    private readonly record struct StreamingParam(StreamingRequestDto Request, VideoRequestDto? VideoRequest, int I, string Val);

    private readonly record struct VideoRequestParam(VideoRequestDto? VideoRequest, string Val);
}
