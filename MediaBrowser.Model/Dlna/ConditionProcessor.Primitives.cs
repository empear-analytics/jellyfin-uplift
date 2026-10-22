using System;
using System.Globalization;
using System.Numerics;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// The condition processor.
    /// </summary>
    public static partial class ConditionProcessor
    {
        private static bool IsConditionSatisfied(ProfileCondition condition, int? currentValue)
        {
            if (!currentValue.HasValue)
            {
                // If the value is unknown, it satisfies if not marked as required
                return !condition.IsRequired;
            }

            var conditionType = condition.Condition;
            if (condition.Condition == ProfileConditionType.EqualsAny)
            {
                return EqualsAnyInt(condition, currentValue);
            }

            if (int.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected))
            {
                return Compare(condition, conditionType, currentValue.Value, expected);
            }

            return false;
        }

        private static bool EqualsAnyInt(ProfileCondition condition, int? currentValue)
        {
            foreach (var singleConditionString in condition.Value.AsSpan().Split('|'))
            {
                if (int.TryParse(singleConditionString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int conditionValue)
                    && conditionValue.Equals(currentValue))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Compare<T>(ProfileCondition condition, ProfileConditionType conditionType, T currentValue, T expected)
            where T : IEquatable<T>, IComparisonOperators<T, T, bool>
        {
            switch (conditionType)
            {
                case ProfileConditionType.Equals:
                    return currentValue.Equals(expected);
                case ProfileConditionType.GreaterThanEqual:
                    return currentValue >= expected;
                case ProfileConditionType.LessThanEqual:
                    return currentValue <= expected;
                case ProfileConditionType.NotEquals:
                    return !currentValue.Equals(expected);
                default:
                    throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition);
            }
        }

        private static bool IsConditionSatisfied(ProfileCondition condition, string? currentValue)
        {
            if (string.IsNullOrEmpty(currentValue))
            {
                // If the value is unknown, it satisfies if not marked as required
                return !condition.IsRequired;
            }

            string expected = condition.Value;

            switch (condition.Condition)
            {
                case ProfileConditionType.EqualsAny:
                    return expected.Split('|').Contains(currentValue, StringComparison.OrdinalIgnoreCase);
                case ProfileConditionType.Equals:
                    return string.Equals(currentValue, expected, StringComparison.OrdinalIgnoreCase);
                case ProfileConditionType.NotEquals:
                    return !string.Equals(currentValue, expected, StringComparison.OrdinalIgnoreCase);
                default:
                    throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition);
            }
        }

        private static bool IsConditionSatisfied(ProfileCondition condition, bool? currentValue)
        {
            if (!currentValue.HasValue)
            {
                // If the value is unknown, it satisfies if not marked as required
                return !condition.IsRequired;
            }

            if (bool.TryParse(condition.Value, out var expected))
            {
                switch (condition.Condition)
                {
                    case ProfileConditionType.Equals:
                        return currentValue.Value == expected;
                    case ProfileConditionType.NotEquals:
                        return currentValue.Value != expected;
                    default:
                        throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition);
                }
            }

            return false;
        }

        private static bool IsConditionSatisfied(ProfileCondition condition, double? currentValue)
        {
            if (!currentValue.HasValue)
            {
                // If the value is unknown, it satisfies if not marked as required
                return !condition.IsRequired;
            }

            var conditionType = condition.Condition;
            if (condition.Condition == ProfileConditionType.EqualsAny)
            {
                foreach (var singleConditionString in condition.Value.AsSpan().Split('|'))
                {
                    if (double.TryParse(singleConditionString, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double conditionValue)
                        && conditionValue.Equals(currentValue))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (double.TryParse(condition.Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var expected))
            {
                return Compare(condition, conditionType, currentValue.Value, expected);
            }

            return false;
        }
    }
}
