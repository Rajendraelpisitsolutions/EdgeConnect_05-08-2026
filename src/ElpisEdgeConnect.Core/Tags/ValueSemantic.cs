// ============================================================================
// File: Tags/ValueSemantic.cs
// Purpose: Semantic interpretation of a tag's value (distinct from its wire
//          ValueType). Drives chart rendering defaults, historian
//          compression hints, OPC UA node-type selection.
//
// LOCKED for v1: only the 5 values listed below are supported. Event and
// Alarm are explicitly reserved for the future OPC UA Events & Alarms
// infrastructure (Phase 5) — adding them as enum values without backing
// implementation invites misuse.
//
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone G.6
// Milestone: G.6 (Phase 4)
// ============================================================================

namespace ElpisEdgeConnect.Core.Tags;

/// <summary>
/// Semantic interpretation of a tag's value, separate from its wire-level
/// <c>CanonicalValueType</c>. A <c>bool</c> wire value is usually
/// <see cref="Digital"/> but could be <see cref="State"/>; a <c>float32</c>
/// is usually <see cref="Analog"/> but could be a <see cref="Counter"/>.
/// Drives downstream rendering / compression / OPC UA node-type decisions.
/// </summary>
public enum ValueSemantic
{
    /// <summary>
    /// Continuous numeric measurement (feed rate, temperature, spindle
    /// load). Charted as a line; historian uses delta-compression.
    /// </summary>
    Analog,

    /// <summary>
    /// Boolean state (running, door_closed, alarm_active). Charted as a
    /// step function; historian uses on-change-only compression.
    /// </summary>
    Digital,

    /// <summary>
    /// Monotonically-increasing total (parts_count, energy_kwh).
    /// Historian uses run-length encoding plus reset-detection; dashboards
    /// typically render the DERIVATIVE (parts/hr, kW from kWh).
    /// </summary>
    Counter,

    /// <summary>
    /// Enumerated state (mode=AUTO/MDI/JOG/EDIT). Rendered as a
    /// categorical color band on charts; historian stores transitions only.
    /// </summary>
    State,

    /// <summary>
    /// String identifier (part_id, program/main_program). Not numeric;
    /// rendered as a text annotation alongside a primary chart, not
    /// charted itself.
    /// </summary>
    Text,

    // Reserved for Phase 5+ infrastructure (OPC UA Events & Alarms):
    //   Event  — discrete occurrence with payload (cycle complete, tool change)
    //   Alarm  — state-with-severity, requires acknowledgement workflow
    // These are NOT added as enum values today because we don't yet have
    // the infrastructure to back them; adding the values without
    // implementation invites misuse. See PHASE4_EXECUTION_PLAN.md §3
    // Non-goals.
}
