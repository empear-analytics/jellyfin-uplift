using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// The streaming helpers.
/// </summary>
public static partial class StreamingHelpers
{
    /// <summary>
    /// Gets the current streaming state.
    /// </summary>
    /// <param name="request">The <see cref="StreamingRequestDto"/> and the <see cref="HttpContext"/>.</param>
    /// <param name="services">The services the streaming state is built with.</param>
    /// <param name="transcodingJobType">The <see cref="TranscodingJobType"/>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="Task"/> containing the current <see cref="StreamState"/>.</returns>
    public static async Task<StreamState> GetStreamingState(
        StreamingRequestContext request,
        StreamingServices services,
        TranscodingJobType transcodingJobType,
        CancellationToken cancellationToken)
    {
        var httpRequest = request.HttpContext.Request;
        if (!string.IsNullOrWhiteSpace(request.StreamingRequest.Params))
        {
            ParseParams(request.StreamingRequest);
        }

        request.StreamingRequest.StreamOptions = ParseStreamOptions(httpRequest.Query);
        var url = GetRequestedUrl(httpRequest);

        if (string.IsNullOrEmpty(request.StreamingRequest.AudioCodec))
        {
            request.StreamingRequest.AudioCodec = services.EncodingHelper.InferAudioCodec(url);
        }

        var state = new StreamState(services.MediaSourceManager, transcodingJobType, services.TranscodeManager)
        {
            Request = request.StreamingRequest,
            RequestedUrl = url,
            UserAgent = httpRequest.Headers[HeaderNames.UserAgent]
        };

        ApplyUser(state, request.HttpContext, services.UserManager);

        ApplySupportedCodecs(state, request.StreamingRequest, services.MediaEncoder);

        var item = services.LibraryManager.GetItemById<BaseItem>(request.StreamingRequest.Id)
            ?? throw new ResourceNotFoundException();

        state.IsInputVideo = item.MediaType == MediaType.Video;

        MediaSourceInfo? mediaSource = null;
        if (string.IsNullOrWhiteSpace(request.StreamingRequest.LiveStreamId))
        {
            mediaSource = GetTranscodingJobMediaSource(mediaSource, request.StreamingRequest, services.TranscodeManager);

            if (mediaSource is null)
            {
                mediaSource = await GetPlaybackMediaSource(request.StreamingRequest, services.MediaSourceManager, services.LibraryManager, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            mediaSource = await GetLiveStreamMediaSource(state, request.StreamingRequest, services.MediaSourceManager, cancellationToken).ConfigureAwait(false);
        }

        var encodingOptions = services.ServerConfigurationManager.GetEncodingOptions();

        services.EncodingHelper.AttachMediaSourceInfo(state, encodingOptions, mediaSource, url);

        string? containerInternal = GetOutputContainer(state, request.StreamingRequest, mediaSource);

        var outputAudioCodec = request.StreamingRequest.AudioCodec;
        state.OutputAudioCodec = outputAudioCodec;
        state.OutputContainer = (containerInternal ?? string.Empty).TrimStart('.');
        state.OutputAudioChannels = services.EncodingHelper.GetNumAudioChannelsParam(state, state.AudioStream, state.OutputAudioCodec);
        ApplyOutputAudioBitrate(state, request.StreamingRequest, services.EncodingHelper, outputAudioCodec);

        ApplyVideoOutputSettings(state, services.EncodingHelper, encodingOptions, services.MediaEncoder);

        var ext = string.IsNullOrWhiteSpace(state.OutputContainer)
            ? GetOutputFileExtension(state, mediaSource)
            : ("." + GetContainerFileExtension(state.OutputContainer));

        state.OutputFilePath = GetOutputFilePath(state, new OutputFileLocation(ext, services.ServerConfigurationManager), request.StreamingRequest.DeviceId, request.StreamingRequest.PlaySessionId);

        return state;
    }

    private static string GetRequestedUrl(HttpRequest httpRequest)
    {
        if (httpRequest.Path.Value is null)
        {
            throw new ResourceNotFoundException(nameof(httpRequest.Path));
        }

        var url = httpRequest.Path.Value.AsSpan().RightPart('.').ToString();
        return url;
    }

    private static void ApplyUser(StreamState state, HttpContext httpContext, IUserManager userManager)
    {
        var userId = httpContext.User.GetUserId();
        if (!userId.IsEmpty())
        {
            state.User = userManager.GetUserById(userId);
        }
    }

    private static MediaSourceInfo? GetTranscodingJobMediaSource(MediaSourceInfo? mediaSource, StreamingRequestDto streamingRequest, ITranscodeManager transcodeManager)
    {
        var currentJob = !string.IsNullOrWhiteSpace(streamingRequest.PlaySessionId)
            ? transcodeManager.GetTranscodingJob(streamingRequest.PlaySessionId)
            : null;

        if (currentJob is not null)
        {
            mediaSource = currentJob.MediaSource;
        }

        return mediaSource;
    }

    private static void ApplySupportedCodecs(StreamState state, StreamingRequestDto streamingRequest, IMediaEncoder mediaEncoder)
    {
        if (state.IsVideoRequest && !string.IsNullOrWhiteSpace(state.Request.VideoCodec))
        {
            state.SupportedVideoCodecs = state.Request.VideoCodec.Split(',', StringSplitOptions.RemoveEmptyEntries);
            state.Request.VideoCodec = state.SupportedVideoCodecs.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(streamingRequest.AudioCodec))
        {
            state.SupportedAudioCodecs = streamingRequest.AudioCodec.Split(',', StringSplitOptions.RemoveEmptyEntries);
            state.Request.AudioCodec = state.SupportedAudioCodecs.FirstOrDefault(mediaEncoder.CanEncodeToAudioCodec)
                                       ?? state.SupportedAudioCodecs.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(streamingRequest.SubtitleCodec))
        {
            state.SupportedSubtitleCodecs = streamingRequest.SubtitleCodec.Split(',', StringSplitOptions.RemoveEmptyEntries);
            state.Request.SubtitleCodec = state.SupportedSubtitleCodecs.FirstOrDefault(mediaEncoder.CanEncodeToSubtitleCodec)
                                          ?? state.SupportedSubtitleCodecs.FirstOrDefault();
        }
    }

    private static void ApplyOutputAudioBitrate(StreamState state, StreamingRequestDto streamingRequest, EncodingHelper encodingHelper, string? outputAudioCodec)
    {
        if (EncodingHelper.LosslessAudioCodecs.Contains(outputAudioCodec))
        {
            state.OutputAudioBitrate = state.AudioStream.BitRate ?? 0;
        }
        else
        {
            state.OutputAudioBitrate = encodingHelper.GetAudioBitrateParam(streamingRequest.AudioBitRate, streamingRequest.AudioCodec, state.AudioStream, state.OutputAudioChannels) ?? 0;
        }
    }

    private static async Task<MediaSourceInfo?> GetPlaybackMediaSource(
        StreamingRequestDto streamingRequest,
        IMediaSourceManager mediaSourceManager,
        ILibraryManager libraryManager,
        CancellationToken cancellationToken)
    {
        var mediaSources = await mediaSourceManager.GetPlaybackMediaSources(libraryManager.GetItemById<BaseItem>(streamingRequest.Id), null, false, false, cancellationToken).ConfigureAwait(false);

        MediaSourceInfo? mediaSource = string.IsNullOrEmpty(streamingRequest.MediaSourceId)
            ? mediaSources[0]
            : mediaSources.FirstOrDefault(i => string.Equals(i.Id, streamingRequest.MediaSourceId, StringComparison.Ordinal));

        if (mediaSource is null && Guid.Parse(streamingRequest.MediaSourceId).Equals(streamingRequest.Id))
        {
            mediaSource = mediaSources[0];
        }

        return mediaSource;
    }

    private static async Task<MediaSourceInfo?> GetLiveStreamMediaSource(
        StreamState state,
        StreamingRequestDto streamingRequest,
        IMediaSourceManager mediaSourceManager,
        CancellationToken cancellationToken)
    {
        var liveStreamInfo = await mediaSourceManager.GetLiveStreamWithDirectStreamProvider(streamingRequest.LiveStreamId, cancellationToken).ConfigureAwait(false);
        MediaSourceInfo? mediaSource = liveStreamInfo.Item1;
        state.DirectStreamProvider = liveStreamInfo.Item2;

        // The requested live stream is no longer open. This commonly happens when a client keeps
        // polling the HLS playlist (e.g. live.m3u8) after the stream was disposed because its
        // consumer count dropped to zero. GetLiveStreamWithDirectStreamProvider returns a null
        // MediaSource in that case, so return 404 instead of dereferencing it below.
        if (mediaSource is null)
        {
            throw new ResourceNotFoundException($"The live stream with id {streamingRequest.LiveStreamId} could not be found or is no longer available.");
        }

        // Cap the max bitrate when it is too high. This is usually due to ffmpeg is unable to probe the source liveTV streams' bitrate.
        if (mediaSource.FallbackMaxStreamingBitrate is not null && streamingRequest.VideoBitRate is not null)
        {
            streamingRequest.VideoBitRate = Math.Min(streamingRequest.VideoBitRate.Value, mediaSource.FallbackMaxStreamingBitrate.Value);
        }

        return mediaSource;
    }

    private static string? GetOutputContainer(StreamState state, StreamingRequestDto streamingRequest, MediaSourceInfo? mediaSource)
    {
        string? containerInternal = Path.GetExtension(state.RequestedUrl);

        if (IsMissingLiveStreamContainer(containerInternal, streamingRequest, mediaSource))
        {
            containerInternal = ".ts";
        }

        if (!string.IsNullOrEmpty(streamingRequest.Container))
        {
            containerInternal = streamingRequest.Container;
        }

        if (string.IsNullOrEmpty(containerInternal))
        {
            containerInternal = streamingRequest.Static ?
                StreamBuilder.NormalizeMediaSourceFormatIntoSingleContainer(state.InputContainer, null, DlnaProfileType.Audio)
                : GetOutputFileExtension(state, mediaSource);
        }

        return containerInternal;
    }

    private static bool IsMissingLiveStreamContainer(string? containerInternal, StreamingRequestDto streamingRequest, MediaSourceInfo? mediaSource)
    {
        return string.IsNullOrEmpty(containerInternal)
            && (!string.IsNullOrWhiteSpace(streamingRequest.LiveStreamId)
                || (mediaSource != null && mediaSource.IsInfiniteStream));
    }

    private static void ApplyVideoOutputSettings(StreamState state, EncodingHelper encodingHelper, EncodingOptions encodingOptions, IMediaEncoder mediaEncoder)
    {
        if (state.VideoRequest is null)
        {
            return;
        }

        state.OutputVideoCodec = state.Request.VideoCodec;
        state.OutputVideoBitrate = encodingHelper.GetVideoBitrateParamValue(state.VideoRequest, state.VideoStream, state.OutputVideoCodec);

        encodingHelper.TryStreamCopy(state, encodingOptions);

        if (!EncodingHelper.IsCopyCodec(state.OutputVideoCodec) && state.OutputVideoBitrate.HasValue)
        {
            var isVideoResolutionNotRequested = IsVideoResolutionNotRequested(state.VideoRequest);

            if (isVideoResolutionNotRequested
                && IsRequestedBitrateAtLeastSource(state, state.VideoRequest))
            {
                // Don't downscale the resolution if the width/height/MaxWidth/MaxHeight is not requested,
                // and the requested video bitrate is greater than source video bitrate.
                if (HasSourceVideoDimensions(state))
                {
                    state.VideoRequest.MaxWidth = state.VideoStream?.Width;
                    state.VideoRequest.MaxHeight = state.VideoStream?.Height;
                }
            }
            else
            {
                var h264EquivalentBitrate = EncodingHelper.ScaleBitrate(
                    state.OutputVideoBitrate.Value,
                    state.ActualOutputVideoCodec,
                    "h264");
                var resolution = ResolutionNormalizer.Normalize(
                    state.VideoStream?.BitRate,
                    state.OutputVideoBitrate.Value,
                    h264EquivalentBitrate,
                    state.VideoRequest.MaxWidth,
                    state.VideoRequest.MaxHeight,
                    state.TargetFramerate);

                state.VideoRequest.MaxWidth = resolution.MaxWidth;
                state.VideoRequest.MaxHeight = resolution.MaxHeight;
            }
        }

        if (IsAudioReencodedToSameCodec(state))
        {
            state.OutputAudioCodec = state.SupportedAudioCodecs.Where(c => !EncodingHelper.LosslessAudioCodecs.Contains(c)).FirstOrDefault(mediaEncoder.CanEncodeToAudioCodec);
        }
    }

    private static bool HasSourceVideoDimensions(StreamState state)
    {
        return state.VideoStream.Width.HasValue || state.VideoStream.Height.HasValue;
    }

    private static bool IsVideoResolutionNotRequested(VideoRequestDto videoRequest)
    {
        return !videoRequest.Width.HasValue
            && !videoRequest.Height.HasValue
            && !videoRequest.MaxWidth.HasValue
            && !videoRequest.MaxHeight.HasValue;
    }

    private static bool IsRequestedBitrateAtLeastSource(StreamState state, VideoRequestDto videoRequest)
    {
        return state.VideoStream is not null
            && videoRequest.VideoBitRate.HasValue
            && state.VideoStream.BitRate.HasValue
            && videoRequest.VideoBitRate.Value >= state.VideoStream.BitRate.Value;
    }

    private static bool IsAudioReencodedToSameCodec(StreamState state)
    {
        return state.AudioStream is not null && !EncodingHelper.IsCopyCodec(state.OutputAudioCodec) && string.Equals(state.AudioStream.Codec, state.OutputAudioCodec, StringComparison.OrdinalIgnoreCase) && state.OutputAudioBitrate.HasValue;
    }

    /// <summary>
    /// Parses query parameters as StreamOptions.
    /// </summary>
    /// <param name="queryString">The query string.</param>
    /// <returns>A <see cref="Dictionary{String,String}"/> containing the stream options.</returns>
    private static Dictionary<string, string?> ParseStreamOptions(IQueryCollection queryString)
    {
        Dictionary<string, string?> streamOptions = new Dictionary<string, string?>();
        foreach (var param in queryString)
        {
            if (param.Key.Length > 0 && char.IsLower(param.Key[0]))
            {
                // This was probably not parsed initially and should be a StreamOptions
                // or the generated URL should correctly serialize it
                // TODO: This should be incorporated either in the lower framework for parsing requests
                streamOptions[param.Key] = param.Value;
            }
        }

        return streamOptions;
    }

    /// <summary>
    /// Gets the output file extension.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="mediaSource">The mediaSource.</param>
    /// <returns>System.String.</returns>
    private static string GetOutputFileExtension(StreamState state, MediaSourceInfo? mediaSource)
    {
        var ext = Path.GetExtension(state.RequestedUrl);
        if (!string.IsNullOrEmpty(ext))
        {
            return ext;
        }

        return GetCodecExtension(state, mediaSource);
    }

    private static string GetCodecExtension(StreamState state, MediaSourceInfo? mediaSource)
    {
        // Try to infer based on the desired video codec
        if (state.IsVideoRequest)
        {
            var videoCodec = state.Request.VideoCodec;

            if (string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase))
            {
                return ".ts";
            }

            if (IsHevcOrAv1Codec(videoCodec))
            {
                return ".mp4";
            }

            if (string.Equals(videoCodec, "theora", StringComparison.OrdinalIgnoreCase))
            {
                return ".ogv";
            }

            if (IsVpxCodec(videoCodec))
            {
                return ".webm";
            }

            if (string.Equals(videoCodec, "wmv", StringComparison.OrdinalIgnoreCase))
            {
                return ".asf";
            }
        }
        else
        {
            return GetAudioCodecExtension(state, mediaSource);
        }

        return GetContainerExtension(mediaSource);
    }

    private static string GetAudioCodecExtension(StreamState state, MediaSourceInfo? mediaSource)
    {
        // Try to infer based on the desired audio codec
        var audioCodec = state.Request.AudioCodec;

        if (string.Equals("aac", audioCodec, StringComparison.OrdinalIgnoreCase))
        {
            return ".aac";
        }

        if (string.Equals("mp3", audioCodec, StringComparison.OrdinalIgnoreCase))
        {
            return ".mp3";
        }

        if (string.Equals("vorbis", audioCodec, StringComparison.OrdinalIgnoreCase))
        {
            return ".ogg";
        }

        if (string.Equals("wma", audioCodec, StringComparison.OrdinalIgnoreCase))
        {
            return ".wma";
        }

        return GetContainerExtension(mediaSource);
    }

    private static string GetContainerExtension(MediaSourceInfo? mediaSource)
    {
        // Fallback to the container of mediaSource
        if (!string.IsNullOrEmpty(mediaSource?.Container))
        {
            var idx = mediaSource.Container.IndexOf(',', StringComparison.OrdinalIgnoreCase);
            return '.' + (idx == -1 ? mediaSource.Container : mediaSource.Container[..idx]).Trim();
        }

        throw new InvalidOperationException("Failed to find an appropriate file extension");
    }

    private static bool IsHevcOrAv1Codec(string? videoCodec)
    {
        return string.Equals(videoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(videoCodec, "av1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVpxCodec(string? videoCodec)
    {
        return string.Equals(videoCodec, "vp8", StringComparison.OrdinalIgnoreCase)
            || string.Equals(videoCodec, "vp9", StringComparison.OrdinalIgnoreCase)
            || string.Equals(videoCodec, "vpx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the output file path for transcoding.
    /// </summary>
    /// <param name="state">The current <see cref="StreamState"/>.</param>
    /// <param name="location">The file extension of the output file and the configuration that holds the transcode folder.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="playSessionId">The play session id.</param>
    /// <returns>The complete file path, including the folder, for the transcoding file.</returns>
    private static string GetOutputFilePath(StreamState state, OutputFileLocation location, string? deviceId, string? playSessionId)
    {
        var data = $"{state.MediaPath}-{state.UserAgent}-{deviceId!}-{playSessionId!}";

        var filename = data.GetMD5().ToString("N", CultureInfo.InvariantCulture);
        var ext = location.OutputFileExtension.ToLowerInvariant();
        var folder = location.ServerConfigurationManager.GetTranscodePath();

        return Path.Combine(folder, filename + ext);
    }

    /// <summary>
    /// Parses the container into its file extension.
    /// </summary>
    /// <param name="container">The container.</param>
    private static string? GetContainerFileExtension(string? container)
    {
        if (string.Equals(container, "mpegts", StringComparison.OrdinalIgnoreCase))
        {
            return "ts";
        }

        if (string.Equals(container, "matroska", StringComparison.OrdinalIgnoreCase))
        {
            return "mkv";
        }

        return container;
    }

#pragma warning disable CA1034 // Parameter object of GetStreamingState; nested to keep it with its only user
    /// <summary>
    /// The streaming request and the HTTP context it arrived with.
    /// </summary>
    /// <param name="StreamingRequest">The <see cref="StreamingRequestDto"/>.</param>
    /// <param name="HttpContext">The <see cref="HttpContext"/>.</param>
    public readonly record struct StreamingRequestContext(StreamingRequestDto StreamingRequest, HttpContext HttpContext);

    /// <summary>
    /// The services a streaming state is built with.
    /// </summary>
    /// <param name="MediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="UserManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="LibraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="ServerConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="MediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="EncodingHelper">Instance of <see cref="EncodingHelper"/>.</param>
    /// <param name="TranscodeManager">Instance of the <see cref="ITranscodeManager"/> interface.</param>
    public readonly record struct StreamingServices(IMediaSourceManager MediaSourceManager, IUserManager UserManager, ILibraryManager LibraryManager, IServerConfigurationManager ServerConfigurationManager, IMediaEncoder MediaEncoder, EncodingHelper EncodingHelper, ITranscodeManager TranscodeManager);
#pragma warning restore CA1034

    private readonly record struct OutputFileLocation(string OutputFileExtension, IServerConfigurationManager ServerConfigurationManager);
}
