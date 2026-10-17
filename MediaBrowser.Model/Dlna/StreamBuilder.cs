using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        // Aliases
        internal const TranscodeReason ContainerReasons = TranscodeReason.ContainerNotSupported | TranscodeReason.ContainerBitrateExceedsLimit;
        internal const TranscodeReason AudioCodecReasons = TranscodeReason.AudioBitrateNotSupported | TranscodeReason.AudioChannelsNotSupported | TranscodeReason.AudioProfileNotSupported | TranscodeReason.AudioSampleRateNotSupported | TranscodeReason.SecondaryAudioNotSupported | TranscodeReason.AudioBitDepthNotSupported | TranscodeReason.AudioIsExternal;
        internal const TranscodeReason AudioReasons = TranscodeReason.AudioCodecNotSupported | AudioCodecReasons;
        internal const TranscodeReason VideoCodecReasons = TranscodeReason.VideoResolutionNotSupported | TranscodeReason.AnamorphicVideoNotSupported | TranscodeReason.InterlacedVideoNotSupported | TranscodeReason.VideoBitDepthNotSupported | TranscodeReason.VideoBitrateNotSupported | TranscodeReason.VideoFramerateNotSupported | TranscodeReason.VideoLevelNotSupported | TranscodeReason.RefFramesNotSupported | TranscodeReason.VideoRangeTypeNotSupported | TranscodeReason.VideoProfileNotSupported | TranscodeReason.VideoRotationNotSupported;
        internal const TranscodeReason VideoReasons = TranscodeReason.VideoCodecNotSupported | VideoCodecReasons;
        internal const TranscodeReason DirectStreamReasons = AudioReasons | TranscodeReason.ContainerNotSupported | TranscodeReason.VideoCodecTagNotSupported;

        private const string ManifestContainers = "hls,applehttp,dash";

        private readonly ILogger _logger;
        private readonly ITranscoderSupport _transcoderSupport;
        private static readonly string[] _supportedHlsVideoCodecs = ["h264", "hevc", "vp9", "av1"];
        private static readonly string[] _supportedHlsAudioCodecsTs = ["aac", "ac3", "eac3", "mp3"];
        private static readonly string[] _supportedHlsAudioCodecsMp4 = ["aac", "ac3", "eac3", "mp3", "alac", "flac", "opus", "dts", "truehd"];

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamBuilder"/> class.
        /// </summary>
        /// <param name="transcoderSupport">The <see cref="ITranscoderSupport"/> object.</param>
        /// <param name="logger">The <see cref="ILogger"/> object.</param>
        public StreamBuilder(ITranscoderSupport transcoderSupport, ILogger logger)
        {
            _transcoderSupport = transcoderSupport;
            _logger = logger;
        }

        /// <summary>
        /// Gets the optimal audio stream.
        /// </summary>
        /// <param name="options">The <see cref="MediaOptions"/> object to get the audio stream from.</param>
        /// <returns>The <see cref="StreamInfo"/> of the optimal audio stream.</returns>
        public StreamInfo? GetOptimalAudioStream(MediaOptions options)
        {
            ValidateMediaOptions(options, false);

            List<StreamInfo> streams = [];
            foreach (var mediaSource in options.MediaSources)
            {
                if (!(string.IsNullOrEmpty(options.MediaSourceId)
                    || string.Equals(mediaSource.Id, options.MediaSourceId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                StreamInfo? streamInfo = GetOptimalAudioStream(mediaSource, options);
                if (streamInfo is not null)
                {
                    streamInfo.DeviceId = options.DeviceId;
                    streamInfo.DeviceProfileId = options.Profile.Id?.ToString("N", CultureInfo.InvariantCulture);
                    streams.Add(streamInfo);
                }
            }

            return GetOptimalStream(streams, options.GetMaxBitrate(true) ?? 0);
        }

        private StreamInfo? GetOptimalAudioStream(MediaSourceInfo item, MediaOptions options)
        {
            var playlistItem = new StreamInfo
            {
                ItemId = options.ItemId,
                MediaType = DlnaProfileType.Audio,
                MediaSource = item,
                RunTimeTicks = item.RunTimeTicks,
                Context = options.Context,
                DeviceProfile = options.Profile
            };

            if (options.ForceDirectPlay)
            {
                playlistItem.PlayMethod = PlayMethod.DirectPlay;
                playlistItem.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Audio);
                return playlistItem;
            }

            if (options.ForceDirectStream)
            {
                playlistItem.PlayMethod = PlayMethod.DirectStream;
                playlistItem.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Audio);
                return playlistItem;
            }

            MediaStream audioStream = item.GetDefaultAudioStream(null);

            ArgumentNullException.ThrowIfNull(audioStream);

            var directPlayInfo = GetAudioDirectPlayProfile(item, audioStream, options);

            var directPlayMethod = directPlayInfo.PlayMethod;
            var transcodeReasons = directPlayInfo.TranscodeReasons;

            if (directPlayMethod is PlayMethod.DirectPlay)
            {
                var audioFailureReasons = GetCompatibilityAudioCodec(options, item, item.Container, audioStream, null, false, false);
                transcodeReasons |= audioFailureReasons;

                if (audioFailureReasons == 0)
                {
                    playlistItem.PlayMethod = directPlayMethod.Value;
                    playlistItem.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Audio, directPlayInfo.Profile);

                    return playlistItem;
                }
            }

            if (directPlayMethod is PlayMethod.DirectStream)
            {
                var remuxContainer = item.TranscodingContainer ?? "ts";
                string[] supportedHlsContainers = ["ts", "mp4"];
                // If the container specified for the profile is an HLS supported container, use that container instead, overriding the preference
                // The client should be responsible to ensure this container is compatible
                remuxContainer = Array.Exists(supportedHlsContainers, element => string.Equals(element, directPlayInfo.Profile?.Container, StringComparison.OrdinalIgnoreCase)) ? directPlayInfo.Profile?.Container : remuxContainer;
                bool codeIsSupported = IsRemuxAudioCodecSupported(item, remuxContainer, directPlayInfo.Profile);

                if (codeIsSupported)
                {
                    playlistItem.PlayMethod = directPlayMethod.Value;
                    playlistItem.Container = remuxContainer;
                    playlistItem.TranscodeReasons = transcodeReasons;
                    playlistItem.SubProtocol = item.TranscodingSubProtocol;
                    item.TranscodingContainer = remuxContainer;
                    return playlistItem;
                }

                transcodeReasons |= TranscodeReason.AudioCodecNotSupported;
                playlistItem.TranscodeReasons = transcodeReasons;
            }

            TranscodingProfile? transcodingProfile = FindAudioTranscodingProfile(options, playlistItem);

            if (transcodingProfile is not null)
            {
                if (!item.SupportsTranscoding)
                {
                    return null;
                }

                SetStreamInfoOptionsFromTranscodingProfile(item, playlistItem, transcodingProfile);

                var inputAudioChannels = audioStream.Channels;
                var inputAudioBitrate = audioStream.BitRate;
                var inputAudioSampleRate = audioStream.SampleRate;
                var inputAudioBitDepth = audioStream.BitDepth;

                var audioTranscodingConditions = GetProfileConditionsForAudio(options.Profile.CodecProfiles, transcodingProfile.Container, transcodingProfile.AudioCodec, inputAudioChannels, inputAudioBitrate, inputAudioSampleRate, inputAudioBitDepth, false).ToArray();
                ApplyTranscodingConditions(playlistItem, audioTranscodingConditions, null, true, true);

                // Honor requested max channels
                playlistItem.GlobalMaxAudioChannels = options.MaxAudioChannels;

                SetAudioTranscodingBitrate(options, playlistItem);

                // Pure audio transcoding does not support comma separated list of transcoding codec at the moment.
                // So just use the AudioCodec as is would be safe enough as the _transcoderSupport.CanEncodeToAudioCodec
                // would fail so this profile will not even be picked up.
                SetTranscodingAudioCodecIfMissing(playlistItem, transcodingProfile);
            }

            playlistItem.TranscodeReasons = transcodeReasons;
            return playlistItem;
        }

        private static bool IsRemuxAudioCodecSupported(MediaSourceInfo item, string? remuxContainer, DirectPlayProfile? directPlayProfile)
        {
            bool codeIsSupported;
            if (item.TranscodingSubProtocol == MediaStreamProtocol.hls)
            {
                // Enforce HLS audio codec restrictions
                if (string.Equals(remuxContainer, "mp4", StringComparison.OrdinalIgnoreCase))
                {
                    codeIsSupported = _supportedHlsAudioCodecsMp4.Contains(directPlayProfile?.AudioCodec ?? directPlayProfile?.Container);
                }
                else
                {
                    codeIsSupported = _supportedHlsAudioCodecsTs.Contains(directPlayProfile?.AudioCodec ?? directPlayProfile?.Container);
                }
            }
            else
            {
                // Let's assume the client has given a correct container for http
                codeIsSupported = true;
            }

            return codeIsSupported;
        }

        private TranscodingProfile? FindAudioTranscodingProfile(MediaOptions options, StreamInfo playlistItem)
        {
            TranscodingProfile? transcodingProfile = null;
            foreach (var tcProfile in options.Profile.TranscodingProfiles)
            {
                if (tcProfile.Type == playlistItem.MediaType
                    && tcProfile.Context == options.Context
                    && _transcoderSupport.CanEncodeToAudioCodec(tcProfile.AudioCodec ?? tcProfile.Container))
                {
                    transcodingProfile = tcProfile;
                    break;
                }
            }

            return transcodingProfile;
        }

        private static void SetAudioTranscodingBitrate(MediaOptions options, StreamInfo playlistItem)
        {
            var configuredBitrate = options.GetMaxBitrate(true);

            long transcodingBitrate = options.AudioTranscodingBitrate
                ?? (options.Context == EncodingContext.Streaming ? options.Profile.MusicStreamingTranscodingBitrate : null)
                ?? configuredBitrate
                ?? 128000;

            if (configuredBitrate.HasValue)
            {
                transcodingBitrate = Math.Min(configuredBitrate.Value, transcodingBitrate);
            }

            var longBitrate = Math.Min(transcodingBitrate, playlistItem.AudioBitrate ?? transcodingBitrate);
            playlistItem.AudioBitrate = longBitrate > int.MaxValue ? int.MaxValue : Convert.ToInt32(longBitrate);
        }

        private static void SetTranscodingAudioCodecIfMissing(StreamInfo playlistItem, TranscodingProfile transcodingProfile)
        {
            if (playlistItem.AudioCodecs.Count == 0 && !string.IsNullOrWhiteSpace(transcodingProfile.AudioCodec))
            {
                playlistItem.AudioCodecs = [transcodingProfile.AudioCodec];
            }
        }

        /// <summary>
        /// Gets the optimal video stream.
        /// </summary>
        /// <param name="options">The <see cref="MediaOptions"/> object to get the video stream from.</param>
        /// <returns>The <see cref="StreamInfo"/> of the optimal video stream.</returns>
        public StreamInfo? GetOptimalVideoStream(MediaOptions options)
        {
            ValidateMediaOptions(options, true);

            var mediaSources = string.IsNullOrEmpty(options.MediaSourceId)
                ? options.MediaSources
                : options.MediaSources.Where(x => string.Equals(x.Id, options.MediaSourceId, StringComparison.OrdinalIgnoreCase));

            List<StreamInfo> streams = [];
            foreach (var mediaSourceInfo in mediaSources)
            {
                var streamInfo = BuildVideoItem(mediaSourceInfo, options);
                if (streamInfo is not null)
                {
                    streams.Add(streamInfo);
                }
            }

            foreach (var stream in streams)
            {
                stream.DeviceId = options.DeviceId;
                stream.DeviceProfileId = options.Profile.Id?.ToString("N", CultureInfo.InvariantCulture);
            }

            return GetOptimalStream(streams, options.GetMaxBitrate(false) ?? 0);
        }

        private static StreamInfo? GetOptimalStream(List<StreamInfo> streams, long maxBitrate)
            => SortMediaSources(streams, maxBitrate).FirstOrDefault();

        private static IOrderedEnumerable<StreamInfo> SortMediaSources(List<StreamInfo> streams, long maxBitrate)
        {
            return streams.OrderBy(i =>
            {
                // Nothing beats direct playing a file
                if (i.PlayMethod == PlayMethod.DirectPlay && i.MediaSource?.Protocol == MediaProtocol.File)
                {
                    return 0;
                }

                return 1;
            }).ThenBy(i =>
            {
                switch (i.PlayMethod)
                {
                    // Let's assume direct streaming a file is just as desirable as direct playing a remote url
                    case PlayMethod.DirectStream:
                    case PlayMethod.DirectPlay:
                        return 0;
                    default:
                        return 1;
                }
            }).ThenBy(i =>
            {
                switch (i.MediaSource?.Protocol)
                {
                    case MediaProtocol.File:
                        return 0;
                    default:
                        return 1;
                }
            }).ThenBy(i =>
            {
                if (maxBitrate > 0)
                {
                    if (i.MediaSource?.Bitrate is not null)
                    {
                        return Math.Abs(i.MediaSource.Bitrate.Value - maxBitrate);
                    }
                }

                return 0;
            }).ThenBy(streams.IndexOf);
        }

        private static TranscodeReason GetTranscodeReasonForFailedCondition(ProfileCondition condition)
        {
            switch (condition.Property)
            {
                case ProfileConditionValue.AudioBitrate:
                    return TranscodeReason.AudioBitrateNotSupported;

                case ProfileConditionValue.AudioChannels:
                    return TranscodeReason.AudioChannelsNotSupported;

                case ProfileConditionValue.AudioProfile:
                    return TranscodeReason.AudioProfileNotSupported;

                case ProfileConditionValue.AudioSampleRate:
                    return TranscodeReason.AudioSampleRateNotSupported;

                case ProfileConditionValue.Has64BitOffsets:
                    // TODO
                    return 0;

                case ProfileConditionValue.Height:
                    return TranscodeReason.VideoResolutionNotSupported;

                case ProfileConditionValue.IsAnamorphic:
                    return TranscodeReason.AnamorphicVideoNotSupported;

                case ProfileConditionValue.IsAvc:
                    // TODO
                    return 0;

                case ProfileConditionValue.IsInterlaced:
                    return TranscodeReason.InterlacedVideoNotSupported;

                case ProfileConditionValue.IsSecondaryAudio:
                    return TranscodeReason.SecondaryAudioNotSupported;

                case ProfileConditionValue.NumStreams:
                    return TranscodeReason.StreamCountExceedsLimit;

                case ProfileConditionValue.NumAudioStreams:
                    // TODO
                    return 0;

                case ProfileConditionValue.NumVideoStreams:
                    // TODO
                    return 0;

                case ProfileConditionValue.PacketLength:
                    // TODO
                    return 0;

                case ProfileConditionValue.RefFrames:
                    return TranscodeReason.RefFramesNotSupported;

                case ProfileConditionValue.VideoBitDepth:
                    return TranscodeReason.VideoBitDepthNotSupported;

                case ProfileConditionValue.AudioBitDepth:
                    return TranscodeReason.AudioBitDepthNotSupported;

                case ProfileConditionValue.VideoBitrate:
                    return TranscodeReason.VideoBitrateNotSupported;

                case ProfileConditionValue.VideoCodecTag:
                    return TranscodeReason.VideoCodecTagNotSupported;

                case ProfileConditionValue.VideoFramerate:
                    return TranscodeReason.VideoFramerateNotSupported;

                case ProfileConditionValue.VideoLevel:
                    return TranscodeReason.VideoLevelNotSupported;

                case ProfileConditionValue.VideoProfile:
                    return TranscodeReason.VideoProfileNotSupported;

                case ProfileConditionValue.VideoRangeType:
                    return TranscodeReason.VideoRangeTypeNotSupported;

                case ProfileConditionValue.VideoRotation:
                    return TranscodeReason.VideoRotationNotSupported;

                case ProfileConditionValue.VideoTimestamp:
                    // TODO
                    return 0;

                case ProfileConditionValue.Width:
                    return TranscodeReason.VideoResolutionNotSupported;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Normalizes input container.
        /// </summary>
        /// <param name="inputContainer">The input container.</param>
        /// <param name="profile">The <see cref="DeviceProfile"/>.</param>
        /// <param name="type">The <see cref="DlnaProfileType"/>.</param>
        /// <param name="playProfile">The <see cref="DirectPlayProfile"/> object to get the video stream from.</param>
        /// <returns>The normalized input container.</returns>
        public static string? NormalizeMediaSourceFormatIntoSingleContainer(string inputContainer, DeviceProfile? profile, DlnaProfileType type, DirectPlayProfile? playProfile = null)
        {
            // If the source is Live TV the inputContainer will be null until the mediasource is probed on first access
            if (profile is null || IsSingleOrEmptyContainer(inputContainer))
            {
                return inputContainer;
            }

            var formats = ContainerHelper.Split(inputContainer);
            var playProfiles = playProfile is null ? profile.DirectPlayProfiles : [playProfile];
            foreach (var format in formats)
            {
                foreach (var directPlayProfile in playProfiles)
                {
                    if (directPlayProfile.Type != type)
                    {
                        continue;
                    }

                    if (directPlayProfile.SupportsContainer(format))
                    {
                        return format;
                    }
                }
            }

            return inputContainer;
        }

        private static bool IsSingleOrEmptyContainer(string inputContainer)
        {
            return string.IsNullOrEmpty(inputContainer) || !inputContainer.Contains(',', StringComparison.OrdinalIgnoreCase);
        }

        private (DirectPlayProfile? Profile, PlayMethod? PlayMethod, TranscodeReason TranscodeReasons) GetAudioDirectPlayProfile(MediaSourceInfo item, MediaStream audioStream, MediaOptions options)
        {
            var directPlayProfile = options.Profile.DirectPlayProfiles
                .FirstOrDefault(x => x.Type == DlnaProfileType.Audio && IsAudioDirectPlaySupported(x, item, audioStream));

            TranscodeReason transcodeReasons = 0;
            if (directPlayProfile is null)
            {
                _logger.LogDebug(
                    "Profile: {0}, No audio direct play profiles found for {1} with codec {2}",
                    options.Profile.Name ?? "Unknown Profile",
                    item.Path ?? "Unknown path",
                    audioStream.Codec ?? "Unknown codec");

                var directStreamProfile = options.Profile.DirectPlayProfiles
                    .FirstOrDefault(x => x.Type == DlnaProfileType.Audio && IsAudioDirectStreamSupported(x, item, audioStream));

                if (directStreamProfile is not null)
                {
                    directPlayProfile = directStreamProfile;
                    transcodeReasons |= TranscodeReason.ContainerNotSupported;
                }
                else
                {
                    return (null, null, GetTranscodeReasonsFromDirectPlayProfile(item, null, audioStream, options.Profile.DirectPlayProfiles));
                }
            }

            // The profile describes what the device supports
            // If device requirements are satisfied then allow both direct stream and direct play
            // Note: As of 10.10 codebase, SupportsDirectPlay is always true because the MediaSourceInfo initializes this key as true
            // Need to check additionally for current transcode reasons
            if (item.SupportsDirectPlay && transcodeReasons == 0)
            {
                if (!IsBitrateLimitExceeded(item, options.GetMaxBitrate(true) ?? 0))
                {
                    if (options.EnableDirectPlay)
                    {
                        return (directPlayProfile, PlayMethod.DirectPlay, 0);
                    }
                }
                else
                {
                    transcodeReasons |= TranscodeReason.ContainerBitrateExceedsLimit;
                }
            }

            // While options takes the network and other factors into account. Only applies to direct stream
            if (item.SupportsDirectStream)
            {
                if (!IsBitrateLimitExceeded(item, options.GetMaxBitrate(true) ?? 0))
                {
                    // Note: as of 10.10 codebase, the options.EnableDirectStream is always false due to
                    // "direct-stream http streaming is currently broken"
                    // Don't check that option for audio as we always assume that is supported
                    if (transcodeReasons == TranscodeReason.ContainerNotSupported)
                    {
                        return (directPlayProfile, PlayMethod.DirectStream, transcodeReasons);
                    }
                }
                else
                {
                    transcodeReasons |= TranscodeReason.ContainerBitrateExceedsLimit;
                }
            }

            return (directPlayProfile, null, transcodeReasons);
        }

        private static TranscodeReason GetTranscodeReasonsFromDirectPlayProfile(MediaSourceInfo item, MediaStream? videoStream, MediaStream audioStream, IEnumerable<DirectPlayProfile> directPlayProfiles)
        {
            var mediaType = videoStream is null ? DlnaProfileType.Audio : DlnaProfileType.Video;

            var containerSupported = false;
            var audioSupported = false;
            var videoSupported = false;

            foreach (var profile in directPlayProfiles)
            {
                // Check container type
                if (profile.Type == mediaType && profile.SupportsContainer(item.Container))
                {
                    containerSupported = true;

                    videoSupported = videoStream is null || profile.SupportsVideoCodec(videoStream.Codec);

                    audioSupported = audioStream is null || profile.SupportsAudioCodec(audioStream.Codec);

                    if (videoSupported && audioSupported)
                    {
                        break;
                    }
                }
            }

            TranscodeReason reasons = 0;
            if (!containerSupported)
            {
                reasons |= TranscodeReason.ContainerNotSupported;
            }

            if (!videoSupported)
            {
                reasons |= TranscodeReason.VideoCodecNotSupported;
            }

            if (!audioSupported)
            {
                reasons |= TranscodeReason.AudioCodecNotSupported;
            }

            return reasons;
        }

        private static void SetStreamInfoOptionsFromTranscodingProfile(MediaSourceInfo item, StreamInfo playlistItem, TranscodingProfile transcodingProfile)
        {
            var container = transcodingProfile.Container;
            var protocol = transcodingProfile.Protocol;

            item.TranscodingContainer = container;
            item.TranscodingSubProtocol = protocol;

            if (playlistItem.PlayMethod == PlayMethod.Transcode)
            {
                playlistItem.Container = container;
                playlistItem.SubProtocol = protocol;
            }

            playlistItem.TranscodeSeekInfo = transcodingProfile.TranscodeSeekInfo;
            if (int.TryParse(transcodingProfile.MaxAudioChannels, CultureInfo.InvariantCulture, out int transcodingMaxAudioChannels))
            {
                playlistItem.TranscodingMaxAudioChannels = transcodingMaxAudioChannels;
            }

            playlistItem.EstimateContentLength = transcodingProfile.EstimateContentLength;

            playlistItem.CopyTimestamps = transcodingProfile.CopyTimestamps;
            playlistItem.EnableSubtitlesInManifest = transcodingProfile.EnableSubtitlesInManifest;
            playlistItem.EnableMpegtsM2TsMode = transcodingProfile.EnableMpegtsM2TsMode;

            playlistItem.EnableAudioVbrEncoding = transcodingProfile.EnableAudioVbrEncoding;

            if (transcodingProfile.MinSegments > 0)
            {
                playlistItem.MinSegments = transcodingProfile.MinSegments;
            }

            if (transcodingProfile.SegmentLength > 0)
            {
                playlistItem.SegmentLength = transcodingProfile.SegmentLength;
            }
        }

        private static void SetStreamInfoOptionsFromDirectPlayProfile(MediaOptions options, MediaSourceInfo item, StreamInfo playlistItem, DirectPlayProfile? directPlayProfile)
        {
            var container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Video, directPlayProfile);
            var protocol = MediaStreamProtocol.http;

            item.TranscodingContainer = container;
            item.TranscodingSubProtocol = protocol;

            playlistItem.Container = container;
            playlistItem.SubProtocol = protocol;

            playlistItem.VideoCodecs = [item.VideoStream.Codec];
            playlistItem.AudioCodecs = ContainerHelper.Split(directPlayProfile?.AudioCodec);
        }

        private StreamInfo BuildVideoItem(MediaSourceInfo item, MediaOptions options)
        {
            ArgumentNullException.ThrowIfNull(item);

            StreamInfo playlistItem = new StreamInfo
            {
                ItemId = options.ItemId,
                MediaType = DlnaProfileType.Video,
                MediaSource = item,
                RunTimeTicks = item.RunTimeTicks,
                Context = options.Context,
                DeviceProfile = options.Profile,
                SubtitleStreamIndex = options.SubtitleStreamIndex ?? GetDefaultSubtitleStreamIndex(item, options.Profile.SubtitleProfiles),
                AlwaysBurnInSubtitleWhenTranscoding = options.AlwaysBurnInSubtitleWhenTranscoding
            };

            var subtitleStream = playlistItem.SubtitleStreamIndex.HasValue ? item.GetMediaStream(MediaStreamType.Subtitle, playlistItem.SubtitleStreamIndex.Value) : null;

            var audioStream = item.GetDefaultAudioStream(options.AudioStreamIndex ?? item.DefaultAudioStreamIndex);
            if (audioStream is not null)
            {
                playlistItem.AudioStreamIndex = audioStream.Index;
            }

            // Collect candidate audio streams
            ICollection<MediaStream> candidateAudioStreams = GetCandidateAudioStreams(item, options, audioStream);

            var videoStream = item.VideoStream;

            var bitrateLimitExceeded = IsBitrateLimitExceeded(item, options.GetMaxBitrate(false) ?? 0);
            var isEligibleForDirectPlay = IsEligibleForDirectPlay(options, bitrateLimitExceeded);
            var isEligibleForDirectStream = IsEligibleForDirectStream(options, bitrateLimitExceeded);
            TranscodeReason transcodeReasons = 0;

            // Force transcode or remux for BD/DVD folders
            if (IsDiscFolder(item))
            {
                isEligibleForDirectPlay = false;
            }

            // A manifest is not a byte stream, so it cannot be handed to the client as one. The variant
            // and segment URIs inside it are relative to the origin and do not resolve against the
            // Jellyfin url the client would fetch it from.
            if (ContainerHelper.ContainsContainer(ManifestContainers, item.Container))
            {
                isEligibleForDirectPlay = false;
            }

            if (bitrateLimitExceeded)
            {
                transcodeReasons = TranscodeReason.ContainerBitrateExceedsLimit;
            }

            _logger.LogDebug(
                "Profile: {0}, Path: {1}, isEligibleForDirectPlay: {2}, isEligibleForDirectStream: {3}",
                options.Profile.Name ?? "Unknown Profile",
                item.Path ?? "Unknown path",
                isEligibleForDirectPlay,
                isEligibleForDirectStream);

            DirectPlayProfile? directPlayProfile = null;
            if (isEligibleForDirectPlay || isEligibleForDirectStream)
            {
                // See if it can be direct played
                var directPlayInfo = GetVideoDirectPlayProfile(options, item, videoStream, audioStream, candidateAudioStreams, subtitleStream, isEligibleForDirectPlay, isEligibleForDirectStream);
                var directPlay = directPlayInfo.PlayMethod;
                transcodeReasons |= directPlayInfo.TranscodeReasons;

                if (directPlay.HasValue)
                {
                    directPlayProfile = directPlayInfo.Profile;
                    playlistItem.PlayMethod = directPlay.Value;
                    playlistItem.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Video, directPlayProfile);
                    var videoCodec = videoStream?.Codec;
                    playlistItem.VideoCodecs = videoCodec is null ? [] : [videoCodec];

                    if (directPlay == PlayMethod.DirectPlay)
                    {
                        playlistItem.SubProtocol = MediaStreamProtocol.http;

                        var audioStreamIndex = directPlayInfo.AudioStreamIndex ?? audioStream?.Index;
                        SetDirectPlayAudioStream(playlistItem, item, audioStreamIndex);
                    }
                    else if (directPlay == PlayMethod.DirectStream)
                    {
                        playlistItem.AudioStreamIndex = audioStream?.Index;
                        SetDirectStreamAudioCodecs(playlistItem, audioStream, directPlayProfile);

                        SetStreamInfoOptionsFromDirectPlayProfile(options, item, playlistItem, directPlayProfile);
                        BuildStreamVideoItem(playlistItem, options, item, videoStream, audioStream, candidateAudioStreams, directPlayProfile?.Container, directPlayProfile?.VideoCodec, directPlayProfile?.AudioCodec);
                    }

                    SetDirectPlaySubtitle(item, options, playlistItem, subtitleStream, directPlay.Value, directPlayProfile);
                }

                _logger.LogDebug(
                    "DirectPlay Result for Profile: {0}, Path: {1}, PlayMethod: {2}, AudioStreamIndex: {3}, SubtitleStreamIndex: {4}, Reasons: {5}",
                    options.Profile.Name ?? "Anonymous Profile",
                    item.Path ?? "Unknown path",
                    directPlayInfo.PlayMethod,
                    directPlayInfo.AudioStreamIndex ?? audioStream?.Index,
                    playlistItem.SubtitleStreamIndex,
                    directPlayInfo.TranscodeReasons);
            }

            playlistItem.TranscodeReasons = transcodeReasons;

            ApplyVideoTranscodeProfile(item, options, playlistItem, videoStream, audioStream, candidateAudioStreams, subtitleStream);

            _logger.LogDebug(
                "StreamBuilder.BuildVideoItem( Profile={0}, Path={1}, AudioStreamIndex={2}, SubtitleStreamIndex={3} ) => ( PlayMethod={4}, TranscodeReason={5} ) {6}",
                options.Profile.Name ?? "Anonymous Profile",
                item.Path ?? "Unknown path",
                options.AudioStreamIndex,
                options.SubtitleStreamIndex,
                playlistItem.PlayMethod,
                playlistItem.TranscodeReasons,
                playlistItem.ToUrl("media:", "<token>", null));

            item.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Video, directPlayProfile);
            return playlistItem;
        }

        private static bool IsEligibleForDirectPlay(MediaOptions options, bool bitrateLimitExceeded)
        {
            return options.EnableDirectPlay && (options.ForceDirectPlay || !bitrateLimitExceeded);
        }

        private static bool IsEligibleForDirectStream(MediaOptions options, bool bitrateLimitExceeded)
        {
            return options.EnableDirectStream && (options.ForceDirectStream || !bitrateLimitExceeded);
        }

        private static bool IsDiscFolder(MediaSourceInfo item)
        {
            return item.VideoType == VideoType.Dvd || item.VideoType == VideoType.BluRay;
        }

        private static void SetDirectStreamAudioCodecs(StreamInfo playlistItem, MediaStream? audioStream, DirectPlayProfile? directPlayProfile)
        {
            if (audioStream is not null)
            {
                playlistItem.AudioCodecs = ContainerHelper.Split(directPlayProfile?.AudioCodec);
            }
        }

        private void SetDirectPlaySubtitle(MediaSourceInfo item, MediaOptions options, StreamInfo playlistItem, MediaStream? subtitleStream, PlayMethod directPlay, DirectPlayProfile? directPlayProfile)
        {
            if (subtitleStream is not null)
            {
                var subtitleProfile = GetSubtitleProfile(item, subtitleStream, options.Profile.SubtitleProfiles, directPlay, _transcoderSupport, directPlayProfile?.Container, null);

                playlistItem.SubtitleDeliveryMethod = subtitleProfile.Method;
                playlistItem.SubtitleFormat = subtitleProfile.Format;
            }
        }

        private static ICollection<MediaStream> GetCandidateAudioStreams(MediaSourceInfo item, MediaOptions options, MediaStream? audioStream)
        {
            ICollection<MediaStream> candidateAudioStreams = audioStream is null ? [] : [audioStream];
            // When the index is explicitly required by client or the default is specified by user, don't do any stream reselection.
            if (!item.DefaultAudioIndexSource.HasFlag(AudioIndexSource.User) && (options.AudioStreamIndex is null or < 0))
            {
                // When user has no preferences allow stream selection on all streams.
                candidateAudioStreams = SelectCandidatesWithoutPreference(item, audioStream, candidateAudioStreams);

                candidateAudioStreams = SelectCandidatesByLanguageOrDefault(item, audioStream, candidateAudioStreams);
            }

            return candidateAudioStreams;
        }

        private static ICollection<MediaStream> SelectCandidatesWithoutPreference(MediaSourceInfo item, MediaStream? audioStream, ICollection<MediaStream> candidateAudioStreams)
        {
            if (item.DefaultAudioIndexSource == AudioIndexSource.None && audioStream is not null)
            {
                candidateAudioStreams = item.MediaStreams.Where(stream => stream.Type == MediaStreamType.Audio).ToArray();
                if (audioStream.IsDefault)
                {
                    // If default is picked, only allow selection within default streams.
                    candidateAudioStreams = candidateAudioStreams.Where(stream => stream.IsDefault).ToArray();
                }
            }

            return candidateAudioStreams;
        }

        private static ICollection<MediaStream> SelectCandidatesByLanguageOrDefault(MediaSourceInfo item, MediaStream? audioStream, ICollection<MediaStream> candidateAudioStreams)
        {
            if (item.DefaultAudioIndexSource.HasFlag(AudioIndexSource.Language))
            {
                // If user has language preference, only allow stream selection within the same language.
                candidateAudioStreams = item.MediaStreams.Where(stream => stream.Type == MediaStreamType.Audio && stream.Language == audioStream?.Language).ToArray();
                if (item.DefaultAudioIndexSource.HasFlag(AudioIndexSource.Default))
                {
                    var defaultStreamsInPreferredLanguage = candidateAudioStreams.Where(stream => stream.IsDefault).ToArray();

                    // If the user also prefers default streams, try limit selection within default tracks in the same language.
                    // If there is no default stream in the preferred language, allow selection on all default streams to match the "Play default audio track regardless of language" setting.
                    candidateAudioStreams = defaultStreamsInPreferredLanguage.Length > 0
                        ? defaultStreamsInPreferredLanguage
                        : item.MediaStreams.Where(stream => stream.Type == MediaStreamType.Audio && stream.IsDefault).ToArray();
                }
            }
            else if (item.DefaultAudioIndexSource.HasFlag(AudioIndexSource.Default))
            {
                // If user prefers default streams, only allow stream selection on default streams.
                candidateAudioStreams = item.MediaStreams.Where(stream => stream.Type == MediaStreamType.Audio && stream.IsDefault).ToArray();
            }

            return candidateAudioStreams;
        }

        private static void SetDirectPlayAudioStream(StreamInfo playlistItem, MediaSourceInfo item, int? audioStreamIndex)
        {
            if (audioStreamIndex.HasValue)
            {
                playlistItem.AudioStreamIndex = audioStreamIndex;
                var audioCodec = item.GetMediaStream(MediaStreamType.Audio, audioStreamIndex.Value)?.Codec;
                playlistItem.AudioCodecs = audioCodec is null ? [] : [audioCodec];
            }
        }

        private void ApplyVideoTranscodeProfile(MediaSourceInfo item, MediaOptions options, StreamInfo playlistItem, MediaStream? videoStream, MediaStream? audioStream, ICollection<MediaStream> candidateAudioStreams, MediaStream? subtitleStream)
        {
            if (playlistItem.PlayMethod != PlayMethod.DirectStream && playlistItem.PlayMethod != PlayMethod.DirectPlay)
            {
                // Can't direct play, find the transcoding profile
                // If we do this for direct-stream we will overwrite the info
                var (transcodingProfile, playMethod) = GetVideoTranscodeProfile(item, options, videoStream, audioStream, playlistItem);

                if (transcodingProfile is not null && playMethod.HasValue)
                {
                    SetStreamInfoOptionsFromTranscodingProfile(item, playlistItem, transcodingProfile);

                    BuildStreamVideoItem(playlistItem, options, item, videoStream, audioStream, candidateAudioStreams, transcodingProfile.Container, transcodingProfile.VideoCodec, transcodingProfile.AudioCodec);

                    playlistItem.PlayMethod = PlayMethod.Transcode;

                    if (subtitleStream is not null)
                    {
                        var subtitleProfile = GetSubtitleProfile(item, subtitleStream, options.Profile.SubtitleProfiles, PlayMethod.Transcode, _transcoderSupport, transcodingProfile.Container, transcodingProfile.Protocol);
                        playlistItem.SubtitleDeliveryMethod = subtitleProfile.Method;
                        playlistItem.SubtitleFormat = subtitleProfile.Format;
                        playlistItem.SubtitleCodecs = [subtitleProfile.Format];
                    }

                    if ((playlistItem.TranscodeReasons & (VideoReasons | TranscodeReason.ContainerBitrateExceedsLimit)) != 0)
                    {
                        ApplyTranscodingConditions(playlistItem, transcodingProfile.Conditions, null, true, true);
                    }
                }
            }
        }

        private (TranscodingProfile? Profile, PlayMethod? PlayMethod) GetVideoTranscodeProfile(
            MediaSourceInfo item,
            MediaOptions options,
            MediaStream? videoStream,
            MediaStream? audioStream,
            StreamInfo playlistItem)
        {
            var mediaSource = playlistItem.MediaSource;

            ArgumentNullException.ThrowIfNull(mediaSource);

            if (!(item.SupportsTranscoding || item.SupportsDirectStream))
            {
                return (null, null);
            }

            var transcodingProfiles = options.Profile.TranscodingProfiles
                .Where(i => i.Type == playlistItem.MediaType && i.Context == options.Context);

            if (item.UseMostCompatibleTranscodingProfile)
            {
                transcodingProfiles = transcodingProfiles.Where(i => string.Equals(i.Container, "ts", StringComparison.OrdinalIgnoreCase));
            }

            var videoCodec = videoStream?.Codec;
            var audioCodec = audioStream?.Codec;

            var analyzedProfiles = transcodingProfiles
                .Select(transcodingProfile =>
                {
                    var rank = (Video: 3, Audio: 3);

                    var container = transcodingProfile.Container;

                    if (videoStream is not null
                        && options.AllowVideoStreamCopy
                        && ContainerHelper.ContainsContainer(transcodingProfile.VideoCodec, videoCodec))
                    {
                        var failures = GetCompatibilityVideoCodec(options, mediaSource, container, videoStream);
                        rank.Video = failures == 0 ? 1 : 2;
                    }

                    if (audioStream is not null
                        && options.AllowAudioStreamCopy)
                    {
                        // For Audio stream, we prefer the audio codec that can be directly copied, then the codec that can otherwise satisfies
                        // the transcoding conditions, then the one does not satisfy the transcoding conditions.
                        // For example: A client can support both aac and flac, but flac only supports 2 channels while aac supports 6.
                        // When the source audio is 6 channel flac, we should transcode to 6 channel aac, instead of down-mix to 2 channel flac.
                        var transcodingAudioCodecs = ContainerHelper.Split(transcodingProfile.AudioCodec);

                        foreach (var transcodingAudioCodec in transcodingAudioCodecs)
                        {
                            var failures = GetCompatibilityAudioCodec(options, mediaSource, container, audioStream, transcodingAudioCodec, true, false);

                            var rankAudio = 3;

                            if (failures == 0)
                            {
                                rankAudio = string.Equals(transcodingAudioCodec, audioCodec, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                            }

                            rank.Audio = Math.Min(rank.Audio, rankAudio);

                            if (rank.Audio == 1)
                            {
                                break;
                            }
                        }
                    }

                    PlayMethod playMethod = PlayMethod.Transcode;

                    if (rank.Video == 1)
                    {
                        playMethod = PlayMethod.DirectStream;
                    }

                    return (Profile: transcodingProfile, PlayMethod: playMethod, Rank: rank);
                })
                .OrderBy(analysis => analysis.Rank);

            var profileMatch = analyzedProfiles.FirstOrDefault();

            return (profileMatch.Profile, profileMatch.PlayMethod);
        }

        private (DirectPlayProfile? Profile, PlayMethod? PlayMethod, int? AudioStreamIndex, TranscodeReason TranscodeReasons) GetVideoDirectPlayProfile(
            MediaOptions options,
            MediaSourceInfo mediaSource,
            MediaStream? videoStream,
            MediaStream? audioStream,
            ICollection<MediaStream> candidateAudioStreams,
            MediaStream? subtitleStream,
            bool isEligibleForDirectPlay,
            bool isEligibleForDirectStream)
        {
            if (options.ForceDirectPlay)
            {
                return (null, PlayMethod.DirectPlay, audioStream?.Index, 0);
            }

            if (options.ForceDirectStream)
            {
                return (null, PlayMethod.DirectStream, audioStream?.Index, 0);
            }

            DeviceProfile profile = options.Profile;
            string container = mediaSource.Container;

            // Check container conditions
            var containerProfileReasons = GetCompatibilityContainer(options, mediaSource, container, videoStream);

            // Check video conditions
            var videoCodecProfileReasons = videoStream is null ? default : GetCompatibilityVideoCodec(options, mediaSource, container, videoStream);

            // Check audio candidates profile conditions
            var audioStreamMatches = candidateAudioStreams.ToDictionary(s => s, audioStream => GetCompatibilityAudioCodecDirect(options, mediaSource, container, audioStream, true, mediaSource.IsSecondaryAudio(audioStream) ?? false));

            TranscodeReason subtitleProfileReasons = GetDirectPlaySubtitleReasons(options, mediaSource, subtitleStream, container);

            var containerSupported = false;
            TranscodeReason[] rankings = [TranscodeReason.VideoCodecNotSupported, VideoCodecReasons, TranscodeReason.AudioCodecNotSupported, AudioCodecReasons, ContainerReasons];

            // Check DirectPlay profiles to see if it can be direct played
            var analyzedProfiles = profile.DirectPlayProfiles
                .Where(directPlayProfile => directPlayProfile.Type == DlnaProfileType.Video)
                .Select((directPlayProfile, order) =>
                {
                    TranscodeReason directPlayProfileReasons = 0;
                    TranscodeReason audioCodecProfileReasons = 0;

                    // Check container type
                    if (!directPlayProfile.SupportsContainer(container))
                    {
                        directPlayProfileReasons |= TranscodeReason.ContainerNotSupported;
                    }
                    else
                    {
                        containerSupported = true;
                    }

                    // Check video codec
                    string? videoCodec = videoStream?.Codec;
                    if (!directPlayProfile.SupportsVideoCodec(videoCodec))
                    {
                        directPlayProfileReasons |= TranscodeReason.VideoCodecNotSupported;
                    }

                    // Check audio codec
                    MediaStream? selectedAudioStream = null;
                    if (candidateAudioStreams.Count != 0)
                    {
                        selectedAudioStream = candidateAudioStreams.FirstOrDefault(audioStream => directPlayProfile.SupportsAudioCodec(audioStream.Codec));
                        if (selectedAudioStream is null)
                        {
                            directPlayProfileReasons |= TranscodeReason.AudioCodecNotSupported;
                        }
                        else
                        {
                            audioCodecProfileReasons = audioStreamMatches.GetValueOrDefault(selectedAudioStream);
                        }
                    }

                    var failureReasons = directPlayProfileReasons | containerProfileReasons | subtitleProfileReasons;

                    if ((failureReasons & TranscodeReason.VideoCodecNotSupported) == 0)
                    {
                        failureReasons |= videoCodecProfileReasons;
                    }

                    if ((failureReasons & TranscodeReason.AudioCodecNotSupported) == 0)
                    {
                        failureReasons |= audioCodecProfileReasons;
                    }

                    var directStreamFailureReasons = failureReasons & (~DirectStreamReasons);

                    PlayMethod? playMethod = null;
                    if (CanDirectPlayWith(failureReasons, isEligibleForDirectPlay, mediaSource))
                    {
                        playMethod = PlayMethod.DirectPlay;
                    }
                    else if (CanDirectStreamWith(directStreamFailureReasons, isEligibleForDirectStream, mediaSource))
                    {
                        playMethod = PlayMethod.DirectStream;
                    }

                    var ranked = GetRank(ref failureReasons, rankings);

                    return (Result: (Profile: directPlayProfile, PlayMethod: playMethod, AudioStreamIndex: selectedAudioStream?.Index, TranscodeReason: failureReasons), Order: order, Rank: ranked);
                })
                .OrderByDescending(analysis => analysis.Result.PlayMethod)
                .ThenByDescending(analysis => analysis.Rank)
                .ThenBy(analysis => analysis.Order)
                .ToArray()
                .ToLookup(analysis => analysis.Result.PlayMethod is not null);

            var profileMatch = analyzedProfiles[true]
                .Select(analysis => analysis.Result)
                .FirstOrDefault();
            if (profileMatch.Profile is not null)
            {
                return profileMatch;
            }

            var failureReasons = analyzedProfiles[false]
                .Select(analysis => analysis.Result)
                .Where(result => !containerSupported || !result.TranscodeReason.HasFlag(TranscodeReason.ContainerNotSupported))
                .FirstOrDefault().TranscodeReason;
            if (failureReasons == 0)
            {
                failureReasons = TranscodeReason.DirectPlayError;
            }

            return (Profile: null, PlayMethod: null, AudioStreamIndex: null, TranscodeReasons: failureReasons);
        }

        private static bool CanDirectPlayWith(TranscodeReason failureReasons, bool isEligibleForDirectPlay, MediaSourceInfo mediaSource)
        {
            return failureReasons == 0 && isEligibleForDirectPlay && mediaSource.SupportsDirectPlay;
        }

        private static bool CanDirectStreamWith(TranscodeReason directStreamFailureReasons, bool isEligibleForDirectStream, MediaSourceInfo mediaSource)
        {
            return directStreamFailureReasons == 0 && isEligibleForDirectStream && mediaSource.SupportsDirectStream;
        }

        private TranscodeReason GetDirectPlaySubtitleReasons(MediaOptions options, MediaSourceInfo mediaSource, MediaStream? subtitleStream, string container)
        {
            TranscodeReason subtitleProfileReasons = 0;
            if (subtitleStream is not null)
            {
                var subtitleProfile = GetSubtitleProfile(mediaSource, subtitleStream, options.Profile.SubtitleProfiles, PlayMethod.DirectPlay, _transcoderSupport, container, null);

                if (IsUnsupportedDirectPlaySubtitleMethod(subtitleProfile))
                {
                    _logger.LogDebug("Not eligible for {0} due to unsupported subtitles", PlayMethod.DirectPlay);
                    subtitleProfileReasons |= TranscodeReason.SubtitleCodecNotSupported;
                }
            }

            return subtitleProfileReasons;
        }

        private static bool IsUnsupportedDirectPlaySubtitleMethod(SubtitleProfile subtitleProfile)
        {
            return subtitleProfile.Method != SubtitleDeliveryMethod.Drop
                && subtitleProfile.Method != SubtitleDeliveryMethod.External
                && subtitleProfile.Method != SubtitleDeliveryMethod.Embed;
        }

        private bool IsBitrateLimitExceeded(MediaSourceInfo item, long maxBitrate)
        {
            // Don't restrict bitrate if item is remote.
            if (item.IsRemote)
            {
                return false;
            }

            // If no maximum bitrate is set, default to no maximum bitrate.
            long requestedMaxBitrate = maxBitrate > 0 ? maxBitrate : int.MaxValue;

            // If we don't know the item bitrate, then force a transcode if requested max bitrate is under 40 mbps
            int itemBitrate = item.Bitrate ?? 40000000;

            if (itemBitrate > requestedMaxBitrate)
            {
                _logger.LogDebug(
                    "Bitrate exceeds limit: media bitrate: {MediaBitrate}, max bitrate: {MaxBitrate}",
                    itemBitrate,
                    requestedMaxBitrate);
                return true;
            }

            return false;
        }

        private static void ValidateMediaOptions(MediaOptions options, bool isMediaSource)
        {
            if (options.ItemId.IsEmpty())
            {
                ArgumentException.ThrowIfNullOrEmpty(options.DeviceId);
            }

            if (options.Profile is null)
            {
                throw new ArgumentException("Profile is required");
            }

            if (options.MediaSources is null)
            {
                throw new ArgumentException("MediaSources is required");
            }

            if (isMediaSource)
            {
                if (options.AudioStreamIndex.HasValue && string.IsNullOrEmpty(options.MediaSourceId))
                {
                    throw new ArgumentException("MediaSourceId is required when a specific audio stream is requested");
                }

                if (options.SubtitleStreamIndex.HasValue && string.IsNullOrEmpty(options.MediaSourceId))
                {
                    throw new ArgumentException("MediaSourceId is required when a specific subtitle stream is requested");
                }
            }
        }

        private static bool IsAudioContainerSupported(DirectPlayProfile profile, MediaSourceInfo item)
        {
            // Check container type
            if (!profile.SupportsContainer(item.Container))
            {
                return false;
            }

            // Never direct play audio in matroska when the device only declare support for webm.
            // The first check is not enough because mkv is assumed can be webm.
            // See https://github.com/jellyfin/jellyfin/issues/13344
            return !ContainerHelper.ContainsContainer("mkv", item.Container)
                   || profile.SupportsContainer("mkv");
        }

        private static bool IsAudioDirectPlaySupported(DirectPlayProfile profile, MediaSourceInfo item, MediaStream audioStream)
        {
            if (!IsAudioContainerSupported(profile, item))
            {
                return false;
            }

            // Check audio codec
            string? audioCodec = audioStream?.Codec;
            if (!profile.SupportsAudioCodec(audioCodec))
            {
                return false;
            }

            return true;
        }

        private static bool IsAudioDirectStreamSupported(DirectPlayProfile profile, MediaSourceInfo item, MediaStream audioStream)
        {
            // Check container type, this should NOT be supported
            // If the container is supported, the file should be directly played
            if (IsAudioContainerSupported(profile, item))
            {
                return false;
            }

            // Check audio codec, we cannot use the SupportsAudioCodec here
            // Because that one assumes empty container supports all codec, which is just useless
            string? audioCodec = audioStream?.Codec;
            return string.Equals(profile.AudioCodec, audioCodec, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(profile.Container, audioCodec, StringComparison.OrdinalIgnoreCase);
        }

        private int GetRank(ref TranscodeReason a, TranscodeReason[] rankings)
        {
            var index = 1;
            foreach (var flag in rankings)
            {
                var reason = a & flag;
                if (reason != 0)
                {
                    return index;
                }

                index++;
            }

            return index;
        }
    }
}
