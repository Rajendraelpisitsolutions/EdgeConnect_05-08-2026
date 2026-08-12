// ============================================================================
// File: OpcUaClientEndToEndTests.cs
// Purpose: End-to-end happy-path validation of OpcUaClientSourceAdapter
//          against the in-process StandardServer fixture. Exercises the
//          full notification pipeline:
//
//            OPC stack publish thread
//                → Subscription.FastDataChangeCallback
//                → NotificationDispatcher (bounded Channel)
//                → CanonicalDataPoint via OpcUaTypeMapper
//                → adapter.SubscribeAsync(...)
//                → test consumer
//
//          Each test owns its own adapter instance; the server fixture
//          is shared across the class via IClassFixture so the OPC
//          stack's ~1-2s server-start cost is paid once per class, not
//          per test.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §1.3
//            PR 7a plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Integration.Tests.OpcUaClient;

[Trait("Category", "OpcUaClient")]
public sealed class OpcUaClientEndToEndTests : IClassFixture<OpcUaClientEndToEndTests.SharedServer>, IAsyncLifetime
{
    private readonly SharedServer _shared;
    private OpcUaClientSourceAdapter? _adapter;

    public OpcUaClientEndToEndTests(SharedServer shared) { _shared = shared; }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_adapter is not null) await _adapter.DisposeAsync();
    }

    [Fact]
    public async Task AdapterConnects_AndReachesRunningState()
    {
        _adapter = NewAdapter();
        var config = AdapterConfig(_adapter.InstanceId, _shared.Fixture.EndpointUrl, new[]
        {
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"),
                DisplayName = "Counter",
            },
        });
        await _adapter.InitializeAsync(config, CancellationToken.None);
        await _adapter.StartAsync(CancellationToken.None);

        _adapter.State.Should().Be(AdapterState.Running);
        var health = await _adapter.CheckHealthAsync(CancellationToken.None);
        health.Level.Should().Be(HealthLevel.Healthy);
        health.Metrics!["subscriptionsActive"].Should().Be(1);
        health.Metrics["monitoredItemsActive"].Should().Be(1);
    }

    [Fact]
    public async Task SubscribeAsync_YieldsCanonicalDataPoints_FromSimulatedCounter()
    {
        _adapter = NewAdapter();
        var config = AdapterConfig(_adapter.InstanceId, _shared.Fixture.EndpointUrl, new[]
        {
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"),
                DisplayName = "Counter",
            },
        });
        await _adapter.InitializeAsync(config, CancellationToken.None);
        await _adapter.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var collected = new List<CanonicalDataPoint>();
        await foreach (var cdp in _adapter.SubscribeAsync(cts.Token))
        {
            collected.Add(cdp);
            if (collected.Count >= 3) break;
        }

        collected.Should().HaveCountGreaterThanOrEqualTo(3);
        collected.Should().AllSatisfy(cdp =>
        {
            cdp.TagName.Should().Be("Counter");
            cdp.ProtocolName.Should().Be(OpcUaClientSourceConfiguration.ProtocolNameConstant);
            cdp.Quality.Should().Be(DataQuality.Good);
            cdp.ValueType.Should().Be(CanonicalValueType.Integer);
            cdp.Value.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task SubscribeAsync_YieldsDistinctValues_OverMultipleTicks()
    {
        _adapter = NewAdapter();
        var config = AdapterConfig(_adapter.InstanceId, _shared.Fixture.EndpointUrl, new[]
        {
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"),
                DisplayName = "Counter",
            },
        });
        await _adapter.InitializeAsync(config, CancellationToken.None);
        await _adapter.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var values = new List<int>();
        await foreach (var cdp in _adapter.SubscribeAsync(cts.Token))
        {
            if (cdp.Value is int v) values.Add(v);
            if (values.Count >= 5) break;
        }

        values.Should().HaveCountGreaterThanOrEqualTo(5);
        values.Distinct().Should().HaveCountGreaterThan(1,
            "Counter is a monotonically increasing tag — successive notifications must observe distinct values.");
    }

    [Fact]
    public async Task SubscribeAsync_MultipleTags_AllProduceCanonicalPoints()
    {
        _adapter = NewAdapter();
        var config = AdapterConfig(_adapter.InstanceId, _shared.Fixture.EndpointUrl, new[]
        {
            new MonitoredItemConfig { NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"), DisplayName = "Counter" },
            new MonitoredItemConfig { NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Sine"), DisplayName = "Sine" },
            new MonitoredItemConfig { NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Square"), DisplayName = "Square" },
        });
        await _adapter.InitializeAsync(config, CancellationToken.None);
        await _adapter.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var tagsSeen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var cdp in _adapter.SubscribeAsync(cts.Token))
        {
            tagsSeen.Add(cdp.TagName);
            if (tagsSeen.Count == 3) break;
        }

        tagsSeen.Should().BeEquivalentTo(new[] { "Counter", "Sine", "Square" });
    }

    [Fact]
    public async Task StopAsync_FromRunning_TransitionsCleanly()
    {
        _adapter = NewAdapter();
        var config = AdapterConfig(_adapter.InstanceId, _shared.Fixture.EndpointUrl, new[]
        {
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"),
                DisplayName = "Counter",
            },
        });
        await _adapter.InitializeAsync(config, CancellationToken.None);
        await _adapter.StartAsync(CancellationToken.None);
        _adapter.State.Should().Be(AdapterState.Running);

        await _adapter.StopAsync(CancellationToken.None);

        _adapter.State.Should().Be(AdapterState.Stopped);
    }

    private static OpcUaClientSourceAdapter NewAdapter() =>
        new($"opcua-e2e-{Guid.NewGuid():N}", NullLogger<OpcUaClientSourceAdapter>.Instance);

    private static OpcUaClientSourceConfiguration AdapterConfig(
        string instanceId,
        string endpointUrl,
        IReadOnlyList<MonitoredItemConfig> items) => new()
    {
        InstanceId = instanceId,
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "test-server",
        EndpointUrl = endpointUrl,
        ApplicationUri = $"urn:elpis:edgeconnect:test:client:{Guid.NewGuid():N}",
        SecurityMode = OpcUaSecurityMode.None,
        AuthMode = OpcUaAuthMode.Anonymous,
        AutoAcceptUntrustedServerCertificate = true,
        MonitoredItems = items,
    };

    /// <summary>
    /// Shared fixture — single server instance per test class. xUnit
    /// instantiates this once per <see cref="OpcUaClientEndToEndTests"/>
    /// run.
    /// </summary>
    public sealed class SharedServer : IAsyncLifetime
    {
        public OpcUaClientInProcessServerFixture Fixture { get; private set; } = null!;
        public async Task InitializeAsync() => Fixture = await OpcUaClientInProcessServerFixture.StartAsync();
        public async Task DisposeAsync() => await Fixture.DisposeAsync();
    }
}
