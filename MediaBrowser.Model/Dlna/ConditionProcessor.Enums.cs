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
        private static bool IsConditionSatisfied(ProfileCondition condition, TransportStreamTimestamp? timestamp)
        {
            if (!timestamp.HasValue)
            {
                // If the value is unknown, it satisfies if not marked as required
                return !condition.IsRequired;
            }

            var expected = Enum.Parse<TransportStreamTimestamp>(condition.Value, true);

            switch (condition.Condition)
            {
                case ProfileConditionType.Equals:
                    return timestamp == expected;
                case ProfileConditionType.NotEquals:
                    return timestamp != expected;
                default:
                    throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition);
            }
        }

        private static bool IsConditionSatisfied(ProfileCondition condition, VideoRangeType? currentValue)
        {
            if (!currentValue.HasValue || currentValue.Equals(VideoRangeType.Unknown))
            {
                // If the value is unknown, it satisfies if not marked as required
                return !condition.IsRequired;
            }

            // Special case: HDR10 also satisfies if the video is HDR10Plus
            if (currentValue.Value == VideoRangeType.HDR10Plus)
            {
                if (IsConditionSatisfied(condition, VideoRangeType.HDR10))
                {
                    return true;
                }
            }

            var conditionType = condition.Condition;
            if (conditionType == ProfileConditionType.EqualsAny)
            {
                return EqualsAnyVideoRangeType(condition, currentValue);
            }

            if (Enum.TryParse(condition.Value, true, out VideoRangeType expected))
            {
                return CompareVideoRangeType(condition, conditionType, currentValue.Value, expected);
            }

            return false;
        }

        private static bool EqualsAnyVideoRangeType(ProfileCondition condition, VideoRangeType? currentValue)
        {
            foreach (var singleConditionString in condition.Value.AsSpan().Split('|'))
            {
                if (Enum.TryParse(singleConditionString, true, out VideoRangeType conditionValue)
                    && conditionValue.Equals(currentValue))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CompareVideoRangeType(ProfileCondition condition, ProfileConditionType conditionType, VideoRangeType currentValue, VideoRangeType expected)
        {
            return conditionType switch
            {
                ProfileConditionType.Equals => currentValue == expected,
                ProfileConditionType.NotEquals => currentValue != expected,
                _ => throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition)
            };
        }
    }
}
