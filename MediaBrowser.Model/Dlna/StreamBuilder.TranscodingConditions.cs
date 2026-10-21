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
            int? audioChannels,
            int? audioBitrate,
            int? audioSampleRate,
            int? audioBitDepth,
            string audioProfile,
            bool? isSecondaryAudio)
        {
            return codecProfiles
                .Where(profile => profile.Type == CodecType.VideoAudio &&
                    profile.ContainsAnyCodec(codec, container) &&
                    profile.ApplyConditions.All(applyCondition => ConditionProcessor.IsVideoAudioConditionSatisfied(applyCondition, audioChannels, audioBitrate, audioSampleRate, audioBitDepth, audioProfile, isSecondaryAudio)))
                .SelectMany(profile => profile.Conditions)
                .Where(condition => !ConditionProcessor.IsVideoAudioConditionSatisfied(condition, audioChannels, audioBitrate, audioSampleRate, audioBitDepth, audioProfile, isSecondaryAudio));
        }

        private static IEnumerable<ProfileCondition> GetProfileConditionsForAudio(
            IEnumerable<CodecProfile> codecProfiles,
            string container,
            string? codec,
            int? audioChannels,
            int? audioBitrate,
            int? audioSampleRate,
            int? audioBitDepth,
            bool checkConditions)
        {
            var conditions = codecProfiles
                .Where(profile => profile.Type == CodecType.Audio &&
                    profile.ContainsAnyCodec(codec, container) &&
                    profile.ApplyConditions.All(applyCondition => ConditionProcessor.IsAudioConditionSatisfied(applyCondition, audioChannels, audioBitrate, audioSampleRate, audioBitDepth)))
                .SelectMany(profile => profile.Conditions);

            if (!checkConditions)
            {
                return conditions;
            }

            return conditions.Where(condition => !ConditionProcessor.IsAudioConditionSatisfied(condition, audioChannels, audioBitrate, audioSampleRate, audioBitDepth));
        }

        private void ApplyTranscodingConditions(StreamInfo item, IEnumerable<ProfileCondition> conditions, string? qualifier, bool enableQualifiedConditions, bool enableNonQualifiedConditions)
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

                var context = new TranscodingCaseContext(item, condition, value, qualifier, enableQualifiedConditions, enableNonQualifiedConditions);

                if (TranscodingCaseIsSkipped(context))
                {
                    continue;
                }
            }
        }
    }
}
