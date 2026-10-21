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
                    {
                        if (!context.EnableNonQualifiedConditions)
                        {
                            return true;
                        }

                        ApplyAudioBitrateCondition(context.Item, context.Condition, context.Value);

                        break;
                    }

                case ProfileConditionValue.AudioSampleRate:
                    {
                        if (!context.EnableNonQualifiedConditions)
                        {
                            return true;
                        }

                        ApplyAudioSampleRateCondition(context.Item, context.Condition, context.Value);

                        break;
                    }

                case ProfileConditionValue.AudioChannels:
                    {
                        if (IsDisabledByQualifier(context.Qualifier, context.EnableQualifiedConditions, context.EnableNonQualifiedConditions))
                        {
                            return true;
                        }

                        ApplyAudioChannelsCondition(context.Item, context.Condition, context.Value, context.Qualifier);

                        break;
                    }

                case ProfileConditionValue.IsAvc:
                    {
                        if (!context.EnableNonQualifiedConditions)
                        {
                            return true;
                        }

                        ApplyAvcCondition(context.Item, context.Condition, context.Value);

                        break;
                    }

                case ProfileConditionValue.IsAnamorphic:
                    {
                        if (!context.EnableNonQualifiedConditions)
                        {
                            return true;
                        }

                        ApplyAnamorphicCondition(context.Item, context.Condition, context.Value);

                        break;
                    }

                case ProfileConditionValue.IsInterlaced:
                    {
                        if (IsDisabledByQualifier(context.Qualifier, context.EnableQualifiedConditions, context.EnableNonQualifiedConditions))
                        {
                            return true;
                        }

                        ApplyInterlacedCondition(context.Item, context.Condition, context.Value, context.Qualifier);

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
                        if (IsDisabledByQualifier(context.Qualifier, context.EnableQualifiedConditions, context.EnableNonQualifiedConditions))
                        {
                            return true;
                        }

                        ApplyRefFramesCondition(context.Item, context.Condition, context.Value, context.Qualifier);

                        break;
                    }

                case ProfileConditionValue.VideoBitDepth:
                    {
                        if (IsDisabledByQualifier(context.Qualifier, context.EnableQualifiedConditions, context.EnableNonQualifiedConditions))
                        {
                            return true;
                        }

                        ApplyVideoBitDepthCondition(context.Item, context.Condition, context.Value, context.Qualifier);

                        break;
                    }

                case ProfileConditionValue.VideoProfile:
                    {
                        if (string.IsNullOrEmpty(context.Qualifier))
                        {
                            return true;
                        }

                        ApplyVideoProfileCondition(context.Item, context.Condition, context.Value, context.Qualifier);

                        break;
                    }

                case ProfileConditionValue.VideoRangeType:
                    {
                        if (string.IsNullOrEmpty(context.Qualifier))
                        {
                            return true;
                        }

                        ApplyVideoRangeTypeCondition(context.Item, context.Condition, context.Value, context.Qualifier);

                        break;
                    }

                case ProfileConditionValue.VideoCodecTag:
                    {
                        if (string.IsNullOrEmpty(context.Qualifier))
                        {
                            return true;
                        }

                        ApplyVideoCodecTagCondition(context.Item, context.Condition, context.Value, context.Qualifier);

                        break;
                    }

                case ProfileConditionValue.VideoRotation:
                    {
                        if (string.IsNullOrEmpty(context.Qualifier))
                        {
                            return true;
                        }

                        ApplyVideoRotationCondition(context.Item, context.Condition, context.Value, context.Qualifier);

                        break;
                    }

                case ProfileConditionValue.Height:
                    {
                        if (!context.EnableNonQualifiedConditions)
                        {
                            return true;
                        }

                        ApplyHeightCondition(context.Item, context.Condition, context.Value);

                        break;
                    }

                case ProfileConditionValue.VideoBitrate:
                    {
                        if (!context.EnableNonQualifiedConditions)
                        {
                            return true;
                        }

                        ApplyVideoBitrateCondition(context.Item, context.Condition, context.Value);

                        break;
                    }

                case ProfileConditionValue.VideoFramerate:
                    {
                        if (!context.EnableNonQualifiedConditions)
                        {
                            return true;
                        }

                        ApplyVideoFramerateCondition(context.Item, context.Condition, context.Value);

                        break;
                    }

                case ProfileConditionValue.VideoLevel:
                    {
                        if (string.IsNullOrEmpty(context.Qualifier))
                        {
                            return true;
                        }

                        ApplyVideoLevelCondition(context.Item, context.Condition, context.Value, context.Qualifier);

                        break;
                    }

                case ProfileConditionValue.Width:
                    {
                        if (!context.EnableNonQualifiedConditions)
                        {
                            return true;
                        }

                        ApplyWidthCondition(context.Item, context.Condition, context.Value);

                        break;
                    }

                default:
                    break;
            }

            return false;
        }

        private readonly record struct TranscodingCaseContext(StreamInfo Item, ProfileCondition Condition, string Value, string? Qualifier, bool EnableQualifiedConditions, bool EnableNonQualifiedConditions);
    }
}
