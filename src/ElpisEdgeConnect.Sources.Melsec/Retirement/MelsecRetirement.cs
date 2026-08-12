// ============================================================================
// File: Retirement/MelsecRetirement.cs
// Purpose: MELSEC implementation of the durable retirement-attestation pattern
//          (ISourceRetirement), mirroring S7Retirement. Lock-free transport
//          close up front; the durable Completion resolves Proven only when the
//          in-flight read worker has exited (wire idle). A wedged read leaves
//          Completion pending; a close-initiation failure still yields a durable
//          operation (terminal Unproven, distinct code).
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md (Rule 5)
//            docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7
// ============================================================================

using System;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;

namespace ElpisEdgeConnect.Sources.Melsec.Retirement;

/// <summary>Stable MELSEC retirement detail codes.</summary>
internal static class MelsecRetirementDetailCodes
{
    public static readonly AdapterRetirementDetailCode Initiated = new("MELSEC.RETIRE_INITIATED");
    public static readonly AdapterRetirementDetailCode WireIdleProven = new("MELSEC.RETIRE_WIRE_IDLE");
    public static readonly AdapterRetirementDetailCode CloseFailed = new("MELSEC.RETIRE_CLOSE_FAILED");
    public static readonly AdapterRetirementDetailCode Faulted = new("MELSEC.RETIRE_FAULT");
}

/// <summary>Builds the MELSEC <see cref="AdapterRetirementOperation"/> (deterministically testable).</summary>
internal static class MelsecRetirement
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
            CallbackDrainApplicable = false,  // polling — no callbacks
            BackgroundWorkApplicable = false, // reconnect is on-demand, no background loop/timer
            DetailCode = MelsecRetirementDetailCodes.Initiated,
        };

        var tcs = new TaskCompletionSource<AdapterQuiescenceAttestation>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            initiateClose(); // prompt, non-blocking, lock-free
        }
        catch (Exception) // close-initiation failure still yields a durable operation
        {
            tcs.TrySetResult(TerminalUnproven(MelsecRetirementDetailCodes.CloseFailed));
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
            tcs.TrySetResult(TerminalUnproven(MelsecRetirementDetailCodes.Faulted));
        }
    }

    private static AdapterQuiescenceAttestation Proven() => new()
    {
        Worker = AdapterSurfaceState.Proven,
        CallbackDrain = AdapterSurfaceState.NotApplicable,
        BackgroundWork = AdapterSurfaceState.NotApplicable,
        DetailCode = MelsecRetirementDetailCodes.WireIdleProven,
    };

    private static AdapterQuiescenceAttestation TerminalUnproven(AdapterRetirementDetailCode code) => new()
    {
        Worker = AdapterSurfaceState.Unproven,
        CallbackDrain = AdapterSurfaceState.NotApplicable,
        BackgroundWork = AdapterSurfaceState.NotApplicable,
        DetailCode = code,
    };
}
