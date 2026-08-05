// ============================================================================
// File: Retirement/CallbackDrainResult.cs
// Purpose: Structured result of draining the OPC UA notification dispatcher
//          during retirement. The CallbackDrain surface is only Proven when
//          FullyDrained is true; a drain timeout (accepted work left undrained)
//          is terminal Unproven. Distinguishing fully-drained from
//          timeout/dropped is REQUIRED — a void StopAsync is not a valid proof
//          seam (commit-3.0 OPC UA surface-model review, 2026-06-26).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// Slice 0 — commit 3.0 (inert; wired at the 3.1 cutover).
// ============================================================================

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// The outcome of <see cref="NotificationDispatcher.RetireAndDrainAsync"/>:
/// whether the accepted callback work fully drained, how many CDPs were dropped
/// on a drain timeout, and how many callbacks were rejected after the ingress
/// was closed. Only <see cref="FullyDrained"/> admits a <c>CallbackDrain =
/// Proven</c> attestation.
/// </summary>
internal sealed record CallbackDrainResult
{
    /// <summary>
    /// True only when every accepted batch drained before the timeout (the
    /// channel completed). A drain timeout sets this false — terminal Unproven.
    /// </summary>
    public required bool FullyDrained { get; init; }

    /// <summary>CDPs left in the channel and shed when the drain timed out.</summary>
    public required long DroppedAtShutdown { get; init; }

    /// <summary>
    /// Callbacks the stack delivered AFTER the ingress was closed; rejected and
    /// recorded (never enqueued). Does not block proof — they were explicitly
    /// refused under the retirement path, not silently lost.
    /// </summary>
    public required long RejectedAfterRetirement { get; init; }
}
