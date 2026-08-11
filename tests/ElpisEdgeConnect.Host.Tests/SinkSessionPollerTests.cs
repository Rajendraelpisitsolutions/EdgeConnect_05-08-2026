// ============================================================================
// File: SinkSessionPollerTests.cs
// Purpose: H.2 tests for SinkSessionPoller. Pin:
//            * Filters to ISessionTrackingSink adapters; skips non-tracking ones
//            * Calls ISinkSessionHealthSink.RecordActiveSessions on every tick
//            * Survives ActiveSessions throwing on a single adapter (treats as empty)
//            * Idle-waits cleanly when there are no session-tracking sinks
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.MockAdapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class SinkSessionPollerTests
{
    private sealed class CapturingHealthSink : ISinkSessionHealthSink
    {
        public ConcurrentBag<(string RouteId, string SinkId, IReadOnlyList<SinkSessionSummary> Sessions)> Records { get; }
            = new();

        public void RecordActiveSessions(
            string routeId,
            string sinkInstanceId,
            IReadOnlyList<SinkSessionSummary> sessions)
        {
            Records.Add((routeId, sinkInstanceId, sessions));
        }
    }

    private sealed class TestTrackingSink : ISinkAdapter, ISessionTrackingSink
    {
        private readonly List<SinkSessionSummary> _sessions;
        public TestTrackingSink(string instanceId, IEnumerable<SinkSessionSummary> sessions, bool throwOnRead = false)
        {
            InstanceId = instanceId;
            _sessions = sessions.ToList();
            _throwOnRead = throwOnRead;
        }
        private readonly bool _throwOnRead;

        public string InstanceId { get; }
        public string ProtocolName => "test-tracking";
        public SinkCapabilities Capabilities => SinkCapabilities.Pull | SinkCapabilities.SessionTracking;
        public AdapterState State => AdapterState.Running;
        public IReadOnlyList<SinkSessionSummary> ActiveSessions
        {
            get
            {
                if (_throwOnRead) throw new InvalidOperationException("simulated read failure");
                return _sessions;
            }
        }

        public Task InitializeAsync(SinkConfiguration config, CancellationToken ct) => Task.CompletedTask;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct) => Task.FromResult(new AdapterHealth
        {
            State = State, Level = HealthLevel.Healthy, CheckedAt = DateTime.UtcNow,
        });
        public Task<PublishResult> PublishAsync(IReadOnlyList<ElpisEdgeConnect.Core.Model.CanonicalDataPoint> points, CancellationToken ct) =>
            Task.FromResult(PublishResult.Successful(points.Count, TimeSpan.Zero));
        public Task UpdateCurrentValuesAsync(IReadOnlyList<ElpisEdgeConnect.Core.Model.CanonicalDataPoint> points, CancellationToken ct) => Task.CompletedTask;
        public Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct) => Task.FromResult(ValidationResult.Success());
        public ValueTask DisposeAsync() => default;
    }

    private sealed record TestSinkConfig : SinkConfiguration;

    private static SinkRegistration Registration(ISinkAdapter adapter, string routeId = "route-1") => new()
    {
        Adapter = adapter,
        Config = new TestSinkConfig
        {
            InstanceId = adapter.InstanceId,
            ProtocolName = adapter.ProtocolName,
        },
        RouteId = routeId,
    };

    [Fact]
    public async Task Poller_PushesActiveSessionsToHealthSink_OnEveryTick()
    {
        var sessions = new[]
        {
            new SinkSessionSummary
            {
                SessionId = "s1",
                ConnectedAtUtc = DateTime.UtcNow,
                SubscriptionCount = 2,
                MonitoredItemCount = 8,
            },
        };
        var adapter = new TestTrackingSink("opcua-1", sessions);
        var healthSink = new CapturingHealthSink();
        var poller = new SinkSessionPoller(
            new[] { Registration(adapter) },
            healthSink,
            NullLogger<SinkSessionPoller>.Instance,
            interval: TimeSpan.FromMilliseconds(50));

        await poller.StartAsync(CancellationToken.None);
        // Wait for ~3 ticks
        await Task.Delay(200);
        await poller.StopAsync(CancellationToken.None);

        healthSink.Records.Should().NotBeEmpty();
        var first = healthSink.Records.First();
        first.RouteId.Should().Be("route-1");
        first.SinkId.Should().Be("opcua-1");
        first.Sessions.Should().HaveCount(1);
        first.Sessions[0].SessionId.Should().Be("s1");
    }

    [Fact]
    public async Task Poller_SkipsNonTrackingSinks()
    {
        // A regular MockSinkAdapter does NOT implement ISessionTrackingSink.
        // The poller must not invoke RecordActiveSessions for it.
        var mockAdapter = new MockSinkAdapter("mqtt-mock");
        var trackingAdapter = new TestTrackingSink("opcua-1", Array.Empty<SinkSessionSummary>());
        var healthSink = new CapturingHealthSink();
        var poller = new SinkSessionPoller(
            new[] { Registration(mockAdapter), Registration(trackingAdapter, routeId: "route-2") },
            healthSink,
            NullLogger<SinkSessionPoller>.Instance,
            interval: TimeSpan.FromMilliseconds(50));

        await poller.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await poller.StopAsync(CancellationToken.None);

        healthSink.Records.Should().AllSatisfy(r => r.SinkId.Should().Be("opcua-1"));
        healthSink.Records.Should().AllSatisfy(r => r.RouteId.Should().Be("route-2"));
    }

    [Fact]
    public async Task Poller_TreatsActiveSessionsThrow_AsEmptyListAndContinues()
    {
        var adapter = new TestTrackingSink("opcua-broken", Array.Empty<SinkSessionSummary>(), throwOnRead: true);
        var healthSink = new CapturingHealthSink();
        var poller = new SinkSessionPoller(
            new[] { Registration(adapter) },
            healthSink,
            NullLogger<SinkSessionPoller>.Instance,
            interval: TimeSpan.FromMilliseconds(50));

        await poller.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await poller.StopAsync(CancellationToken.None);

        healthSink.Records.Should().NotBeEmpty();
        healthSink.Records.Should().AllSatisfy(r => r.Sessions.Should().BeEmpty());
    }

    [Fact]
    public async Task Poller_IdleWaits_WhenNoSessionTrackingSinks()
    {
        var mockAdapter = new MockSinkAdapter("mqtt-mock");
        var healthSink = new CapturingHealthSink();
        var poller = new SinkSessionPoller(
            new[] { Registration(mockAdapter) },
            healthSink,
            NullLogger<SinkSessionPoller>.Instance,
            interval: TimeSpan.FromMilliseconds(20));

        await poller.StartAsync(CancellationToken.None);
        await Task.Delay(80);
        await poller.StopAsync(CancellationToken.None);

        healthSink.Records.Should().BeEmpty();
    }
}
