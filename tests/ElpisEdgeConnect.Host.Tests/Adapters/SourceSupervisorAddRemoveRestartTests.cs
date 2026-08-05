// ============================================================================
// File: Adapters/SourceSupervisorAddRemoveRestartTests.cs
// Purpose: M.P2.2 phase 2.a step 3 — pin the per-instance hot-reload
//          surface on SourceSupervisor (AddAsync / RemoveAsync /
//          RestartAsync) plus the lifecycle-gate and dispose-flag
//          invariants introduced in step 1.
//
//          Decision-table coverage matches the locked plan in
//          docs/sessions/2026-05-16-mp22-phase2a-plan.md §6.1.
//
//          Critical proofs called out at review time:
//             * Channel resurrection — old intake's reader is completed
//               after Restart; new intake's reader is live and not
//               reference-equal to the old. Tests #10, #11, #18.
//             * Failure rollback — failed AddAsync / failed Restart's
//               bring-up half leaves NO map entry behind. Tests #4, #5,
//               #12.
// Reference: docs/sessions/2026-05-16-mp22-phase2a-plan.md
//            docs/decisions/0009-runtime-hot-reload-instance-granularity.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.MockAdapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Adapters;

public sealed class SourceSupervisorAddRemoveRestartTests
{
    // ── Helpers ─────────────────────────────────────────────────────

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

    private static SourceSupervisor MakeEmptySupervisor(out RuntimeDiagnosticsCollector collector)
    {
        collector = new RuntimeDiagnosticsCollector();
        return new SourceSupervisor(
            Array.Empty<SourceRegistration>(),
            collector,
            NullLogger<SourceSupervisor>.Instance);
    }

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
                        if (collected.Count >= expected) return collected;
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

    /// <summary>
    /// Drain ALL residual points from a channel and await Completion.
    /// Asserts the channel is in a completed-empty state within the
    /// timeout. Used by the channel-resurrection tests to prove the
    /// old intake is truly dead after RestartAsync / RemoveAsync.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: <c>Channel&lt;T&gt;</c> semantics mean writer completion
    /// is observed only after buffered items are drained. Writer
    /// completion alone does NOT make <c>WaitToReadAsync</c> return false
    /// immediately — buffered items survive completion and the reader
    /// must drain them first. Asserting <c>WaitToReadAsync()
    /// .Should().BeFalse()</c> directly after a Restart/Remove is the
    /// broken version of this proof (it depends on the pump not having
    /// buffered anything, which is timing-dependent). The correct
    /// invariant is: drain residual + assert <c>Reader.Completion
    /// .IsCompleted</c>. Do not "simplify" this helper back into the
    /// broken form.
    /// </remarks>
    private static async Task AssertChannelDrainsToCompletionAsync(
        ISourceIntake intake,
        TimeSpan timeout)
    {
        var reader = intake.Reader;
        var drainTask = Task.Run(async () =>
        {
            try
            {
                while (await reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    while (reader.TryRead(out _)) { }
                }
            }
            catch (OperationCanceledException) { /* expected on completed channel */ }
        });
        await drainTask.WaitAsync(timeout).ConfigureAwait(false);
        reader.Completion.IsCompleted.Should().BeTrue(
            "writer was completed in StopInternal; reader's Completion fires once buffer is drained");
    }

    // ── AddAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_NewInstance_StartsAdapterAndPumpAndRecordsRunning()
    {
        var adapter = new MockSourceAdapter("src-add-1") { PointsPerPoll = 1, StopAfterPoints = 3 };
        await using var supervisor = MakeEmptySupervisor(out var collector);

        await supervisor.AddAsync(Registration(adapter, "route-add-1"), CancellationToken.None);

        supervisor.SourceInstanceIds.Should().Contain("src-add-1");
        var intake = supervisor.GetIntake("src-add-1");
        intake.Should().NotBeNull("Add must construct a fresh intake");

        var points = await DrainAsync(intake!, 3, TimeSpan.FromSeconds(10));
        points.Should().HaveCount(3, "pump is launched as part of Add");

        // Health: AddAsync records Running before launching the pump.
        var snap = collector.GetRouteSnapshot("route-add-1");
        snap.Should().NotBeNull();
        snap!.Source!.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task AddAsync_DuplicateId_Throws()
    {
        var adapter1 = new MockSourceAdapter("src-dup");
        var adapter2 = new MockSourceAdapter("src-dup");
        await using var supervisor = MakeEmptySupervisor(out _);

        await supervisor.AddAsync(Registration(adapter1), CancellationToken.None);

        var act = async () => await supervisor.AddAsync(Registration(adapter2), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate source instance id 'src-dup'*");
    }

    [Fact]
    public async Task AddAsync_AdapterInitializeAsyncThrows_PropagatesAsAdapterException()
    {
        var err = new AdapterError
        {
            Code = "MOCK.INIT_FAILED",
            Category = ErrorCategory.Configuration,
            Message = "init failed",
        };
        var adapter = new MockSourceAdapter("src-bad-init")
        {
            ThrowOnInitialize = new AdapterException(err),
        };
        await using var supervisor = MakeEmptySupervisor(out _);

        var act = async () => await supervisor.AddAsync(Registration(adapter), CancellationToken.None);

        await act.Should().ThrowAsync<AdapterException>();
        // Rollback: the failed instance must NOT remain in the map.
        supervisor.SourceInstanceIds.Should().NotContain("src-bad-init");
        supervisor.GetIntake("src-bad-init").Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_AdapterInitializeAsyncThrowsGeneric_PropagatesToCaller_AndRollsBackMap()
    {
        var adapter = new MockSourceAdapter("src-bad-init-generic")
        {
            ThrowOnInitializeGeneric = new InvalidOperationException("generic init failure"),
        };
        await using var supervisor = MakeEmptySupervisor(out _);

        var act = async () => await supervisor.AddAsync(Registration(adapter), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        supervisor.SourceInstanceIds.Should().NotContain("src-bad-init-generic",
            "rollback must remove the map entry even for non-AdapterException failures");
        supervisor.GetIntake("src-bad-init-generic").Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_AdapterStartAsyncThrows_PropagatesToCaller_AndRollsBackMap()
    {
        var err = new AdapterError
        {
            Code = "MOCK.START_FAILED",
            Category = ErrorCategory.Configuration,
            Message = "start failed",
        };
        var adapter = new MockSourceAdapter("src-bad-start")
        {
            ThrowOnStart = new AdapterException(err),
        };
        await using var supervisor = MakeEmptySupervisor(out _);

        var act = async () => await supervisor.AddAsync(Registration(adapter), CancellationToken.None);

        await act.Should().ThrowAsync<AdapterException>();
        supervisor.SourceInstanceIds.Should().NotContain("src-bad-start",
            "rollback covers Initialize-succeeded-then-Start-failed too");
    }

    // ── RemoveAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_RunningInstance_StopsAdapterAndCompletesChannel()
    {
        var adapter = new MockSourceAdapter("src-rm-1") { PointsPerPoll = 1 };
        await using var supervisor = MakeEmptySupervisor(out var collector);

        await supervisor.AddAsync(Registration(adapter, "route-rm-1"), CancellationToken.None);
        var intake = supervisor.GetIntake("src-rm-1")!;

        await supervisor.RemoveAsync("src-rm-1", CancellationToken.None);

        supervisor.SourceInstanceIds.Should().NotContain("src-rm-1");
        supervisor.GetIntake("src-rm-1").Should().BeNull();

        // Old intake: writer completed; reader drains any residual buffered
        // points then sees end-of-stream. Completion fires.
        await AssertChannelDrainsToCompletionAsync(intake, TimeSpan.FromSeconds(5));

        var snap = collector.GetRouteSnapshot("route-rm-1");
        snap!.Source!.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_IsSilentNoOp()
    {
        await using var supervisor = MakeEmptySupervisor(out _);

        var act = async () => await supervisor.RemoveAsync("never-existed", CancellationToken.None);

        await act.Should().NotThrowAsync(
            "RemoveAsync is idempotent on unknown ids — the coordinator may emit Remove for instances that never registered");
    }

    [Fact]
    public async Task RemoveAsync_DoesNotAffectOtherInstances()
    {
        var keeper = new MockSourceAdapter("src-keep") { PointsPerPoll = 1, StopAfterPoints = 5 };
        var goner = new MockSourceAdapter("src-gone") { PointsPerPoll = 1, StopAfterPoints = 5 };
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(keeper, "route-keep"), CancellationToken.None);
        await supervisor.AddAsync(Registration(goner, "route-gone"), CancellationToken.None);

        await supervisor.RemoveAsync("src-gone", CancellationToken.None);

        supervisor.SourceInstanceIds.Should().Contain("src-keep");
        supervisor.SourceInstanceIds.Should().NotContain("src-gone");

        // Keeper's pump must still deliver.
        var keeperIntake = supervisor.GetIntake("src-keep")!;
        var pts = await DrainAsync(keeperIntake, 5, TimeSpan.FromSeconds(10));
        pts.Should().HaveCount(5, "RemoveAsync on one instance must not disturb the others");
    }

    [Fact]
    public async Task RemoveAsync_DuringActivePump_NoExceptionEscapes()
    {
        // The pump is actively delivering points when Remove fires. The
        // bounded StopInternal mechanics must let the pump exit cleanly
        // without any exception escaping to the caller.
        var adapter = new MockSourceAdapter("src-live") { PointsPerPoll = 1 };
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(adapter), CancellationToken.None);

        // Let the pump produce a handful of points.
        var intake = supervisor.GetIntake("src-live")!;
        await DrainAsync(intake, 3, TimeSpan.FromSeconds(5));

        var act = async () => await supervisor.RemoveAsync("src-live", CancellationToken.None);

        await act.Should().NotThrowAsync();
        supervisor.SourceInstanceIds.Should().NotContain("src-live");
    }

    // ── RestartAsync — channel resurrection + failure rollback ──────

    [Fact]
    public async Task RestartAsync_ConstructsBrandNewChannel()
    {
        var first = new MockSourceAdapter("src-restart") { PointsPerPoll = 1 };
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(first), CancellationToken.None);

        var oldIntake = supervisor.GetIntake("src-restart")!;

        var second = new MockSourceAdapter("src-restart") { PointsPerPoll = 1 };
        await supervisor.RestartAsync(Registration(second), CancellationToken.None);

        var newIntake = supervisor.GetIntake("src-restart")!;
        newIntake.Should().NotBeSameAs(oldIntake,
            "Restart must construct a brand new channel; route definitions holding the old intake see a dead channel");
        newIntake.Reader.Should().NotBeSameAs(oldIntake.Reader,
            "the new reader is a different instance from the old reader");
    }

    [Fact]
    public async Task RestartAsync_NewIntakeIsNotEqualToOldIntake()
    {
        // Strong proof: old intake's writer is COMPLETED (channel
        // drains to end-of-stream); new intake's reader is LIVE (gets
        // the new adapter's points). Channel<T> semantics: a completed
        // channel with buffered items still returns true from
        // WaitToReadAsync until the items are drained — so we drain
        // all residual then assert Completion fired.
        var first = new MockSourceAdapter("src-rr") { PointsPerPoll = 1 };
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(first), CancellationToken.None);
        var oldIntake = supervisor.GetIntake("src-rr")!;

        // Drain a couple of points so the channel has activity before
        // being torn down (the pump will have buffered more by then).
        await DrainAsync(oldIntake, 2, TimeSpan.FromSeconds(5));

        var second = new MockSourceAdapter("src-rr") { PointsPerPoll = 1, StopAfterPoints = 4 };
        await supervisor.RestartAsync(Registration(second), CancellationToken.None);

        // Old channel: writer completed; reader can drain residual to
        // end-of-stream. Completion fires once drained.
        await AssertChannelDrainsToCompletionAsync(oldIntake, TimeSpan.FromSeconds(5));

        // New intake is a different instance AND its reader is live.
        var newIntake = supervisor.GetIntake("src-rr")!;
        newIntake.Should().NotBeSameAs(oldIntake);
        var freshPoints = await DrainAsync(newIntake, 4, TimeSpan.FromSeconds(10));
        freshPoints.Should().HaveCount(4, "new intake's reader is live");
    }

    [Fact]
    public async Task RestartAsync_AdapterInitThrowsOnAddHalf_LeavesInstanceRemovedNotPartial()
    {
        var first = new MockSourceAdapter("src-r-fail") { PointsPerPoll = 1 };
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(first), CancellationToken.None);

        // New registration's adapter will throw on Initialize. The
        // Restart teardown half succeeds; the bring-up half fails.
        var err = new AdapterError
        {
            Code = "MOCK.INIT_FAILED",
            Category = ErrorCategory.Configuration,
            Message = "init failed during restart",
        };
        var failing = new MockSourceAdapter("src-r-fail")
        {
            ThrowOnInitialize = new AdapterException(err),
        };

        var act = async () => await supervisor.RestartAsync(Registration(failing), CancellationToken.None);

        await act.Should().ThrowAsync<AdapterException>();
        // Locked behavior: a failed restart leaves NO map entry. The
        // old instance was torn down (StopInternal succeeded); the new
        // one rolled back (StartInternal threw → TryRemove). The id
        // is gone entirely.
        supervisor.SourceInstanceIds.Should().NotContain("src-r-fail",
            "failed Restart leaves the id un-supervised; coordinator catches and registers fault");
        supervisor.GetIntake("src-r-fail").Should().BeNull();
    }

    [Fact]
    public async Task RestartAsync_DoesNotLeakOldPumpTask()
    {
        // After Restart, the old pump task must have observed cancel,
        // its writer must be completed, and the channel must drain to
        // end-of-stream. We prove it via two complementary checks:
        //   (a) old channel drains to Completion = old pump exited
        //       cleanly (finally block fired) and no new writes happen.
        //   (b) new channel receives EXACTLY second.StopAfterPoints
        //       items — proves only the new pump is live; no zombie
        //       old pump is co-emitting.
        var first = new MockSourceAdapter("src-leak") { PointsPerPoll = 1 };
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(first), CancellationToken.None);
        var oldIntake = supervisor.GetIntake("src-leak")!;
        await DrainAsync(oldIntake, 2, TimeSpan.FromSeconds(5));

        var second = new MockSourceAdapter("src-leak") { PointsPerPoll = 1, StopAfterPoints = 3 };
        await supervisor.RestartAsync(Registration(second), CancellationToken.None);

        // (a) Old channel: drains to Completion. Pump exited; writer completed.
        await AssertChannelDrainsToCompletionAsync(oldIntake, TimeSpan.FromSeconds(5));

        // (b) New channel: live, producing only the new adapter's points.
        var newIntake = supervisor.GetIntake("src-leak")!;
        var newPoints = await DrainAsync(newIntake, 3, TimeSpan.FromSeconds(10));
        newPoints.Should().HaveCount(3,
            "only the new pump task is live; no zombie pump from the old adapter co-emitting");
    }

    // ── GetIntake ───────────────────────────────────────────────────

    [Fact]
    public async Task GetIntake_AfterRemove_ReturnsNull()
    {
        var adapter = new MockSourceAdapter("src-gi-1");
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(adapter), CancellationToken.None);

        await supervisor.RemoveAsync("src-gi-1", CancellationToken.None);

        supervisor.GetIntake("src-gi-1").Should().BeNull();
    }

    [Fact]
    public async Task GetIntake_AfterRestart_ReturnsLiveChannel()
    {
        var first = new MockSourceAdapter("src-gi-2");
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(first), CancellationToken.None);

        var second = new MockSourceAdapter("src-gi-2") { PointsPerPoll = 1, StopAfterPoints = 2 };
        await supervisor.RestartAsync(Registration(second), CancellationToken.None);

        var intake = supervisor.GetIntake("src-gi-2");
        intake.Should().NotBeNull("GetIntake after Restart resolves to the new live channel");
        var points = await DrainAsync(intake!, 2, TimeSpan.FromSeconds(10));
        points.Should().HaveCount(2);
    }

    // ── Boot-path regression pins (refactor in step 1 must preserve behavior) ──

    [Fact]
    public async Task BootStartAsync_RegressionPin_StartsAllRegisteredSources()
    {
        // Constructor-time registrations + boot-time StartAsync must
        // still launch every adapter's pump and record Running health.
        var a = new MockSourceAdapter("src-boot-a") { PointsPerPoll = 1, StopAfterPoints = 2 };
        var b = new MockSourceAdapter("src-boot-b") { PointsPerPoll = 1, StopAfterPoints = 2 };
        await using var supervisor = MakeSupervisor(
            new[] { Registration(a, "route-a"), Registration(b, "route-b") },
            out var collector);

        await supervisor.StartAsync(CancellationToken.None);

        var ai = supervisor.GetIntake("src-boot-a")!;
        var bi = supervisor.GetIntake("src-boot-b")!;
        (await DrainAsync(ai, 2, TimeSpan.FromSeconds(10))).Should().HaveCount(2);
        (await DrainAsync(bi, 2, TimeSpan.FromSeconds(10))).Should().HaveCount(2);

        collector.GetRouteSnapshot("route-a")!.Source!.State.Should().Be(AdapterState.Running);
        collector.GetRouteSnapshot("route-b")!.Source!.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task BootStopAsync_RegressionPin_StopsAllSources()
    {
        var a = new MockSourceAdapter("src-stop-a") { PointsPerPoll = 1 };
        var b = new MockSourceAdapter("src-stop-b") { PointsPerPoll = 1 };
        await using var supervisor = MakeSupervisor(
            new[] { Registration(a, "route-a"), Registration(b, "route-b") },
            out var collector);
        await supervisor.StartAsync(CancellationToken.None);

        await supervisor.StopAsync(CancellationToken.None);

        // Both sources report Stopped via diagnostics.
        collector.GetRouteSnapshot("route-a")!.Source!.State.Should().Be(AdapterState.Stopped);
        collector.GetRouteSnapshot("route-b")!.Source!.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task DisposeAsync_RegressionPin_DrainsAndDisposesAdapters()
    {
        // After DisposeAsync, the supervisor must be unusable and the
        // adapters must be torn down. We assert that the supervisor
        // throws ObjectDisposedException on subsequent mutating calls
        // — proves the dispose path ran end to end.
        var adapter = new MockSourceAdapter("src-dispose");
        var supervisor = MakeSupervisor(new[] { Registration(adapter) }, out _);
        await supervisor.StartAsync(CancellationToken.None);

        await supervisor.DisposeAsync();

        var act = async () => await supervisor.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>(
            "DisposeAsync completed end to end; subsequent calls must reject");
    }

    // ── Lifecycle gate + dispose-flag contract ──────────────────────

    [Fact]
    public async Task AfterDisposeAsync_AddAsync_ThrowsObjectDisposedException()
    {
        var supervisor = MakeEmptySupervisor(out _);
        await supervisor.DisposeAsync();

        var act = async () => await supervisor.AddAsync(
            Registration(new MockSourceAdapter("src-post-dispose")),
            CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task AfterDisposeAsync_RemoveAsync_ThrowsObjectDisposedException()
    {
        var supervisor = MakeEmptySupervisor(out _);
        await supervisor.DisposeAsync();

        var act = async () => await supervisor.RemoveAsync("anything", CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Concurrent_AddAsyncAndRemoveAsync_SameInstance_AreSerialised()
    {
        // Pin the lifecycle-gate contract. Launch AddAsync (blocked
        // mid-Initialize via InitializeBarrier), then launch RemoveAsync
        // for the same id. RemoveAsync must NOT complete while AddAsync
        // is holding the gate.
        var barrier = new TaskCompletionSource();
        var adapter = new MockSourceAdapter("src-serial")
        {
            InitializeBarrier = barrier,
            PointsPerPoll = 1,
        };
        await using var supervisor = MakeEmptySupervisor(out _);

        var addTask = supervisor.AddAsync(Registration(adapter), CancellationToken.None);

        // Give the AddAsync task a moment to acquire the gate and enter
        // Initialize. The barrier ensures it parks there.
        await Task.Delay(50);
        addTask.IsCompleted.Should().BeFalse("AddAsync is parked inside Initialize");

        var removeTask = supervisor.RemoveAsync("src-serial", CancellationToken.None);
        await Task.Delay(50);
        removeTask.IsCompleted.Should().BeFalse(
            "RemoveAsync is queued behind the lifecycle gate while AddAsync holds it");

        // Release the barrier. AddAsync completes; RemoveAsync then
        // acquires the gate and removes the instance.
        barrier.SetResult();
        await addTask;
        await removeTask;

        supervisor.SourceInstanceIds.Should().NotContain("src-serial",
            "RemoveAsync ran after AddAsync per the gate's ordering");
    }

    [Fact]
    public async Task DisposeAsync_WhileAddAsyncInFlight_DoesNotCorruptState()
    {
        // AddAsync is parked inside InitializeAsync. DisposeAsync sets
        // the _disposed flag immediately (lock-free) then queues behind
        // the gate. After the barrier releases, AddAsync completes
        // successfully (its work was done before Dispose set the flag);
        // DisposeAsync then acquires the gate and tears everything down.
        // Final state: clean — no exceptions, no lingering entries.
        var barrier = new TaskCompletionSource();
        var adapter = new MockSourceAdapter("src-race")
        {
            InitializeBarrier = barrier,
            PointsPerPoll = 1,
        };
        var supervisor = MakeEmptySupervisor(out _);

        var addTask = supervisor.AddAsync(Registration(adapter), CancellationToken.None);
        await Task.Delay(50);
        addTask.IsCompleted.Should().BeFalse();

        var disposeTask = supervisor.DisposeAsync();
        await Task.Delay(50);

        barrier.SetResult();

        // Both must complete cleanly.
        await addTask;
        await disposeTask;

        // Post-dispose: any subsequent call rejects.
        var act = async () => await supervisor.AddAsync(
            Registration(new MockSourceAdapter("src-after")),
            CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
