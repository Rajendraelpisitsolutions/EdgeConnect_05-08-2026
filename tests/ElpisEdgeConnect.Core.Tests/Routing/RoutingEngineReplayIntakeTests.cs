// ============================================================================
// File: Routing/RoutingEngineReplayIntakeTests.cs
// Covers: K1.3 slice 2 — the RouteWorker intake pump for a replay route appends on the
//         TRACKED path (points + latest_value manifest + next_sequence) at the route's
//         fixed generation, with NO depth backpressure. A tracked-append failure FAULTS
//         the route (never a silent drop). Uses a real SqliteBuffer via
//         DefaultRouteBufferFactory + a replay-aware sink whose base publish drains (the
//         ReplayRouteDriver is slice 3), so only the intake is under test.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.md §R5 slice 2;
//            v3.2 §C1 (fixed generation).
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineReplayIntakeTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task ReplayRoute_Intake_Uses_TrackedAppend_And_Maintains_Manifest()
    {
        var dataPath = NewDataPath();
        try
        {
            var source = new FakeSourceIntake("src-1");
            var sink = new FakeReplayAwareSink("sp") { DrainViaBasePublish = true };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", source, sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);

            // Three DISTINCT metrics (distinct tag paths) with UTC timestamps.
            for (var i = 0; i < 3; i++)
            {
                await source.WriteAsync(RoutingTestData.MakePoint(i, tag: $"Spindle/T{i}"), Ct);
            }
            source.Complete();

            var db = BufferPath(dataPath, "route-a");
            await WaitForAsync(() => CountManifest(db) == 3, TimeSpan.FromSeconds(5));

            // If the intake had used the legacy EnqueueAsync, the enabled store would have
            // rejected it and the route would have faulted. It did not.
            engine.GetRouteState("route-a").Should().NotBe(RouteState.Failed);
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Intake_TrackedAppend_Failure_Faults_The_Route()
    {
        var dataPath = NewDataPath();
        try
        {
            var source = new FakeSourceIntake("src-1");
            var sink = new FakeReplayAwareSink("sp") { DrainViaBasePublish = true };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", source, sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);

            // A non-UTC device timestamp is unrepresentable on the tracked path (fail loud, never
            // silently shift) — the tracked append faults the route rather than dropping the point.
            await source.WriteAsync(NonUtcPoint(), Ct);

            await WaitForAsync(() => engine.GetRouteState("route-a") == RouteState.Failed, TimeSpan.FromSeconds(5));

            engine.GetRouteState("route-a").Should().Be(RouteState.Failed);
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Intake_Failure_Fault_Reason_Preserves_The_Original_Error()
    {
        var dataPath = NewDataPath();
        try
        {
            var recorder = new FailedReasonRecorder();
            var source = new FakeSourceIntake("src-1");
            var sink = new FakeReplayAwareSink("sp") { DrainViaBasePublish = true };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath), recorder);

            await engine.RegisterRouteAsync(ReplayDef("route-a", source, sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await source.WriteAsync(NonUtcPoint(), Ct);

            await WaitForAsync(() => recorder.FailedReason is not null, TimeSpan.FromSeconds(5));

            // The route faults with the REAL tracked-append error, not the cleanup cancellation.
            recorder.FailedReason.Should().Contain("BufferException");
            recorder.FailedReason.Should().Contain("Tracked append"); // the actual tracked-append cause
            recorder.FailedReason.Should().NotContain("OperationCanceled");
            recorder.FailedReason.Should().NotContain("Canceled");
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_External_Stop_Is_A_Clean_Stop_Not_Failed()
    {
        var dataPath = NewDataPath();
        try
        {
            var source = new FakeSourceIntake("src-1");
            var sink = new FakeReplayAwareSink("sp") { DrainViaBasePublish = true };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", source, sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await source.WriteAsync(RoutingTestData.MakePoint(0, tag: "Spindle/T0"), Ct);
            await WaitForAsync(() => CountManifest(BufferPath(dataPath, "route-a")) == 1, TimeSpan.FromSeconds(5));

            await engine.StopRouteAsync("route-a", Ct);

            engine.GetRouteState("route-a").Should().NotBe(RouteState.Failed);
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Intake_Appends_At_The_Persisted_NonZero_Generation()
    {
        var dataPath = NewDataPath();
        try
        {
            var db = BufferPath(dataPath, "route-a");
            // Drive the store to generation 1 before the route reopens it.
            await using (var store = await SqliteRouteStore.OpenAsync("route-a", db, ReplaySfPolicy()))
            {
                await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);
                await store.AppendAsync(new[] { RoutingTestData.MakePoint(0, tag: "Spindle/Old") }, 0, Ct);
                await store.AckAsync("sp", 0, Ct);
                await store.AdvanceGenerationAsync(0, 1, Ct);
            }

            var source = new FakeSourceIntake("src-1");
            var sink = new FakeReplayAwareSink("sp") { DrainViaBasePublish = true };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", source, sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await source.WriteAsync(RoutingTestData.MakePoint(1, tag: "Spindle/New"), Ct);

            // The new metric is appended at the PERSISTED generation (1), not a hardcoded 0.
            await WaitForAsync(() => ManifestGenerationOf(db, "Spindle/New") == 1, TimeSpan.FromSeconds(5));
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Driver_Birth_Failure_Faults_Route_While_Source_Open()
    {
        var dataPath = NewDataPath();
        try
        {
            var recorder = new FailedReasonRecorder();
            var source = new FakeSourceIntake("src-1");
            var sink = new FakeReplayAwareSink("sp") { BeginThrows = new InvalidOperationException("birth boom") };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath), recorder);

            await engine.RegisterRouteAsync(ReplayDef("route-a", source, sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);

            // The source is NEVER completed. The driver's birth failure must still fault the route
            // promptly (concurrent intake/driver supervision), not wait for the source to end.
            await WaitForAsync(() => engine.GetRouteState("route-a") == RouteState.Failed, TimeSpan.FromSeconds(5));

            recorder.FailedReason.Should().Contain("birth boom");
            recorder.FailedReason.Should().NotContain("Canceled");
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Session_Id_Advances_Across_Unregister_ReRegister()
    {
        var dataPath = NewDataPath();
        try
        {
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            var sink1 = new FakeReplayAwareSink("sp");
            await engine.RegisterRouteAsync(ReplayDef("route-a", new FakeSourceIntake("s1"), sink1), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await WaitForAsync(() => sink1.BeginCount == 1, TimeSpan.FromSeconds(5));
            var s1 = sink1.LastSessionId;

            await engine.StopRouteAsync("route-a", Ct);
            await engine.UnregisterRouteAsync("route-a", Ct);

            // Re-register the same route id — a NEW Route object. The engine's process-wide session
            // source must still mint a strictly-greater session id (a per-Route counter would reset).
            var sink2 = new FakeReplayAwareSink("sp");
            await engine.RegisterRouteAsync(ReplayDef("route-a", new FakeSourceIntake("s2"), sink2), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await WaitForAsync(() => sink2.BeginCount == 1, TimeSpan.FromSeconds(5));
            var s2 = sink2.LastSessionId;

            s1.Should().NotBeNull();
            s2.Should().NotBeNull();
            s2!.Value.Value.Should().BeGreaterThan(s1!.Value.Value);
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Intake_Continues_While_Data_Is_Paused_For_A_Rebirth()
    {
        var dataPath = NewDataPath();
        try
        {
            using var rebirthGate = new SemaphoreSlim(0, 1);
            var source = new FakeSourceIntake("src-1");
            var sink = new FakeReplayAwareSink("sp")
            {
                RequestRebirthOnNextPublish = true,
                RebirthGate = rebirthGate,
            };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", source, sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);

            // First metric → the driver publishes it, the sink forces a rebirth, and RebirthAsync
            // blocks on the gate: the DATA path is now paused mid-rebirth.
            await source.WriteAsync(RoutingTestData.MakePoint(0, tag: "Spindle/T0"), Ct);
            await WaitForAsync(() => sink.RebirthCount == 1, TimeSpan.FromSeconds(5));

            var db = BufferPath(dataPath, "route-a");
            // While DATA is paused, the intake pump keeps appending on the tracked path — the manifest
            // grows to three distinct metrics even though not a single DATA publish can complete.
            await source.WriteAsync(RoutingTestData.MakePoint(1, tag: "Spindle/T1"), Ct);
            await source.WriteAsync(RoutingTestData.MakePoint(2, tag: "Spindle/T2"), Ct);
            await WaitForAsync(() => CountManifest(db) == 3, TimeSpan.FromSeconds(5));

            // Release the rebirth: the driver promotes the epoch and re-drives, delivering every point
            // (the triggering one plus the two appended while DATA was paused) under the new epoch.
            rebirthGate.Release();
            await WaitForAsync(() => sink.ReplayPublishedPoints.Count >= 3, TimeSpan.FromSeconds(5));

            engine.GetRouteState("route-a").Should().NotBe(RouteState.Failed);
            sink.RebirthCount.Should().Be(1);
            sink.LastRebirth!.Epoch.Value.Should().Be(1);
        }
        finally { TryDeleteDir(dataPath); }
    }

    // ---- slice 5: graceful session end + config-replace reason --------------

    [Fact]
    public async Task ReplayRoute_Clean_Stop_Emits_EndSession_Once_With_Reason_Stop_Before_Stopped()
    {
        var dataPath = NewDataPath();
        try
        {
            var sink = new FakeReplayAwareSink("sp");
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", new FakeSourceIntake("s"), sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await WaitForAsync(() => sink.BeginCount == 1, TimeSpan.FromSeconds(5));

            await engine.StopRouteAsync("route-a", Ct);

            // StopRouteAsync awaits the worker, which awaits the driver, which emits End BEFORE it
            // completes — so End (Core) has run by the time the route is Stopped (before Host StopAsync).
            sink.EndSessionCount.Should().Be(1);
            sink.LastEndReason.Should().Be(ReplaySessionEndReason.Stop);
            engine.GetRouteState("route-a").Should().Be(RouteState.Stopped);
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_ConfigurationReplaced_Stop_Emits_End_With_That_Reason()
    {
        var dataPath = NewDataPath();
        try
        {
            var sink = new FakeReplayAwareSink("sp");
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", new FakeSourceIntake("s"), sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await WaitForAsync(() => sink.BeginCount == 1, TimeSpan.FromSeconds(5));

            // The config-replace teardown seam: an EXPLICIT reason, never inferred from the cancellation.
            await engine.StopRouteAsync("route-a", ReplaySessionEndReason.ConfigurationReplaced, Ct);

            sink.EndSessionCount.Should().Be(1);
            sink.LastEndReason.Should().Be(ReplaySessionEndReason.ConfigurationReplaced);
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Unregister_With_ConfigurationReplaced_Ends_The_Session_With_That_Reason()
    {
        var dataPath = NewDataPath();
        try
        {
            var sink = new FakeReplayAwareSink("sp");
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", new FakeSourceIntake("s"), sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await WaitForAsync(() => sink.BeginCount == 1, TimeSpan.FromSeconds(5));

            await engine.UnregisterRouteAsync("route-a", ReplaySessionEndReason.ConfigurationReplaced, Ct);

            sink.EndSessionCount.Should().Be(1);
            sink.LastEndReason.Should().Be(ReplaySessionEndReason.ConfigurationReplaced);
            engine.RegisteredRouteIds.Should().NotContain("route-a"); // fully unregistered
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Birth_Failure_Does_Not_Emit_EndSession()
    {
        var dataPath = NewDataPath();
        try
        {
            var sink = new FakeReplayAwareSink("sp") { BeginThrows = new InvalidOperationException("birth boom") };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", new FakeSourceIntake("s"), sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await WaitForAsync(() => engine.GetRouteState("route-a") == RouteState.Failed, TimeSpan.FromSeconds(5));

            // A session that never begun is never ended — End is only for a successfully begun session.
            sink.EndSessionCount.Should().Be(0);
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_EndSession_Failure_Is_Reported_But_Still_Reaches_Stopped()
    {
        var dataPath = NewDataPath();
        try
        {
            var sink = new FakeReplayAwareSink("sp") { EndThrows = new InvalidOperationException("death boom") };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", new FakeSourceIntake("s"), sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await WaitForAsync(() => sink.BeginCount == 1, TimeSpan.FromSeconds(5));

            await engine.StopRouteAsync("route-a", Ct);

            // An End failure must NOT abort reverse-phase cleanup: the route still reaches Stopped.
            sink.EndSessionCount.Should().Be(1);
            engine.GetRouteState("route-a").Should().Be(RouteState.Stopped);
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Blocking_EndSession_Does_Not_Wedge_Stop()
    {
        // [s5 r1 blocker 1] A non-cooperative EndSessionAsync must not wedge StopRouteAsync — the End is
        // bounded (engine-configured), so the whole stop chain completes within the bound.
        var dataPath = NewDataPath();
        try
        {
            var sink = new FakeReplayAwareSink("sp") { EndBlocksUntilCancelled = true };
            await using var engine = new RoutingEngine(
                new DefaultRouteBufferFactory(dataPath), replayEndSessionTimeout: TimeSpan.FromMilliseconds(200));

            await engine.RegisterRouteAsync(ReplayDef("route-a", new FakeSourceIntake("s"), sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await WaitForAsync(() => sink.BeginCount == 1, TimeSpan.FromSeconds(5));

            await engine.StopRouteAsync("route-a", Ct).WaitAsync(TimeSpan.FromSeconds(5));

            engine.GetRouteState("route-a").Should().Be(RouteState.Stopped);
            sink.EndSessionCount.Should().Be(1);
            sink.EndTokenObservedCancellation.Should().BeTrue();
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task ReplayRoute_Concurrent_Stops_Do_Not_Overwrite_The_Winners_Reason()
    {
        // [s5 r1 blocker 2] Caller A wins with ConfigurationReplaced; caller B (ordinary Stop) races in
        // while the route is Stopping. B must lose the claim and NOT clobber the reason — End observes
        // ConfigurationReplaced exactly once.
        var dataPath = NewDataPath();
        try
        {
            using var endGate = new SemaphoreSlim(0, 1);
            var sink = new FakeReplayAwareSink("sp") { EndGate = endGate };
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(ReplayDef("route-a", new FakeSourceIntake("s"), sink), Ct);
            await engine.StartRouteAsync("route-a", Ct);
            await WaitForAsync(() => sink.BeginCount == 1, TimeSpan.FromSeconds(5));

            // A wins and the driver reaches End (blocked on the gate) — the route is now Stopping.
            var a = engine.StopRouteAsync("route-a", ReplaySessionEndReason.ConfigurationReplaced, Ct);
            await WaitForAsync(() => sink.EndSessionCount == 1, TimeSpan.FromSeconds(5));

            // B races in while Stopping; it must lose the claim and leave the reason alone.
            var b = engine.StopRouteAsync("route-a", ReplaySessionEndReason.Stop, Ct);

            endGate.Release();
            await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(5));

            sink.EndSessionCount.Should().Be(1);
            sink.LastEndReason.Should().Be(ReplaySessionEndReason.ConfigurationReplaced);
            engine.GetRouteState("route-a").Should().Be(RouteState.Stopped);
        }
        finally { TryDeleteDir(dataPath); }
    }

    // ---- helpers ------------------------------------------------------------

    private static RouteDefinition ReplayDef(string routeId, FakeSourceIntake source, FakeReplayAwareSink sink) => new()
    {
        RouteId = routeId,
        GatewayId = RoutingTestData.GatewayId,
        Source = source,
        Sinks = new[] { (ISinkAdapter)sink },
        Filter = TagFilter.AcceptAll,
        BufferPolicy = ReplaySfPolicy(),
        Delivery = RoutingTestData.DefaultDeliveryPolicy(),
    };

    private static BufferPolicy ReplaySfPolicy() => new()
    {
        Mode = BufferMode.StoreAndForward,
        MaxDepth = 64,
        DropPolicy = DropPolicy.Block,
        ReclaimInterval = TimeSpan.FromMilliseconds(50),
    };

    private static CanonicalDataPoint NonUtcPoint() =>
        new CanonicalDataPointBuilder()
            .WithGateway(RoutingTestData.GatewayId)
            .WithSource("src-test", "mock")
            .WithDevice("dev1")
            .WithTag("tag", "Spindle/Speed")
            .WithValue(1.0, CanonicalValueType.Double)
            .WithQuality(DataQuality.Good)
            .WithTimestamps(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local),
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithSequence(0)
            .Build();

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new TimeoutException("Predicate did not become true within the timeout.");
    }

    private static string NewDataPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "edgeconnect-k13-intake", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string BufferPath(string dataPath, string routeId) =>
        Path.Combine(dataPath, "buffer", routeId + ".db");

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private static long CountManifest(string dbPath) => ScalarLong(dbPath, "SELECT COUNT(*) FROM latest_value;");

    private static long ManifestGenerationOf(string dbPath, string tagPath) =>
        ScalarLong(dbPath, "SELECT schema_generation FROM latest_value WHERE tag_path = $t;", ("$t", tagPath));

    private static long ScalarLong(string dbPath, string sql, params (string Name, object Value)[] args)
    {
        if (!File.Exists(dbPath))
        {
            return -1;
        }

        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        var result = cmd.ExecuteScalar();
        return result is null || result is DBNull ? -1 : (long)result;
    }

    private sealed class FailedReasonRecorder : IRoutingEngineDiagnostics
    {
        private volatile string? _failedReason;
        public string? FailedReason => _failedReason;

        public void OnRouteStateChanged(RouteStateChangedEvent evt)
        {
            if (evt.To == RouteState.Failed)
            {
                _failedReason = evt.Reason;
            }
        }

        public void OnSinkDegraded(SinkDegradedEvent evt) { }
        public void OnSinkDraining(SinkDrainingEvent evt) { }
        public void OnSinkRecovered(SinkRecoveredEvent evt) { }
        public void OnBackpressureDropped(BackpressureDroppedEvent evt) { }
    }
}
