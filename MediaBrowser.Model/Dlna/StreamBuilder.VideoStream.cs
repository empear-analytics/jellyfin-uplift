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
        private void BuildStreamVideoItem(
            StreamInfo playlistItem,
            MediaOptions options,
            MediaSourceInfo item,
            MediaStream? videoStream,
            MediaStream? audioStream,
            IEnumerable<MediaStream> candidateAudioStreams,
            string? container,
            string? videoCodec,
            string? audioCodec)
        {
            var videoCodecs = GetPreferredVideoCodecs(playlistItem, videoStream, videoCodec);

            playlistItem.VideoCodecs = videoCodecs;
            if (videoStream is not null && !ContainerHelper.ContainsContainer(videoCodecs, false, videoStream.Codec))
            {
                playlistItem.TranscodeReasons |= TranscodeReason.VideoCodecNotSupported;
            }

            // Copy video codec options as a starting point, this applies to transcode and direct-stream
            playlistItem.MaxFramerate = videoStream?.ReferenceFrameRate;
            var qualifier = videoStream?.Codec;
            CopyVideoStreamOptions(playlistItem, videoStream, qualifier);

            var audioCodecs = GetPreferredAudioCodecs(playlistItem, audioStream, audioCodec);

            var audioStreamWithSupportedCodec = candidateAudioStreams.Where(stream => ContainerHelper.ContainsContainer(audioCodecs, false, stream.Codec)).FirstOrDefault();

            var channelsExceedsLimit = IsAudioChannelLimitExceeded(audioStreamWithSupportedCodec, playlistItem);

            var directAudioFailures = audioStreamWithSupportedCodec is null ? default : GetCompatibilityAudioCodec(options, item, container ?? string.Empty, audioStreamWithSupportedCodec, null, true, false);

            playlistItem.TranscodeReasons |= directAudioFailures;
            if (audioStream is not null && audioStreamWithSupportedCodec is null)
            {
                playlistItem.TranscodeReasons |= TranscodeReason.AudioCodecNotSupported;
            }

            var directAudioStreamSatisfied = IsDirectAudioStreamSatisfied(audioStreamWithSupportedCodec, channelsExceedsLimit, directAudioFailures, playlistItem);

            var directAudioStream = directAudioStreamSatisfied ? audioStreamWithSupportedCodec : null;

            if (channelsExceedsLimit && playlistItem.TargetAudioStream is not null)
            {
                playlistItem.TranscodeReasons |= TranscodeReason.AudioChannelsNotSupported;
                playlistItem.TargetAudioStream.Channels = playlistItem.TranscodingMaxAudioChannels;
            }

            playlistItem.AudioCodecs = audioCodecs;
            if (directAudioStream is not null)
            {
                audioStream = directAudioStream;
                playlistItem.AudioStreamIndex = audioStream.Index;
                audioCodecs = [audioStream.Codec];
                playlistItem.AudioCodecs = audioCodecs;

                // Copy matching audio codec options
                playlistItem.AudioSampleRate = audioStream.SampleRate;
                playlistItem.SetOption(qualifier, "audiochannels", audioStream.Channels?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

                CopyAudioProfileAndLevel(playlistItem, audioStream);
            }

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

            TransportStreamTimestamp? timestamp = videoStream is null ? TransportStreamTimestamp.None : item.Timestamp;
            int? packetLength = videoStream?.PacketLength;
            int? refFrames = videoStream?.RefFrames;

            int numStreams = item.MediaStreams.Count;
            int? numAudioStreams = item.GetStreamCount(MediaStreamType.Audio);
            int? numVideoStreams = item.GetStreamCount(MediaStreamType.Video);

            var useSubContainer = playlistItem.SubProtocol == MediaStreamProtocol.hls;

            var appliedVideoConditions = options.Profile.CodecProfiles
                .Where(i => i.Type == CodecType.Video &&
                    i.ContainsAnyCodec(playlistItem.VideoCodecs, container, useSubContainer) &&
                    i.ApplyConditions.All(applyCondition => ConditionProcessor.IsVideoConditionSatisfied(applyCondition, width, height, bitDepth, videoBitrate, videoProfile, videoRangeType, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numStreams, numVideoStreams, numAudioStreams, videoCodecTag, isAvc, videoRotation)))
                // Reverse codec profiles for backward compatibility - first codec profile has higher priority
                .Reverse();
            ApplyVideoCodecProfileConditions(playlistItem, appliedVideoConditions, container, useSubContainer);

            // Honor requested max channels
            playlistItem.GlobalMaxAudioChannels = channelsExceedsLimit ? playlistItem.TranscodingMaxAudioChannels : options.MaxAudioChannels;

            int audioBitrate = GetAudioBitrate(options.GetMaxBitrate(true) ?? 0, playlistItem.TargetAudioCodec, audioStream, playlistItem);
            playlistItem.AudioBitrate = Math.Min(playlistItem.AudioBitrate ?? audioBitrate, audioBitrate);

            bool? isSecondaryAudio = audioStream is null ? null : item.IsSecondaryAudio(audioStream);
            int? inputAudioBitrate = audioStream?.BitRate;
            int? audioChannels = audioStream?.Channels;
            string? audioProfile = audioStream?.Profile;
            int? inputAudioSampleRate = audioStream?.SampleRate;
            int? inputAudioBitDepth = audioStream?.BitDepth;

            var appliedAudioConditions = options.Profile.CodecProfiles
                .Where(i => i.Type == CodecType.VideoAudio &&
                    i.ContainsAnyCodec(playlistItem.AudioCodecs, container) &&
                    i.ApplyConditions.All(applyCondition => ConditionProcessor.IsVideoAudioConditionSatisfied(applyCondition, audioChannels, inputAudioBitrate, inputAudioSampleRate, inputAudioBitDepth, audioProfile, isSecondaryAudio)))
                // Reverse codec profiles for backward compatibility - first codec profile has higher priority
                .Reverse();

            ApplyAudioCodecProfileConditions(playlistItem, appliedAudioConditions, container);

            var maxBitrateSetting = options.GetMaxBitrate(false);
            // Honor max rate
            ApplyMaxVideoBitrate(playlistItem, maxBitrateSetting);

            LogTranscodeResult(options, item, playlistItem, audioStream);
        }

        private static bool IsDirectAudioStreamSatisfied(MediaStream? audioStreamWithSupportedCodec, bool channelsExceedsLimit, TranscodeReason directAudioFailures, StreamInfo playlistItem)
        {
            var directAudioStreamSatisfied = audioStreamWithSupportedCodec is not null && !channelsExceedsLimit
                && directAudioFailures == 0;

            directAudioStreamSatisfied = directAudioStreamSatisfied && !playlistItem.TranscodeReasons.HasFlag(TranscodeReason.ContainerBitrateExceedsLimit);
            return directAudioStreamSatisfied;
        }

        private static List<string> GetPreferredAudioCodecs(StreamInfo playlistItem, MediaStream? audioStream, string? audioCodec)
        {
            // Prefer matching audio codecs, could do better here
            var audioCodecs = ContainerHelper.Split(audioCodec).ToList();

            if (audioCodecs.Count == 0 && audioStream is not null)
            {
                // Add the original codec if no codec is specified
                audioCodecs.Add(audioStream.Codec);
            }

            // Enforce HLS audio codec restrictions
            audioCodecs = FilterHlsAudioCodecs(playlistItem, audioCodecs);

            return audioCodecs;
        }

        private static bool IsAudioChannelLimitExceeded(MediaStream? audioStreamWithSupportedCodec, StreamInfo playlistItem)
        {
            return audioStreamWithSupportedCodec is not null && audioStreamWithSupportedCodec.Channels > (playlistItem.TranscodingMaxAudioChannels ?? int.MaxValue);
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
    }
}
