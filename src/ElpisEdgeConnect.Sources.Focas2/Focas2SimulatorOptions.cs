// ============================================================================
// File: Focas2SimulatorOptions.cs
// Purpose: Parser + cache for EDGECONNECT_FOCAS2_SIMULATOR — the SINGLE
//          activation path for pointing FOCAS2 sources at an external CNC
//          simulator instead of the native Fwlib64 library.
//
//          Deliberately mirrors Focas2DemoModeOptions (M.2b.3.1): saved
//          configuration cannot enable it, only the process environment
//          variable can, and the cache is read once at first access and
//          immutable for the process lifetime. Toggling requires a restart.
//
//          ACCEPTED truthy values:  true, 1, yes  (case-insensitive, trimmed)
//          Anything else (false, 0, no, empty, unset) -> disabled.
//
//          Unlike demo mode, the simulator backend still honours the per
//          source IP address and port from configuration: the adapter dials
//          the address the operator configured, it is the wire protocol that
//          differs, not the topology.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Activation flag for the FOCAS2 simulator backend. Reads
/// <c>EDGECONNECT_FOCAS2_SIMULATOR</c> once and caches the result for the
/// process lifetime.
/// </summary>
/// <remarks>
/// <para>
/// This class is the ONLY supported path for enabling the simulator backend.
/// Saved configuration (gateway.json, draft API, any persisted store) cannot
/// activate it. The cache is read-once-and-frozen so the backend choice is
/// stable across the whole process — toggling requires a restart.
/// </para>
/// <para>
/// When both this and <see cref="Focas2DemoModeOptions"/> are enabled, the
/// simulator wins: it is the more specific request, since it names an
/// external system the operator deliberately started.
/// </para>
/// </remarks>
public static class Focas2SimulatorOptions
{
    /// <summary>The single environment variable that activates the simulator backend.</summary>
    public const string EnvVarName = "EDGECONNECT_FOCAS2_SIMULATOR";

    /// <summary>
    /// Message emitted via <c>LogCritical</c> at process startup when the
    /// simulator backend is active. Distinct, grep-friendly phrasing so log
    /// monitoring can pattern-match this specific entry without treating it
    /// as a system failure.
    /// </summary>
    public const string StartupCriticalMessage =
        "FOCAS2 SIMULATOR BACKEND ACTIVE — all FOCAS2 sources talk to an external CNC " +
        "simulator, not real controllers. Clear EDGECONNECT_FOCAS2_SIMULATOR and restart " +
        "for production behaviour.";

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
