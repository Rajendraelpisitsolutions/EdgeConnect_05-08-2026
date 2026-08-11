// ============================================================================
// File: Retirement/ModbusRetirement.cs
// Purpose: Modbus implementation of the durable retirement-attestation pattern
//          (ISourceRetirement). BeginRetirement initiates a LOCK-FREE transport
//          close promptly, then resolves the durable Completion only when the
//          in-flight read worker has exited (the wire is idle). A wedged read
//          leaves Completion pending — the host records UnprovenAtDeadline
//          separately; a later worker exit still resolves to Proven (clearing
//          the activation barrier without a process restart).
//
//          M1: a close-initiation failure still returns a durable operation
//          whose Completion is terminal Unproven (distinct detail code) — the
//          host never receives an exception instead of an operation.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4 (F2),
//            §7 (Modbus representative adapter); commit-3.0 Modbus pattern review.
// Slice 0 — commit 3.0 (inert; the live supervisor wires this at the 3.1 cutover).
// ============================================================================

using System;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Retirement;

/// <summary>Stable Modbus retirement detail codes.</summary>
internal static class ModbusRetirementDetailCodes
{
    public static readonly AdapterRetirementDetailCode Initiated = new("MODBUS.RETIRE_INITIATED");
    public static readonly AdapterRetirementDetailCode WireIdleProven = new("MODBUS.RETIRE_WIRE_IDLE");
    public static readonly AdapterRetirementDetailCode CloseFailed = new("MODBUS.RETIRE_CLOSE_FAILED");
    public static readonly AdapterRetirementDetailCode Faulted = new("MODBUS.RETIRE_FAULT");
}

/// <summary>
/// Builds the Modbus <see cref="AdapterRetirementOperation"/>. Decoupled from the
/// adapter so the durable-operation behaviour is deterministically testable with
/// injected close / worker-exit delegates.
/// </summary>
internal static class ModbusRetirement
{
    /// <summary>
    /// Begin retirement: initiate the (lock-free) close promptly and return a
    /// durable operation whose <c>Completion</c> resolves to <c>Proven</c> when
    /// <paramref name="awaitWorkerExit"/> completes, terminal <c>Unproven</c> if it
    /// faults, or terminal <c>Unproven</c> (<c>MODBUS.RETIRE_CLOSE_FAILED</c>) if
    /// the close itself throws. Always returns an operation — never throws to the host.
    /// </summary>
    public static AdapterRetirementOperation Begin(
        Action initiateClose,
        Func<Task> awaitWorkerExit,
        AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(initiateClose);
        ArgumentNullException.ThrowIfNull(awaitWorkerExit);
        ArgumentNullException.ThrowIfNull(context);

        var snapshot = new AdapterRetirementSnapshot
        {
            WorkerApplicable = true,
            CallbackDrainApplicable = false,   // Modbus is polling — no callbacks
            BackgroundWorkApplicable = false,  // no timers/reconnect loops/dispatchers (M3)
            DetailCode = ModbusRetirementDetailCodes.Initiated,
        };

        // RunContinuationsAsynchronously: host continuations must not run inline on
        // the adapter cleanup/socket thread.
        var tcs = new TaskCompletionSource<AdapterQuiescenceAttestation>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            initiateClose(); // prompt, non-blocking, lock-free
        }
        catch (Exception) // M1: close-initiation failure still yields a durable operation
        {
            tcs.TrySetResult(TerminalUnproven(ModbusRetirementDetailCodes.CloseFailed));
            return new AdapterRetirementOperation { Snapshot = snapshot, Completion = tcs.Task };
        }

        _ = ResolveAsync(tcs, awaitWorkerExit);
        return new AdapterRetirementOperation { Snapshot = snapshot, Completion = tcs.Task };
    }

    private static async Task ResolveAsync(
        TaskCompletionSource<AdapterQuiescenceAttestation> tcs, Func<Task> awaitWorkerExit)
    {
        try
        {
            await awaitWorkerExit().ConfigureAwait(false);
            tcs.TrySetResult(Proven());
        }
        catch (Exception) // fail closed: any worker-exit error → terminal Unproven
        {
            tcs.TrySetResult(TerminalUnproven(ModbusRetirementDetailCodes.Faulted));
        }
    }

    private static AdapterQuiescenceAttestation Proven() => new()
    {
        Worker = AdapterSurfaceState.Proven,
        CallbackDrain = AdapterSurfaceState.NotApplicable,
        BackgroundWork = AdapterSurfaceState.NotApplicable,
        DetailCode = ModbusRetirementDetailCodes.WireIdleProven,
    };

    private static AdapterQuiescenceAttestation TerminalUnproven(AdapterRetirementDetailCode code) => new()
    {
        Worker = AdapterSurfaceState.Unproven,
        CallbackDrain = AdapterSurfaceState.NotApplicable,
        BackgroundWork = AdapterSurfaceState.NotApplicable,
        DetailCode = code,
    };
}
