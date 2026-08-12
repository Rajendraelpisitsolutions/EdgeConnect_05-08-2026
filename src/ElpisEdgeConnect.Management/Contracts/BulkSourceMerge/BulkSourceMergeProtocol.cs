// ============================================================================
// File: Contracts/BulkSourceMerge/BulkSourceMergeProtocol.cs
// Purpose: Allowlist of protocol ids accepted by BulkSourceMergeService.
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3-lock-final.md
//            docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md §7
// ============================================================================

namespace ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;

/// <summary>
/// Allowlist of protocol identifiers accepted by the bulk-source-merge
/// service. Any value outside this enum is rejected at the API boundary
/// before reaching the service. Matches the protocol ids the chip 3
/// templates use.
/// </summary>
public enum BulkSourceMergeProtocol
{
    /// <summary>FANUC FOCAS2 — template-fanuc-v1.json.</summary>
    Focas2 = 1,

    /// <summary>Brother Speedio HTTP — template-brother-v1.json.</summary>
    BrotherHttp = 2,

    /// <summary>Modbus TCP — template-modbus-v1.json.</summary>
    ModbusTcp = 3,

    /// <summary>MTConnect — template-mtconnect-v1.json.</summary>
    Mtconnect = 4,
}
