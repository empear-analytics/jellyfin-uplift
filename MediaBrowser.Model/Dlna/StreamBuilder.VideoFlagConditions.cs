using System;
using System.Globalization;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
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

        private static void ApplyAnamorphicCondition(StreamInfo item, ProfileCondition condition, string value)
        {
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
        }

        private static void ApplyInterlacedCondition(StreamInfo item, ProfileCondition condition, string value, string? qualifier)
        {
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
        }
    }
}
