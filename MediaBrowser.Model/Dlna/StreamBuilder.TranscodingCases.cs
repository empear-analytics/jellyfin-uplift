using System;
using System.Linq;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        private static bool IsDisabledByQualifier(string? qualifier, bool enableQualifiedConditions, bool enableNonQualifiedConditions)
        {
            var enabled = string.IsNullOrEmpty(qualifier) ? enableNonQualifiedConditions : enableQualifiedConditions;
            if (!enabled)
            {
                return true;
            }

            return false;
        }

        private static void ApplyVideoProfileCondition(StreamInfo item, ProfileCondition condition, string conditionValue, string? qualifier)
        {
            // Change from split by | to comma
            // Strip spaces to avoid having to encode
            var values = conditionValue
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
        }

        private static bool TranscodingCaseIsSkipped(TranscodingCaseContext context)
        {
            switch (context.Condition.Property)
            {
                case ProfileConditionValue.AudioBitrate:
                    return IsAudioBitrateCaseSkipped(context);

                case ProfileConditionValue.AudioSampleRate:
                    return IsAudioSampleRateCaseSkipped(context);

                case ProfileConditionValue.AudioChannels:
                    return IsAudioChannelsCaseSkipped(context);

                case ProfileConditionValue.IsAvc:
                    return IsAvcCaseSkipped(context);

                case ProfileConditionValue.IsAnamorphic:
                    return IsAnamorphicCaseSkipped(context);

                case ProfileConditionValue.IsInterlaced:
                    return IsInterlacedCaseSkipped(context);

                default:
                    return IsUnsupportedPropertyCaseSkipped(context);
            }
        }

        private static bool IsUnsupportedPropertyCaseSkipped(TranscodingCaseContext context)
        {
            if (IsNotSupportedYetProperty(context.Condition.Property))
            {
                return false;
            }

            switch (context.Condition.Property)
            {
                case ProfileConditionValue.RefFrames:
                    return IsRefFramesCaseSkipped(context);

                default:
                    return IsVideoProfileFamilyCaseSkipped(context);
            }
        }

        private static bool IsNotSupportedYetProperty(ProfileConditionValue property)
        {
            // Not supported yet
            return property is ProfileConditionValue.AudioProfile
                or ProfileConditionValue.Has64BitOffsets
                or ProfileConditionValue.PacketLength
                or ProfileConditionValue.NumStreams
                or ProfileConditionValue.NumAudioStreams
                or ProfileConditionValue.NumVideoStreams
                or ProfileConditionValue.IsSecondaryAudio
                or ProfileConditionValue.VideoTimestamp;
        }

        private static bool IsVideoProfileFamilyCaseSkipped(TranscodingCaseContext context)
        {
            switch (context.Condition.Property)
            {
                case ProfileConditionValue.VideoBitDepth:
                    return IsVideoBitDepthCaseSkipped(context);

                case ProfileConditionValue.VideoProfile:
                    return IsVideoProfileCaseSkipped(context);

                case ProfileConditionValue.VideoRangeType:
                    return IsVideoRangeTypeCaseSkipped(context);

                case ProfileConditionValue.VideoCodecTag:
                    return IsVideoCodecTagCaseSkipped(context);

                default:
                    return IsVideoOutputCaseSkipped(context);
            }
        }

        private static bool IsVideoOutputCaseSkipped(TranscodingCaseContext context)
        {
            switch (context.Condition.Property)
            {
                case ProfileConditionValue.VideoRotation:
                    return IsVideoRotationCaseSkipped(context);

                case ProfileConditionValue.Height:
                    return IsHeightCaseSkipped(context);

                case ProfileConditionValue.VideoBitrate:
                    return IsVideoBitrateCaseSkipped(context);

                case ProfileConditionValue.VideoFramerate:
                    return IsVideoFramerateCaseSkipped(context);

                case ProfileConditionValue.VideoLevel:
                    return IsVideoLevelCaseSkipped(context);

                case ProfileConditionValue.Width:
                    return IsWidthCaseSkipped(context);

                default:
                    break;
            }

            return false;
        }

        private static bool IsAudioBitrateCaseSkipped(TranscodingCaseContext context)
        {
            if (!context.EnableNonQualifiedConditions)
            {
                return true;
            }

            ApplyAudioBitrateCondition(context.Item, context.Condition, context.Value);

            return false;
        }

        private static bool IsAudioSampleRateCaseSkipped(TranscodingCaseContext context)
        {
            if (!context.EnableNonQualifiedConditions)
            {
                return true;
            }

            ApplyAudioSampleRateCondition(context.Item, context.Condition, context.Value);

            return false;
        }

        private static bool IsAudioChannelsCaseSkipped(TranscodingCaseContext context)
        {
            if (IsDisabledByQualifier(context.Qualifier, context.EnableQualifiedConditions, context.EnableNonQualifiedConditions))
            {
                return true;
            }

            ApplyAudioChannelsCondition(context.Item, context.Condition, context.Value, context.Qualifier);

            return false;
        }

        private static bool IsAvcCaseSkipped(TranscodingCaseContext context)
        {
            if (!context.EnableNonQualifiedConditions)
            {
                return true;
            }

            ApplyAvcCondition(context.Item, context.Condition, context.Value);

            return false;
        }

        private static bool IsAnamorphicCaseSkipped(TranscodingCaseContext context)
        {
            if (!context.EnableNonQualifiedConditions)
            {
                return true;
            }

            ApplyAnamorphicCondition(context.Item, context.Condition, context.Value);

            return false;
        }

        private static bool IsInterlacedCaseSkipped(TranscodingCaseContext context)
        {
            if (IsDisabledByQualifier(context.Qualifier, context.EnableQualifiedConditions, context.EnableNonQualifiedConditions))
            {
                return true;
            }

            ApplyInterlacedCondition(context.Item, context.Condition, context.Value, context.Qualifier);

            return false;
        }

        private static bool IsRefFramesCaseSkipped(TranscodingCaseContext context)
        {
            if (IsDisabledByQualifier(context.Qualifier, context.EnableQualifiedConditions, context.EnableNonQualifiedConditions))
            {
                return true;
            }

            ApplyRefFramesCondition(context.Item, context.Condition, context.Value, context.Qualifier);

            return false;
        }

        private static bool IsVideoBitDepthCaseSkipped(TranscodingCaseContext context)
        {
            if (IsDisabledByQualifier(context.Qualifier, context.EnableQualifiedConditions, context.EnableNonQualifiedConditions))
            {
                return true;
            }

            ApplyVideoBitDepthCondition(context.Item, context.Condition, context.Value, context.Qualifier);

            return false;
        }

        private static bool IsVideoProfileCaseSkipped(TranscodingCaseContext context)
        {
            if (string.IsNullOrEmpty(context.Qualifier))
            {
                return true;
            }

            ApplyVideoProfileCondition(context.Item, context.Condition, context.Value, context.Qualifier);

            return false;
        }

        private static bool IsVideoRangeTypeCaseSkipped(TranscodingCaseContext context)
        {
            if (string.IsNullOrEmpty(context.Qualifier))
            {
                return true;
            }

            ApplyVideoRangeTypeCondition(context.Item, context.Condition, context.Value, context.Qualifier);

            return false;
        }

        private static bool IsVideoCodecTagCaseSkipped(TranscodingCaseContext context)
        {
            if (string.IsNullOrEmpty(context.Qualifier))
            {
                return true;
            }

            ApplyVideoCodecTagCondition(context.Item, context.Condition, context.Value, context.Qualifier);

            return false;
        }

        private static bool IsVideoRotationCaseSkipped(TranscodingCaseContext context)
        {
            if (string.IsNullOrEmpty(context.Qualifier))
            {
                return true;
            }

            ApplyVideoRotationCondition(context.Item, context.Condition, context.Value, context.Qualifier);

            return false;
        }

        private static bool IsHeightCaseSkipped(TranscodingCaseContext context)
        {
            if (!context.EnableNonQualifiedConditions)
            {
                return true;
            }

            ApplyHeightCondition(context.Item, context.Condition, context.Value);

            return false;
        }

        private static bool IsVideoBitrateCaseSkipped(TranscodingCaseContext context)
        {
            if (!context.EnableNonQualifiedConditions)
            {
                return true;
            }

            ApplyVideoBitrateCondition(context.Item, context.Condition, context.Value);

            return false;
        }

        private static bool IsVideoFramerateCaseSkipped(TranscodingCaseContext context)
        {
            if (!context.EnableNonQualifiedConditions)
            {
                return true;
            }

            ApplyVideoFramerateCondition(context.Item, context.Condition, context.Value);

            return false;
        }

        private static bool IsVideoLevelCaseSkipped(TranscodingCaseContext context)
        {
            if (string.IsNullOrEmpty(context.Qualifier))
            {
                return true;
            }

            ApplyVideoLevelCondition(context.Item, context.Condition, context.Value, context.Qualifier);

            return false;
        }

        private static bool IsWidthCaseSkipped(TranscodingCaseContext context)
        {
            if (!context.EnableNonQualifiedConditions)
            {
                return true;
            }

            ApplyWidthCondition(context.Item, context.Condition, context.Value);

            return false;
        }

        private readonly record struct TranscodingCaseContext(StreamInfo Item, ProfileCondition Condition, string Value, string? Qualifier, bool EnableQualifiedConditions, bool EnableNonQualifiedConditions);
    }
}
