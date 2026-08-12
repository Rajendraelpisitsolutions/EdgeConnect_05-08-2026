// ============================================================================
// File: Generation/SourceLifecycleBlockReason.cs
// Purpose: Stable, structured reasons a source's replacement/activation is
//          blocked at runtime — the dedicated source-lifecycle vocabulary (F3),
//          kept DISTINCT from configuration faults, adapter/device errors, and
//          the later Slice A liveness reason fields.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §5 (F3).
// Slice 0 — commit 3.0 (inert; the supervisor consumes these at the 3.1 cutover).
// ============================================================================

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>Why a source's replacement/activation is currently blocked at runtime.</summary>
internal enum SourceLifecycleBlockReason
{
    /// <summary>Not blocked.</summary>
    None = 0,

    /// <summary>The adapter does not implement <c>ISourceRetirement</c>; it fails closed (never inferred quiescent).</summary>
    RetirementCapabilityUnsupported = 1,

    /// <summary>A retired generation's quiescence is not yet proven; replacement is withheld.</summary>
    AwaitingQuiescence = 2,

    /// <summary>The retirement deadline elapsed with quiescence still unproven (operation retained, may yet prove).</summary>
    QuiescenceUnprovenAtDeadline = 3,

    /// <summary>The adapter terminally cannot prove quiescence; operator action (controlled process restart) is required.</summary>
    QuiescenceTerminallyUnproven = 4,
}
