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
        private void BuildStreamVideoItem(StreamInfo playlistItem, MediaOptions options, BuildStreamVideoItemPartsFirst firstParts, BuildStreamVideoItemPartsSecond secondParts)
        {
            var audioStream = firstParts.AudioStream;
            var videoCodecs = GetPreferredVideoCodecs(playlistItem, firstParts.VideoStream, secondParts.VideoCodec);

            playlistItem.VideoCodecs = videoCodecs;
            MarkUnsupportedVideoCodec(playlistItem, firstParts.VideoStream, videoCodecs);

            // Copy video codec options as a starting point, this applies to transcode and direct-stream
            var qualifier = VideoStreamItemCopyVideoOptions(playlistItem, firstParts.VideoStream);

            var audioCodecs = GetPreferredAudioCodecs(playlistItem, audioStream, secondParts.AudioCodec);

            var audioStreamWithSupportedCodec = firstParts.CandidateAudioStreams.Where(stream => ContainerHelper.ContainsContainer(audioCodecs, false, stream.Codec)).FirstOrDefault();

            var channelsExceedsLimit = IsAudioChannelLimitExceeded(audioStreamWithSupportedCodec, playlistItem);

            var directAudioFailures = GetDirectAudioFailures(options, firstParts.Item, secondParts.Container, audioStreamWithSupportedCodec);

            playlistItem.TranscodeReasons |= directAudioFailures;
            MarkUnsupportedAudioCodec(playlistItem, audioStream, audioStreamWithSupportedCodec);

            var directAudioStreamSatisfied = IsDirectAudioStreamSatisfied(audioStreamWithSupportedCodec, channelsExceedsLimit, directAudioFailures, playlistItem);

            var directAudioStream = directAudioStreamSatisfied ? audioStreamWithSupportedCodec : null;

            ApplyAudioChannelLimit(playlistItem, channelsExceedsLimit);

            playlistItem.AudioCodecs = audioCodecs;
            if (directAudioStream is not null)
            {
                audioStream = directAudioStream;
                playlistItem.AudioStreamIndex = audioStream.Index;
                audioCodecs = [audioStream.Codec];
                playlistItem.AudioCodecs = audioCodecs;

                CopyDirectAudioOptions(playlistItem, audioStream, qualifier);
            }

            int? width = firstParts.VideoStream?.Width;
            int? height = firstParts.VideoStream?.Height;
            int? bitDepth = firstParts.VideoStream?.BitDepth;
            int? videoBitrate = firstParts.VideoStream?.BitRate;
            double? videoLevel = firstParts.VideoStream?.Level;
            string? videoProfile = firstParts.VideoStream?.Profile;
            VideoRangeType? videoRangeType = firstParts.VideoStream?.VideoRangeType;
            float videoFramerate = firstParts.VideoStream is null ? 0 : firstParts.VideoStream.ReferenceFrameRate ?? 0;
            bool? isAnamorphic = firstParts.VideoStream?.IsAnamorphic;
            bool? isInterlaced = firstParts.VideoStream?.IsInterlaced;
            string? videoCodecTag = firstParts.VideoStream?.CodecTag;
            bool? isAvc = firstParts.VideoStream?.IsAVC;
            int? videoRotation = firstParts.VideoStream?.Rotation;

            TransportStreamTimestamp? timestamp = firstParts.VideoStream is null ? TransportStreamTimestamp.None : firstParts.Item.Timestamp;
            int? packetLength = firstParts.VideoStream?.PacketLength;
            int? refFrames = firstParts.VideoStream?.RefFrames;

            int numStreams = firstParts.Item.MediaStreams.Count;
            int? numAudioStreams = firstParts.Item.GetStreamCount(MediaStreamType.Audio);
            int? numVideoStreams = firstParts.Item.GetStreamCount(MediaStreamType.Video);

            var useSubContainer = playlistItem.SubProtocol == MediaStreamProtocol.hls;

            var appliedVideoConditions = options.Profile.CodecProfiles
                .Where(i => i.Type == CodecType.Video &&
                    i.ContainsAnyCodec(playlistItem.VideoCodecs, secondParts.Container, useSubContainer) &&
                    i.ApplyConditions.All(applyCondition => ConditionProcessor.IsVideoConditionSatisfied(applyCondition, width, height, bitDepth, videoBitrate, videoProfile, videoRangeType, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numStreams, numVideoStreams, numAudioStreams, videoCodecTag, isAvc, videoRotation)))
                // Reverse codec profiles for backward compatibility - first codec profile has higher priority
                .Reverse();
            ApplyVideoCodecProfileConditions(playlistItem, appliedVideoConditions, secondParts.Container, useSubContainer);

            ApplyAudioLimits(options, playlistItem, channelsExceedsLimit, audioStream);

            var audioConditionContext = new AudioConditionContext(options, firstParts.Item, secondParts.Container, audioStream);
            ApplyAudioConditions(playlistItem, audioConditionContext);

            var maxBitrateSetting = options.GetMaxBitrate(false);
            // Honor max rate
            ApplyMaxVideoBitrate(playlistItem, maxBitrateSetting);

            LogTranscodeResult(options, firstParts.Item, playlistItem, audioStream);
        }

        private static string? VideoStreamItemCopyVideoOptions(StreamInfo playlistItem, MediaStream? videoStream)
        {
            playlistItem.MaxFramerate = videoStream?.ReferenceFrameRate;
            var qualifier = videoStream?.Codec;
            CopyVideoStreamOptions(playlistItem, videoStream, qualifier);
            return qualifier;
        }

        private static void CopyDirectAudioOptions(StreamInfo playlistItem, MediaStream audioStream, string? qualifier)
        {
            // Copy matching audio codec options
            playlistItem.AudioSampleRate = audioStream.SampleRate;
            playlistItem.SetOption(qualifier, "audiochannels", audioStream.Channels?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            CopyAudioProfileAndLevel(playlistItem, audioStream);
        }

        private void LogTranscodeResult(MediaOptions options, MediaSourceInfo item, StreamInfo playlistItem, MediaStream? audioStream)
        {
            _logger.LogDebug(
                "Transcode Result for Profile: {Profile}, Path: {Path}, PlayMethod: {PlayMethod}, AudioStreamIndex: {AudioStreamIndex}, SubtitleStreamIndex: {SubtitleStreamIndex}, Reasons: {TranscodeReason}",
                options.Profile.Name ?? "Anonymous Profile",
                item.Path ?? "Unknown path",
                playlistItem.PlayMethod,
                audioStream?.Index,
                playlistItem.SubtitleStreamIndex,
                playlistItem.TranscodeReasons);
        }

        private void ApplyAudioLimits(MediaOptions options, StreamInfo playlistItem, bool channelsExceedsLimit, MediaStream? audioStream)
        {
            // Honor requested max channels
            playlistItem.GlobalMaxAudioChannels = channelsExceedsLimit ? playlistItem.TranscodingMaxAudioChannels : options.MaxAudioChannels;

            ApplyAudioBitrateLimit(options, playlistItem, audioStream);
        }

        private void ApplyAudioConditions(StreamInfo playlistItem, AudioConditionContext context)
        {
            bool? isSecondaryAudio = context.AudioStream is null ? null : context.Item.IsSecondaryAudio(context.AudioStream);
            int? inputAudioBitrate = context.AudioStream?.BitRate;
            int? audioChannels = context.AudioStream?.Channels;
            string? audioProfile = context.AudioStream?.Profile;
            int? inputAudioSampleRate = context.AudioStream?.SampleRate;
            int? inputAudioBitDepth = context.AudioStream?.BitDepth;

            var appliedAudioConditions = context.Options.Profile.CodecProfiles
                .Where(i => i.Type == CodecType.VideoAudio &&
                    i.ContainsAnyCodec(playlistItem.AudioCodecs, context.Container) &&
                    i.ApplyConditions.All(applyCondition => ConditionProcessor.IsVideoAudioConditionSatisfied(applyCondition, audioChannels, inputAudioBitrate, inputAudioSampleRate, inputAudioBitDepth, audioProfile, isSecondaryAudio)))
                // Reverse codec profiles for backward compatibility - first codec profile has higher priority
                .Reverse();

            ApplyAudioCodecProfileConditions(playlistItem, appliedAudioConditions, context.Container);
        }

        private readonly record struct BuildStreamVideoItemPartsFirst(MediaSourceInfo Item, MediaStream? VideoStream, MediaStream? AudioStream, IEnumerable<MediaStream> CandidateAudioStreams);

        private readonly record struct BuildStreamVideoItemPartsSecond(string? Container, string? VideoCodec, string? AudioCodec);

        private readonly record struct AudioConditionContext(MediaOptions Options, MediaSourceInfo Item, string? Container, MediaStream? AudioStream);
    }
}
