// ============================================================================
// File: Pipeline/Steps/GlobMatcher.cs
// Purpose: Small glob-pattern matcher for tag paths. Supports three tokens:
//            *   — zero or more characters within a single path segment
//            ?   — exactly one character within a single path segment
//            **  — zero or more characters INCLUDING the '/' separator
//          No regex support. Path separator is '/'.
// Reference: PHASE1_EXECUTION_PLAN.md C1, D6 decision: * ? ** only.
// ============================================================================

using System;
using System.Buffers;

namespace ElpisEdgeConnect.Core.Pipeline.Steps;

/// <summary>
/// Immutable compiled glob pattern. Build once per pattern and reuse.
/// </summary>
public sealed class GlobMatcher
{
    /// <summary>Matches everything (`*` wildcard shortcut).</summary>
    public static GlobMatcher MatchAll { get; } = new("**", GlobKind.MatchAll);

    private static readonly SearchValues<char> GlobTokens = SearchValues.Create("*?");

    private enum GlobKind
    {
        MatchAll,
        Exact,
        Wildcard,
    }

    private readonly string _pattern;
    private readonly GlobKind _kind;

    private GlobMatcher(string pattern, GlobKind kind)
    {
        _pattern = pattern;
        _kind = kind;
    }

    /// <summary>Original pattern string.</summary>
    public string Pattern => _pattern;

    /// <summary>Compile a glob pattern. Throws on empty/whitespace.</summary>
    public static GlobMatcher Compile(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Glob pattern must not be empty or whitespace.", nameof(pattern));
        }

        if (pattern == "*" || pattern == "**")
        {
            return MatchAll;
        }

        // Exact-literal fast path (no glob tokens).
        if (pattern.AsSpan().IndexOfAny(GlobTokens) < 0)
        {
            return new GlobMatcher(pattern, GlobKind.Exact);
        }

        return new GlobMatcher(pattern, GlobKind.Wildcard);
    }

    /// <summary>True if <paramref name="path"/> matches this pattern.</summary>
    public bool IsMatch(string path)
    {
        if (path is null)
        {
            return false;
        }

        return _kind switch
        {
            GlobKind.MatchAll => true,
            GlobKind.Exact => string.Equals(_pattern, path, StringComparison.Ordinal),
            _ => GlobMatch(_pattern, 0, path, 0),
        };
    }

    // Recursive backtracking matcher. Path-segment separator is '/'.
    //   *  matches zero or more chars that are NOT '/'
    //   ** matches zero or more chars INCLUDING '/'
    //   ?  matches exactly one char that is NOT '/'
    // Patterns are small; recursion depth is bounded by pattern length.
    private static bool GlobMatch(string pat, int pi, string path, int si)
    {
        while (pi < pat.Length)
        {
            char pc = pat[pi];

            if (pc == '*')
            {
                bool isDoubleStar = pi + 1 < pat.Length && pat[pi + 1] == '*';
                if (isDoubleStar)
                {
                    // Consume the "**". Try matching the remainder of the pattern
                    // against every suffix of path (including empty).
                    int nextPi = pi + 2;
                    for (int k = si; k <= path.Length; k++)
                    {
                        if (GlobMatch(pat, nextPi, path, k))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                // Single '*': match any run of non-'/' characters.
                int nextPi1 = pi + 1;
                for (int k = si; k <= path.Length; k++)
                {
                    if (GlobMatch(pat, nextPi1, path, k))
                    {
                        return true;
                    }
                    if (k < path.Length && path[k] == '/')
                    {
                        break;
                    }
                }
                return false;
            }

            if (pc == '?')
            {
                if (si >= path.Length || path[si] == '/')
                {
                    return false;
                }
                pi++;
                si++;
                continue;
            }

            if (si >= path.Length || path[si] != pc)
            {
                return false;
            }
            pi++;
            si++;
        }

        return si == path.Length;
    }
}
