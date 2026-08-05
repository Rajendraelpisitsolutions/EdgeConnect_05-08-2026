// ============================================================================
// File: Buffer/QuarantinedPoint.cs
// Purpose: Detail record for a single point the buffer could not serialize and
//          therefore skipped ("quarantined") instead of failing the whole
//          enqueue. Carries just enough to explain the skip to an operator
//          without coupling the buffer to the routing/diagnostics layer.
// Reference: docs/decisions (quarantine-and-continue policy), ARCHITECTURE_BLUEPRINT.md §19.8
//
// Policy context:
//   A point whose value cannot be serialized (e.g. an unsupported metadata
//   value type, or a non-UTC DateTime) must NEVER strand a route. The buffer
//   skips it, counts it (BufferStats.Quarantined), and reports it via an
//   optional construction-time callback so the routing layer can raise a loud,
//   structured diagnostics event. The rest of the batch is committed normally.
// ============================================================================

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Describes a single point that the buffer could not serialize and skipped
/// rather than failing the enqueue. Reported through the optional quarantine
/// callback supplied at buffer construction.
/// </summary>
public sealed record QuarantinedPoint
{
    /// <summary>The canonical tag name of the skipped point.</summary>
    public required string TagName { get; init; }

    /// <summary>
    /// Human-readable reason — the serialization exception type and message
    /// (e.g. "InvalidDataException: Unsupported metadata value runtime type:
    /// Decimal"). Diagnostic only.
    /// </summary>
    public required string Reason { get; init; }
}
