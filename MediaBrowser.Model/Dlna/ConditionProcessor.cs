using System;
using System.Globalization;
using System.Numerics;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// The condition processor.
    /// </summary>
    public static partial class ConditionProcessor
    {
        /// <summary>
        /// Checks if a video condition is satisfied.
        /// </summary>
        /// <param name="condition">The <see cref="ProfileCondition"/>.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="videoBitDepth">The bit depth.</param>
        /// <param name="videoBitrate">The bitrate.</param>
        /// <param name="videoProfile">The video profile.</param>
        /// <param name="videoRangeType">The <see cref="VideoRangeType"/>.</param>
        /// <param name="videoLevel">The video level.</param>
        /// <param name="videoFramerate">The framerate.</param>
        /// <param name="packetLength">The packet length.</param>
        /// <param name="timestamp">The <see cref="TransportStreamTimestamp"/>.</param>
        /// <param name="isAnamorphic">A value indicating whether the video is anamorphic.</param>
        /// <param name="isInterlaced">A value indicating whether the video is interlaced.</param>
        /// <param name="refFrames">The reference frames.</param>
        /// <param name="numStreams">The number of streams.</param>
        /// <param name="numVideoStreams">The number of video streams.</param>
        /// <param name="numAudioStreams">The number of audio streams.</param>
        /// <param name="videoCodecTag">The video codec tag.</param>
        /// <param name="isAvc">A value indicating whether the video is AVC.</param>
        /// <param name="videoRotation">The video rotation angle, usually 0 or +-90/180.</param>
        /// <returns><b>True</b> if the condition is satisfied.</returns>
        public static bool IsVideoConditionSatisfied(
            ProfileCondition condition,
            int? width,
            int? height,
            int? videoBitDepth,
            int? videoBitrate,
            string? videoProfile,
            VideoRangeType? videoRangeType,
            double? videoLevel,
            float? videoFramerate,
            int? packetLength,
            TransportStreamTimestamp? timestamp,
            bool? isAnamorphic,
            bool? isInterlaced,
            int? refFrames,
            int numStreams,
            int? numVideoStreams,
            int? numAudioStreams,
            string? videoCodecTag,
            bool? isAvc,
            int? videoRotation)
        {
            switch (condition.Property)
            {
                case ProfileConditionValue.IsInterlaced:
                    return IsConditionSatisfied(condition, isInterlaced);
                case ProfileConditionValue.IsAnamorphic:
                    return IsConditionSatisfied(condition, isAnamorphic);
                case ProfileConditionValue.IsAvc:
                    return IsConditionSatisfied(condition, isAvc);
                case ProfileConditionValue.VideoFramerate:
                    return IsConditionSatisfied(condition, videoFramerate);
                case ProfileConditionValue.VideoLevel:
                    return IsConditionSatisfied(condition, videoLevel);
                default:
                    return VideoConditionProfileRangeOrCodecTagSatisfied(new VideoConditionSizeContext(condition, refFrames, numStreams, numAudioStreams, numVideoStreams, timestamp, videoRotation, packetLength, videoBitDepth, videoBitrate, height, width), videoProfile, videoRangeType, videoCodecTag);
            }
        }

        /// <summary>
        /// Checks if a image condition is satisfied.
        /// </summary>
        /// <param name="condition">The <see cref="ProfileCondition"/>.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <returns><b>True</b> if the condition is satisfied.</returns>
        public static bool IsImageConditionSatisfied(ProfileCondition condition, int? width, int? height)
        {
            switch (condition.Property)
            {
                case ProfileConditionValue.Height:
                    return IsConditionSatisfied(condition, height);
                case ProfileConditionValue.Width:
                    return IsConditionSatisfied(condition, width);
                default:
                    throw new ArgumentException("Unexpected condition on image file: " + condition.Property);
            }
        }

        /// <summary>
        /// Checks if an audio condition is satisfied.
        /// </summary>
        /// <param name="condition">The <see cref="ProfileCondition"/>.</param>
        /// <param name="audioChannels">The channel count.</param>
        /// <param name="audioBitrate">The bitrate.</param>
        /// <param name="audioSampleRate">The sample rate.</param>
        /// <param name="audioBitDepth">The bit depth.</param>
        /// <returns><b>True</b> if the condition is satisfied.</returns>
        public static bool IsAudioConditionSatisfied(ProfileCondition condition, int? audioChannels, int? audioBitrate, int? audioSampleRate, int? audioBitDepth)
        {
            switch (condition.Property)
            {
                case ProfileConditionValue.AudioBitrate:
                    return IsConditionSatisfied(condition, audioBitrate);
                case ProfileConditionValue.AudioChannels:
                    return IsConditionSatisfied(condition, audioChannels);
                case ProfileConditionValue.AudioSampleRate:
                    return IsConditionSatisfied(condition, audioSampleRate);
                case ProfileConditionValue.AudioBitDepth:
                    return IsConditionSatisfied(condition, audioBitDepth);
                default:
                    throw new ArgumentException("Unexpected condition on audio file: " + condition.Property);
            }
        }

        /// <summary>
        /// Checks if an audio condition is satisfied for a video.
        /// </summary>
        /// <param name="condition">The <see cref="ProfileCondition"/>.</param>
        /// <param name="audioChannels">The channel count.</param>
        /// <param name="audioBitrate">The bitrate.</param>
        /// <param name="audioSampleRate">The sample rate.</param>
        /// <param name="audioBitDepth">The bit depth.</param>
        /// <param name="audioProfile">The profile.</param>
        /// <param name="isSecondaryTrack">A value indicating whether the audio is a secondary track.</param>
        /// <returns><b>True</b> if the condition is satisfied.</returns>
        public static bool IsVideoAudioConditionSatisfied(
            ProfileCondition condition,
            int? audioChannels,
            int? audioBitrate,
            int? audioSampleRate,
            int? audioBitDepth,
            string? audioProfile,
            bool? isSecondaryTrack)
        {
            switch (condition.Property)
            {
                case ProfileConditionValue.AudioProfile:
                    return IsConditionSatisfied(condition, audioProfile);
                case ProfileConditionValue.AudioBitrate:
                    return IsConditionSatisfied(condition, audioBitrate);
                case ProfileConditionValue.AudioChannels:
                    return IsConditionSatisfied(condition, audioChannels);
                case ProfileConditionValue.IsSecondaryAudio:
                    return IsConditionSatisfied(condition, isSecondaryTrack);
                case ProfileConditionValue.AudioSampleRate:
                    return IsConditionSatisfied(condition, audioSampleRate);
                case ProfileConditionValue.AudioBitDepth:
                    return IsConditionSatisfied(condition, audioBitDepth);
                default:
                    throw new ArgumentException("Unexpected condition on audio file: " + condition.Property);
            }
        }

        private static bool VideoConditionProfileRangeOrCodecTagSatisfied(VideoConditionSizeContext context, string? videoProfile, VideoRangeType? videoRangeType, string? videoCodecTag)
        {
            switch (context.Condition.Property)
            {
                case ProfileConditionValue.VideoProfile:
                    return IsConditionSatisfied(context.Condition, videoProfile);
                case ProfileConditionValue.VideoRangeType:
                    return IsConditionSatisfied(context.Condition, videoRangeType);
                case ProfileConditionValue.VideoCodecTag:
                    return IsConditionSatisfied(context.Condition, videoCodecTag);
                default:
                    return VideoConditionPacketBitrateOrSizeSatisfied(context);
            }
        }

        private static bool VideoConditionPacketBitrateOrSizeSatisfied(VideoConditionSizeContext context)
        {
            switch (context.Condition.Property)
            {
                case ProfileConditionValue.PacketLength:
                    return IsConditionSatisfied(context.Condition, context.PacketLength);
                case ProfileConditionValue.VideoBitDepth:
                    return IsConditionSatisfied(context.Condition, context.VideoBitDepth);
                case ProfileConditionValue.VideoBitrate:
                    return IsConditionSatisfied(context.Condition, context.VideoBitrate);
                case ProfileConditionValue.Height:
                    return IsConditionSatisfied(context.Condition, context.Height);
                case ProfileConditionValue.Width:
                    return IsConditionSatisfied(context.Condition, context.Width);
                default:
                    return VideoConditionRefFramesStreamsOrRotationSatisfied(new VideoConditionContext(context.Condition, context.RefFrames, context.NumStreams, context.NumAudioStreams, context.NumVideoStreams, context.Timestamp, context.VideoRotation));
            }
        }

        private static bool VideoConditionRefFramesStreamsOrRotationSatisfied(VideoConditionContext context)
        {
            switch (context.Condition.Property)
            {
                case ProfileConditionValue.RefFrames:
                    return IsConditionSatisfied(context.Condition, context.RefFrames);
                case ProfileConditionValue.NumStreams:
                    return IsConditionSatisfied(context.Condition, context.NumStreams);
                case ProfileConditionValue.NumAudioStreams:
                    return IsConditionSatisfied(context.Condition, context.NumAudioStreams);
                case ProfileConditionValue.NumVideoStreams:
                    return IsConditionSatisfied(context.Condition, context.NumVideoStreams);
                case ProfileConditionValue.VideoTimestamp:
                    return IsConditionSatisfied(context.Condition, context.Timestamp);
                case ProfileConditionValue.VideoRotation:
                    return IsConditionSatisfied(context.Condition, context.VideoRotation);
                default:
                    return true;
            }
        }

        private readonly record struct VideoConditionContext(
            ProfileCondition Condition,
            int? RefFrames,
            int NumStreams,
            int? NumAudioStreams,
            int? NumVideoStreams,
            TransportStreamTimestamp? Timestamp,
            int? VideoRotation);

        private readonly record struct VideoConditionSizeContext(
            ProfileCondition Condition,
            int? RefFrames,
            int NumStreams,
            int? NumAudioStreams,
            int? NumVideoStreams,
            TransportStreamTimestamp? Timestamp,
            int? VideoRotation,
            int? PacketLength,
            int? VideoBitDepth,
            int? VideoBitrate,
            int? Height,
            int? Width);
    }
}
