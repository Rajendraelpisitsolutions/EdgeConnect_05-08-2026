// ============================================================================
// File: LockedTuningKnobs.cs
// Purpose: Codifies the 12 OPC UA Client tuning-knob defaults locked at
//          plan v2.1 §2.5 (sign-off 2026-05-28). These are the values
//          every stack-ceiling measurement runs against; changing any of
//          them invalidates the locked baseline and requires a v2.1
//          amendment.
//
// Source-of-truth doc: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §2.5
//                      docs/benchmarks/multi-protocol-workload-profiles.md §3 Profile A
//
// LOCKED — do not change without amending v2.1.
// ============================================================================

namespace ElpisEdgeConnect.Benchmarks.StackCeiling;

/// <summary>
/// The 12 locked OPC UA Client tuning-knob defaults from v2.1 §2.5.
/// Codified as <c>const</c> so any drift between the plan doc and the
/// running benchmark is caught at compile time.
/// </summary>
internal static class LockedTuningKnobs
{
    /// <summary>50 ms — matches plan §1.1 target. Smaller wastes server CPU per Prosys/Beckhoff guidance.</summary>
    public const int PublishingIntervalMs = 50;

    /// <summary>50 ms (= publish). Don't sub-sample below publish without reason.</summary>
    public const int SamplingIntervalMs = 50;

    /// <summary>20 (= 1 s wall). Reference-client value. Stack default 10 is too tight.</summary>
    public const uint KeepAliveCount = 20;

    /// <summary>60 (≥ 3× keepalive, = 3 s wall). Stack default 1000 too lenient for edge.</summary>
    public const uint LifetimeCount = 60;

    /// <summary>1,000 — caps single message size; prevents one fat publish stalling the channel.</summary>
    public const uint MaxNotificationsPerPublish = 1000;

    /// <summary>10 per subscription — bounded backlog before stack drops oldest.</summary>
    public const uint MaxMessageCount = 10;

    /// <summary>2 for analog. Override to 10 for discrete/events on the per-item Subscribe call.</summary>
    public const uint AnalogQueueSize = 2;

    /// <summary>10 for discrete/events. Override target for non-analog items.</summary>
    public const uint DiscreteQueueSize = 10;

    /// <summary>True — edge prefers fresh data over backfill.</summary>
    public const bool DiscardOldest = true;

    /// <summary>True — required for §19.6 ordering invariant.</summary>
    public const bool SequentialPublishing = true;

    /// <summary>False — enables zero-loss <c>TransferSubscriptions</c> on reconnect.</summary>
    public const bool DeleteSubscriptionsOnClose = false;

    /// <summary>5,000 ms — reference-client default.</summary>
    public const int SessionKeepAliveIntervalMs = 5_000;

    /// <summary>1,000 ms — reference-client default initial reconnect delay.</summary>
    public const int ReconnectPeriodMs = 1_000;

    /// <summary>15,000 ms — reference-client default reconnect backoff ceiling.</summary>
    public const int ReconnectPeriodExponentialBackoffCeilingMs = 15_000;

    /// <summary>60,000 ms — reference-client default session timeout.</summary>
    public const uint SessionTimeoutMs = 60_000;

    /// <summary>
    /// Subscription items-per-subscription ceiling per OPC UA stack
    /// universal server limit (Issue #564 in OPCFoundation/UA-.NETStandard).
    /// Drives the &quot;30K tags → 30 subscriptions; 50K → 50; 75K → 75&quot;
    /// math.
    /// </summary>
    public const int MaxItemsPerSubscription = 1_000;

    /// <summary>
    /// Compute the required <c>MinPublishRequestCount</c> for a given
    /// subscription count. Locked rule: <c>= subscription_count + 2</c>
    /// (per v2.1 §2.5 and stack guidance via Issue #564). Prevents
    /// notification loss under burst.
    /// </summary>
    public static int MinPublishRequestCount(int subscriptionCount) => subscriptionCount + 2;
}
