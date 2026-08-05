// ============================================================================
// File: Retirement/S7Retirement.cs
// Purpose: S7 implementation of the durable retirement-attestation pattern
//          (ISourceRetirement), mirroring the approved Modbus pattern. Lock-free
//          transport close up front; durable Completion resolves Proven only
//          when the in-flight Sharp7 read worker has exited (wire idle); a wedged
//          read leaves Completion pending; a close-initiation failure still
//          yields a durable operation (terminal Unproven, distinct code).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// Slice 0 — commit 3.0 (inert; wired at the 3.1 cutover).
// ============================================================================

using System;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;

namespace ElpisEdgeConnect.Sources.S7.Retirement;

/// <summary>Stable S7 retirement detail codes.</summary>
internal static class S7RetirementDetailCodes
{
    public static readonly AdapterRetirementDetailCode Initiated = new("S7.RETIRE_INITIATED");
    public static readonly AdapterRetirementDetailCode WireIdleProven = new("S7.RETIRE_WIRE_IDLE");
    public static readonly AdapterRetirementDetailCode CloseFailed = new("S7.RETIRE_CLOSE_FAILED");
    public static readonly AdapterRetirementDetailCode Faulted = new("S7.RETIRE_FAULT");
}

/// <summary>Builds the S7 <see cref="AdapterRetirementOperation"/> (deterministically testable).</summary>
internal static class S7Retirement
{
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
            CallbackDrainApplicable = false,   // S7 is polling — no callbacks
            BackgroundWorkApplicable = false,  // reconnect is on-demand, no background loop/timer
            DetailCode = S7RetirementDetailCodes.Initiated,
        };

        var tcs = new TaskCompletionSource<AdapterQuiescenceAttestation>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            initiateClose(); // prompt, non-blocking, lock-free
        }
        catch (Exception) // close-initiation failure still yields a durable operation
        {
            tcs.TrySetResult(TerminalUnproven(S7RetirementDetailCodes.CloseFailed));
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
        catch (Exception) // fail closed
        {
            tcs.TrySetResult(TerminalUnproven(S7RetirementDetailCodes.Faulted));
        }
    }

    private static AdapterQuiescenceAttestation Proven() => new()
    {
        Worker = AdapterSurfaceState.Proven,
        CallbackDrain = AdapterSurfaceState.NotApplicable,
        BackgroundWork = AdapterSurfaceState.NotApplicable,
        DetailCode = S7RetirementDetailCodes.WireIdleProven,
    };

    private static AdapterQuiescenceAttestation TerminalUnproven(AdapterRetirementDetailCode code) => new()
    {
        Worker = AdapterSurfaceState.Unproven,
        CallbackDrain = AdapterSurfaceState.NotApplicable,
        BackgroundWork = AdapterSurfaceState.NotApplicable,
        DetailCode = code,
    };
}
