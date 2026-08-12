// ============================================================================
// File: Adapters/Retirement/PullAdapterRetirement.cs
// Purpose: Builds the AdapterRetirementOperation for supervisor-driven PULL
//          adapters (MTConnect, Brother HTTP). Their ONLY adapter-owned
//          execution surface is the in-flight poll, drained via a
//          PollQuiescenceGate → Worker. They own no callback ingress and no
//          background work creator (reconnect loop / timer / dispatcher),
//          verified per adapter, so those surfaces are NotApplicable.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// Slice 0 — commit 3.0 (inert; wired at the 3.1 cutover).
// ============================================================================

using System;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Adapters.Retirement;

/// <summary>
/// Constructs the durable retirement operation for a pull adapter from its
/// <see cref="PollQuiescenceGate"/>. Worker is proven only when the in-flight
/// poll has drained; a wedged poll leaves <c>Completion</c> pending.
/// </summary>
public static class PullAdapterRetirement
{
    /// <param name="gate">The adapter's poll-admission gate.</param>
    /// <param name="initiated">Detail code stamped on the snapshot.</param>
    /// <param name="provenWorkerIdle">Detail code for a fully-drained (Proven) attestation.</param>
    /// <param name="context">Retirement observation context.</param>
    public static AdapterRetirementOperation Begin(
        PollQuiescenceGate gate,
        AdapterRetirementDetailCode initiated,
        AdapterRetirementDetailCode provenWorkerIdle,
        AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(context);

        var snapshot = new AdapterRetirementSnapshot
        {
            WorkerApplicable = true,          // in-flight poll (drained via the gate)
            CallbackDrainApplicable = false,  // no callbacks — SubscribeAsync unsupported
            BackgroundWorkApplicable = false, // no timer/loop/coordinator (verified per adapter)
            DetailCode = initiated,
        };

        var tcs = new TaskCompletionSource<AdapterQuiescenceAttestation>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _ = ResolveAsync(tcs, gate, provenWorkerIdle);
        return new AdapterRetirementOperation { Snapshot = snapshot, Completion = tcs.Task };
    }

    private static async Task ResolveAsync(
        TaskCompletionSource<AdapterQuiescenceAttestation> tcs,
        PollQuiescenceGate gate,
        AdapterRetirementDetailCode provenWorkerIdle)
    {
        await gate.BeginQuiescingAsync().ConfigureAwait(false);
        tcs.TrySetResult(new AdapterQuiescenceAttestation
        {
            Worker = AdapterSurfaceState.Proven,
            CallbackDrain = AdapterSurfaceState.NotApplicable,
            BackgroundWork = AdapterSurfaceState.NotApplicable,
            DetailCode = provenWorkerIdle,
        });
    }
}
