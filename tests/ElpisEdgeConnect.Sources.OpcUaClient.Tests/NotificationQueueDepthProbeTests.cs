// ============================================================================
// Tests: NotificationQueueDepthProbeTests — pin the reflection wrapper's
//        defensive behaviour against stack drift.
//
//        Invariants:
//          * IsAvailable = true when the locked field resolves at
//            construction (current stack version)
//          * Depth returns -1 on null subscription
//          * Depth returns -1 when reflection throws
//          * Field name pinned at "m_messageCache" (catches accidental
//            rename during refactors)
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §2.6
//            PR 4 plan amendment #3 (user lock 2026-05-29)
// ============================================================================

using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class NotificationQueueDepthProbeTests
{
    [Fact]
    public void Constructor_PinsTheLockedFieldName()
    {
        // If a future stack refactor renames m_messageCache, this
        // string-literal check forces the test author to deliberately
        // update the constant (rather than the probe silently degrading
        // to "always -1").
        NotificationQueueDepthProbe.MessageCacheFieldName.Should().Be("m_messageCache");
    }

    [Fact]
    public void IsAvailable_TrueAgainstPinnedStackVersion()
    {
        // The OPC Foundation stack pinned at 1.5.376.232 exposes
        // m_messageCache as a private instance field. If a future stack
        // upgrade removes / renames it, this test fails — prompts a
        // probe refactor rather than silent metric degradation.
        var probe = new NotificationQueueDepthProbe();

        probe.IsAvailable.Should().BeTrue(
            "the locked stack version (1.5.376.232) exposes m_messageCache; "
            + "a future stack upgrade may rename this field — when it does, refactor the probe.");
    }

    [Fact]
    public void Depth_NullSubscription_ReturnsMinusOne()
    {
        var probe = new NotificationQueueDepthProbe();

        probe.Depth(null!).Should().Be(-1);
    }

    [Fact]
    public void Depth_ZeroOnFreshSubscription_OrMinusOneIfReflectionFails()
    {
        // A freshly constructed Subscription should have an empty cache
        // (depth = 0). But if reflection raises (the cache field is null
        // before Subscription.Create / not assigned), the probe returns
        // -1. Either outcome is acceptable for this defensive test —
        // what matters is the probe NEVER throws.
        var probe = new NotificationQueueDepthProbe();
        var subscription = new Opc.Ua.Client.Subscription();

        var depth = probe.Depth(subscription);

        depth.Should().BeGreaterOrEqualTo(-1, "depth is either 0 (empty cache) or -1 (not yet initialised)");
        depth.Should().BeLessOrEqualTo(0, "a fresh subscription cannot have positive depth");
    }
}
