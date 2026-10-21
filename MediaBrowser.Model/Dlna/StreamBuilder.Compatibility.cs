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
        private TranscodeReason AggregateFailureConditions(MediaSourceInfo mediaSource, DeviceProfile profile, string type, IEnumerable<ProfileCondition> conditions)
        {
            return conditions.Aggregate<ProfileCondition, TranscodeReason>(0, (reasons, i) =>
            {
                LogConditionFailure(profile, type, i, mediaSource);
                var transcodeReasons = GetTranscodeReasonForFailedCondition(i);
                return reasons | transcodeReasons;
            });
        }

        private void LogConditionFailure(DeviceProfile profile, string type, ProfileCondition condition, MediaSourceInfo mediaSource)
        {
            _logger.LogDebug(
                "Profile: {0}, DirectPlay=false. Reason={1}.{2} Condition: {3}. ConditionValue: {4}. IsRequired: {5}. Path: {6}",
                type,
                profile.Name ?? "Unknown Profile",
                condition.Property,
                condition.Condition,
                condition.Value ?? string.Empty,
                condition.IsRequired,
                mediaSource.Path ?? "Unknown path");
        }

        /// <summary>
        /// Check the profile conditions.
        /// </summary>
        /// <param name="conditions">Profile conditions.</param>
        /// <param name="mediaSource">Media source.</param>
        /// <param name="videoStream">Video stream.</param>
        /// <returns>Failed profile conditions.</returns>
        private IEnumerable<ProfileCondition> CheckVideoConditions(ProfileCondition[] conditions, MediaSourceInfo mediaSource, MediaStream? videoStream)
        {
            int? width = videoStream?.Width;
            int? height = videoStream?.Height;
            int? bitDepth = videoStream?.BitDepth;
            int? videoBitrate = videoStream?.BitRate;
            double? videoLevel = videoStream?.Level;
            string? videoProfile = videoStream?.Profile;
            VideoRangeType? videoRangeType = videoStream?.VideoRangeType;
            float videoFramerate = videoStream is null ? 0 : videoStream.ReferenceFrameRate ?? 0;
            bool? isAnamorphic = videoStream?.IsAnamorphic;
            bool? isInterlaced = videoStream?.IsInterlaced;
            string? videoCodecTag = videoStream?.CodecTag;
            bool? isAvc = videoStream?.IsAVC;
            int? videoRotation = videoStream?.Rotation;

            TransportStreamTimestamp? timestamp = videoStream is null ? TransportStreamTimestamp.None : mediaSource.Timestamp;
            int? packetLength = videoStream?.PacketLength;
            int? refFrames = videoStream?.RefFrames;

            int numStreams = mediaSource.MediaStreams.Count;
            int? numAudioStreams = mediaSource.GetStreamCount(MediaStreamType.Audio);
            int? numVideoStreams = mediaSource.GetStreamCount(MediaStreamType.Video);

            return conditions.Where(applyCondition => !ConditionProcessor.IsVideoConditionSatisfied(applyCondition, width, height, bitDepth, videoBitrate, videoProfile, videoRangeType, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numStreams, numVideoStreams, numAudioStreams, videoCodecTag, isAvc, videoRotation));
        }

        /// <summary>
        /// Check the compatibility of the container.
        /// </summary>
        /// <param name="options">Media options.</param>
        /// <param name="mediaSource">Media source.</param>
        /// <param name="container">Container.</param>
        /// <param name="videoStream">Video stream.</param>
        /// <returns>Transcode reasons if the container is not fully compatible.</returns>
        private TranscodeReason GetCompatibilityContainer(MediaOptions options, MediaSourceInfo mediaSource, string container, MediaStream? videoStream)
        {
            var profile = options.Profile;

            var failures = AggregateFailureConditions(
                mediaSource,
                profile,
                "VideoCodecProfile",
                profile.ContainerProfiles
                    .Where(containerProfile => containerProfile.Type == DlnaProfileType.Video && containerProfile.ContainsContainer(container))
                    .SelectMany(containerProfile => CheckVideoConditions(containerProfile.Conditions, mediaSource, videoStream)));

            return failures;
        }

        /// <summary>
        /// Check the compatibility of the video codec.
        /// </summary>
        /// <param name="options">Media options.</param>
        /// <param name="mediaSource">Media source.</param>
        /// <param name="container">Container.</param>
        /// <param name="videoStream">Video stream.</param>
        /// <returns>Transcode reasons if the video stream is not fully compatible.</returns>
        private TranscodeReason GetCompatibilityVideoCodec(MediaOptions options, MediaSourceInfo mediaSource, string container, MediaStream videoStream)
        {
            var profile = options.Profile;

            string videoCodec = videoStream.Codec;

            var failures = AggregateFailureConditions(
                mediaSource,
                profile,
                "VideoCodecProfile",
                profile.CodecProfiles
                    .Where(codecProfile => codecProfile.Type == CodecType.Video &&
                        codecProfile.ContainsAnyCodec(videoCodec, container) &&
                        !CheckVideoConditions(codecProfile.ApplyConditions, mediaSource, videoStream).Any())
                    .SelectMany(codecProfile => CheckVideoConditions(codecProfile.Conditions, mediaSource, videoStream)));

            return failures;
        }

        /// <summary>
        /// Check the compatibility of the audio codec.
        /// </summary>
        /// <param name="options">Media options.</param>
        /// <param name="mediaSource">Media source.</param>
        /// <param name="container">Container.</param>
        /// <param name="audioStream">Audio stream.</param>
        /// <param name="transcodingAudioCodec">Override audio codec.</param>
        /// <param name="isVideo">The media source is video.</param>
        /// <param name="isSecondaryAudio">The audio stream is secondary.</param>
        /// <returns>Transcode reasons if the audio stream is not fully compatible.</returns>
        private TranscodeReason GetCompatibilityAudioCodec(MediaOptions options, MediaSourceInfo mediaSource, string container, MediaStream audioStream, string? transcodingAudioCodec, bool isVideo, bool isSecondaryAudio)
        {
            var profile = options.Profile;

            var audioCodec = transcodingAudioCodec ?? audioStream.Codec;
            var audioProfile = audioStream.Profile;
            var audioChannels = audioStream.Channels;
            var audioBitrate = audioStream.BitRate;
            var audioSampleRate = audioStream.SampleRate;
            var audioBitDepth = audioStream.BitDepth;

            var audioFailureConditions = isVideo
                ? GetProfileConditionsForVideoAudio(profile.CodecProfiles, container, audioCodec, new GetProfileConditionsForVideoAudioParts(audioChannels, audioBitrate, audioSampleRate, audioBitDepth, audioProfile, isSecondaryAudio))
                : GetProfileConditionsForAudio(profile.CodecProfiles, container, new GetProfileConditionsForAudioParts(audioCodec, audioChannels, audioBitrate, audioSampleRate, audioBitDepth), true);

            var failures = AggregateFailureConditions(mediaSource, profile, "AudioCodecProfile", audioFailureConditions);

            return failures;
        }

        /// <summary>
        /// Check the compatibility of the audio codec for direct playback.
        /// </summary>
        /// <param name="options">Media options.</param>
        /// <param name="mediaSource">Media source.</param>
        /// <param name="container">Container.</param>
        /// <param name="audioStream">Audio stream.</param>
        /// <param name="isVideo">The media source is video.</param>
        /// <param name="isSecondaryAudio">The audio stream is secondary.</param>
        /// <returns>Transcode reasons if the audio stream is not fully compatible for direct playback.</returns>
        private TranscodeReason GetCompatibilityAudioCodecDirect(MediaOptions options, MediaSourceInfo mediaSource, string container, MediaStream audioStream, bool isVideo, bool isSecondaryAudio)
        {
            var failures = GetCompatibilityAudioCodec(options, mediaSource, container, audioStream, null, isVideo, isSecondaryAudio);

            if (audioStream.IsExternal)
            {
                failures |= TranscodeReason.AudioIsExternal;
            }

            return failures;
        }
    }
}
