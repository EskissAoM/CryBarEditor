using System;
using System.Text.RegularExpressions;

namespace CryBar.Utilities;

/// <summary>
/// Lightweight glob matching for filter textboxes.
/// Empty pattern matches everything. Pattern without '*' matches via case-insensitive substring.
/// Pattern with '*' is treated as a regex (escaped literals, '*' becomes '.*'), matched
/// unanchored and case-insensitive. So 'villager*female' matches any path containing 'villager'
/// followed by anything followed by 'female'.
/// </summary>
public static class GlobMatcher
{
    public static bool IsMatch(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;

        if (pattern.IndexOf('*') < 0)
            return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        return GetOrBuild(pattern).IsMatch(input);
    }

    /// <summary>
    /// Converts a glob pattern (supporting only '*') into a regex body: each literal char is
    /// escaped and each '*' becomes '.*'. The result is unanchored.
    /// </summary>
    public static string ToRegexPattern(string globPattern)
        => Regex.Escape(globPattern).Replace("\\*", ".*");

    // Single-entry per-thread cache. Filter passes are sequential on the UI thread and reuse
    // the same pattern for every item in the list, so this collapses N regex builds to 1.
    [ThreadStatic] static string? _cachedPattern;
    [ThreadStatic] static Regex? _cachedRegex;

    static Regex GetOrBuild(string pattern)
    {
        if (!string.Equals(_cachedPattern, pattern, StringComparison.Ordinal))
        {
            _cachedRegex = new Regex(ToRegexPattern(pattern), RegexOptions.IgnoreCase);
            _cachedPattern = pattern;
        }
        return _cachedRegex!;
    }
}
