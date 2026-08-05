// ============================================================================
// File: Diagnostics/RouteTapTests.cs
// Covers: the demand-driven Live Data Tap capture service (ADR-0018 / ADR-0017,
//         M1). Activation + per-route isolation + hot-path-clean-at-idle +
//         bounded-ring eviction + per-sink fan-out + cooldown release +
//         cursor reads + the capture-time masker hook (wired in M1.5).
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class RouteTapTests
{
    private static CanonicalDataPoint P(string tag = "t", long seq = 1, object? value = null) =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW")
            .WithSource("src", "mock")
            .WithDevice("dev")
            .WithTag(tag, tag)
            .WithValue(value ?? 1.0, CanonicalValueType.Double)
            .WithGoodQuality(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithSequence(seq)
            .Build();

    private static CanonicalDataPoint[] Batch(params CanonicalDataPoint[] pts) => pts;

    [Fact]
    public void IsTapActive_NoSubscriber_ReturnsFalse()
    {
        var tap = new RouteTap();
        tap.IsTapActive("r1").Should().BeFalse();
        tap.IsTapActive("unknown").Should().BeFalse();
    }

    [Fact]
    public void Subscribe_ActivatesOnlyThatRoute_RouteIsolation()
    {
        var tap = new RouteTap();
        using var _ = tap.Subscribe("r1");

        tap.IsTapActive("r1").Should().BeTrue();
        tap.IsTapActive("r2").Should().BeFalse("tapping r1 must never activate r2 (ADR-0017 Rule 3)");
    }

    [Fact]
    public void Capture_WhenInactive_IsNoOp_HotPathClean()
    {
        var tap = new RouteTap();

        // No subscriber → the runtime would see IsTapActive=false and skip
        // capture. Even if capture is called, it must do nothing.
        tap.IsTapActive("r1").Should().BeFalse();
        tap.CaptureSource("r1", Batch(P("a"), P("b")));
        tap.CaptureSink("r1", "snk", Batch(P("a")));

        tap.ReadSince("r1", 0).Should().BeEmpty();
    }

    [Fact]
    public void CaptureSource_WhenActive_StoredWithCorrelationAndMonotonicSequence()
    {
        var tap = new RouteTap();
        using var _ = tap.Subscribe("r1");

        tap.CaptureSource("r1", Batch(P("a", seq: 10), P("b", seq: 11)));

        var caps = tap.ReadSince("r1", 0);
        caps.Should().HaveCount(2);
        caps.Should().OnlyContain(c => c.Side == TapSide.Source && c.SinkInstanceId == null);
        caps[0].CaptureSequence.Should().Be(1);
        caps[1].CaptureSequence.Should().Be(2);
        caps[0].CorrelationId.Should().Contain("r1").And.Contain("|a|").And.EndWith("|10");
    }

    [Fact]
    public void CaptureSink_PerSink_FanOut_KeepsRingsSeparate()
    {
        var tap = new RouteTap();
        using var _ = tap.Subscribe("r1");

        tap.CaptureSource("r1", Batch(P("a")));
        tap.CaptureSink("r1", "sink-A", Batch(P("a")));
        tap.CaptureSink("r1", "sink-B", Batch(P("a")));

        var caps = tap.ReadSince("r1", 0);
        caps.Should().HaveCount(3);
        caps.Should().ContainSingle(c => c.Side == TapSide.Source);
        caps.Should().ContainSingle(c => c.Side == TapSide.Sink && c.SinkInstanceId == "sink-A");
        caps.Should().ContainSingle(c => c.Side == TapSide.Sink && c.SinkInstanceId == "sink-B");
    }

    [Fact]
    public void Ring_EvictsOldestSilently_WhenFull()
    {
        var tap = new RouteTap(new RouteTapOptions { RingCapacity = 3 });
        using var _ = tap.Subscribe("r1");

        for (var i = 0; i < 5; i++)
        {
            tap.CaptureSource("r1", Batch(P("a", seq: i)));
        }

        var caps = tap.ReadSince("r1", 0);
        caps.Should().HaveCount(3, "ring holds at most 3; oldest two evicted silently");
        caps[0].CaptureSequence.Should().Be(3);
        caps[2].CaptureSequence.Should().Be(5);
    }

    [Fact]
    public void ReadSince_Cursor_ReturnsOnlyNewerCaptures()
    {
        var tap = new RouteTap();
        using var _ = tap.Subscribe("r1");
        tap.CaptureSource("r1", Batch(P("a"), P("b"), P("c")));

        tap.ReadSince("r1", 0).Should().HaveCount(3);
        var tail = tap.ReadSince("r1", 2);
        tail.Should().ContainSingle();
        tail[0].CaptureSequence.Should().Be(3);
    }

    [Fact]
    public void Cooldown_KeepsActiveAfterUnsubscribe_ThenDeactivatesAndReleases()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var tap = new RouteTap(
            new RouteTapOptions { Cooldown = TimeSpan.FromSeconds(60) },
            utcNow: () => now);

        var sub = tap.Subscribe("r1");
        tap.CaptureSource("r1", Batch(P("a")));
        tap.IsTapActive("r1").Should().BeTrue();

        sub.Dispose(); // last subscriber leaves → cooldown starts
        tap.IsTapActive("r1").Should().BeTrue("still within the 60 s cooldown — capture must not drop on brief navigate-away");
        tap.CaptureSource("r1", Batch(P("b"))); // still captured during cooldown
        tap.ReadSince("r1", 0).Should().HaveCount(2);

        now = now.AddSeconds(61); // cooldown expires
        tap.IsTapActive("r1").Should().BeFalse();

        tap.Sweep(); // reclaims the route's rings
        tap.ReadSince("r1", 0).Should().BeEmpty("rings released after the cooldown");
    }

    [Fact]
    public void MultiSubscriber_RefCounted_StaysActiveUntilLastLeaves()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var tap = new RouteTap(
            new RouteTapOptions { Cooldown = TimeSpan.FromSeconds(60) },
            utcNow: () => now);

        var a = tap.Subscribe("r1");
        var b = tap.Subscribe("r1");

        a.Dispose();
        tap.IsTapActive("r1").Should().BeTrue("one subscriber remains");

        b.Dispose();
        now = now.AddSeconds(61);
        tap.IsTapActive("r1").Should().BeFalse("last subscriber gone + cooldown elapsed");
    }

    [Fact]
    public void GetStatus_ReportsActiveCountsAndTruncation()
    {
        var tap = new RouteTap(new RouteTapOptions { RingCapacity = 2 });

        tap.GetStatus("unknown").Should().Be(RouteTapStatus.Inactive);

        using var _ = tap.Subscribe("r1");
        tap.CaptureSource("r1", Batch(P("a"), P("b"), P("c"))); // 3 into a cap-2 ring → 1 evicted
        tap.CaptureSink("r1", "snk", Batch(P("a")));

        var st = tap.GetStatus("r1");
        st.Active.Should().BeTrue();
        st.SourceCaptured.Should().Be(3, "TotalAdded counts every capture, including evicted");
        st.SinkCaptured.Should().Be(1);
        st.Truncated.Should().BeTrue("the source ring evicted — the operator sees a recent sample");
    }

    [Fact]
    public void Masker_AppliedAtCapture_M15Hook()
    {
        // M1.5 injects the real sensitive-tag masker here. Prove the hook runs.
        var tap = new RouteTap(masker: p =>
            p.TagName == "secret"
                ? new CanonicalDataPointBuilder()
                    .WithGateway(p.GatewayId).WithSource(p.SourceInstanceId, p.ProtocolName)
                    .WithDevice(p.DeviceId).WithTag(p.TagName, p.TagPath)
                    .WithValue("***", CanonicalValueType.String)
                    .WithGoodQuality(p.DeviceTimestamp).WithSequence(p.SequenceNumber).Build()
                : p);
        using var _ = tap.Subscribe("r1");

        tap.CaptureSource("r1", Batch(P("secret", value: 1.0), P("public", value: 2.0)));

        var caps = tap.ReadSince("r1", 0);
        caps.Should().Contain(c => c.Point.TagName == "secret" && Equals(c.Point.Value, "***"));
        caps.Should().Contain(c => c.Point.TagName == "public" && Equals(c.Point.Value, 2.0));
    }
}
