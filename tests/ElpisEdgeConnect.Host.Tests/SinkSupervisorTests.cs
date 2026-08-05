// ============================================================================
// File: SinkSupervisorTests.cs
// Purpose: Phase 4 tests for SinkSupervisor. Pin lifecycle, per-adapter
//          isolation on start failures, and adapter-state diagnostics push.
// Milestone: D — phase 4.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.MockAdapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class SinkSupervisorTests
{
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

    private static SinkSupervisor MakeSupervisor(
        IEnumerable<SinkRegistration> registrations,
        out RuntimeDiagnosticsCollector collector)
    {
        collector = new RuntimeDiagnosticsCollector();
        return new SinkSupervisor(registrations, collector, NullLogger<SinkSupervisor>.Instance);
    }

    [Fact]
    public async Task Supervisor_StartsAndStopsRegisteredSinks()
    {
        var sink = new MockSinkAdapter("sink-1");
        var supervisor = MakeSupervisor(new[] { Registration(sink) }, out var collector);

        await supervisor.StartAsync(CancellationToken.None);
        sink.State.Should().Be(AdapterState.Running);

        var snap = collector.GetRouteSnapshot("route-1")!;
        snap.Sinks.Should().ContainSingle()
            .Which.AdapterState.Should().Be(AdapterState.Running);

        await supervisor.StopAsync(CancellationToken.None);
        sink.State.Should().Be(AdapterState.Stopped);

        var stoppedSnap = collector.GetRouteSnapshot("route-1")!;
        stoppedSnap.Sinks.Should().ContainSingle()
            .Which.AdapterState.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public void Supervisor_RejectsDuplicateInstanceIds()
    {
        var s1 = new MockSinkAdapter("sink-1");
        var s2 = new MockSinkAdapter("sink-1");

        Action act = () => MakeSupervisor(new[] { Registration(s1), Registration(s2) }, out _);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate sink instance id 'sink-1'*");
    }

    [Fact]
    public async Task Supervisor_StartFailureOnOneSink_DoesNotBlockOthers()
    {
        // throwingSink throws AdapterException on InitializeAsync via a
        // lightweight wrapper. healthySink starts normally.
        var throwingSink = new ThrowingMockSinkAdapter("sink-bad");
        var healthySink = new MockSinkAdapter("sink-good");

        var supervisor = MakeSupervisor(new[]
        {
            new SinkRegistration { Adapter = throwingSink, Config = Config("sink-bad"), RouteId = "route-bad" },
            new SinkRegistration { Adapter = healthySink, Config = Config("sink-good"), RouteId = "route-good" },
        }, out var collector);

        await supervisor.StartAsync(CancellationToken.None);

        // Healthy sink is Running.
        healthySink.State.Should().Be(AdapterState.Running);
        var goodSnap = collector.GetRouteSnapshot("route-good")!;
        goodSnap.Sinks.Should().ContainSingle()
            .Which.AdapterState.Should().Be(AdapterState.Running);

        // Throwing sink is reported Failed via diagnostics.
        var badSnap = collector.GetRouteSnapshot("route-bad")!;
        badSnap.Sinks.Should().ContainSingle()
            .Which.AdapterState.Should().Be(AdapterState.Failed);
        badSnap.Sinks[0].LastError.Should().NotBeNull();
        badSnap.Sinks[0].LastError!.Code.Should().Be("MOCK.SINK_INIT_THREW");

        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_OnUnstartedSupervisor_IsNoOp()
    {
        var sink = new MockSinkAdapter("sink-1");
        var supervisor = MakeSupervisor(new[] { Registration(sink) }, out _);

        var act = async () => await supervisor.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tiny test-only sink that throws on Initialize. Used to pin the
    /// per-adapter isolation contract.
    /// </summary>
    private sealed class ThrowingMockSinkAdapter : ISinkAdapter
    {
        public ThrowingMockSinkAdapter(string instanceId)
        {
            InstanceId = instanceId;
        }

        public string InstanceId { get; }
        public string ProtocolName => "mock-throw";
        public SinkCapabilities Capabilities => SinkCapabilities.Push;
        public AdapterState State { get; private set; } = AdapterState.Created;

        public Task InitializeAsync(SinkConfiguration config, CancellationToken ct)
            => throw new AdapterException(new AdapterError
            {
                Code = "MOCK.SINK_INIT_THREW",
                Category = ErrorCategory.Internal,
                Message = "deterministic init failure",
            });

        public Task StartAsync(CancellationToken ct) { State = AdapterState.Running; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) { State = AdapterState.Stopped; return Task.CompletedTask; }

        public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
            => Task.FromResult(new AdapterHealth
            {
                State = State,
                Level = HealthLevel.Unhealthy,
                CheckedAt = DateTime.UtcNow,
            });

        public Task<PublishResult> PublishAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct)
            => Task.FromResult(PublishResult.Successful(points.Count, TimeSpan.Zero));

        public Task UpdateCurrentValuesAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct)
            => Task.CompletedTask;

        public Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct)
            => Task.FromResult(ValidationResult.Success());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
