// ============================================================================
// File: Adapters/Retirement/AdapterRetirementDetailCode.cs
// Purpose: A stable, strongly-typed retirement detail code — NOT a bare string
//          (a "structured string" drifts into free text). The management /
//          lifecycle surface depends on stable codes, not log text. Adapters
//          define their own known constants of this type.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4.
// Slice 0 — commit 3.0 (inert).
// ============================================================================

namespace ElpisEdgeConnect.Core.Adapters.Retirement;

/// <summary>
/// A stable, structured retirement detail code (e.g. <c>"MODBUS.RETIRE_WIRE_IDLE"</c>).
/// Adapters expose known constants of this type; consumers switch on the value,
/// never parse free text.
/// </summary>
/// <param name="Value">The stable code string.</param>
public readonly record struct AdapterRetirementDetailCode(string Value)
{
    /// <inheritdoc/>
    public override string ToString() => Value;
}
