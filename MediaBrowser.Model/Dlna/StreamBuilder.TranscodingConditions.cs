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
        private static IEnumerable<ProfileCondition> GetProfileConditionsForVideoAudio(
            IEnumerable<CodecProfile> codecProfiles,
            string container,
            string codec,
            GetProfileConditionsForVideoAudioParts parts)
        {
            return codecProfiles
                .Where(profile => profile.Type == CodecType.VideoAudio &&
                    profile.ContainsAnyCodec(codec, container) &&
                    profile.ApplyConditions.All(applyCondition => ConditionProcessor.IsVideoAudioConditionSatisfied(applyCondition, parts.AudioChannels, parts.AudioBitrate, parts.AudioSampleRate, parts.AudioBitDepth, parts.AudioProfile, parts.IsSecondaryAudio)))
                .SelectMany(profile => profile.Conditions)
                .Where(condition => !ConditionProcessor.IsVideoAudioConditionSatisfied(condition, parts.AudioChannels, parts.AudioBitrate, parts.AudioSampleRate, parts.AudioBitDepth, parts.AudioProfile, parts.IsSecondaryAudio));
        }

        private static IEnumerable<ProfileCondition> GetProfileConditionsForAudio(
            IEnumerable<CodecProfile> codecProfiles,
            string container,
            GetProfileConditionsForAudioParts parts,
            bool checkConditions)
        {
            var conditions = codecProfiles
                .Where(profile => profile.Type == CodecType.Audio &&
                    profile.ContainsAnyCodec(parts.Codec, container) &&
                    profile.ApplyConditions.All(applyCondition => ConditionProcessor.IsAudioConditionSatisfied(applyCondition, parts.AudioChannels, parts.AudioBitrate, parts.AudioSampleRate, parts.AudioBitDepth)))
                .SelectMany(profile => profile.Conditions);

            if (!checkConditions)
            {
                return conditions;
            }

            return conditions.Where(condition => !ConditionProcessor.IsAudioConditionSatisfied(condition, parts.AudioChannels, parts.AudioBitrate, parts.AudioSampleRate, parts.AudioBitDepth));
        }

        private void ApplyTranscodingConditions(StreamInfo item, IEnumerable<ProfileCondition> conditions, ApplyTranscodingConditionsParts parts)
        {
            foreach (ProfileCondition condition in conditions)
            {
                string value = condition.Value;

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                // No way to express this
                if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    continue;
                }

                var context = new TranscodingCaseContext(item, condition, value, parts.Qualifier, parts.EnableQualifiedConditions, parts.EnableNonQualifiedConditions);

                if (TranscodingCaseIsSkipped(context))
                {
                    continue;
                }
            }
        }

        private readonly record struct GetProfileConditionsForVideoAudioParts(int? AudioChannels, int? AudioBitrate, int? AudioSampleRate, int? AudioBitDepth, string AudioProfile, bool? IsSecondaryAudio);

        private readonly record struct GetProfileConditionsForAudioParts(string? Codec, int? AudioChannels, int? AudioBitrate, int? AudioSampleRate, int? AudioBitDepth);

        private readonly record struct ApplyTranscodingConditionsParts(string? Qualifier, bool EnableQualifiedConditions, bool EnableNonQualifiedConditions);
    }
}
