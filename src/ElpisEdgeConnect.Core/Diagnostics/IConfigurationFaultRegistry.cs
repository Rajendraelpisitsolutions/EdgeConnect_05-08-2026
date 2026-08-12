// ============================================================================
// File: Diagnostics/IConfigurationFaultRegistry.cs
// Purpose: Live, in-memory registry of cross-record configuration faults
//          observed at runtime. Read-only from the Studio / management
//          API; write surface is internal to the host's startup pipeline
//          and (in M.P2.2) the runtime-reload coordinator.
//
//          Locked design properties per the M.P2.1 review:
//             * In-memory only — never persisted into config or to disk.
//               The audit chain is the durable record.
//             * Runtime-facing — faults reflect "what failed to come up"
//               not "what the operator intended."
//             * Keyed by (Kind, InstanceId) — registering a fault for an
//               already-faulted instance REPLACES the prior entry; this
//               is the expected behavior on a re-init failure.
//             * Cleared on successful re-init — ClearFor() is called by
//               the supervisor that owns the instance when it transitions
//               into a healthy state. Stale fault entries are a bug.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.P2.1
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Live registry of cross-record configuration faults. Surfaced via
/// the diagnostics API for the Studio's Configuration-faults panel and
/// for the Faulted state precedence on Sources / Destinations / Routes
/// pages.
/// </summary>
public interface IConfigurationFaultRegistry
{
    /// <summary>
    /// Add or replace a fault. Faults are keyed by
    /// (<see cref="ConfigurationFault.Kind"/>, <see cref="ConfigurationFault.InstanceId"/>).
    /// Calling <see cref="Register"/> for the same key replaces the previous entry.
    /// </summary>
    void Register(ConfigurationFault fault);

    /// <summary>
    /// Clear any fault recorded for the given (kind, instance id). Called by
    /// supervisors when an instance successfully (re-)initializes — runtime
    /// succeeded, so any prior fault is stale and must not linger on the
    /// Studio surface.
    /// </summary>
    /// <returns><c>true</c> if a fault was removed; <c>false</c> if none was present.</returns>
    bool ClearFor(ConfigurationFaultKind kind, string instanceId);

    /// <summary>
    /// Snapshot of all current faults, ordered by
    /// <see cref="ConfigurationFault.ObservedAtUtc"/> ascending. Returns
    /// a fresh list each call so callers can mutate without affecting
    /// the registry.
    /// </summary>
    IReadOnlyList<ConfigurationFault> GetFaults();

    /// <summary>Snapshot of faults filtered by kind.</summary>
    IReadOnlyList<ConfigurationFault> GetFaultsFor(ConfigurationFaultKind kind);

    /// <summary>Is the given (kind, instance id) currently faulted?</summary>
    bool IsFaulted(ConfigurationFaultKind kind, string instanceId);
}
