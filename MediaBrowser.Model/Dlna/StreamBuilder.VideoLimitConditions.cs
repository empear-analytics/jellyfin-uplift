using System;
using System.Globalization;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        private static void ApplyHeightCondition(StreamInfo item, ProfileCondition condition, string value)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.MaxHeight = num;
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.MaxHeight = Math.Min(num, item.MaxHeight ?? num);
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.MaxHeight = Math.Max(num, item.MaxHeight ?? num);
                }
            }
        }

        private static void ApplyVideoBitrateCondition(StreamInfo item, ProfileCondition condition, string value)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.VideoBitrate = num;
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.VideoBitrate = Math.Min(num, item.VideoBitrate ?? num);
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.VideoBitrate = Math.Max(num, item.VideoBitrate ?? num);
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
    }
}
