// ============================================================================
// File: Focas2DemoModeOptions.cs
// Purpose: Parser + cache for EDGECONNECT_FOCAS2_FAKE_MODE — the SINGLE
//          activation path for FOCAS2 demo mode. Lock H of M.2b.3.1 v2:
//          saved configuration cannot enable demo mode. Only the process
//          environment variable can.
//
//          The cache is read once at first access (Locked F) and immutable
//          for the process lifetime. Toggling requires restart.
//
//          ACCEPTED truthy values:  true, 1, yes  (case-insensitive, trimmed)
//          Anything else (false, 0, no, empty, unset) → disabled.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md §1
//            (v3 amendment carries v2's §2 Locked A/F/H unchanged)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Activation flag for FOCAS2 demo mode. Reads
/// <c>EDGECONNECT_FOCAS2_FAKE_MODE</c> once and caches the result for the
/// process lifetime.
/// </summary>
/// <remarks>
/// <para>
/// This class is the ONLY supported path for enabling demo mode. Saved
/// configuration (gateway.json, draft API, any persisted store) cannot
/// activate the fake. The cache is read-once-and-frozen so demo state is
/// stable across the whole process — toggling requires a restart.
/// </para>
/// <para>
/// Test code can invoke <see cref="Reset"/> via <c>InternalsVisibleTo</c>
/// to clear the cache between tests; production callers have no way to
/// reset.
/// </para>
/// </remarks>
public static class Focas2DemoModeOptions
{
    /// <summary>The single environment variable that activates demo mode.</summary>
    public const string EnvVarName = "EDGECONNECT_FOCAS2_FAKE_MODE";

    /// <summary>
    /// Message emitted via <c>LogCritical</c> at process startup when demo
    /// mode is active. Distinct, grep-friendly phrasing so log monitoring
    /// can pattern-match this specific entry without treating it as a
    /// system failure.
    /// </summary>
    public const string StartupCriticalMessage =
        "FOCAS2 FAKE MODE ACTIVE — all FOCAS2 sources use a synthetic controller. " +
        "Clear EDGECONNECT_FOCAS2_FAKE_MODE and restart for production behaviour.";

    private static readonly object _gate = new();
    private static bool? _cached;

    /// <summary>
    /// True when the env var is set to a truthy value at first access.
    /// Frozen for the process lifetime after the first read.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            if (_cached is { } cached)
            {
                return cached;
            }
            lock (_gate)
            {
                if (_cached is { } cached2)
                {
                    return cached2;
                }
                _cached = ReadFromEnvironment();
                return _cached.Value;
            }
        }
    }

    private static bool ReadFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        var trimmed = raw.Trim();
        // Accepted truthy values: "true" (case-insensitive), "1", "yes" (case-insensitive).
        // Everything else (including "false", "0", "no", "anything-else") is treated as disabled.
        return string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || trimmed == "1"
            || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clear the cached value so the next access re-reads the env var.
    /// Intended for test cleanup only — there is no production-time path
    /// to call this; <c>InternalsVisibleTo</c> on the assembly grants
    /// access to the Sources.Focas2.Tests project.
    /// </summary>
    internal static void Reset()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }
}
