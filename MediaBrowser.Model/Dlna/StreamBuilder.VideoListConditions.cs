using System;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        private static void ApplyVideoRangeTypeCondition(StreamInfo item, ProfileCondition condition, string value, string? qualifier)
        {
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
        }
    }
}
