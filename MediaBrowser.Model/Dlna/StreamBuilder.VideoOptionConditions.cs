using System;
using System.Globalization;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        private static void ApplyRefFramesCondition(StreamInfo item, ProfileCondition condition, string value, string? qualifier)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.SetOption(qualifier, "maxrefframes", num.ToString(CultureInfo.InvariantCulture));
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.SetOption(qualifier, "maxrefframes", Math.Min(num, item.GetTargetRefFrames(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.SetOption(qualifier, "maxrefframes", Math.Max(num, item.GetTargetRefFrames(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private static void ApplyVideoBitDepthCondition(StreamInfo item, ProfileCondition condition, string value, string? qualifier)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.SetOption(qualifier, "videobitdepth", num.ToString(CultureInfo.InvariantCulture));
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.SetOption(qualifier, "videobitdepth", Math.Min(num, item.GetTargetVideoBitDepth(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.SetOption(qualifier, "videobitdepth", Math.Max(num, item.GetTargetVideoBitDepth(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private static void ApplyVideoLevelCondition(StreamInfo item, ProfileCondition condition, string value, string? qualifier)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var num))
            {
                if (condition.Condition == ProfileConditionType.Equals)
                {
                    item.SetOption(qualifier, "level", num.ToString(CultureInfo.InvariantCulture));
                }
                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                {
                    item.SetOption(qualifier, "level", Math.Min(num, item.GetTargetVideoLevel(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                }
                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    item.SetOption(qualifier, "level", Math.Max(num, item.GetTargetVideoLevel(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                }
            }
        }
    }
}
