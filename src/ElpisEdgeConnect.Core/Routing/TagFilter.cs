// ============================================================================
// File: Routing/TagFilter.cs
// Purpose: Runtime implementation of TagFilterConfig. Compiles include/exclude
//          glob patterns into regex once at construction and evaluates each
//          CanonicalDataPoint.TagName against them.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4, PHASE1_EXECUTION_PLAN.md C3
// Milestone: C3 Commit 2 (phase 1 — happy path)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Compiled tag filter: evaluates whether a given
/// <see cref="CanonicalDataPoint.TagName"/> is admitted by the route's
/// <see cref="TagFilterConfig"/>. Matching is case-insensitive per blueprint
/// §4.4. Includes apply first; excludes override.
/// </summary>
public sealed class TagFilter
{
    /// <summary>Singleton admitting every tag (used when no filter is configured).</summary>
    public static TagFilter AcceptAll { get; } = new(new TagFilterConfig());

    private readonly Regex[] _include;
    private readonly Regex[] _exclude;
    private readonly bool _includeAll;

    /// <summary>Compile a <see cref="TagFilterConfig"/> into an efficient matcher.</summary>
    public TagFilter(TagFilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _include = Compile(config.Include);
        _exclude = Compile(config.Exclude);
        _includeAll = _include.Length == 0
            || (config.Include.Count == 1 && config.Include[0] == "*");
    }

    /// <summary>True if <paramref name="point"/>'s tag is admitted by this filter.</summary>
    public bool IsMatch(CanonicalDataPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return IsMatch(point.TagName);
    }

    /// <summary>True if <paramref name="tagName"/> is admitted by this filter.</summary>
    public bool IsMatch(string tagName)
    {
        ArgumentNullException.ThrowIfNull(tagName);

        if (!_includeAll)
        {
            var matched = false;
            foreach (var pattern in _include)
            {
                if (pattern.IsMatch(tagName))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                return false;
            }
        }

        foreach (var pattern in _exclude)
        {
            if (pattern.IsMatch(tagName))
            {
                return false;
            }
        }

        return true;
    }

    private static Regex[] Compile(IReadOnlyList<string>? globs)
    {
        if (globs is null || globs.Count == 0)
        {
            return Array.Empty<Regex>();
        }

        var regexes = new Regex[globs.Count];
        for (var i = 0; i < globs.Count; i++)
        {
            regexes[i] = new Regex(
                GlobToRegex(globs[i]),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
        return regexes;
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder(glob.Length + 4);
        sb.Append('^');
        foreach (var c in glob)
        {
            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    break;
                case '?':
                    sb.Append('.');
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}
