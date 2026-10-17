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
    }
}
