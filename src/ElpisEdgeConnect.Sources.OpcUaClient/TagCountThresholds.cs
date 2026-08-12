// ============================================================================
// File: TagCountThresholds.cs
// Purpose: Pure-logic classifier for the operator-facing tag-count
//          guardrails surfaced by the OPC UA Client wizard. Maps a
//          selected-tag count to one of four severity tiers aligned to
//          the v2.1 benchmark governance profiles.
//
//          Lives in Sources.OpcUaClient (next to the planner cap
//          MaxMonitoredItemsPerSession) rather than in Management so
//          the threshold values stay with the protocol's domain
//          knowledge. The wizard consumes via a project reference.
//
// LOCKED tier boundaries (per PR 7c plan + amendments, user lock
// 2026-05-29; aligned to v2.1 §6 Q9 measurement profiles):
//
//   ≤  9_999  → Informational  — silent / no banner
//   10_000–29_999 → Informational banner ("non-trivial workload")
//   30_000–74_999 → Warning      — within 30K validated profile but
//                                  approaching the stretch tier
//   75_000–99_999 → StrongWarning — exploratory territory; not
//                                  benchmark-validated
//  ≥100_000      → Blocked      — exceeds the per-session cap;
//                                  wizard Save must be disabled
//
//   The 100K cap matches OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession.
//   Any future revision to that cap MUST flow through here too.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §6 Q9
//            PR 7c plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Severity tiers a selected-tag count falls into, surfaced by the
/// wizard as banners + Save-button gating.
/// </summary>
public enum TagCountSeverity
{
    /// <summary>Below the informational threshold (≤ 9 999). No UX surface.</summary>
    None = 0,

    /// <summary>10 000 – 29 999. Operator banner: "non-trivial workload".</summary>
    Informational = 1,

    /// <summary>30 000 – 74 999. Banner: approaching stretch profile.</summary>
    Warning = 2,

    /// <summary>75 000 – 99 999. Banner: exploratory; not benchmark-validated.</summary>
    StrongWarning = 3,

    /// <summary>≥ 100 000. Wizard Save MUST be blocked.</summary>
    Blocked = 4,
}

/// <summary>
/// Classified outcome of a tag-count check. Carries the severity, the
/// operator-facing label, and a numeric breakdown of the profile-
/// alignment fields the Review step renders verbatim.
/// </summary>
public sealed record TagCountClassification
{
    /// <summary>The classified severity tier.</summary>
    public required TagCountSeverity Severity { get; init; }

    /// <summary>
    /// Operator-facing one-line label (e.g. "✓ Within validated profile").
    /// Rendered verbatim by the wizard's banner + Review-step
    /// Operational Expectations section.
    /// </summary>
    public required string ProfileAlignmentLabel { get; init; }

    /// <summary>
    /// Number of subscriptions the planner will allocate for the given
    /// tag count. Surfaced on Review per amendment #5
    /// (Operational Expectations).
    /// </summary>
    public required int ExpectedSubscriptions { get; init; }

    /// <summary>
    /// True when <see cref="Severity"/> is <see cref="TagCountSeverity.Blocked"/>.
    /// The wizard MUST disable Save when this flag is set
    /// (amendment #3, user lock 2026-05-29).
    /// </summary>
    public bool BlocksSave => Severity == TagCountSeverity.Blocked;
}

/// <summary>
/// Pure-logic tag-count threshold classifier. Used by both the
/// wizard's Browse-step live banner (amendment additional rec #7 —
/// running selection summary) and the Review-step Operational
/// Expectations section (amendment #5).
/// </summary>
public static class TagCountThresholds
{
    /// <summary>Lower bound of the Informational tier.</summary>
    public const int InformationalLowerBound = 10_000;

    /// <summary>Lower bound of the Warning tier (= upper bound of 30K validated profile).</summary>
    public const int WarningLowerBound = 30_000;

    /// <summary>Lower bound of the StrongWarning tier (= upper bound of the stretch profile).</summary>
    public const int StrongWarningLowerBound = 75_000;

    /// <summary>Lower bound of the Blocked tier (= per-session cap).</summary>
    public const int BlockedLowerBound = OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession;

    /// <summary>
    /// Classify a tag count into a severity tier, label, and
    /// subscription-count estimate. <paramref name="count"/> below zero
    /// is treated as zero (defensive — bound to a wizard field that
    /// should never deliver a negative).
    /// </summary>
    public static TagCountClassification Classify(int count)
    {
        var normalised = Math.Max(0, count);
        var severity = ClassifySeverity(normalised);
        return new TagCountClassification
        {
            Severity = severity,
            ProfileAlignmentLabel = LabelFor(severity),
            ExpectedSubscriptions = SubscriptionsFor(normalised),
        };
    }

    private static TagCountSeverity ClassifySeverity(int count)
    {
        if (count >= BlockedLowerBound) return TagCountSeverity.Blocked;
        if (count >= StrongWarningLowerBound) return TagCountSeverity.StrongWarning;
        if (count >= WarningLowerBound) return TagCountSeverity.Warning;
        if (count >= InformationalLowerBound) return TagCountSeverity.Informational;
        return TagCountSeverity.None;
    }

    private static string LabelFor(TagCountSeverity severity) => severity switch
    {
        TagCountSeverity.None           => "✓ Within validated profile",
        TagCountSeverity.Informational  => "✓ Within validated profile",
        TagCountSeverity.Warning        => "⚠ Stretch profile",
        TagCountSeverity.StrongWarning  => "⚠ Exploratory territory",
        TagCountSeverity.Blocked        => "⛔ Exceeds session cap",
        _                               => "✓ Within validated profile",
    };

    private static int SubscriptionsFor(int count)
    {
        if (count <= 0) return 0;
        var perSub = OpcUaClientSubscriptionPlanner.MaxItemsPerSubscription;
        return (count + perSub - 1) / perSub;
    }
}
