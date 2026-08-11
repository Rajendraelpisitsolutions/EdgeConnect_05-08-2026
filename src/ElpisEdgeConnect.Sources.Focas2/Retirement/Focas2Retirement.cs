// ============================================================================
// File: Retirement/Focas2Retirement.cs
// Purpose: FOCAS2 implementation of the durable retirement-attestation pattern
//          (ISourceRetirement). DIFFERENT proof class from Modbus/S7: the worker
//          is the fwlib-affine dedicated thread, so "Worker Proven" requires the
//          thread to ACTUALLY terminate (Focas2Thread.WaitForThreadExitAsync) —
//          a Join timeout or method return is NOT proof. Cleanup is initiated in
//          a thread-affinity-safe, non-blocking way (enqueue the handle-free on
//          the affine thread + complete the queue); a wedged native call leaves
//          Completion pending (the orphan/resource policy governs the still-live
//          thread — this is NOT proof of quiescence), and a late thread exit
//          resolves Proven.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// Slice 0 — commit 3.0 (inert; wired at the 3.1 cutover).
// ============================================================================

using System;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;

namespace ElpisEdgeConnect.Sources.Focas2.Retirement;

/// <summary>Stable FOCAS2 retirement detail codes.</summary>
internal static class Focas2RetirementDetailCodes
{
    public static readonly AdapterRetirementDetailCode Initiated = new("FOCAS2.RETIRE_INITIATED");
    public static readonly AdapterRetirementDetailCode ThreadExitedProven = new("FOCAS2.RETIRE_THREAD_EXITED");
    public static readonly AdapterRetirementDetailCode CleanupFailed = new("FOCAS2.RETIRE_CLEANUP_FAILED");
}

/// <summary>
/// Builds the FOCAS2 <see cref="AdapterRetirementOperation"/>. Decoupled from the
/// adapter so the durable-operation behaviour is deterministically testable with
/// injected cleanup-initiation / thread-exit delegates.
/// </summary>
internal static class Focas2Retirement
{
    public static AdapterRetirementOperation Begin(
        Action initiateThreadCleanup,
        Func<Task> awaitThreadExit,
        AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(initiateThreadCleanup);
        ArgumentNullException.ThrowIfNull(awaitThreadExit);
        ArgumentNullException.ThrowIfNull(context);

        var snapshot = new AdapterRetirementSnapshot
        {
            WorkerApplicable = true,           // the fwlib-affine dedicated thread
            CallbackDrainApplicable = false,   // FOCAS2 is polling — no callbacks
            BackgroundWorkApplicable = false,  // reconnect is on-demand; no background loop/timer
            DetailCode = Focas2RetirementDetailCodes.Initiated,
        };

        var tcs = new TaskCompletionSource<AdapterQuiescenceAttestation>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            initiateThreadCleanup(); // non-blocking, thread-affinity-safe
        }
        catch (Exception) // cleanup-initiation failure still yields a durable operation
        {
            tcs.TrySetResult(TerminalUnproven(Focas2RetirementDetailCodes.CleanupFailed));
            return new AdapterRetirementOperation { Snapshot = snapshot, Completion = tcs.Task };
        }

        _ = ResolveAsync(tcs, awaitThreadExit);
        return new AdapterRetirementOperation { Snapshot = snapshot, Completion = tcs.Task };
    }

    private static async Task ResolveAsync(
        TaskCompletionSource<AdapterQuiescenceAttestation> tcs, Func<Task> awaitThreadExit)
    {
        try
        {
            await awaitThreadExit().ConfigureAwait(false); // resolves only on TRUE thread termination
            tcs.TrySetResult(Proven());
        }
        catch (Exception) // fail closed
        {
            // The thread-exit task faults ONLY when the affine final cleanup threw
            // (Focas2Thread captures it and surfaces it via the exit task), so a
            // faulted exit is precisely a cleanup failure — give operators the
            // specific code rather than the generic fault.
            tcs.TrySetResult(TerminalUnproven(Focas2RetirementDetailCodes.CleanupFailed));
        }
    }

    private static AdapterQuiescenceAttestation Proven() => new()
    {
        Worker = AdapterSurfaceState.Proven,
        CallbackDrain = AdapterSurfaceState.NotApplicable,
        BackgroundWork = AdapterSurfaceState.NotApplicable,
        DetailCode = Focas2RetirementDetailCodes.ThreadExitedProven,
    };

    private static AdapterQuiescenceAttestation TerminalUnproven(AdapterRetirementDetailCode code) => new()
    {
        Worker = AdapterSurfaceState.Unproven,
        CallbackDrain = AdapterSurfaceState.NotApplicable,
        BackgroundWork = AdapterSurfaceState.NotApplicable,
        DetailCode = code,
    };
}
