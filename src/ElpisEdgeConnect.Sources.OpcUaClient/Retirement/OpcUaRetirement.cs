// ============================================================================
// File: Retirement/OpcUaRetirement.cs
// Purpose: OPC UA Client implementation of the durable retirement-attestation
//          pattern (ISourceRetirement). Async-native with THREE surfaces:
//            * Worker        = NotApplicable — the drain pump is supervisor-owned
//                              (ConsumeAsync), not adapter-owned.
//            * CallbackDrain  = Applicable — the FastDataChangeCallback ingress
//                              and the bounded notification channel. Proven only
//                              when ingress is CLOSED *and* accepted work fully
//                              drained. A closed session alone is NOT proof.
//            * BackgroundWork = Applicable — the reconnect coordinator, which can
//                              re-wire callbacks; detach + dispose it BEFORE the
//                              drain so it cannot resurrect ingress mid-drain.
//          Sequencing (review-corrected):
//            - SYNCHRONOUS, non-blocking Begin: set ONLY the authoritative
//              dispatcher ingress flag (cheap, cannot block). No UA-stack calls.
//            - ASYNCHRONOUS resolution (off any caller lock): unwire/delete
//              subscriptions (best-effort defence-in-depth), detach/dispose the
//              coordinator (BackgroundWork), then drain the dispatcher (CallbackDrain).
//          The Host OBSERVATION token is NOT used to decide terminal attestation
//          (Blocker 1): host-deadline expiry is the host's evidence and must leave
//          Completion observable for late proof; only the dispatcher's OWN drain
//          budget can yield terminal CallbackDrain = Unproven.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7;
//            commit-3.0 complete-diff review (Blockers 1 & 2, 2026-06-26).
// Slice 0 — commit 3.0 (inert; wired at the 3.1 cutover).
// ============================================================================

using System;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Retirement;

/// <summary>Stable OPC UA Client retirement detail codes.</summary>
internal static class OpcUaRetirementDetailCodes
{
    public static readonly AdapterRetirementDetailCode Initiated = new("OPCUA.RETIRE_INITIATED");
    public static readonly AdapterRetirementDetailCode Proven = new("OPCUA.RETIRE_PROVEN");
    public static readonly AdapterRetirementDetailCode CallbackUndrained = new("OPCUA.RETIRE_CALLBACK_UNDRAINED");
    public static readonly AdapterRetirementDetailCode BackgroundWorkFault = new("OPCUA.RETIRE_BACKGROUND_FAULT");
    public static readonly AdapterRetirementDetailCode Faulted = new("OPCUA.RETIRE_FAULT");
}

/// <summary>
/// Builds the OPC UA Client <see cref="AdapterRetirementOperation"/>
/// (deterministically testable via injected delegates — no live session/stack).
/// </summary>
internal static class OpcUaRetirement
{
    /// <param name="closeIngressFlag">
    /// SYNCHRONOUS, non-blocking — set ONLY the dispatcher's authoritative
    /// ingress-retiring flag. Must not call the UA stack or block (Blocker 2).
    /// </param>
    /// <param name="unwireSubscriptions">
    /// ASYNC best-effort — null/delete subscriptions so the stack stops delivering
    /// at the source. Does NOT gate proof (the ingress flag is authoritative); a
    /// failure here is swallowed.
    /// </param>
    /// <param name="stopBackgroundWork">
    /// ASYNC — detach + dispose the reconnect coordinator. Throwing ⇒
    /// <c>BackgroundWork = Unproven</c> (fail closed).
    /// </param>
    /// <param name="drainCallbacks">
    /// ASYNC — complete + drain the dispatcher channel, returning a structured
    /// result. Only <see cref="CallbackDrainResult.FullyDrained"/> is proof. MUST
    /// NOT be cancelled by the host observation token (Blocker 1).
    /// </param>
    /// <param name="context">Retirement observation context (NOT used for proof).</param>
    public static AdapterRetirementOperation Begin(
        Action closeIngressFlag,
        Func<Task> unwireSubscriptions,
        Func<Task> stopBackgroundWork,
        Func<Task<CallbackDrainResult>> drainCallbacks,
        AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(closeIngressFlag);
        ArgumentNullException.ThrowIfNull(unwireSubscriptions);
        ArgumentNullException.ThrowIfNull(stopBackgroundWork);
        ArgumentNullException.ThrowIfNull(drainCallbacks);
        ArgumentNullException.ThrowIfNull(context);

        var snapshot = new AdapterRetirementSnapshot
        {
            WorkerApplicable = false,         // supervisor-owned drain pump (NotApplicable)
            CallbackDrainApplicable = true,   // FastDataChangeCallback ingress + channel
            BackgroundWorkApplicable = true,  // reconnect coordinator
            DetailCode = OpcUaRetirementDetailCodes.Initiated,
        };

        var tcs = new TaskCompletionSource<AdapterQuiescenceAttestation>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            // Authoritative ingress close — cheap, non-blocking, no UA-stack calls.
            // After this, OnNotification rejects + records (the drain becomes proof).
            closeIngressFlag();
        }
        catch (Exception) // even a flag-set fault still yields a durable operation
        {
            tcs.TrySetResult(TerminalUnproven(OpcUaRetirementDetailCodes.Faulted));
            return new AdapterRetirementOperation { Snapshot = snapshot, Completion = tcs.Task };
        }

        // All resourceful cleanup happens here, OFF the caller's lock (Blocker 2).
        _ = ResolveAsync(tcs, unwireSubscriptions, stopBackgroundWork, drainCallbacks);
        return new AdapterRetirementOperation { Snapshot = snapshot, Completion = tcs.Task };
    }

    private static async Task ResolveAsync(
        TaskCompletionSource<AdapterQuiescenceAttestation> tcs,
        Func<Task> unwireSubscriptions,
        Func<Task> stopBackgroundWork,
        Func<Task<CallbackDrainResult>> drainCallbacks)
    {
        // Step 2 — best-effort UA-stack unwire/delete (defence-in-depth; the
        // ingress flag already rejects, so a failure here does not gate proof).
        try { await unwireSubscriptions().ConfigureAwait(false); }
        catch (Exception) { /* best-effort */ }

        // Step 3 — detach + dispose the reconnect coordinator BEFORE draining so it
        // cannot re-wire callbacks (resurrect ingress) mid-drain.
        AdapterSurfaceState background;
        try
        {
            await stopBackgroundWork().ConfigureAwait(false);
            background = AdapterSurfaceState.Proven;
        }
        catch (Exception)
        {
            background = AdapterSurfaceState.Unproven; // fail closed
        }

        // Step 4 — drain the dispatcher. Only a fully-drained result is proof; a
        // dispatcher-OWNED drain-budget timeout (not the host token) is terminal
        // Unproven.
        AdapterSurfaceState callback;
        try
        {
            var drain = await drainCallbacks().ConfigureAwait(false);
            callback = drain.FullyDrained
                ? AdapterSurfaceState.Proven
                : AdapterSurfaceState.Unproven;
        }
        catch (Exception)
        {
            callback = AdapterSurfaceState.Unproven; // fail closed
        }

        var code = (callback, background) switch
        {
            (AdapterSurfaceState.Proven, AdapterSurfaceState.Proven) => OpcUaRetirementDetailCodes.Proven,
            (not AdapterSurfaceState.Proven, _) => OpcUaRetirementDetailCodes.CallbackUndrained,
            _ => OpcUaRetirementDetailCodes.BackgroundWorkFault,
        };

        // Step 6 — resolve only after BOTH applicable surfaces are determined.
        tcs.TrySetResult(new AdapterQuiescenceAttestation
        {
            Worker = AdapterSurfaceState.NotApplicable,
            CallbackDrain = callback,
            BackgroundWork = background,
            DetailCode = code,
        });
    }

    private static AdapterQuiescenceAttestation TerminalUnproven(AdapterRetirementDetailCode code) => new()
    {
        Worker = AdapterSurfaceState.NotApplicable,
        CallbackDrain = AdapterSurfaceState.Unproven,
        BackgroundWork = AdapterSurfaceState.Unproven,
        DetailCode = code,
    };
}
