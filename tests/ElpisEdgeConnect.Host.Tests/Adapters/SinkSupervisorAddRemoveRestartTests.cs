// ============================================================================
// File: Adapters/SinkSupervisorAddRemoveRestartTests.cs
// Purpose: M.P2.2 phase 2.a step 5 — pin the per-instance hot-reload
//          surface on SinkSupervisor (AddAsync / RemoveAsync /
//          RestartAsync) plus the lifecycle-gate and dispose-flag
//          invariants introduced in step 4.
//
//          Mirrors SourceSupervisorAddRemoveRestartTests with the
//          locked sink-side differences:
//             * No channels — no channel-resurrection invariant.
//             * Boot-time per-adapter isolation must remain bit-
//               identical: AdapterException records Failed with the
//               carried error; generic exception records Failed with
//               synthetic HOST.SINK_START_THREW. The existing
//               Supervisor_StartFailureOnOneSink_DoesNotBlockOthers
//               pin lives in the legacy SinkSupervisorTests file; the
//               new BootStartAsync_RegressionPin_PerAdapterIsolation*
//               tests below pin the same property via the refactored
//               internal helpers.
//             * The supervisor TRUSTS the caller (ADR-0009 Decision 2):
//               RemoveAsync never peeks at config to ref-count. Pinned
//               by RemoveAsync_DoesNotPeekAtConfig_TrustsCaller.
// Reference: docs/sessions/2026-05-16-mp22-phase2a-plan.md §6.2
//            docs/decisions/0009-runtime-hot-reload-instance-granularity.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.MockAdapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Adapters;

public sealed class SinkSupervisorAddRemoveRestartTests
{
    // ── Helpers ─────────────────────────────────────────────────────

    private static MockSinkConfiguration Config(string instanceId) => new()
    {
        InstanceId = instanceId,
        ProtocolName = "mock",
    };

    private static SinkRegistration Registration(MockSinkAdapter adapter, string routeId = "route-1") => new()
    {
        Adapter = adapter,
        Config = Config(adapter.InstanceId),
        RouteId = routeId,
    };

    private static SinkSupervisor MakeEmptySupervisor(out RuntimeDiagnosticsCollector collector)
    {
        collector = new RuntimeDiagnosticsCollector();
        return new SinkSupervisor(
            Array.Empty<SinkRegistration>(),
            collector,
            NullLogger<SinkSupervisor>.Instance);
    }

    private static SinkSupervisor MakeSupervisor(
        IEnumerable<SinkRegistration> registrations,
        out RuntimeDiagnosticsCollector collector)
    {
        collector = new RuntimeDiagnosticsCollector();
        return new SinkSupervisor(registrations, collector, NullLogger<SinkSupervisor>.Instance);
    }

    // ── AddAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_NewInstance_StartsAdapterAndRecordsRunning()
    {
        var adapter = new MockSinkAdapter("sink-add-1");
        await using var supervisor = MakeEmptySupervisor(out var collector);

        await supervisor.AddAsync(Registration(adapter, "route-add-1"), CancellationToken.None);

        supervisor.Registrations.Should().ContainSingle()
            .Which.Adapter.InstanceId.Should().Be("sink-add-1");
        adapter.State.Should().Be(AdapterState.Running);

        var snap = collector.GetRouteSnapshot("route-add-1");
        snap.Should().NotBeNull();
        snap!.Sinks.Should().ContainSingle()
            .Which.AdapterState.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task AddAsync_DuplicateId_Throws()
    {
        var adapter1 = new MockSinkAdapter("sink-dup");
        var adapter2 = new MockSinkAdapter("sink-dup");
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(adapter1), CancellationToken.None);

        var act = async () => await supervisor.AddAsync(Registration(adapter2), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate sink instance id 'sink-dup'*");
    }

    [Fact]
    public async Task AddAsync_AdapterInitializeAsyncThrows_PropagatesAsAdapterException()
    {
        // Hot-reload contract: AddAsync does NOT swallow the exception
        // (unlike boot StartAsync's per-adapter try/catch). It bubbles
        // so the coordinator's TryWithFaultAsync can register a fault.
        var err = new AdapterError
        {
            Code = "MOCK.SINK_INIT_FAILED",
            Category = ErrorCategory.Configuration,
            Message = "init failed",
        };
        var adapter = new MockSinkAdapter("sink-bad-init")
        {
            ThrowOnInitialize = new AdapterException(err),
        };
        await using var supervisor = MakeEmptySupervisor(out _);

        var act = async () => await supervisor.AddAsync(Registration(adapter), CancellationToken.None);

        await act.Should().ThrowAsync<AdapterException>();
        // Rollback: the failed instance must NOT remain in the map.
        supervisor.Registrations.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_AdapterInitializeAsyncThrowsGeneric_PropagatesToCaller_AndRollsBackMap()
    {
        // Same as #3 but with a non-AdapterException. Hot-reload still
        // propagates — the coordinator catches anything.
        var adapter = new MockSinkAdapter("sink-bad-init-generic")
        {
            ThrowOnInitializeGeneric = new InvalidOperationException("generic init failure"),
        };
        await using var supervisor = MakeEmptySupervisor(out _);

        var act = async () => await supervisor.AddAsync(Registration(adapter), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        supervisor.Registrations.Should().BeEmpty(
            "rollback must remove the map entry even for non-AdapterException failures");
    }

    // ── RemoveAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_RunningInstance_StopsAdapterAndRecordsStopped()
    {
        var adapter = new MockSinkAdapter("sink-rm-1");
        await using var supervisor = MakeEmptySupervisor(out var collector);
        await supervisor.AddAsync(Registration(adapter, "route-rm-1"), CancellationToken.None);

        await supervisor.RemoveAsync("sink-rm-1", CancellationToken.None);

        supervisor.Registrations.Should().BeEmpty();
        adapter.State.Should().Be(AdapterState.Stopped);

        var snap = collector.GetRouteSnapshot("route-rm-1");
        snap!.Sinks.Should().ContainSingle()
            .Which.AdapterState.Should().Be(AdapterState.Stopped);
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
        var keeper = new MockSinkAdapter("sink-keep");
        var goner = new MockSinkAdapter("sink-gone");
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(keeper, "route-keep"), CancellationToken.None);
        await supervisor.AddAsync(Registration(goner, "route-gone"), CancellationToken.None);

        await supervisor.RemoveAsync("sink-gone", CancellationToken.None);

        supervisor.Registrations.Should().ContainSingle()
            .Which.Adapter.InstanceId.Should().Be("sink-keep");
        keeper.State.Should().Be(AdapterState.Running,
            "RemoveAsync on one instance must not disturb the others");
        goner.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task RemoveAsync_DoesNotPeekAtConfig_TrustsCaller()
    {
        // ADR-0009 Decision 2 / Phase 2 design correction #2: the
        // supervisor's RemoveAsync TRUSTS the caller. It does not look
        // at IConfigurationManager to decide "is this sink still
        // referenced by other routes." Reference-counting is the
        // coordinator's job (Phase 2.c).
        //
        // This test pins the contract by construction:
        //   * RemoveAsync's signature accepts only (id, ct) — no
        //     IConfigurationManager parameter, no IConfigurationRegistry
        //     parameter, no route-set parameter.
        //   * The supervisor has no field of type IConfigurationManager
        //     or similar (verified by the constructor signature: only
        //     IEnumerable<SinkRegistration>, ISinkHealthSink, ILogger).
        //   * Calling RemoveAsync simply removes the named sink,
        //     regardless of any external state.
        var adapter = new MockSinkAdapter("sink-no-peek");
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(adapter, "route-no-peek"), CancellationToken.None);

        // Even if a "ref-count says still in use" world existed, the
        // supervisor wouldn't see it. RemoveAsync removes the instance.
        await supervisor.RemoveAsync("sink-no-peek", CancellationToken.None);

        supervisor.Registrations.Should().BeEmpty(
            "supervisor trusts the caller; no config peek to defer removal");
        adapter.State.Should().Be(AdapterState.Stopped);
    }

    // ── RestartAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RestartAsync_ReplacesAdapter()
    {
        var first = new MockSinkAdapter("sink-restart");
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(first), CancellationToken.None);

        var second = new MockSinkAdapter("sink-restart");
        await supervisor.RestartAsync(Registration(second), CancellationToken.None);

        // The old adapter was stopped; the new one is running.
        first.State.Should().Be(AdapterState.Stopped);
        second.State.Should().Be(AdapterState.Running);

        // Map holds exactly the new registration.
        var regs = supervisor.Registrations;
        regs.Should().ContainSingle();
        regs[0].Adapter.Should().BeSameAs(second,
            "Restart replaces the adapter reference; the new ISinkAdapter is what the routing engine sees");
    }

    [Fact]
    public async Task RestartAsync_AdapterInitThrowsOnAddHalf_LeavesInstanceRemovedNotPartial()
    {
        var first = new MockSinkAdapter("sink-r-fail");
        await using var supervisor = MakeEmptySupervisor(out _);
        await supervisor.AddAsync(Registration(first), CancellationToken.None);

        var err = new AdapterError
        {
            Code = "MOCK.SINK_INIT_FAILED",
            Category = ErrorCategory.Configuration,
            Message = "init failed during restart",
        };
        var failing = new MockSinkAdapter("sink-r-fail")
        {
            ThrowOnInitialize = new AdapterException(err),
        };

        var act = async () => await supervisor.RestartAsync(Registration(failing), CancellationToken.None);

        await act.Should().ThrowAsync<AdapterException>();
        // Locked behavior: failed restart leaves NO map entry. The
        // teardown half succeeded (first stopped); the bring-up half
        // rolled back. The id is gone entirely.
        supervisor.Registrations.Should().BeEmpty(
            "failed Restart leaves the id un-supervised; coordinator catches and registers fault");
        first.State.Should().Be(AdapterState.Stopped,
            "old adapter was stopped before the new one's init failed");
    }

    // ── Boot-path regression pins ───────────────────────────────────

    [Fact]
    public async Task BootStartAsync_RegressionPin_PerAdapterIsolationPreserved()
    {
        // Critical regression pin: the refactored boot StartAsync
        // must still catch per-adapter exceptions and CONTINUE with
        // the next sink. AdapterException → records Failed with the
        // carried error. Generic exception → records Failed with
        // synthetic HOST.SINK_START_THREW.
        var initFailErr = new AdapterError
        {
            Code = "MOCK.SINK_INIT_THREW",
            Category = ErrorCategory.Internal,
            Message = "deterministic init failure",
        };
        var throwing = new MockSinkAdapter("sink-bad-boot")
        {
            ThrowOnInitialize = new AdapterException(initFailErr),
        };
        var generic = new MockSinkAdapter("sink-generic-boot")
        {
            ThrowOnInitializeGeneric = new InvalidOperationException("generic boot failure"),
        };
        var healthy = new MockSinkAdapter("sink-healthy-boot");

        var supervisor = MakeSupervisor(new[]
        {
            new SinkRegistration { Adapter = throwing, Config = Config(throwing.InstanceId), RouteId = "route-bad" },
            new SinkRegistration { Adapter = generic,  Config = Config(generic.InstanceId),  RouteId = "route-generic" },
            new SinkRegistration { Adapter = healthy,  Config = Config(healthy.InstanceId),  RouteId = "route-healthy" },
        }, out var collector);

        await supervisor.StartAsync(CancellationToken.None);

        // Healthy sink: Running.
        healthy.State.Should().Be(AdapterState.Running);
        var healthySnap = collector.GetRouteSnapshot("route-healthy")!;
        healthySnap.Sinks.Should().ContainSingle().Which.AdapterState.Should().Be(AdapterState.Running);

        // Throwing sink: Failed with the AdapterException's error code.
        var badSnap = collector.GetRouteSnapshot("route-bad")!;
        badSnap.Sinks.Should().ContainSingle().Which.AdapterState.Should().Be(AdapterState.Failed);
        badSnap.Sinks[0].LastError!.Code.Should().Be("MOCK.SINK_INIT_THREW");

        // Generic-exception sink: Failed with synthetic HOST.SINK_START_THREW.
        var genericSnap = collector.GetRouteSnapshot("route-generic")!;
        genericSnap.Sinks.Should().ContainSingle().Which.AdapterState.Should().Be(AdapterState.Failed);
        genericSnap.Sinks[0].LastError!.Code.Should().Be("HOST.SINK_START_THREW");

        await supervisor.StopAsync(CancellationToken.None);
        await supervisor.DisposeAsync();
    }

    [Fact]
    public async Task BootStopAsync_RegressionPin_StopsAllSinks()
    {
        var a = new MockSinkAdapter("sink-stop-a");
        var b = new MockSinkAdapter("sink-stop-b");
        await using var supervisor = MakeSupervisor(new[]
        {
            Registration(a, "route-a"),
            Registration(b, "route-b"),
        }, out var collector);
        await supervisor.StartAsync(CancellationToken.None);

        await supervisor.StopAsync(CancellationToken.None);

        a.State.Should().Be(AdapterState.Stopped);
        b.State.Should().Be(AdapterState.Stopped);
        collector.GetRouteSnapshot("route-a")!.Sinks[0].AdapterState.Should().Be(AdapterState.Stopped);
        collector.GetRouteSnapshot("route-b")!.Sinks[0].AdapterState.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task DisposeAsync_RegressionPin_DrainsAndDisposesAdapters()
    {
        // After DisposeAsync, the supervisor is unusable. Subsequent
        // mutating calls reject — proves the dispose path ran end to end.
        var adapter = new MockSinkAdapter("sink-dispose");
        var supervisor = MakeSupervisor(new[] { Registration(adapter) }, out _);
        await supervisor.StartAsync(CancellationToken.None);

        await supervisor.DisposeAsync();

        var act = async () => await supervisor.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ── Dispose-during-flight race ──────────────────────────────────

    [Fact]
    public async Task DisposeAsync_WhileAddAsyncInFlight_DoesNotCorruptState()
    {
        // AddAsync parks inside InitializeAsync. DisposeAsync sets the
        // _disposed flag immediately (lock-free) then queues behind the
        // gate. After barrier releases, AddAsync completes; DisposeAsync
        // then acquires the gate and tears everything down. Final state:
        // clean — subsequent calls reject with ObjectDisposedException.
        var barrier = new TaskCompletionSource();
        var adapter = new MockSinkAdapter("sink-race")
        {
            InitializeBarrier = barrier,
        };
        var supervisor = MakeEmptySupervisor(out _);

        var addTask = supervisor.AddAsync(Registration(adapter), CancellationToken.None);
        await Task.Delay(50);
        addTask.IsCompleted.Should().BeFalse("AddAsync is parked inside Initialize");

        var disposeTask = supervisor.DisposeAsync();
        await Task.Delay(50);

        barrier.SetResult();

        // Both complete cleanly.
        await addTask;
        await disposeTask;

        // Post-dispose: any subsequent call rejects.
        var act = async () => await supervisor.AddAsync(
            Registration(new MockSinkAdapter("sink-after")),
            CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ── Lifecycle-gate + dispose-flag contract ──────────────────────

    [Fact]
    public async Task AfterDisposeAsync_AddAsync_ThrowsObjectDisposedException()
    {
        var supervisor = MakeEmptySupervisor(out _);
        await supervisor.DisposeAsync();

        var act = async () => await supervisor.AddAsync(
            Registration(new MockSinkAdapter("sink-post-dispose")),
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
        // Lifecycle-gate contract: launch AddAsync (parked inside
        // Initialize via InitializeBarrier), then RemoveAsync for the
        // same id. RemoveAsync must NOT complete while AddAsync holds
        // the gate.
        var barrier = new TaskCompletionSource();
        var adapter = new MockSinkAdapter("sink-serial")
        {
            InitializeBarrier = barrier,
        };
        await using var supervisor = MakeEmptySupervisor(out _);

        var addTask = supervisor.AddAsync(Registration(adapter), CancellationToken.None);
        await Task.Delay(50);
        addTask.IsCompleted.Should().BeFalse("AddAsync parked inside Initialize");

        var removeTask = supervisor.RemoveAsync("sink-serial", CancellationToken.None);
        await Task.Delay(50);
        removeTask.IsCompleted.Should().BeFalse(
            "RemoveAsync is queued behind the lifecycle gate while AddAsync holds it");

        // Release the barrier. AddAsync completes; RemoveAsync then
        // acquires the gate and removes the instance.
        barrier.SetResult();
        await addTask;
        await removeTask;

        supervisor.Registrations.Should().BeEmpty(
            "RemoveAsync ran after AddAsync per the gate's ordering");
    }
}
