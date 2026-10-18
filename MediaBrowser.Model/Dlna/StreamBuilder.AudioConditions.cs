using System;
using System.Globalization;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        private static void ApplyAudioBitrateCondition(StreamInfo item, ProfileCondition condition, string value)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.AudioBitrate = num;
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.AudioBitrate = Math.Min(num, item.AudioBitrate ?? num);
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.AudioBitrate = Math.Max(num, item.AudioBitrate ?? num);
                }
            }
        }

        private static void ApplyAudioSampleRateCondition(StreamInfo item, ProfileCondition condition, string value)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.AudioSampleRate = num;
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.AudioSampleRate = Math.Min(num, item.AudioSampleRate ?? num);
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.AudioSampleRate = Math.Max(num, item.AudioSampleRate ?? num);
                }
            }
        }

        private static void ApplyAudioChannelsCondition(StreamInfo item, ProfileCondition condition, string value, string? qualifier)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.SetOption(qualifier, "audiochannels", num.ToString(CultureInfo.InvariantCulture));
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.SetOption(qualifier, "audiochannels", Math.Min(num, item.GetTargetAudioChannels(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.SetOption(qualifier, "audiochannels", Math.Max(num, item.GetTargetAudioChannels(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                }
            }
        }
    }
}
