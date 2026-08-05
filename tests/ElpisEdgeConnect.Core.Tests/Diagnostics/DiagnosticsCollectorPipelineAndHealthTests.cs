// ============================================================================
// File: Diagnostics/DiagnosticsCollectorPipelineAndHealthTests.cs
// Purpose: Phase 3 tests for the RuntimeDiagnosticsCollector as the
//          concrete implementation of ITransformMetricsRecorder,
//          ISourceHealthSink, and ISinkHealthSink. Pins:
//            * per-step pipeline counters accumulate correctly
//            * source push updates flow into the route snapshot
//            * sink adapter state is merged with publish-level state on
//              the SAME SinkHealthSnapshot (no parallel state store)
//            * a single snapshot reflects a coherent point in time across
//              all three dimensions
// Milestone: C4 — phase 3.
// ============================================================================

using System;
using System.Linq;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class DiagnosticsCollectorPipelineAndHealthTests
{
    [Fact]
    public void RecordStep_AccumulatesPerStepCounters()
    {
        var c = new RuntimeDiagnosticsCollector();

        c.RecordStep("r1", "filter", 100, 80, 20, TimeSpan.FromMilliseconds(5));
        c.RecordStep("r1", "rename", 80, 80, 0, TimeSpan.FromMilliseconds(2));
        c.RecordStep("r1", "filter", 50, 40, 10, TimeSpan.FromMilliseconds(3));
        c.RecordStep("r1", "rename", 40, 40, 0, TimeSpan.FromMilliseconds(1));

        var snap = c.GetRouteSnapshot("r1")!;
        snap.Pipeline.Steps.Should().HaveCount(2);

        var filter = snap.Pipeline.Steps.Single(s => s.StepKind == "filter");
        filter.Invocations.Should().Be(2);
        filter.PointsIn.Should().Be(150);
        filter.PointsOut.Should().Be(120);
        filter.TotalDuration.Should().Be(TimeSpan.FromMilliseconds(8));

        var rename = snap.Pipeline.Steps.Single(s => s.StepKind == "rename");
        rename.Invocations.Should().Be(2);
        rename.PointsIn.Should().Be(120);
        rename.PointsOut.Should().Be(120);
    }

    [Fact]
    public void PipelineRollup_UsesFirstAndLastStepForTopLevelTotals()
    {
        var c = new RuntimeDiagnosticsCollector();

        // First-seen step becomes the entry; last-seen becomes the exit.
        c.RecordStep("r1", "entry", 100, 90, 10, TimeSpan.Zero);
        c.RecordStep("r1", "mid", 90, 85, 5, TimeSpan.Zero);
        c.RecordStep("r1", "exit", 85, 80, 5, TimeSpan.Zero);
        c.RecordStep("r1", "entry", 50, 50, 0, TimeSpan.Zero);
        c.RecordStep("r1", "mid", 50, 45, 5, TimeSpan.Zero);
        c.RecordStep("r1", "exit", 45, 40, 5, TimeSpan.Zero);

        var p = c.GetRouteSnapshot("r1")!.Pipeline;
        p.BatchesProcessed.Should().Be(2);
        p.PointsIn.Should().Be(150);
        p.PointsOut.Should().Be(120);
        p.Steps.Select(s => s.StepKind).Should().Equal("entry", "mid", "exit");
    }

    [Fact]
    public void RecordSourceObservation_UpdatesCountsAndLastObservedAt()
    {
        var c = new RuntimeDiagnosticsCollector();
        var t0 = DateTime.UtcNow;

        c.RecordSourceObservation("r1", "src-1", "mock", 10, t0);
        c.RecordSourceObservation("r1", "src-1", "mock", 15, t0.AddSeconds(1));
        c.RecordSourceObservation("r1", "src-1", "mock", 0, t0.AddSeconds(2)); // no-op observation

        var src = c.GetRouteSnapshot("r1")!.Source!;
        src.SourceInstanceId.Should().Be("src-1");
        src.ProtocolName.Should().Be("mock");
        src.PointsObserved.Should().Be(25);
        src.LastPointAtUtc.Should().Be(t0.AddSeconds(1)); // unchanged by 0-count observation
    }

    [Fact]
    public void RecordSourceState_UpdatesStateAndError()
    {
        var c = new RuntimeDiagnosticsCollector();

        c.RecordSourceState("r1", "src-1", "mock", AdapterState.Running, null);
        var snap1 = c.GetRouteSnapshot("r1")!;
        snap1.Source!.State.Should().Be(AdapterState.Running);
        snap1.Source.LastError.Should().BeNull();

        var err = new AdapterError
        {
            Code = "MOCK.CONN_LOST",
            Category = ErrorCategory.Network,
            Message = "connection dropped",
        };
        c.RecordSourceState("r1", "src-1", "mock", AdapterState.Failed, err);

        var snap2 = c.GetRouteSnapshot("r1")!;
        snap2.Source!.State.Should().Be(AdapterState.Failed);
        snap2.Source.LastError.Should().NotBeNull();
        snap2.Source.LastError!.Code.Should().Be("MOCK.CONN_LOST");
        snap2.Source.LastErrorAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void RecordSourceState_RecoveryToRunning_ClearsStaleError()
    {
        // Regression: a source that reconnected (Degraded -> Running with no
        // current error) must NOT keep showing the old connect error forever.
        // Before the fix, the "if (lastError is not null)" guard preserved the
        // stale error, so the Overview showed MODBUS.CONNECT_FAILED even while
        // the source was Running and reading again.
        var c = new RuntimeDiagnosticsCollector();

        c.RecordSourceState("r1", "src-1", "modbustcp", AdapterState.Degraded, new AdapterError
        {
            Code = "MODBUS.CONNECT_FAILED",
            Category = ErrorCategory.Network,
            Message = "device not reachable",
        });
        c.GetRouteSnapshot("r1")!.Source!.LastError!.Code.Should().Be("MODBUS.CONNECT_FAILED");

        // Device comes back: supervisor records Running with a null current error.
        c.RecordSourceState("r1", "src-1", "modbustcp", AdapterState.Running, null);

        var snap = c.GetRouteSnapshot("r1")!;
        snap.Source!.State.Should().Be(AdapterState.Running);
        snap.Source.LastError.Should().BeNull("a reconnected source must clear the stale error");
        snap.Source.LastErrorAtUtc.Should().BeNull();
    }


    [Fact]
    public void SinkAdapterState_MergesWithPublishLevelStateInSameSnapshot()
    {
        // Single-source-of-truth pin: publish-level state (via
        // OnSinkDegraded/OnSinkRecovered) and adapter-level state (via
        // RecordSinkAdapterState) must live on the SAME SinkHealthSnapshot,
        // proving there is no parallel store.
        var c = new RuntimeDiagnosticsCollector();

        c.OnSinkDegraded(new SinkDegradedEvent
        {
            RouteId = "r1",
            SinkInstanceId = "s1",
            LastError = new AdapterError
            {
                Code = "CORE.PUBLISH_FAILED",
                Category = ErrorCategory.Network,
                Message = "publish failed",
            },
            RetryAttempts = 1,
            ObservedAtUtc = DateTime.UtcNow,
        });

        c.RecordSinkAdapterState(
            "r1",
            "s1",
            AdapterState.Running,
            lastError: null);

        var snap = c.GetRouteSnapshot("r1")!;
        snap.Sinks.Should().HaveCount(1);
        var s = snap.Sinks[0];
        s.IsDegraded.Should().BeTrue();                          // from publish-level
        s.AdapterState.Should().Be(AdapterState.Running);        // from adapter-level
        s.LastError!.Code.Should().Be("CORE.PUBLISH_FAILED");    // publish-level error preserved
    }

    [Fact]
    public void AllDimensions_CoherentInSingleSnapshot()
    {
        // Drive every dimension; take ONE snapshot; verify every field is
        // internally consistent.
        var c = new RuntimeDiagnosticsCollector();
        var now = DateTime.UtcNow;

        // Routing
        c.OnRouteStateChanged(new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Configured,
            To = RouteState.Running,
            ObservedAtUtc = now,
        });
        c.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 4,
            Reason = "full",
            ObservedAtUtc = now,
        });

        // Pipeline
        c.RecordStep("r1", "step-a", 200, 180, 20, TimeSpan.FromMilliseconds(10));

        // Source
        c.RecordSourceObservation("r1", "src-1", "mock", 200, now);
        c.RecordSourceState("r1", "src-1", "mock", AdapterState.Running, null);

        // Sink
        c.OnSinkDegraded(new SinkDegradedEvent
        {
            RouteId = "r1",
            SinkInstanceId = "s1",
            LastError = new AdapterError
            {
                Code = "X.Y",
                Category = ErrorCategory.Network,
                Message = "fail",
            },
            RetryAttempts = 1,
            ObservedAtUtc = now,
        });
        c.RecordSinkAdapterState("r1", "s1", AdapterState.Running, null);

        var snap = c.GetRouteSnapshot("r1")!;

        // Route rollup
        snap.State.Should().Be(RouteState.Running);
        snap.StateTransitionCount.Should().Be(1);
        snap.BackpressureDropCount.Should().Be(4);

        // Source
        snap.Source!.PointsObserved.Should().Be(200);
        snap.Source.State.Should().Be(AdapterState.Running);

        // Pipeline
        snap.Pipeline.BatchesProcessed.Should().Be(1);
        snap.Pipeline.PointsIn.Should().Be(200);
        snap.Pipeline.PointsOut.Should().Be(180);
        snap.Pipeline.Steps.Should().HaveCount(1);

        // Sink
        snap.Sinks.Should().HaveCount(1);
        snap.Sinks[0].IsDegraded.Should().BeTrue();
        snap.Sinks[0].AdapterState.Should().Be(AdapterState.Running);
    }

    [Fact]
    public void RecordStep_UnknownRoute_LazyCreatesRouteEntry()
    {
        // RecordStep arriving before any routing event must still work —
        // pipeline-first routes should show up in the snapshot.
        var c = new RuntimeDiagnosticsCollector();
        c.RecordStep("r1", "step", 10, 10, 0, TimeSpan.Zero);

        var snap = c.GetRouteSnapshot("r1");
        snap.Should().NotBeNull();
        snap!.Pipeline.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void NullAndEmpty_Inputs_AreSilentlyIgnored()
    {
        // Hot-path discipline: never throw on null/empty inputs.
        var c = new RuntimeDiagnosticsCollector();

        var act1 = () => c.RecordStep("", "step", 1, 1, 0, TimeSpan.Zero);
        var act2 = () => c.RecordStep("r1", "", 1, 1, 0, TimeSpan.Zero);
        var act3 = () => c.RecordSourceObservation("", "src", "mock", 1, DateTime.UtcNow);
        var act4 = () => c.RecordSinkAdapterState("r1", "", AdapterState.Running, null);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
        act4.Should().NotThrow();
        c.KnownRouteIds.Should().BeEmpty();
    }
}
