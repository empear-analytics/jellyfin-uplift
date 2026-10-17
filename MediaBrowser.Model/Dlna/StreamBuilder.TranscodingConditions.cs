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

                switch (condition.Property)
                {
                    case ProfileConditionValue.AudioBitrate:
                        {
                            if (!enableNonQualifiedConditions)
                            {
                                continue;
                            }

                            ApplyAudioBitrateCondition(item, condition, value);

                            break;
                        }

                    case ProfileConditionValue.AudioSampleRate:
                        {
                            if (!enableNonQualifiedConditions)
                            {
                                continue;
                            }

                            ApplyAudioSampleRateCondition(item, condition, value);

                            break;
                        }

                    case ProfileConditionValue.AudioChannels:
                        {
                            if (string.IsNullOrEmpty(qualifier))
                            {
                                if (!enableNonQualifiedConditions)
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                if (!enableQualifiedConditions)
                                {
                                    continue;
                                }
                            }

                            ApplyAudioChannelsCondition(item, condition, value, qualifier);

                            break;
                        }

                    case ProfileConditionValue.IsAvc:
                        {
                            if (!enableNonQualifiedConditions)
                            {
                                continue;
                            }

                            ApplyAvcCondition(item, condition, value);

                            break;
                        }

                    case ProfileConditionValue.IsAnamorphic:
                        {
                            if (!enableNonQualifiedConditions)
                            {
                                continue;
                            }

                            if (bool.TryParse(value, out var isAnamorphic))
                            {
                                if (isAnamorphic && condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.RequireNonAnamorphic = true;
                                }
                                else if (!isAnamorphic && condition.Condition == ProfileConditionType.NotEquals)
                                {
                                    item.RequireNonAnamorphic = true;
                                }
                            }

                            break;
                        }

                    case ProfileConditionValue.IsInterlaced:
                        {
                            if (string.IsNullOrEmpty(qualifier))
                            {
                                if (!enableNonQualifiedConditions)
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                if (!enableQualifiedConditions)
                                {
                                    continue;
                                }
                            }

                            if (bool.TryParse(value, out var isInterlaced))
                            {
                                if (!isInterlaced && condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.SetOption(qualifier, "deinterlace", "true");
                                }
                                else if (isInterlaced && condition.Condition == ProfileConditionType.NotEquals)
                                {
                                    item.SetOption(qualifier, "deinterlace", "true");
                                }
                            }

                            break;
                        }

                    case ProfileConditionValue.AudioProfile:
                    case ProfileConditionValue.Has64BitOffsets:
                    case ProfileConditionValue.PacketLength:
                    case ProfileConditionValue.NumStreams:
                    case ProfileConditionValue.NumAudioStreams:
                    case ProfileConditionValue.NumVideoStreams:
                    case ProfileConditionValue.IsSecondaryAudio:
                    case ProfileConditionValue.VideoTimestamp:
                        {
                            // Not supported yet
                            break;
                        }

                    case ProfileConditionValue.RefFrames:
                        {
                            if (string.IsNullOrEmpty(qualifier))
                            {
                                if (!enableNonQualifiedConditions)
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                if (!enableQualifiedConditions)
                                {
                                    continue;
                                }
                            }

                            ApplyRefFramesCondition(item, condition, value, qualifier);

                            break;
                        }

                    case ProfileConditionValue.VideoBitDepth:
                        {
                            if (string.IsNullOrEmpty(qualifier))
                            {
                                if (!enableNonQualifiedConditions)
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                if (!enableQualifiedConditions)
                                {
                                    continue;
                                }
                            }

                            ApplyVideoBitDepthCondition(item, condition, value, qualifier);

                            break;
                        }

                    case ProfileConditionValue.VideoProfile:
                        {
                            if (string.IsNullOrEmpty(qualifier))
                            {
                                continue;
                            }

                            // Change from split by | to comma
                            // Strip spaces to avoid having to encode
                            var values = value
                                .Split('|', StringSplitOptions.RemoveEmptyEntries);

                            if (condition.Condition == ProfileConditionType.Equals)
                            {
                                item.SetOption(qualifier, "profile", string.Join(',', values));
                            }
                            else if (condition.Condition == ProfileConditionType.EqualsAny)
                            {
                                var currentValue = item.GetOption(qualifier, "profile");
                                if (!string.IsNullOrEmpty(currentValue) && values.Any(value => value == currentValue))
                                {
                                    item.SetOption(qualifier, "profile", currentValue);
                                }
                                else
                                {
                                    item.SetOption(qualifier, "profile", string.Join(',', values));
                                }
                            }

                            break;
                        }

                    case ProfileConditionValue.VideoRangeType:
                        {
                            if (string.IsNullOrEmpty(qualifier))
                            {
                                continue;
                            }

                            // change from split by | to comma
                            // strip spaces to avoid having to encode
                            var values = value
                                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                            if (condition.Condition == ProfileConditionType.Equals)
                            {
                                item.SetOption(qualifier, "rangetype", string.Join(',', values));
                            }
                            else if (condition.Condition == ProfileConditionType.NotEquals)
                            {
                                item.SetOption(qualifier, "rangetype", string.Join(',', Enum.GetNames<VideoRangeType>().Except(values)));
                            }
                            else if (condition.Condition == ProfileConditionType.EqualsAny)
                            {
                                var currentValue = item.GetOption(qualifier, "rangetype");
                                if (!string.IsNullOrEmpty(currentValue) && values.Any(v => string.Equals(v, currentValue, StringComparison.OrdinalIgnoreCase)))
                                {
                                    item.SetOption(qualifier, "rangetype", currentValue);
                                }
                                else
                                {
                                    item.SetOption(qualifier, "rangetype", string.Join(',', values));
                                }
                            }

                            break;
                        }

                    case ProfileConditionValue.VideoCodecTag:
                        {
                            if (string.IsNullOrEmpty(qualifier))
                            {
                                continue;
                            }

                            // change from split by | to comma
                            // strip spaces to avoid having to encode
                            var values = value
                                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                            if (condition.Condition == ProfileConditionType.Equals)
                            {
                                item.SetOption(qualifier, "codectag", string.Join(',', values));
                            }
                            else if (condition.Condition == ProfileConditionType.EqualsAny)
                            {
                                var currentValue = item.GetOption(qualifier, "codectag");
                                if (!string.IsNullOrEmpty(currentValue) && values.Any(v => string.Equals(v, currentValue, StringComparison.OrdinalIgnoreCase)))
                                {
                                    item.SetOption(qualifier, "codectag", currentValue);
                                }
                                else
                                {
                                    item.SetOption(qualifier, "codectag", string.Join(',', values));
                                }
                            }

                            break;
                        }

                    case ProfileConditionValue.VideoRotation:
                        {
                            if (string.IsNullOrEmpty(qualifier))
                            {
                                continue;
                            }

                            // change from split by | to comma
                            // strip spaces to avoid having to encode
                            var values = value
                                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                            if (condition.Condition == ProfileConditionType.Equals)
                            {
                                item.SetOption(qualifier, "rotation", string.Join(',', values));
                            }
                            else if (condition.Condition == ProfileConditionType.EqualsAny)
                            {
                                var currentValue = item.GetOption(qualifier, "rotation");
                                if (!string.IsNullOrEmpty(currentValue) && values.Any(v => string.Equals(v, currentValue, StringComparison.OrdinalIgnoreCase)))
                                {
                                    item.SetOption(qualifier, "rotation", currentValue);
                                }
                                else
                                {
                                    item.SetOption(qualifier, "rotation", string.Join(',', values));
                                }
                            }

                            break;
                        }

                    case ProfileConditionValue.Height:
                        {
                            if (!enableNonQualifiedConditions)
                            {
                                continue;
                            }

                            ApplyHeightCondition(item, condition, value);

                            break;
                        }

                    case ProfileConditionValue.VideoBitrate:
                        {
                            if (!enableNonQualifiedConditions)
                            {
                                continue;
                            }

                            ApplyVideoBitrateCondition(item, condition, value);

                            break;
                        }

                    case ProfileConditionValue.VideoFramerate:
                        {
                            if (!enableNonQualifiedConditions)
                            {
                                continue;
                            }

                            ApplyVideoFramerateCondition(item, condition, value);

                            break;
                        }

                    case ProfileConditionValue.VideoLevel:
                        {
                            if (string.IsNullOrEmpty(qualifier))
                            {
                                continue;
                            }

                            ApplyVideoLevelCondition(item, condition, value, qualifier);

                            break;
                        }

                    case ProfileConditionValue.Width:
                        {
                            if (!enableNonQualifiedConditions)
                            {
                                continue;
                            }

                            ApplyWidthCondition(item, condition, value);

                            break;
                        }

                    default:
                        break;
                }
            }
        }

        private static void ApplyAvcCondition(StreamInfo item, ProfileCondition condition, string value)
        {
            if (bool.TryParse(value, out var isAvc))
            {
                if (isAvc && condition.Condition == ProfileConditionType.Equals)
                {
                    item.RequireAvc = true;
                }
                else if (!isAvc && condition.Condition == ProfileConditionType.NotEquals)
                {
                    item.RequireAvc = true;
                }
            }
        }

        private static void ApplyVideoFramerateCondition(StreamInfo item, ProfileCondition condition, string value)
        {
            if (float.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.MaxFramerate = num;
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.MaxFramerate = Math.Min(num, item.MaxFramerate ?? num);
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.MaxFramerate = Math.Max(num, item.MaxFramerate ?? num);
                }
            }
        }

        private static void ApplyWidthCondition(StreamInfo item, ProfileCondition condition, string value)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.MaxWidth = num;
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.MaxWidth = Math.Min(num, item.MaxWidth ?? num);
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.MaxWidth = Math.Max(num, item.MaxWidth ?? num);
                }
            }
        }
    }
}
