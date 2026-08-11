// ============================================================================
// File: Diagnostics/SensitiveTagPolicy.cs
// Purpose: The tap value-privacy allowlist (ADR-0018A). Decides whether a
//          tag's live VALUE must be masked on a diagnostic surface. Built from
//          GatewaySettings.SensitiveTags — an explicit allowlist of exact tag
//          names and globs. NO value heuristics: OT-value sensitivity is
//          domain-specific and guessing creates false positives + false trust.
// Reference: ADR-0018A (Tap Value Privacy Policy), ADR-0018 Rule 6.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Decides whether a tag's live value is sensitive and must be masked on a
/// diagnostic surface (Live Data Tap). Explicit allowlist only — exact tag
/// names and globs (<c>*</c> matches any run, <c>?</c> matches one char),
/// matched case-insensitively against the canonical tag name.
/// </summary>
public sealed class SensitiveTagPolicy
{
    private readonly Regex[] _patterns;

    /// <summary>A policy that matches nothing.</summary>
    public static SensitiveTagPolicy Empty { get; } = new(Array.Empty<string>());

    /// <summary>
    /// Build a policy from the configured patterns. Blank/whitespace entries
    /// are ignored.
    /// </summary>
    public SensitiveTagPolicy(IReadOnlyList<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        _patterns = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Compile)
            .ToArray();
    }

    /// <summary>True when at least one pattern is configured.</summary>
    public bool HasRules => _patterns.Length > 0;

    /// <summary>
    /// True when <paramref name="tagName"/> matches any configured pattern and
    /// its value must therefore be masked. Always false when the policy is
    /// empty.
    /// </summary>
    public bool IsSensitive(string? tagName)
    {
        if (string.IsNullOrEmpty(tagName) || _patterns.Length == 0)
        {
            return false;
        }
        foreach (var re in _patterns)
        {
            if (re.IsMatch(tagName))
            {
                return true;
            }
        }
        return false;
    }

    // Glob → anchored regex. Escape everything, then re-open the two wildcards.
    // A pattern with no wildcard becomes an exact (case-insensitive) match.
    private static Regex Compile(string pattern)
    {
        var escaped = Regex.Escape(pattern.Trim())
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);
        return new Regex(
            "^" + escaped + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

/// <summary>
/// Holds the CURRENT <see cref="SensitiveTagPolicy"/> behind a volatile
/// reference so the tap masker always sees the latest policy after a config
/// reload — a privacy control must never go stale. <see cref="Set"/> is called
/// from the config-reload thread; <see cref="Current"/> is read from capture
/// threads.
/// </summary>
public sealed class SensitiveTagPolicyProvider
{
    private volatile SensitiveTagPolicy _current;

    /// <summary>Start with an initial policy (defaults to empty).</summary>
    public SensitiveTagPolicyProvider(SensitiveTagPolicy? initial = null)
    {
        _current = initial ?? SensitiveTagPolicy.Empty;
    }

    /// <summary>The policy in effect right now.</summary>
    public SensitiveTagPolicy Current => _current;

    /// <summary>Rebuild the policy from a new set of patterns (on config reload).</summary>
    public void Set(IReadOnlyList<string> patterns) =>
        _current = new SensitiveTagPolicy(patterns);
}
