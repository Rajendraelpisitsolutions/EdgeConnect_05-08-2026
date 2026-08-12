// ============================================================================
// File: Adapters/Retirement/AdapterSurfaceState.cs
// Purpose: Quiescence state of one adapter execution surface (worker, callback
//          drain, background/reconnect). Part of the adapter-owned durable
//          retirement attestation — the host NEVER infers proof from StopAsync/
//          DisposeAsync/task completion.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4 (F2).
// Slice 0 — commit 3.0 (inert; no live supervisor wiring).
// ============================================================================

namespace ElpisEdgeConnect.Core.Adapters.Retirement;

/// <summary>Quiescence state of one adapter execution surface at attestation time.</summary>
public enum AdapterSurfaceState
{
    /// <summary>The surface has terminated and cannot create new work.</summary>
    Proven = 0,

    /// <summary>
    /// TERMINAL: the adapter has determined this surface cannot be proven quiesced.
    /// This appears only on a RESOLVED attestation — while cleanup is still in
    /// flight the operation's <c>Completion</c> stays pending instead. Distinct
    /// from the host's separate <c>UnprovenAtDeadline</c> evidence (which does not
    /// resolve the operation).
    /// </summary>
    Unproven = 1,

    /// <summary>The surface does not exist for this adapter (e.g. callbacks on a polling adapter).</summary>
    NotApplicable = 2,
}
