// ============================================================================
// File: Generation/RetirementReason.cs
// Purpose: Why a source generation's publish authority is being revoked.
//          Host-internal alongside the gate surface (focused review #1);
//          promoted to a Core history DTO if/when externally consumed.
// Reference: docs/sessions/2026-06-25-source-generation-foundation-slice-0-spec.md §5.
// Slice 0 — gate foundation (unused scaffolding).
// ============================================================================

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>Why a source generation's publish authority is being revoked.</summary>
internal enum RetirementReason
{
    /// <summary>The source is being stopped.</summary>
    Stop = 0,

    /// <summary>The source configuration is being applied as a new generation.</summary>
    Reconfigure = 1,

    /// <summary>A recovery action is retiring a wedged or failed generation.</summary>
    Recovery = 2,

    /// <summary>The source is being permanently removed from the runtime.</summary>
    PermanentRemoval = 3,

    /// <summary>Activation (initialize/start) failed and the just-authorized generation is rolled back.</summary>
    ActivationRollback = 4,
}
