// ============================================================================
// File: SourceSupervisorTests.cs
// Purpose: Phase 4 tests for SourceSupervisor. Pin lifecycle, channel
//          pump correctness, deterministic failure isolation, and
//          diagnostics push paths.
// Milestone: D — phase 4.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.MockAdapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class SourceSupervisorTests
{
    private static MockSourceConfiguration Config(string instanceId, string deviceId = "dev-1") => new()
    {
        InstanceId = instanceId,
        ProtocolName = "mock",
        DeviceId = deviceId,
    };

    private static SourceRegistration Registration(MockSourceAdapter adapter, string routeId = "route-1") => new()
    {
        Adapter = adapter,
        Config = Config(adapter.InstanceId),
        RouteId = routeId,
    };

    private static SourceSupervisor MakeSupervisor(
        IEnumerable<SourceRegistration> registrations,
        out RuntimeDiagnosticsCollector collector)
    {
        collector = new RuntimeDiagnosticsCollector();
        return new SourceSupervisor(registrations, collector, NullLogger<SourceSupervisor>.Instance);
    }

    private static async Task<List<CanonicalDataPoint>> DrainAsync(ISourceIntake intake, int expected, TimeSpan timeout)
    {
        var collected = new List<CanonicalDataPoint>();
        var deadline = DateTime.UtcNow + timeout;
        var reader = intake.Reader;
        while (collected.Count < expected && DateTime.UtcNow < deadline)
        {
            try
            {
                if (await reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var p))
                    {
                        collected.Add(p);
                        if (collected.Count >= expected)
                        {
                            return collected;
                        }
                    }
                }
                else
                {
                    return collected;
                }
            }
            catch (OperationCanceledException) { return collected; }
        }
        return collected;
    }

    [Fact]
    public async Task Supervisor_PumpsPointsFromAdapterIntoIntakeChannel()
    {
        var src = new MockSourceAdapter("src-1") { PointsPerPoll = 1, StopAfterPoints = 50 };
        await using var supervisor = MakeSupervisor(new[] { Registration(src) }, out var collector);

        var intake = supervisor.GetIntake("src-1");
        intake.Should().NotBeNull("supervisor pre-creates intakes at construction");

        await supervisor.StartAsync(CancellationToken.None);

        var points = await DrainAsync(intake!, 50, TimeSpan.FromSeconds(10));
        points.Should().HaveCount(50);
        points.Select(p => p.SequenceNumber).Should().Equal(Enumerable.Range(1, 50).Select(i => (long)i));

        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Supervisor_RegistersIntakesBeforeStart()
    {
        // Critical for the locked startup ordering: the route definition
        // factory needs to resolve intakes BEFORE the routing engine
        // starts, which is BEFORE StartAsync runs on the supervisor.
        var src = new MockSourceAdapter("src-1");
        await using var supervisor = MakeSupervisor(new[] { Registration(src) }, out _);

        var intake = supervisor.GetIntake("src-1");
        intake.Should().NotBeNull();
        intake!.SourceInstanceId.Should().Be("src-1");
        supervisor.SourceInstanceIds.Should().BeEquivalentTo(new[] { "src-1" });
    }

    [Fact]
    public void Supervisor_RejectsDuplicateInstanceIds()
    {
        var src1 = new MockSourceAdapter("src-1");
        var src2 = new MockSourceAdapter("src-1");

        Action act = () => MakeSupervisor(new[] { Registration(src1), Registration(src2) }, out _);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate source instance id 'src-1'*");
    }

    [Fact]
    public async Task Supervisor_ReportsRunningStateOnStart_AndObservationsOnPump()
    {
        var src = new MockSourceAdapter("src-1") { PointsPerPoll = 5, StopAfterPoints = 10 };
        await using var supervisor = MakeSupervisor(new[] { Registration(src) }, out var collector);

        await supervisor.StartAsync(CancellationToken.None);

        var intake = supervisor.GetIntake("src-1")!;
        var points = await DrainAsync(intake, 10, TimeSpan.FromSeconds(10));
        points.Should().HaveCount(10);

        // Wait briefly for the supervisor to push the final observation.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var snap = collector.GetRouteSnapshot("route-1");
            if (snap?.Source?.PointsObserved == 10) { break; }
            await Task.Delay(10);
        }

        var routeSnap = collector.GetRouteSnapshot("route-1")!;
        routeSnap.Source.Should().NotBeNull();
        routeSnap.Source!.SourceInstanceId.Should().Be("src-1");
        routeSnap.Source.State.Should().Be(AdapterState.Running);
        routeSnap.Source.PointsObserved.Should().Be(10);

        await supervisor.StopAsync(CancellationToken.None);

        // After Stop, the supervisor reports Stopped state.
        var stoppedSnap = collector.GetRouteSnapshot("route-1")!;
        stoppedSnap.Source!.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task Supervisor_AdapterFailureIsIsolatedToFailingSource()
    {
        // src-bad fails permanently; src-good keeps running. The supervisor
        // must mark src-bad Failed without disturbing src-good.
        var bad = new MockSourceAdapter("src-bad");
        var permanentFailure = new TaskCompletionSource();
        bad.FailUntilSignaled = permanentFailure;

        var good = new MockSourceAdapter("src-good") { PointsPerPoll = 1, StopAfterPoints = 5 };

        await using var supervisor = MakeSupervisor(new[]
        {
            new SourceRegistration { Adapter = bad, Config = Config("src-bad"), RouteId = "route-bad" },
            new SourceRegistration { Adapter = good, Config = Config("src-good"), RouteId = "route-good" },
        }, out var collector);

        await supervisor.StartAsync(CancellationToken.None);

        // src-good drains successfully.
        var goodIntake = supervisor.GetIntake("src-good")!;
        var goodPoints = await DrainAsync(goodIntake, 5, TimeSpan.FromSeconds(10));
        goodPoints.Should().HaveCount(5);

        // src-bad reports Failed via diagnostics.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var snap = collector.GetRouteSnapshot("route-bad");
            if (snap?.Source?.State == AdapterState.Failed) { break; }
            await Task.Delay(10);
        }
        var badSnap = collector.GetRouteSnapshot("route-bad")!;
        badSnap.Source!.State.Should().Be(AdapterState.Failed);
        badSnap.Source.LastError.Should().NotBeNull();

        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Supervisor_StopAsync_TerminatesPumpsCleanly()
    {
        // Hold the source's gate so the pump is parked when Stop is called.
        // The pump must observe cancellation and exit; Stop must complete
        // within a bounded budget.
        using var gate = new SemaphoreSlim(0, int.MaxValue);
        var src = new MockSourceAdapter("src-1") { PollGate = gate };
        await using var supervisor = MakeSupervisor(new[] { Registration(src) }, out _);

        await supervisor.StartAsync(CancellationToken.None);

        // The pump is parked on the gate. Call Stop with a generous budget.
        var stopBudget = TimeSpan.FromSeconds(10);
        using var cts = new CancellationTokenSource(stopBudget);
        var stopTask = supervisor.StopAsync(cts.Token);
        await stopTask; // must complete without throwing

        stopTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task GetIntake_UnknownSource_ReturnsNull()
    {
        var src = new MockSourceAdapter("src-1");
        await using var supervisor = MakeSupervisor(new[] { Registration(src) }, out _);

        supervisor.GetIntake("nope").Should().BeNull();
    }
}
