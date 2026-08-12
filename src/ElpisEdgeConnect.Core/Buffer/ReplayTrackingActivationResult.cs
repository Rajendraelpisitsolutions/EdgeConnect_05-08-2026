// ============================================================================
// File: Buffer/ReplayTrackingActivationResult.cs
// Purpose: Result of SqliteRouteStore.ActivateReplayStateTrackingAsync — whether the
//          drained-store activation transition ran now or the store was already
//          enabled, plus the head the replay session starts from. Drained-check and
//          route-mismatch failures are raised as typed BufferExceptions, not returned.
// Reference: docs/sessions/2026-07-14-sparkplug-b-k1.2-route-store-plan-v3.1-amendment.md §1.
// ============================================================================

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>Outcome of a replay-tracking activation call.</summary>
internal enum ReplayTrackingActivationOutcome
{
    /// <summary>Tracking was disabled and has now been enabled (this call performed the transition).</summary>
    Activated = 0,

    /// <summary>Tracking was already enabled (idempotent no-op; one-way <c>disabled → enabled</c>).</summary>
    AlreadyEnabled = 1,
}

/// <summary>The result of a successful replay-tracking activation.</summary>
/// <param name="Outcome">Whether this call activated or the store was already enabled.</param>
/// <param name="ActivationHead">The head the replay sink's cursor was initialized at (== the store head at activation).</param>
internal readonly record struct ReplayTrackingActivationResult(
    ReplayTrackingActivationOutcome Outcome,
    long ActivationHead);
