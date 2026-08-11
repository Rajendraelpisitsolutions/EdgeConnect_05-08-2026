// ============================================================================
// File: Api/ModbusProbeResultDto.cs
// Purpose: Response shape for POST /api/v1/sources/browse/modbus.
//          Common probe fields (Success/ErrorCode/ProbeId/ElapsedMs)
//          match the M.2d.2 v2 §4.4 contract; Modbus-specific diagnostic
//          fields (FunctionCodeUsed, AddressTested, UnitIdTested,
//          ProbeStepReached) added for support-troubleshooting value.
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §4.4
// ============================================================================

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Result of a Modbus TCP Test Connection probe. Wire-level DTO returned
/// to the Studio wizard; renderable inline regardless of HTTP status code
/// (the wizard treats 200+Success=false as "render this error" rather
/// than as a fetch failure — see v2 §4.6).
/// </summary>
public sealed record ModbusProbeResultDto
{
    /// <summary>Probe correlation id — surfaced in the wizard for support tickets.</summary>
    public required string ProbeId { get; init; }

    /// <summary>True when the probe succeeded; false when it ran but the slave rejected.</summary>
    public required bool Success { get; init; }

    /// <summary>Probe round-trip duration in milliseconds.</summary>
    public required int ElapsedMs { get; init; }

    /// <summary>
    /// Categorical error code when <see cref="Success"/> is false.
    /// <c>MODBUS.PROBE_*</c> namespace; license/busy follow the M.2d.2
    /// v2 §4.6 shared shape.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>Operator-facing one-line error message.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Modbus diagnostic field (v2 §4.4): the function code actually used
    /// (FC01/02/03/04). Useful for vendor-quirk debugging — "we tried FC03,
    /// the PLC only supports FC04 on this address class".
    /// </summary>
    public byte? FunctionCodeUsed { get; init; }

    /// <summary>
    /// Modbus diagnostic field (v2 §4.4): the Modbus address actually
    /// probed. Lets support confirm at a glance whether the probe's
    /// fallback address matched the customer's expectations.
    /// </summary>
    public ushort? AddressTested { get; init; }

    /// <summary>
    /// Modbus diagnostic field (v2 §4.4): the slave unit id targeted.
    /// Multi-drop visibility — confirms which unit the probe spoke to.
    /// </summary>
    public byte? UnitIdTested { get; init; }

    /// <summary>
    /// Modbus diagnostic field (v2 §4.4): how far the probe advanced
    /// through the v2 §4.3 escalation ladder before succeeding or failing.
    /// </summary>
    public ModbusProbeStep? ProbeStepReached { get; init; }
}
