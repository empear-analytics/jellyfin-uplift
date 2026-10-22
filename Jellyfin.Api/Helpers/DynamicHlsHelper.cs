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

/// <summary>
/// Dynamic hls helper.
/// </summary>
public partial class DynamicHlsHelper
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ITranscodeManager _transcodeManager;
    private readonly INetworkManager _networkManager;
    private readonly ILogger<DynamicHlsHelper> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly EncodingHelper _encodingHelper;
    private readonly ITrickplayManager _trickplayManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicHlsHelper"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="transcodeManager">Instance of <see cref="ITranscodeManager"/>.</param>
    /// <param name="networkManager">Instance of the <see cref="INetworkManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DynamicHlsHelper}"/> interface.</param>
    /// <param name="httpContextAccessor">Instance of the <see cref="IHttpContextAccessor"/> interface.</param>
    /// <param name="encodingHelper">Instance of <see cref="EncodingHelper"/>.</param>
    /// <param name="trickplayManager">Instance of <see cref="ITrickplayManager"/>.</param>
    public DynamicHlsHelper(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IMediaSourceManager mediaSourceManager,
        IServerConfigurationManager serverConfigurationManager,
        IMediaEncoder mediaEncoder,
        ITranscodeManager transcodeManager,
        INetworkManager networkManager,
        ILogger<DynamicHlsHelper> logger,
        IHttpContextAccessor httpContextAccessor,
        EncodingHelper encodingHelper,
        ITrickplayManager trickplayManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _mediaSourceManager = mediaSourceManager;
        _serverConfigurationManager = serverConfigurationManager;
        _mediaEncoder = mediaEncoder;
        _transcodeManager = transcodeManager;
        _networkManager = networkManager;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _encodingHelper = encodingHelper;
        _trickplayManager = trickplayManager;
    }

    /// <summary>
    /// Get master hls playlist.
    /// </summary>
    /// <param name="transcodingJobType">Transcoding job type.</param>
    /// <param name="streamingRequest">Streaming request dto.</param>
    /// <param name="enableAdaptiveBitrateStreaming">Enable adaptive bitrate streaming.</param>
    /// <returns>A <see cref="Task"/> containing the resulting <see cref="ActionResult"/>.</returns>
    public async Task<ActionResult> GetMasterHlsPlaylist(
        TranscodingJobType transcodingJobType,
        StreamingRequestDto streamingRequest,
        bool enableAdaptiveBitrateStreaming)
    {
        var isHeadRequest = _httpContextAccessor.HttpContext?.Request.Method == WebRequestMethods.Http.Head;
        // CTS lifecycle is managed internally.
        var cancellationTokenSource = new CancellationTokenSource();
        return await GetMasterPlaylistInternal(
            streamingRequest,
            new MasterPlaylistParts(isHeadRequest, enableAdaptiveBitrateStreaming),
            transcodingJobType,
            cancellationTokenSource).ConfigureAwait(false);
    }

    private async Task<ActionResult> GetMasterPlaylistInternal(
        StreamingRequestDto streamingRequest,
        MasterPlaylistParts parts,
        TranscodingJobType transcodingJobType,
        CancellationTokenSource cancellationTokenSource)
    {
        if (_httpContextAccessor.HttpContext is null)
        {
            throw new ResourceNotFoundException(nameof(_httpContextAccessor.HttpContext));
        }

        using var state = await StreamingHelpers.GetStreamingState(
                new StreamingHelpers.StreamingRequestContext(streamingRequest, _httpContextAccessor.HttpContext),
                new StreamingHelpers.StreamingServices(_mediaSourceManager, _userManager, _libraryManager, _serverConfigurationManager, _mediaEncoder, _encodingHelper, _transcodeManager),
                transcodingJobType,
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        _httpContextAccessor.HttpContext.Response.Headers.Append(HeaderNames.Expires, "0");
        if (parts.IsHeadRequest)
        {
            return new FileContentResult(Array.Empty<byte>(), MimeTypes.GetMimeType("playlist.m3u8"));
        }

        var totalBitrate = GetTotalBitrate(state);

        var builder = new StringBuilder();

        builder.AppendLine("#EXTM3U");

        var isLiveStream = state.IsSegmentedLiveStream;

        var queryString = GetPlaylistQueryString(state, _httpContextAccessor.HttpContext.Request);

        // Main stream
        var baseUrl = isLiveStream ? "live.m3u8" : "main.m3u8";
        var playlistUrl = baseUrl + queryString;
        var playlistQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(queryString);

        var subtitleStreams = state.MediaSource
            .MediaStreams
            .Where(i => i.IsTextSubtitleStream)
            .ToList();

        var subtitleGroup = GetSubtitleGroup(state, subtitleStreams);

        if (!string.IsNullOrWhiteSpace(subtitleGroup))
        {
            AddSubtitles(state, subtitleStreams, builder, _httpContextAccessor.HttpContext.User);
        }

        // For DoVi profiles without a compatible base layer (P5 HEVC, P10/bl0 AV1),
        // add a spec-compliant dvh1/dav1 variant before the hvc1 hack variant.
        // SUPPLEMENTAL-CODECS cannot be used for these profiles (no compatible BL to supplement).
        // The DoVi variant is listed first so spec-compliant clients (Apple TV, webOS 24+)
        // select it over the fallback when both have identical BANDWIDTH.
        // Only emit for clients that explicitly declared DOVI support to avoid breaking
        // non-compliant players that don't recognize dvh1/dav1 CODECS strings.
        if (IsRequestedDoviCopyWithKnownProfileAndLevel(state))
        {
            AppendDoviPlaylist(builder, state, new PlaylistVariantParts(playlistUrl, totalBitrate, subtitleGroup));
        }

        var basicPlaylist = AppendPlaylist(builder, state, new PlaylistVariantParts(playlistUrl, totalBitrate, subtitleGroup));

        AppendCompatibilityPlaylists(builder, state, new VariantPlaylistsParts(playlistQuery, baseUrl, totalBitrate, subtitleGroup), basicPlaylist);

        if (EnableAdaptiveBitrateStreaming(state, isLiveStream, parts.EnableAdaptiveBitrateStreaming, _httpContextAccessor.HttpContext.GetNormalizedRemoteIP()))
        {
            AppendBitrateVariantPlaylists(builder, state, new VariantPlaylistsParts(playlistQuery, baseUrl, totalBitrate, subtitleGroup));
        }

        await AddTrickplayIfEnabled(state, isLiveStream, builder, _httpContextAccessor.HttpContext.User).ConfigureAwait(false);

        return new FileContentResult(Encoding.UTF8.GetBytes(builder.ToString()), MimeTypes.GetMimeType("playlist.m3u8"));
    }

    private void AppendBitrateVariantPlaylists(StringBuilder builder, StreamState state, VariantPlaylistsParts parts)
    {
        var requestedVideoBitrate = state.VideoRequest?.VideoBitRate ?? 0;

        // By default, vary by just 200k
        var variation = GetBitrateVariation(parts.TotalBitrate);

        var newBitrate = parts.TotalBitrate - variation;
        var variantQuery = parts.PlaylistQuery;
        variantQuery["VideoBitrate"] = (requestedVideoBitrate - variation).ToString(CultureInfo.InvariantCulture);
        var variantUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(parts.BaseUrl, variantQuery);
        AppendPlaylist(builder, state, new PlaylistVariantParts(variantUrl, newBitrate, parts.SubtitleGroup));

        variation *= 2;
        newBitrate = parts.TotalBitrate - variation;
        variantQuery["VideoBitrate"] = (requestedVideoBitrate - variation).ToString(CultureInfo.InvariantCulture);
        variantUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(parts.BaseUrl, variantQuery);
        AppendPlaylist(builder, state, new PlaylistVariantParts(variantUrl, newBitrate, parts.SubtitleGroup));
    }

    private static int GetTotalBitrate(StreamState state)
    {
        return (state.OutputAudioBitrate ?? 0) + (state.OutputVideoBitrate ?? 0);
    }

    private void AppendCompatibilityPlaylists(StringBuilder builder, StreamState state, VariantPlaylistsParts parts, StringBuilder basicPlaylist)
    {
        if (state.VideoStream is not null && state.VideoRequest is not null)
        {
            var encodingOptions = _serverConfigurationManager.GetEncodingOptions();

            AppendSdrCompatibilityPlaylists(builder, state, parts, encodingOptions);

            AppendH264CompatibilityPlaylist(builder, state, parts, state.VideoStream);

            AppendLevel150CompatibilityPlaylist(builder, state, basicPlaylist);
        }
    }

    private void AppendSdrCompatibilityPlaylists(StringBuilder builder, StreamState state, VariantPlaylistsParts parts, EncodingOptions encodingOptions)
    {
        // Provide AV1 and HEVC SDR entrances for backward compatibility.
        foreach (var sdrVideoCodec in new[] { "av1", "hevc" })
        {
            var isEncodingAllowed = IsSdrEncodingAllowed(encodingOptions, sdrVideoCodec, state);

            if (IsHdrCopyWithEncodingAllowed(isEncodingAllowed, state))
            {
                // Force AV1 and HEVC Main Profile and disable video stream copy.
                state.OutputVideoCodec = sdrVideoCodec;

                var sdrPlaylistQuery = parts.PlaylistQuery;
                sdrPlaylistQuery["VideoCodec"] = sdrVideoCodec;
                sdrPlaylistQuery[sdrVideoCodec + "-profile"] = "main";
                sdrPlaylistQuery["AllowVideoStreamCopy"] = "false";

                var sdrVideoUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(parts.BaseUrl, sdrPlaylistQuery);

                // HACK: Use the same bitrate so that the client can choose by other attributes, such as color range.
                AppendPlaylist(builder, state, new PlaylistVariantParts(sdrVideoUrl, parts.TotalBitrate, parts.SubtitleGroup));

                // Restore the video codec
                state.OutputVideoCodec = "copy";
            }
        }
    }

    private void AppendH264CompatibilityPlaylist(StringBuilder builder, StreamState state, VariantPlaylistsParts parts, MediaStream videoStream)
    {
        // Provide H.264 SDR entrance for backward compatibility.
        if (EncodingHelper.IsCopyCodec(state.OutputVideoCodec)
            && videoStream.VideoRange == VideoRange.HDR)
        {
            // Force H.264 and disable video stream copy.
            state.OutputVideoCodec = "h264";

            var sdrPlaylistQuery = parts.PlaylistQuery;
            sdrPlaylistQuery["VideoCodec"] = "h264";
            sdrPlaylistQuery["AllowVideoStreamCopy"] = "false";

            var sdrVideoUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(parts.BaseUrl, sdrPlaylistQuery);

            // HACK: Use the same bitrate so that the client can choose by other attributes, such as color range.
            AppendPlaylist(builder, state, new PlaylistVariantParts(sdrVideoUrl, parts.TotalBitrate, parts.SubtitleGroup));

            // Restore the video codec
            state.OutputVideoCodec = "copy";
        }
    }

    private static bool IsSdrEncodingAllowed(EncodingOptions encodingOptions, string sdrVideoCodec, StreamState state)
    {
        var isAv1EncodingAllowed = encodingOptions.AllowAv1Encoding
            && string.Equals(sdrVideoCodec, "av1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(state.ActualOutputVideoCodec, "av1", StringComparison.OrdinalIgnoreCase);
        var isHevcEncodingAllowed = encodingOptions.AllowHevcEncoding
            && string.Equals(sdrVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
            && string.Equals(state.ActualOutputVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase);
        return isAv1EncodingAllowed || isHevcEncodingAllowed;
    }

    private async Task AddTrickplayIfEnabled(StreamState state, bool isLiveStream, StringBuilder builder, ClaimsPrincipal user)
    {
        if (!isLiveStream && (state.VideoRequest?.EnableTrickplay ?? false))
        {
            var sourceId = Guid.Parse(state.Request.MediaSourceId);
            var trickplayResolutions = await _trickplayManager.GetTrickplayResolutions(sourceId).ConfigureAwait(false);
            AddTrickplay(state, trickplayResolutions, builder, user);
        }
    }

    private static string GetPlaylistQueryString(StreamState state, HttpRequest request)
    {
        var queryString = request.QueryString.ToString();

        // from universal audio service, need to override the AudioCodec when the actual request differs from original query
        if (!string.Equals(state.OutputAudioCodec, request.Query["AudioCodec"].ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var newQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(queryString);
            newQuery["AudioCodec"] = state.OutputAudioCodec;
            queryString = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(string.Empty, newQuery);
        }

        // from universal audio service
        if (!string.IsNullOrWhiteSpace(state.Request.SegmentContainer)
            && !queryString.Contains("SegmentContainer", StringComparison.OrdinalIgnoreCase))
        {
            queryString += "&SegmentContainer=" + state.Request.SegmentContainer;
        }

        // from universal audio service
        if (!string.IsNullOrWhiteSpace(state.Request.TranscodeReasons)
            && !queryString.Contains("TranscodeReasons=", StringComparison.OrdinalIgnoreCase))
        {
            queryString += "&TranscodeReasons=" + state.Request.TranscodeReasons;
        }

        // Video rotation metadata is only supported in fMP4 remuxing
        if (IsRotatedVideoCopiedToNonMp4Container(state))
        {
            queryString += "&AllowVideoStreamCopy=false";
        }

        return queryString;
    }

    private static string? GetSubtitleGroup(StreamState state, List<MediaStream> subtitleStreams)
    {
        var subtitleGroup = subtitleStreams.Count > 0 && (state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Hls || state.VideoRequest!.EnableSubtitlesInManifest)
            ? "subs"
            : null;

        // If we're burning in subtitles then don't add additional subs to the manifest
        if (state.SubtitleStream is not null && state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Encode)
        {
            subtitleGroup = null;
        }

        return subtitleGroup;
    }

    private void AppendLevel150CompatibilityPlaylist(StringBuilder builder, StreamState state, StringBuilder basicPlaylist)
    {
        // Provide Level 5.0 entrance for backward compatibility.
        // e.g. Apple A10 chips refuse the master playlist containing SDR HEVC Main Level 5.1 video,
        // but in fact it is capable of playing videos up to Level 6.1.
        if (IsSdrHevcCopyAboveLevel150(state))
        {
            var playlistCodecsField = new StringBuilder();
            AppendPlaylistCodecsField(playlistCodecsField, state);

            // Force the video level to 5.0.
            var originalLevel = state.VideoStream.Level;
            state.VideoStream.Level = 150;
            var newPlaylistCodecsField = new StringBuilder();
            AppendPlaylistCodecsField(newPlaylistCodecsField, state);

            // Restore the video level.
            state.VideoStream.Level = originalLevel;
            var newPlaylist = ReplacePlaylistCodecsField(basicPlaylist, playlistCodecsField, newPlaylistCodecsField);
            builder.Append(newPlaylist);
        }
    }

    private static bool IsRotatedVideoCopiedToNonMp4Container(StreamState state)
    {
        return state.VideoStream is not null
            && state.VideoRequest is not null
            && (state.VideoStream?.Rotation ?? 0) != 0
            && EncodingHelper.IsCopyCodec(state.OutputVideoCodec)
            && !string.IsNullOrWhiteSpace(state.Request.SegmentContainer)
            && !string.Equals(state.Request.SegmentContainer, "mp4", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRequestedDoviCopyWithKnownProfileAndLevel(StreamState state)
    {
        return state.VideoStream is not null
            && state.VideoRequest is not null
            && EncodingHelper.IsCopyCodec(state.OutputVideoCodec)
            && state.VideoStream.VideoRangeType == VideoRangeType.DOVI
            && state.VideoStream.DvProfile.HasValue
            && state.VideoStream.DvLevel.HasValue
            && state.GetRequestedRangeTypes(state.VideoStream.Codec)
                .Contains(VideoRangeType.DOVI.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHdrCopyWithEncodingAllowed(bool isEncodingAllowed, StreamState state)
    {
        return isEncodingAllowed
            && EncodingHelper.IsCopyCodec(state.OutputVideoCodec)
            && state.VideoStream.VideoRange == VideoRange.HDR;
    }

    private static bool IsSdrHevcCopyAboveLevel150(StreamState state)
    {
        return EncodingHelper.IsCopyCodec(state.OutputVideoCodec)
            && state.VideoStream.Level.HasValue
            && state.VideoStream.Level > 150
            && state.VideoStream.VideoRange == VideoRange.SDR
            && string.Equals(state.ActualOutputVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase);
    }

    private StringBuilder AppendPlaylist(StringBuilder builder, StreamState state, PlaylistVariantParts parts)
    {
        var playlistBuilder = new StringBuilder();
        playlistBuilder.Append("#EXT-X-STREAM-INF:BANDWIDTH=")
            .Append(parts.Bitrate.ToString(CultureInfo.InvariantCulture))
            .Append(",AVERAGE-BANDWIDTH=")
            .Append(parts.Bitrate.ToString(CultureInfo.InvariantCulture));

        AppendPlaylistVideoRangeField(playlistBuilder, state);

        AppendPlaylistCodecsField(playlistBuilder, state);

        AppendPlaylistSupplementalCodecsField(playlistBuilder, state);

        AppendPlaylistResolutionField(playlistBuilder, state);

        AppendPlaylistFramerateField(playlistBuilder, state);

        if (!string.IsNullOrWhiteSpace(parts.SubtitleGroup))
        {
            playlistBuilder.Append(",SUBTITLES=\"")
                .Append(parts.SubtitleGroup)
                .Append('"');
        }

        playlistBuilder.Append(Environment.NewLine);
        playlistBuilder.AppendLine(parts.Url);
        builder.Append(playlistBuilder);

        return playlistBuilder;
    }

    /// <summary>
    /// Appends a Dolby Vision variant with dvh1/dav1 CODECS for profiles without a compatible
    /// base layer (P5 HEVC, P10/bl0 AV1). This enables spec-compliant HLS clients to detect
    /// DoVi from the manifest rather than relying on init segment inspection.
    /// </summary>
    /// <param name="builder">StringBuilder for the master playlist.</param>
    /// <param name="state">StreamState of the current stream.</param>
    /// <param name="parts">Playlist URL for this variant, bitrate for the BANDWIDTH field, and subtitle group identifier, or null.</param>
    private void AppendDoviPlaylist(StringBuilder builder, StreamState state, PlaylistVariantParts parts)
    {
        var dvProfile = state.VideoStream.DvProfile;
        var dvLevel = state.VideoStream.DvLevel;
        if (dvProfile is null || dvLevel is null)
        {
            return;
        }

        var playlistBuilder = new StringBuilder();
        playlistBuilder.Append("#EXT-X-STREAM-INF:BANDWIDTH=")
            .Append(parts.Bitrate.ToString(CultureInfo.InvariantCulture))
            .Append(",AVERAGE-BANDWIDTH=")
            .Append(parts.Bitrate.ToString(CultureInfo.InvariantCulture));

        playlistBuilder.Append(",VIDEO-RANGE=PQ");

        var dvCodec = HlsCodecStringHelpers.GetDoviString(dvProfile.Value, dvLevel.Value, state.ActualOutputVideoCodec);

        string audioCodecs = GetDoviPlaylistAudioCodecs(state);

        playlistBuilder.Append(",CODECS=\"")
            .Append(dvCodec);
        if (!string.IsNullOrEmpty(audioCodecs))
        {
            playlistBuilder.Append(',').Append(audioCodecs);
        }

        playlistBuilder.Append('"');

        AppendPlaylistResolutionField(playlistBuilder, state);
        AppendPlaylistFramerateField(playlistBuilder, state);

        if (!string.IsNullOrWhiteSpace(parts.SubtitleGroup))
        {
            playlistBuilder.Append(",SUBTITLES=\"")
                .Append(parts.SubtitleGroup)
                .Append('"');
        }

        playlistBuilder.AppendLine();
        playlistBuilder.AppendLine(parts.Url);
        builder.Append(playlistBuilder);
    }

    private string GetDoviPlaylistAudioCodecs(StreamState state)
    {
        string audioCodecs = string.Empty;
        if (!string.IsNullOrEmpty(state.ActualOutputAudioCodec))
        {
            audioCodecs = GetPlaylistAudioCodecs(state);
        }

        return audioCodecs;
    }

    /// <summary>
    /// Appends a VIDEO-RANGE field containing the range of the output video stream.
    /// </summary>
    /// <seealso cref="AppendPlaylist(StringBuilder, StreamState, PlaylistVariantParts)"/>
    /// <param name="builder">StringBuilder to append the field to.</param>
    /// <param name="state">StreamState of the current stream.</param>
    private void AppendPlaylistVideoRangeField(StringBuilder builder, StreamState state)
    {
        if (state.VideoStream is null || state.VideoStream.VideoRange == VideoRange.Unknown)
        {
            return;
        }

        var videoRange = state.VideoStream.VideoRange;
        var videoRangeType = state.VideoStream.VideoRangeType;
        if (!EncodingHelper.IsCopyCodec(state.OutputVideoCodec))
        {
            // Currently we only encode to SDR.
            builder.Append(",VIDEO-RANGE=SDR");
            return;
        }

        if (videoRange == VideoRange.SDR)
        {
            builder.Append(",VIDEO-RANGE=SDR");
        }

        if (videoRange == VideoRange.HDR)
        {
            AppendHdrVideoRangeField(builder, videoRangeType, state.VideoStream);
        }
    }

    private static void AppendHdrVideoRangeField(StringBuilder builder, VideoRangeType videoRangeType, MediaStream videoStream)
    {
        switch (videoRangeType)
        {
            case VideoRangeType.HLG:
            case VideoRangeType.DOVIWithHLG:
            case VideoRangeType.DOVIInvalid when string.Equals(videoStream.ColorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase):
                builder.Append(",VIDEO-RANGE=HLG");
                break;
            default:
                builder.Append(",VIDEO-RANGE=PQ");
                break;
        }
    }

    /// <summary>
    /// Appends a CODECS field containing formatted strings of
    /// the active streams output video and audio codecs.
    /// </summary>
    /// <seealso cref="AppendPlaylist(StringBuilder, StreamState, PlaylistVariantParts)"/>
    /// <seealso cref="GetPlaylistVideoCodecs(StreamState, string, int)"/>
    /// <seealso cref="GetPlaylistAudioCodecs(StreamState)"/>
    /// <param name="builder">StringBuilder to append the field to.</param>
    /// <param name="state">StreamState of the current stream.</param>
    private void AppendPlaylistCodecsField(StringBuilder builder, StreamState state)
    {
        // Video
        string videoCodecs = GetPlaylistVideoCodecsOrEmpty(state);

        // Audio
        string audioCodecs = string.Empty;
        if (!string.IsNullOrEmpty(state.ActualOutputAudioCodec))
        {
            audioCodecs = GetPlaylistAudioCodecs(state);
        }

        StringBuilder codecs = new StringBuilder();

        codecs.Append(videoCodecs);

        if (!string.IsNullOrEmpty(videoCodecs) && !string.IsNullOrEmpty(audioCodecs))
        {
            codecs.Append(',');
        }

        codecs.Append(audioCodecs);

        if (codecs.Length > 1)
        {
            builder.Append(",CODECS=\"")
                .Append(codecs)
                .Append('"');
        }
    }

    private string GetPlaylistVideoCodecsOrEmpty(StreamState state)
    {
        string videoCodecs = string.Empty;
        int? videoCodecLevel = GetOutputVideoCodecLevel(state);
        if (!string.IsNullOrEmpty(state.ActualOutputVideoCodec) && videoCodecLevel.HasValue)
        {
            videoCodecs = GetPlaylistVideoCodecs(state, state.ActualOutputVideoCodec, videoCodecLevel.Value);
        }

        return videoCodecs;
    }

    /// <summary>
    /// Appends a SUPPLEMENTAL-CODECS field containing formatted strings of
    /// the active streams output Dolby Vision Videos.
    /// </summary>
    /// <seealso cref="AppendPlaylist(StringBuilder, StreamState, PlaylistVariantParts)"/>
    /// <seealso cref="GetPlaylistVideoCodecs(StreamState, string, int)"/>
    /// <param name="builder">StringBuilder to append the field to.</param>
    /// <param name="state">StreamState of the current stream.</param>
    private void AppendPlaylistSupplementalCodecsField(StringBuilder builder, StreamState state)
    {
        // HDR dynamic metadata currently cannot exist when transcoding
        if (!EncodingHelper.IsCopyCodec(state.OutputVideoCodec))
        {
            return;
        }

        if (EncodingHelper.IsDovi(state.VideoStream) && !_encodingHelper.IsDoviRemoved(state))
        {
            AppendDvString();
        }
        else if (EncodingHelper.IsHdr10Plus(state.VideoStream) && !_encodingHelper.IsHdr10PlusRemoved(state))
        {
            AppendHdr10PlusString();
        }

        return;

        void AppendDvString()
        {
            var dvProfile = state.VideoStream.DvProfile;
            var dvLevel = state.VideoStream.DvLevel;
            var dvRangeString = state.VideoStream.VideoRangeType switch
            {
                VideoRangeType.DOVIWithHDR10 => "db1p",
                VideoRangeType.DOVIWithHLG => "db4h",
                VideoRangeType.DOVIWithHDR10Plus => "db1p", // The HDR10+ metadata would be removed if Dovi metadata is not removed
                _ => string.Empty // Don't label Dovi with EL and SDR due to compatability issues, ignore invalid configurations
            };

            if (IsIncompleteDvSignal(dvProfile, dvLevel, dvRangeString))
            {
                return;
            }

            var dvFourCc = string.Equals(state.ActualOutputVideoCodec, "av1", StringComparison.OrdinalIgnoreCase) ? "dav1" : "dvh1";
            builder.Append(",SUPPLEMENTAL-CODECS=\"")
                .Append(dvFourCc)
                .Append('.')
                .Append(dvProfile.Value.ToString("D2", CultureInfo.InvariantCulture))
                .Append('.')
                .Append(dvLevel.Value.ToString("D2", CultureInfo.InvariantCulture))
                .Append('/')
                .Append(dvRangeString)
                .Append('"');
        }

        void AppendHdr10PlusString()
        {
            var videoCodecLevel = GetOutputVideoCodecLevel(state);
            if (string.IsNullOrEmpty(state.ActualOutputVideoCodec) || videoCodecLevel is null)
            {
                return;
            }

            var videoCodecString = GetPlaylistVideoCodecs(state, state.ActualOutputVideoCodec, videoCodecLevel.Value);
            builder.Append(",SUPPLEMENTAL-CODECS=\"")
                .Append(videoCodecString)
                .Append('/')
                .Append("cdm4")
                .Append('"');
        }
    }

    private static bool IsIncompleteDvSignal([NotNullWhen(false)] int? dvProfile, [NotNullWhen(false)] int? dvLevel, string dvRangeString)
    {
        return dvProfile is null || dvLevel is null || string.IsNullOrEmpty(dvRangeString);
    }

    /// <summary>
    /// Appends a RESOLUTION field containing the resolution of the output stream.
    /// </summary>
    /// <seealso cref="AppendPlaylist(StringBuilder, StreamState, PlaylistVariantParts)"/>
    /// <param name="builder">StringBuilder to append the field to.</param>
    /// <param name="state">StreamState of the current stream.</param>
    private void AppendPlaylistResolutionField(StringBuilder builder, StreamState state)
    {
        if (state.OutputWidth.HasValue && state.OutputHeight.HasValue)
        {
            builder.Append(",RESOLUTION=")
                .Append(state.OutputWidth.GetValueOrDefault())
                .Append('x')
                .Append(state.OutputHeight.GetValueOrDefault());
        }
    }

    /// <summary>
    /// Appends a FRAME-RATE field containing the framerate of the output stream.
    /// </summary>
    /// <seealso cref="AppendPlaylist(StringBuilder, StreamState, PlaylistVariantParts)"/>
    /// <param name="builder">StringBuilder to append the field to.</param>
    /// <param name="state">StreamState of the current stream.</param>
    private void AppendPlaylistFramerateField(StringBuilder builder, StreamState state)
    {
        double? framerate = GetPlaylistFramerate(state);

        if (framerate.HasValue)
        {
            builder.Append(",FRAME-RATE=")
                .Append(framerate.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static double? GetPlaylistFramerate(StreamState state)
    {
        double? framerate = null;
        if (state.TargetFramerate.HasValue)
        {
            framerate = Math.Round(state.TargetFramerate.GetValueOrDefault(), 3);
        }
        else if (state.VideoStream?.RealFrameRate is not null)
        {
            framerate = Math.Round(state.VideoStream.RealFrameRate.GetValueOrDefault(), 3);
        }

        return framerate;
    }

    private bool EnableAdaptiveBitrateStreaming(StreamState state, bool isLiveStream, bool enableAdaptiveBitrateStreaming, IPAddress ipAddress)
    {
        // Within the local network this will likely do more harm than good.
        if (_networkManager.IsInLocalNetwork(ipAddress))
        {
            return false;
        }

        if (!enableAdaptiveBitrateStreaming)
        {
            return false;
        }

        if (IsLiveStreamOrHasNoMediaPath(isLiveStream, state))
        {
            // Opening live streams is so slow it's not even worth it
            return false;
        }

        if (EncodingHelper.IsCopyCodec(state.OutputVideoCodec))
        {
            return false;
        }

        if (EncodingHelper.IsCopyCodec(state.OutputAudioCodec))
        {
            return false;
        }

        if (!state.IsOutputVideo)
        {
            return false;
        }

        return HasRequestedVideoBitrate(state);
    }

    private static bool IsLiveStreamOrHasNoMediaPath(bool isLiveStream, StreamState state)
    {
        return isLiveStream || string.IsNullOrWhiteSpace(state.MediaPath);
    }

    private static bool HasRequestedVideoBitrate(StreamState state)
    {
        return state.VideoRequest?.VideoBitRate.HasValue ?? false;
    }

    private void AddSubtitles(StreamState state, IEnumerable<MediaStream> subtitles, StringBuilder builder, ClaimsPrincipal user)
    {
        if (state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Drop)
        {
            return;
        }

        var selectedIndex = GetSelectedSubtitleIndex(state);
        const string Format = "#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"subs\",NAME=\"{0}\",DEFAULT={1},FORCED={2},AUTOSELECT=YES,URI=\"{3}\",LANGUAGE=\"{4}\"";

        foreach (var stream in subtitles)
        {
            var name = stream.DisplayTitle;

            var isDefault = selectedIndex.HasValue && selectedIndex.Value == stream.Index;
            var isForced = stream.IsForced;

            var url = string.Format(
                CultureInfo.InvariantCulture,
                "{0}/Subtitles/{1}/subtitles.m3u8?SegmentLength={2}&ApiKey={3}",
                state.Request.MediaSourceId,
                stream.Index.ToString(CultureInfo.InvariantCulture),
                30.ToString(CultureInfo.InvariantCulture),
                user.GetToken());

            var line = string.Format(
                CultureInfo.InvariantCulture,
                Format,
                name,
                isDefault ? "YES" : "NO",
                isForced ? "YES" : "NO",
                url,
                stream.Language ?? "Unknown");

            builder.AppendLine(line);
        }
    }

    private static int? GetSelectedSubtitleIndex(StreamState state)
    {
        return state.SubtitleStream is null || state.SubtitleDeliveryMethod != SubtitleDeliveryMethod.Hls ? (int?)null : state.SubtitleStream.Index;
    }

    /// <summary>
    /// Appends EXT-X-IMAGE-STREAM-INF playlists for each available trickplay resolution.
    /// </summary>
    /// <param name="state">StreamState of the current stream.</param>
    /// <param name="trickplayResolutions">Dictionary of widths to corresponding tiles info.</param>
    /// <param name="builder">StringBuilder to append the field to.</param>
    /// <param name="user">Http user context.</param>
    private void AddTrickplay(StreamState state, Dictionary<int, TrickplayInfo> trickplayResolutions, StringBuilder builder, ClaimsPrincipal user)
    {
        const string playlistFormat = "#EXT-X-IMAGE-STREAM-INF:BANDWIDTH={0},RESOLUTION={1}x{2},CODECS=\"jpeg\",URI=\"{3}\"";

        foreach (var resolution in trickplayResolutions)
        {
            var width = resolution.Key;
            var trickplayInfo = resolution.Value;

            var url = string.Format(
                CultureInfo.InvariantCulture,
                "Trickplay/{0}/tiles.m3u8?MediaSourceId={1}&ApiKey={2}",
                width.ToString(CultureInfo.InvariantCulture),
                state.Request.MediaSourceId,
                user.GetToken());

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                playlistFormat,
                trickplayInfo.Bandwidth.ToString(CultureInfo.InvariantCulture),
                trickplayInfo.Width.ToString(CultureInfo.InvariantCulture),
                trickplayInfo.Height.ToString(CultureInfo.InvariantCulture),
                url);

            builder.AppendLine();
        }
    }

    private int GetBitrateVariation(int bitrate)
    {
        // By default, vary by just 50k
        var variation = 50000;

        if (bitrate >= 10000000)
        {
            variation = 2000000;
        }
        else if (bitrate >= 5000000)
        {
            variation = 1500000;
        }
        else if (bitrate >= 3000000)
        {
            variation = 1000000;
        }
        else
        {
            variation = GetLowBitrateVariation(bitrate, variation);
        }

        return variation;
    }

    private static int GetLowBitrateVariation(int bitrate, int variation)
    {
        if (bitrate >= 2000000)
        {
            variation = 500000;
        }
        else if (bitrate >= 1000000)
        {
            variation = 300000;
        }
        else if (bitrate >= 600000)
        {
            variation = 200000;
        }
        else if (bitrate >= 400000)
        {
            variation = 100000;
        }

        return variation;
    }

    private string ReplacePlaylistCodecsField(StringBuilder playlist, StringBuilder oldValue, StringBuilder newValue)
    {
        var oldPlaylist = playlist.ToString();
        return oldPlaylist.Replace(
            oldValue.ToString(),
            newValue.ToString(),
            StringComparison.Ordinal);
    }

    private readonly record struct MasterPlaylistParts(bool IsHeadRequest, bool EnableAdaptiveBitrateStreaming);

    private readonly record struct VariantPlaylistsParts(Dictionary<string, Microsoft.Extensions.Primitives.StringValues> PlaylistQuery, string BaseUrl, int TotalBitrate, string? SubtitleGroup);

    private readonly record struct PlaylistVariantParts(string Url, int Bitrate, string? SubtitleGroup);
}
