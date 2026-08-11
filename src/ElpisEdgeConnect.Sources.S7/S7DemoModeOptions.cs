// ============================================================================
// File: S7DemoModeOptions.cs
// Purpose: Parser + cache for EDGECONNECT_S7_FAKE_MODE — the SINGLE
//          activation path for Siemens S7 demo mode. Mirrors
//          Focas2DemoModeOptions exactly (ADR-0012 pattern): saved
//          configuration cannot enable demo mode; only the process
//          environment variable can, and the value is read once at first
//          access and frozen for the process lifetime (toggling requires a
//          restart).
//
//          ACCEPTED truthy values: true, 1, yes (case-insensitive, trimmed).
//          Anything else (false, 0, no, empty, unset) → disabled.
// Reference: docs/decisions/0029-s7-demo-mode.md (mirrors ADR-0012)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Activation flag for Siemens S7 demo mode. Reads
/// <c>EDGECONNECT_S7_FAKE_MODE</c> once and caches the result for the
/// process lifetime. The ONLY supported path for enabling demo mode —
/// saved configuration cannot activate the synthetic backend.
/// </summary>
public static class S7DemoModeOptions
{
    /// <summary>The single environment variable that activates demo mode.</summary>
    public const string EnvVarName = "EDGECONNECT_S7_FAKE_MODE";

    /// <summary>
    /// Message emitted via stderr / diagnostics at process startup when demo
    /// mode is active. Distinct, grep-friendly phrasing so log monitoring can
    /// pattern-match this specific entry without treating it as a failure.
    /// </summary>
    public const string StartupCriticalMessage =
        "S7 FAKE MODE ACTIVE — all Siemens S7 sources use a synthetic PLC. " +
        "Clear EDGECONNECT_S7_FAKE_MODE and restart for production behaviour.";

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
        return string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || trimmed == "1"
            || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clear the cached value so the next access re-reads the env var.
    /// Test-cleanup only — no production path calls this
    /// (<c>InternalsVisibleTo</c> grants the Sources.S7.Tests project access).
    /// </summary>
    internal static void Reset()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }
}
