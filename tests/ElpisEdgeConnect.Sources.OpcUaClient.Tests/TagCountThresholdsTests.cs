// ============================================================================
// Tests: TagCountThresholdsTests — pin the PR 7c-1 wizard guardrail
//        thresholds against the v2.1 §6 Q9 benchmark profile boundaries.
//
//        Boundary tests are intentionally exhaustive (one test per
//        threshold edge) because future revisions to the profile tiers
//        are likely to ripple through governance docs + wizard UX, and
//        the breadcrumb of failing boundary tests is the cheapest way
//        to catch a partial update.
// Reference: PR 7c-1 plan + amendments (user lock 2026-05-29)
//            docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §6 Q9
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class TagCountThresholdsTests
{
    // ─── Severity boundaries ──────────────────────────────────────────

    [Theory]
    [InlineData(-100,    TagCountSeverity.None,           false)]  // defensive negative
    [InlineData(0,       TagCountSeverity.None,           false)]
    [InlineData(9_999,   TagCountSeverity.None,           false)]
    [InlineData(10_000,  TagCountSeverity.Informational,  false)]
    [InlineData(29_999,  TagCountSeverity.Informational,  false)]
    [InlineData(30_000,  TagCountSeverity.Warning,        false)]
    [InlineData(50_000,  TagCountSeverity.Warning,        false)]
    [InlineData(74_999,  TagCountSeverity.Warning,        false)]
    [InlineData(75_000,  TagCountSeverity.StrongWarning,  false)]
    [InlineData(99_999,  TagCountSeverity.StrongWarning,  false)]
    [InlineData(100_000, TagCountSeverity.Blocked,        true)]
    [InlineData(100_001, TagCountSeverity.Blocked,        true)]
    public void Classify_SeverityAndBlockedFlag_MatchBoundaryTable(
        int count, TagCountSeverity expectedSeverity, bool expectedBlocked)
    {
        var classification = TagCountThresholds.Classify(count);

        classification.Severity.Should().Be(expectedSeverity);
        classification.BlocksSave.Should().Be(expectedBlocked);
    }

    // ─── Profile alignment labels ─────────────────────────────────────

    [Theory]
    [InlineData(0,       "✓ Within validated profile")]
    [InlineData(25_000,  "✓ Within validated profile")]
    [InlineData(30_000,  "⚠ Stretch profile")]
    [InlineData(50_000,  "⚠ Stretch profile")]
    [InlineData(75_000,  "⚠ Exploratory territory")]
    [InlineData(100_000, "⛔ Exceeds session cap")]
    public void Classify_ProfileAlignmentLabel_MatchesLockedTable(int count, string expectedLabel)
    {
        TagCountThresholds.Classify(count).ProfileAlignmentLabel.Should().Be(expectedLabel);
    }

    // ─── Expected subscription count ──────────────────────────────────

    [Theory]
    [InlineData(0,      0)]    // 0 tags → 0 subscriptions
    [InlineData(1,      1)]    // 1 tag  → 1 subscription
    [InlineData(1_000,  1)]    // exactly at MaxItemsPerSubscription → 1 sub
    [InlineData(1_001,  2)]    // one over → spills into a second sub
    [InlineData(30_000, 30)]   // 30K validated profile
    [InlineData(75_000, 75)]   // 75K exploratory edge
    [InlineData(100_000, 100)] // session cap
    public void Classify_ExpectedSubscriptions_MatchesPlannerBatching(int count, int expectedSubs)
    {
        TagCountThresholds.Classify(count).ExpectedSubscriptions.Should().Be(expectedSubs);
    }

    // ─── Boundary constants must stay aligned to planner cap ─────────

    [Fact]
    public void BlockedLowerBound_EqualsPlannerSessionCap()
    {
        TagCountThresholds.BlockedLowerBound.Should().Be(
            OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession,
            "the wizard's Blocked threshold MUST match the planner's per-session cap — "
            + "any revision to either MUST flow through to the other");
    }
}
