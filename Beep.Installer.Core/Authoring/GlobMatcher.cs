using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Beep.Installer.Engine;

/// <summary>
/// Real gitignore-style glob matcher (Track X5) replacing the old substring heuristic.
/// Supports: <c>**</c> (any directories, including zero), <c>*</c> (within a path segment),
/// <c>?</c> (single char), <c>[abc]</c> char classes, and <c>{dll,exe}</c> brace expansion.
/// Paths and patterns are normalized to '/'.
/// </summary>
public static class GlobMatcher
{
    /// <summary>True when <paramref name="path"/> matches <paramref name="pattern"/>.</summary>
    public static bool Matches(string path, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var p = (pattern ?? "").Trim().Replace('\\', '/');
        var h = (path ?? "").Replace('\\', '/').Trim();

        foreach (var expanded in ExpandBraces(p))
        {
            var regex = ToRegex(expanded);
            if (Regex.IsMatch(h, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="path"/> matches ANY of the <paramref name="patterns"/>.</summary>
    public static bool MatchesAny(string path, IEnumerable<string>? patterns)
        => patterns != null && patterns.Any(p => Matches(path, p));

    /// <summary>
    /// True when a file path is included: empty include list ⇒ include everything; otherwise the
    /// path must match at least one include pattern.
    /// </summary>
    public static bool IsIncluded(string path, IEnumerable<string>? includePatterns)
        => includePatterns == null || !includePatterns.Any() || MatchesAny(path, includePatterns);

    /// <summary>True when a file path is excluded (matches any exclude pattern).</summary>
    public static bool IsExcluded(string path, IEnumerable<string>? excludePatterns)
        => MatchesAny(path, excludePatterns);

    // ── brace expansion {a,b,c} ──
    private static List<string> ExpandBraces(string pattern)
    {
        var results = new List<string>();
        ExpandBraces(pattern, results);
        return results;
    }

    private static void ExpandBraces(string pattern, List<string> results)
    {
        var open = pattern.IndexOf('{');
        if (open < 0) { results.Add(pattern); return; }

        var close = pattern.IndexOf('}', open + 1);
        if (close < 0) { results.Add(pattern); return; } // unmatched → literal

        var prefix = pattern.Substring(0, open);
        var suffix = pattern.Substring(close + 1);
        var options = pattern.Substring(open + 1, close - open - 1).Split(',');

        foreach (var opt in options)
            ExpandBraces(prefix + opt + suffix, results);
    }

    // ── glob → regex ──
    private static string ToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('^');
        for (int i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    // "** /" → match any directory prefix (including none)
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        sb.Append("(?:.*/)?");
                        i += 2; // consumed "**"; loop consumes "/"
                    }
                    else
                    {
                        sb.Append(".*");
                        i += 1; // consumed first "*"; loop consumes second
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else if (c == '[')
            {
                var close = pattern.IndexOf(']', i + 1);
                if (close > i)
                {
                    sb.Append(pattern.Substring(i, close - i + 1));
                    i = close;
                }
                else
                {
                    sb.Append("\\[");
                }
            }
            else if (c == '/')
            {
                sb.Append("/");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}
