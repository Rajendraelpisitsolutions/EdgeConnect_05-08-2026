// ============================================================================
// File: Diagnostics/ISourceHealthSink.cs
// Purpose: Push seam for source-side health and observation counters. The
//          host's source supervisor (D1) calls into the collector via this
//          interface whenever a source adapter reports a state change or
//          delivers a batch of points. The collector is the only concrete
//          implementation; the seam exists so unit tests can swap in fakes
//          and so the contract is explicit in one place.
// Reference: ARCHITECTURE_BLUEPRINT.md §13.1 (three-way diagnostics)
// Milestone: C4 — phase 3.
//
// Design note:
//   This seam is PUSH-ONLY. The collector never polls source adapters.
//   The supervisor observes and pushes. Single source of truth stays in
//   the collector.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Push-only seam for reporting source adapter health and observation
/// events into the diagnostics collector.
/// </summary>
public interface ISourceHealthSink
{
    /// <summary>
    /// Record that a source delivered one or more points. Called once per
    /// batch (or per point, caller's choice) by the source supervisor.
    /// </summary>
    void RecordSourceObservation(
        string routeId,
        string sourceInstanceId,
        string protocolName,
        int pointsObserved,
        DateTime observedAtUtc);

    /// <summary>
    /// Record a source adapter state change (Initialized / Running /
    /// Faulted / Stopped). Supplies an optional error payload for the
    /// Faulted case.
    /// </summary>
    void RecordSourceState(
        string routeId,
        string sourceInstanceId,
        string protocolName,
        AdapterState state,
        AdapterError? lastError);
}
