// ============================================================================
// File: Wizards/OpcUaTestConnectionCooldownFsm.cs
// Purpose: Test Connection button state machine for the OPC UA Client
//          wizard. Pure C# (no Blazor / Razor dependencies) so the wizard's
//          probe-button UX is unit-testable without bUnit.
//
// LOCKED states (per PR 7c plan + amendment #1, user lock 2026-05-29):
//
//     Idle ──────[Begin]─────▶ Probing ──[Complete]──▶ CoolingDown
//      ▲                          │                          │
//      │                          │                          │
//      │                       [Cancel]                   [Cooldown elapsed]
//      │                          │                          │
//      └──────────────────────────┴──────────────────────────┘
//
//   Amendment #1: a cancelled probe goes Probing → Idle DIRECTLY (no
//   cooldown). Rationale: the operator intentionally aborted; forcing
//   a delay before retrying is bad UX.
//
//   Cooldown duration is operator-locked at 1 s after a completed probe
//   (success or failure). Single-active-probe is enforced by the
//   Probing state — the FSM rejects a second Begin while already
//   Probing.
//
// Reference: PR 7c plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Lifecycle state of the OPC UA Client wizard's Test Connection button.
/// </summary>
public enum OpcUaTestConnectionProbeState
{
    /// <summary>Button enabled, no probe in flight.</summary>
    Idle,

    /// <summary>Probe is in flight; button disabled; Cancel affordance visible.</summary>
    Probing,

    /// <summary>Probe finished (success or failure); 1 s cooldown before re-enable.</summary>
    CoolingDown,
}

/// <summary>
/// Pure-C# state machine driving the Test Connection button's enable/
/// disable + Cancel affordance. Unit-testable without bUnit.
/// </summary>
public sealed class OpcUaTestConnectionCooldownFsm
{
    /// <summary>Locked cooldown duration after a completed probe (amendment refinement).</summary>
    public static readonly TimeSpan DefaultCooldownDuration = TimeSpan.FromSeconds(1);

    private readonly TimeSpan _cooldownDuration;

    /// <summary>Construct with the default 1 s cooldown.</summary>
    public OpcUaTestConnectionCooldownFsm() : this(DefaultCooldownDuration) { }

    /// <summary>Construct with a custom cooldown duration (tests use a short window).</summary>
    public OpcUaTestConnectionCooldownFsm(TimeSpan cooldownDuration)
    {
        _cooldownDuration = cooldownDuration;
    }

    /// <summary>Current lifecycle state.</summary>
    public OpcUaTestConnectionProbeState State { get; private set; } = OpcUaTestConnectionProbeState.Idle;

    /// <summary>
    /// UTC instant at which the cooldown elapses. <see langword="null"/>
    /// when <see cref="State"/> is not <see cref="OpcUaTestConnectionProbeState.CoolingDown"/>.
    /// </summary>
    public DateTime? CooldownEndsAtUtc { get; private set; }

    /// <summary>Convenience — the button is enabled only when Idle.</summary>
    public bool ButtonEnabled => State == OpcUaTestConnectionProbeState.Idle;

    /// <summary>Convenience — the Cancel affordance shows only while Probing.</summary>
    public bool CancelVisible => State == OpcUaTestConnectionProbeState.Probing;

    /// <summary>
    /// Operator clicked Test Connection. Idle → Probing. No-op if a
    /// probe is already in flight (single-active-probe per amendment
    /// refinement).
    /// </summary>
    public void BeginProbe()
    {
        if (State != OpcUaTestConnectionProbeState.Idle) return;
        State = OpcUaTestConnectionProbeState.Probing;
    }

    /// <summary>
    /// Probe finished (success or failure). Probing → CoolingDown.
    /// <paramref name="nowUtc"/> sets the cooldown anchor.
    /// </summary>
    public void CompleteProbe(DateTime nowUtc)
    {
        if (State != OpcUaTestConnectionProbeState.Probing) return;
        State = OpcUaTestConnectionProbeState.CoolingDown;
        CooldownEndsAtUtc = nowUtc + _cooldownDuration;
    }

    /// <summary>
    /// Amendment #1: cancelled probe → Idle DIRECTLY (no cooldown).
    /// The operator intentionally aborted; forcing a delay before
    /// retrying is bad UX.
    /// </summary>
    public void CancelProbe()
    {
        if (State != OpcUaTestConnectionProbeState.Probing) return;
        State = OpcUaTestConnectionProbeState.Idle;
        CooldownEndsAtUtc = null;
    }

    /// <summary>
    /// Drive the cooldown clock. Call from the wizard's render loop or
    /// timer tick. When <paramref name="nowUtc"/> >=
    /// <see cref="CooldownEndsAtUtc"/>, transitions CoolingDown → Idle.
    /// </summary>
    /// <returns>True when the call advanced the state.</returns>
    public bool TickCooldown(DateTime nowUtc)
    {
        if (State != OpcUaTestConnectionProbeState.CoolingDown) return false;
        if (CooldownEndsAtUtc is null || nowUtc < CooldownEndsAtUtc) return false;
        State = OpcUaTestConnectionProbeState.Idle;
        CooldownEndsAtUtc = null;
        return true;
    }
}
