using System.Collections.Generic;

namespace FontSelector.Services.OpenType;

/// <summary>
/// Provides high-performance binary search extensions for Unicode character range intervals.
/// </summary>
public static class UnicodeRangeExtensions
{
    /// <summary>
    /// Checks whether the specified Unicode code point is contained within the sorted range list.
    /// Uses binary search for nanosecond-level in-memory lookups.
    /// </summary>
    public static bool ContainsCodePoint(this IReadOnlyList<(uint start, uint end)> ranges, uint codePoint)
    {
        int left = 0;
        int right = ranges.Count - 1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            var (start, end) = ranges[mid];

            if (codePoint >= start && codePoint <= end)
                return true;

            if (codePoint < start)
                right = mid - 1;
            else
                left = mid + 1;
        }

        return false;
    }
}
