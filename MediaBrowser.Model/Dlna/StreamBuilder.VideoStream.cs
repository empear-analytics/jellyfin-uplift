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
            // Prefer matching video codecs
            var videoCodecs = ContainerHelper.Split(videoCodec).ToList();

            if (videoCodecs.Count == 0 && videoStream is not null)
            {
                // Add the original codec if no codec is specified
                videoCodecs.Add(videoStream.Codec);
            }

            // Enforce HLS video codec restrictions
            if (playlistItem.SubProtocol == MediaStreamProtocol.hls)
            {
                videoCodecs = videoCodecs.Where(codec => _supportedHlsVideoCodecs.Contains(codec)).ToList();
            }

            playlistItem.VideoCodecs = videoCodecs;
            if (videoStream is not null && !ContainerHelper.ContainsContainer(videoCodecs, false, videoStream.Codec))
            {
                playlistItem.TranscodeReasons |= TranscodeReason.VideoCodecNotSupported;
            }

            // Copy video codec options as a starting point, this applies to transcode and direct-stream
            playlistItem.MaxFramerate = videoStream?.ReferenceFrameRate;
            var qualifier = videoStream?.Codec;
            if (videoStream?.Level is not null)
            {
                playlistItem.SetOption(qualifier, "level", videoStream.Level.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (videoStream?.BitDepth is not null)
            {
                playlistItem.SetOption(qualifier, "videobitdepth", videoStream.BitDepth.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(videoStream?.Profile))
            {
                playlistItem.SetOption(qualifier, "profile", videoStream.Profile.ToLowerInvariant());
            }

            // Prefer matching audio codecs, could do better here
            var audioCodecs = ContainerHelper.Split(audioCodec).ToList();

            if (audioCodecs.Count == 0 && audioStream is not null)
            {
                // Add the original codec if no codec is specified
                audioCodecs.Add(audioStream.Codec);
            }

            // Enforce HLS audio codec restrictions
            audioCodecs = FilterHlsAudioCodecs(playlistItem, audioCodecs);

            var audioStreamWithSupportedCodec = candidateAudioStreams.Where(stream => ContainerHelper.ContainsContainer(audioCodecs, false, stream.Codec)).FirstOrDefault();

            var channelsExceedsLimit = audioStreamWithSupportedCodec is not null && audioStreamWithSupportedCodec.Channels > (playlistItem.TranscodingMaxAudioChannels ?? int.MaxValue);

            var directAudioFailures = audioStreamWithSupportedCodec is null ? default : GetCompatibilityAudioCodec(options, item, container ?? string.Empty, audioStreamWithSupportedCodec, null, true, false);

            playlistItem.TranscodeReasons |= directAudioFailures;
            if (audioStream is not null && audioStreamWithSupportedCodec is null)
            {
                playlistItem.TranscodeReasons |= TranscodeReason.AudioCodecNotSupported;
            }

            var directAudioStreamSatisfied = audioStreamWithSupportedCodec is not null && !channelsExceedsLimit
                && directAudioFailures == 0;

            directAudioStreamSatisfied = directAudioStreamSatisfied && !playlistItem.TranscodeReasons.HasFlag(TranscodeReason.ContainerBitrateExceedsLimit);

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

                if (!string.IsNullOrEmpty(audioStream.Profile))
                {
                    playlistItem.SetOption(audioStream.Codec, "profile", audioStream.Profile.ToLowerInvariant());
                }

                if (audioStream.Level.HasValue && audioStream.Level.Value != 0)
                {
                    playlistItem.SetOption(audioStream.Codec, "level", audioStream.Level.Value.ToString(CultureInfo.InvariantCulture));
                }
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

            _logger.LogDebug(
                "Transcode Result for Profile: {Profile}, Path: {Path}, PlayMethod: {PlayMethod}, AudioStreamIndex: {AudioStreamIndex}, SubtitleStreamIndex: {SubtitleStreamIndex}, Reasons: {TranscodeReason}",
                options.Profile.Name ?? "Anonymous Profile",
                item.Path ?? "Unknown path",
                playlistItem.PlayMethod,
                audioStream?.Index,
                playlistItem.SubtitleStreamIndex,
                playlistItem.TranscodeReasons);
        }

        private static List<string> FilterHlsAudioCodecs(StreamInfo playlistItem, List<string> audioCodecs)
        {
            if (playlistItem.SubProtocol == MediaStreamProtocol.hls)
            {
                if (string.Equals(playlistItem.Container, "mp4", StringComparison.OrdinalIgnoreCase))
                {
                    audioCodecs = audioCodecs.Where(codec => _supportedHlsAudioCodecsMp4.Contains(codec)).ToList();
                }
                else
                {
                    audioCodecs = audioCodecs.Where(codec => _supportedHlsAudioCodecsTs.Contains(codec)).ToList();
                }
            }

            return audioCodecs;
        }

        private void ApplyVideoCodecProfileConditions(StreamInfo playlistItem, IEnumerable<CodecProfile> appliedVideoConditions, string? container, bool useSubContainer)
        {
            foreach (var condition in appliedVideoConditions)
            {
                foreach (var transcodingVideoCodec in playlistItem.VideoCodecs)
                {
                    if (condition.ContainsAnyCodec(transcodingVideoCodec, container, useSubContainer))
                    {
                        ApplyTranscodingConditions(playlistItem, condition.Conditions, transcodingVideoCodec, true, true);
                        continue;
                    }
                }
            }
        }

        private void ApplyAudioCodecProfileConditions(StreamInfo playlistItem, IEnumerable<CodecProfile> appliedAudioConditions, string? container)
        {
            foreach (var codecProfile in appliedAudioConditions)
            {
                foreach (var transcodingAudioCodec in playlistItem.AudioCodecs)
                {
                    if (codecProfile.ContainsAnyCodec(transcodingAudioCodec, container))
                    {
                        ApplyTranscodingConditions(playlistItem, codecProfile.Conditions, transcodingAudioCodec, true, true);
                        break;
                    }
                }
            }
        }

        private static void ApplyMaxVideoBitrate(StreamInfo playlistItem, int? maxBitrateSetting)
        {
            if (maxBitrateSetting.HasValue)
            {
                var availableBitrateForVideo = maxBitrateSetting.Value;

                if (playlistItem.AudioBitrate.HasValue)
                {
                    availableBitrateForVideo -= playlistItem.AudioBitrate.Value;
                }

                // Make sure the video bitrate is lower than bitrate settings but at least 64k
                // Don't use Math.Clamp as availableBitrateForVideo can be lower then 64k.
                var currentValue = playlistItem.VideoBitrate ?? availableBitrateForVideo;
                playlistItem.VideoBitrate = Math.Max(Math.Min(availableBitrateForVideo, currentValue), 64_000);
            }
        }
    }
}
