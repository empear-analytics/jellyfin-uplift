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
                var remuxContainer = GetAudioRemuxContainer(item, directPlayInfo);
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

            return OptimalAudioTranscodeStream(new OptimalAudioTranscodeContext(options, item, audioStream, playlistItem, transcodeReasons));
        }

        private StreamInfo? OptimalAudioTranscodeStream(OptimalAudioTranscodeContext context)
        {
            TranscodingProfile? transcodingProfile = FindAudioTranscodingProfile(context.Options, context.PlaylistItem);

            if (transcodingProfile is not null)
            {
                if (!context.Item.SupportsTranscoding)
                {
                    return null;
                }

                SetStreamInfoOptionsFromTranscodingProfile(context.Item, context.PlaylistItem, transcodingProfile);

                ApplyAudioTranscodingProfileSettings(context.Options, context.AudioStream, context.PlaylistItem, transcodingProfile);
            }

            context.PlaylistItem.TranscodeReasons = context.TranscodeReasons;
            return context.PlaylistItem;
        }

        private void ApplyAudioTranscodingProfileSettings(MediaOptions options, MediaStream audioStream, StreamInfo playlistItem, TranscodingProfile transcodingProfile)
        {
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

        private static string? GetAudioRemuxContainer(MediaSourceInfo item, (DirectPlayProfile? Profile, PlayMethod? PlayMethod, TranscodeReason TranscodeReasons) directPlayInfo)
        {
            var remuxContainer = item.TranscodingContainer ?? "ts";
            string[] supportedHlsContainers = ["ts", "mp4"];
            // If the container specified for the profile is an HLS supported container, use that container instead, overriding the preference
            // The client should be responsible to ensure this container is compatible
            remuxContainer = Array.Exists(supportedHlsContainers, element => string.Equals(element, directPlayInfo.Profile?.Container, StringComparison.OrdinalIgnoreCase)) ? directPlayInfo.Profile?.Container : remuxContainer;
            return remuxContainer;
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
                if (IsEncodableAudioTranscodingProfile(tcProfile, playlistItem, options))
                {
                    transcodingProfile = tcProfile;
                    break;
                }
            }

            return transcodingProfile;
        }

        private bool IsEncodableAudioTranscodingProfile(TranscodingProfile tcProfile, StreamInfo playlistItem, MediaOptions options)
        {
            return tcProfile.Type == playlistItem.MediaType
                && tcProfile.Context == options.Context
                && _transcoderSupport.CanEncodeToAudioCodec(tcProfile.AudioCodec ?? tcProfile.Container);
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

        private (DirectPlayProfile? Profile, PlayMethod? PlayMethod, TranscodeReason TranscodeReasons) GetAudioDirectPlayProfile(MediaSourceInfo item, MediaStream audioStream, MediaOptions options)
        {
            var directPlayProfile = options.Profile.DirectPlayProfiles
                .FirstOrDefault(x => IsSupportedAudioDirectPlayProfile(x, item, audioStream));

            TranscodeReason transcodeReasons = 0;
            if (directPlayProfile is null)
            {
                LogNoAudioDirectPlayProfile(item, audioStream, options);

                var directStreamProfile = options.Profile.DirectPlayProfiles
                    .FirstOrDefault(x => IsSupportedAudioDirectStreamProfile(x, item, audioStream));

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

            return GetAudioPlayMethodFromProfile(item, options, directPlayProfile, transcodeReasons);
        }

        private (DirectPlayProfile? Profile, PlayMethod? PlayMethod, TranscodeReason TranscodeReasons) GetAudioPlayMethodFromProfile(MediaSourceInfo item, MediaOptions options, DirectPlayProfile? directPlayProfile, TranscodeReason transcodeReasons)
        {
            // The profile describes what the device supports
            // If device requirements are satisfied then allow both direct stream and direct play
            // Note: As of 10.10 codebase, SupportsDirectPlay is always true because the MediaSourceInfo initializes this key as true
            // Need to check additionally for current transcode reasons
            if (CanDirectPlayWithoutReasons(item, transcodeReasons))
            {
                if (!IsAudioBitrateLimitExceeded(item, options))
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

            return GetAudioDirectStreamPlayMethod(item, options, directPlayProfile, transcodeReasons);
        }

        private (DirectPlayProfile? Profile, PlayMethod? PlayMethod, TranscodeReason TranscodeReasons) GetAudioDirectStreamPlayMethod(MediaSourceInfo item, MediaOptions options, DirectPlayProfile? directPlayProfile, TranscodeReason transcodeReasons)
        {
            // While options takes the network and other factors into account. Only applies to direct stream
            if (item.SupportsDirectStream)
            {
                if (!IsAudioBitrateLimitExceeded(item, options))
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

        private bool IsAudioBitrateLimitExceeded(MediaSourceInfo item, MediaOptions options)
        {
            return IsBitrateLimitExceeded(item, options.GetMaxBitrate(true) ?? 0);
        }

        private void LogNoAudioDirectPlayProfile(MediaSourceInfo item, MediaStream audioStream, MediaOptions options)
        {
            _logger.LogDebug(
                "Profile: {0}, No audio direct play profiles found for {1} with codec {2}",
                options.Profile.Name ?? "Unknown Profile",
                item.Path ?? "Unknown path",
                audioStream.Codec ?? "Unknown codec");
        }

        private static bool IsSupportedAudioDirectPlayProfile(DirectPlayProfile x, MediaSourceInfo item, MediaStream audioStream)
        {
            return x.Type == DlnaProfileType.Audio && IsAudioDirectPlaySupported(x, item, audioStream);
        }

        private static bool IsSupportedAudioDirectStreamProfile(DirectPlayProfile x, MediaSourceInfo item, MediaStream audioStream)
        {
            return x.Type == DlnaProfileType.Audio && IsAudioDirectStreamSupported(x, item, audioStream);
        }

        private static bool CanDirectPlayWithoutReasons(MediaSourceInfo item, TranscodeReason transcodeReasons)
        {
            return item.SupportsDirectPlay && transcodeReasons == 0;
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
                if (SupportsMediaTypeAndContainer(profile, mediaType, item))
                {
                    containerSupported = true;

                    videoSupported = SupportsVideoStreamCodec(profile, videoStream);

                    audioSupported = SupportsAudioStreamCodec(profile, audioStream);

                    if (videoSupported && audioSupported)
                    {
                        break;
                    }
                }
            }

            return GetUnsupportedReasons(containerSupported, videoSupported, audioSupported);
        }

        private static TranscodeReason GetUnsupportedReasons(bool containerSupported, bool videoSupported, bool audioSupported)
        {
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

        private static bool SupportsMediaTypeAndContainer(DirectPlayProfile profile, DlnaProfileType mediaType, MediaSourceInfo item)
        {
            return profile.Type == mediaType && profile.SupportsContainer(item.Container);
        }

        private static bool SupportsVideoStreamCodec(DirectPlayProfile profile, MediaStream? videoStream)
        {
            return videoStream is null || profile.SupportsVideoCodec(videoStream.Codec);
        }

        private static bool SupportsAudioStreamCodec(DirectPlayProfile profile, MediaStream audioStream)
        {
            return audioStream is null || profile.SupportsAudioCodec(audioStream.Codec);
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

        private readonly record struct OptimalAudioTranscodeContext(MediaOptions Options, MediaSourceInfo Item, MediaStream AudioStream, StreamInfo PlaylistItem, TranscodeReason TranscodeReasons);
    }
}
