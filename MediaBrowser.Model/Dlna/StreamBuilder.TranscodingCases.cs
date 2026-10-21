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
            if (string.IsNullOrEmpty(qualifier))
            {
                if (!enableNonQualifiedConditions)
                {
                    return true;
                }
            }
            else
            {
                if (!enableQualifiedConditions)
                {
                    return true;
                }
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
    }
}
