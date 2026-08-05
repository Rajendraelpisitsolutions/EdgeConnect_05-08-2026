// ============================================================================
// File: OpcUaClientReconfigureIntegrationTests.cs
// Purpose: End-to-end validation of the PR 6b hot-reconfigure path
//          against the in-process server fixture. Exercises:
//
//            * Add tags at runtime — new MonitoredItem instances are
//              wired to the dispatcher; the new tags emit notifications
//              without a session restart
//            * Remove tags at runtime — the remaining tags continue to
//              emit notifications (proves the executor's Remove phase
//              doesn't tear down the wrong subscriptions)
//            * Idempotent reconfigure (PR 6b amendment #5) — same
//              config in/out increments the counter with
//              lastReconfigureChangeCount=0 and the executor is never
//              hit at the cost-significant Subscription mutation path
//            * Modify SamplingInterval — the diff classifies the
//              change as Modified, executor applies, adapter state
//              stays Running throughout
//
//          Each test owns a fresh fixture + adapter via IAsyncLifetime
//          so reconfigure outcomes do not bleed across tests.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
//            PR 6b + PR 7b plans (user lock 2026-05-29)
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
public sealed class OpcUaClientReconfigureIntegrationTests : IAsyncLifetime
{
    private OpcUaClientInProcessServerFixture? _fixture;
    private OpcUaClientSourceAdapter? _adapter;
    private OpcUaClientSourceConfiguration? _initialConfig;

    public async Task InitializeAsync()
    {
        _fixture = await OpcUaClientInProcessServerFixture.StartAsync();
        _adapter = new OpcUaClientSourceAdapter(
            $"opcua-reconfigure-{Guid.NewGuid():N}",
            NullLogger<OpcUaClientSourceAdapter>.Instance);

        _initialConfig = MakeConfig(_adapter.InstanceId, _fixture.EndpointUrl, new[]
        {
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"),
                DisplayName = "Counter",
            },
        });
        await _adapter.InitializeAsync(_initialConfig, CancellationToken.None);
        await _adapter.StartAsync(CancellationToken.None);
        _adapter.State.Should().Be(AdapterState.Running);
    }

    public async Task DisposeAsync()
    {
        if (_adapter is not null) await _adapter.DisposeAsync();
        if (_fixture is not null) await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Reconfigure_AddTags_NewTagsEmitNotifications()
    {
        // Confirm initial wiring works.
        await ReceiveFromAsync(_adapter!, expectedTagName: "Counter", TimeSpan.FromSeconds(5));

        // Add 2 new tags via hot reconfigure.
        var newConfig = MakeConfig(_adapter!.InstanceId, _fixture!.EndpointUrl, new[]
        {
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"),
                DisplayName = "Counter",
            },
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Sine"),
                DisplayName = "Sine",
            },
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Square"),
                DisplayName = "Square",
            },
        });

        await _adapter!.ReconfigureAsync(newConfig, CancellationToken.None);
        _adapter.State.Should().Be(AdapterState.Running,
            "hot reconfigure must not tear down the session — state must stay Running throughout");

        // Both new tags should emit notifications via the existing
        // dispatcher (proves the FastDataChangeCallback wiring on the
        // newly-added MonitoredItems is correct).
        await ReceiveFromAsync(_adapter, expectedTagName: "Sine", TimeSpan.FromSeconds(10));
        await ReceiveFromAsync(_adapter, expectedTagName: "Square", TimeSpan.FromSeconds(10));

        var health = await _adapter.CheckHealthAsync(CancellationToken.None);
        ((long)health.Metrics!["reconfigureCount"]).Should().Be(1);
        ((int)health.Metrics["lastReconfigureChangeCount"]).Should().Be(2,
            "added 2 tags → change count should reflect 2 Added");
    }

    [Fact]
    public async Task Reconfigure_RemoveTags_RemainingTagsContinue()
    {
        // Reconfigure to a 3-tag baseline first.
        var threeTagConfig = MakeConfig(_adapter!.InstanceId, _fixture!.EndpointUrl, new[]
        {
            new MonitoredItemConfig { NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"), DisplayName = "Counter" },
            new MonitoredItemConfig { NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Sine"), DisplayName = "Sine" },
            new MonitoredItemConfig { NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Square"), DisplayName = "Square" },
        });
        await _adapter!.ReconfigureAsync(threeTagConfig, CancellationToken.None);
        await ReceiveFromAsync(_adapter, "Sine", TimeSpan.FromSeconds(10));

        // Remove Sine.
        var twoTagConfig = MakeConfig(_adapter.InstanceId, _fixture.EndpointUrl, new[]
        {
            new MonitoredItemConfig { NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"), DisplayName = "Counter" },
            new MonitoredItemConfig { NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Square"), DisplayName = "Square" },
        });
        await _adapter.ReconfigureAsync(twoTagConfig, CancellationToken.None);
        _adapter.State.Should().Be(AdapterState.Running);

        // The remaining tags (Counter + Square) must continue to emit
        // notifications. Sampling the next few yields and verifying
        // we see BOTH within the window proves the Remove phase only
        // removed the targeted item, not the surrounding subscription.
        var tagsSeen = await CollectDistinctTagsAsync(_adapter, expectedCount: 2, TimeSpan.FromSeconds(15));
        tagsSeen.Should().BeEquivalentTo(new[] { "Counter", "Square" },
            "Sine was removed; the other two must keep emitting");

        var health = await _adapter.CheckHealthAsync(CancellationToken.None);
        ((long)health.Metrics!["reconfigureCount"]).Should().Be(2,
            "two reconfigures so far — add 2 then remove 1");
    }

    [Fact]
    public async Task Reconfigure_Idempotent_NotificationStreamUninterrupted()
    {
        // Amendment #5 — same config in/out: executor not invoked,
        // counter ticks with changeCount=0, notification stream is
        // uninterrupted.
        await ReceiveFromAsync(_adapter!, "Counter", TimeSpan.FromSeconds(5));

        await _adapter!.ReconfigureAsync(_initialConfig!, CancellationToken.None);
        _adapter.State.Should().Be(AdapterState.Running);

        var health = await _adapter.CheckHealthAsync(CancellationToken.None);
        ((long)health.Metrics!["reconfigureCount"]).Should().Be(1);
        ((int)health.Metrics["lastReconfigureChangeCount"]).Should().Be(0,
            "amendment #5 — idempotent reconfigure surfaces changeCount=0");

        // And the stream is still healthy on the other side.
        await ReceiveFromAsync(_adapter, "Counter", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Reconfigure_ModifySamplingInterval_AdapterStaysRunning()
    {
        // Per-item SamplingInterval change is hot-applicable (amendment
        // #3). The fixture's tags update server-side at ~10 ms cadence;
        // changing the client-side SamplingInterval doesn't materially
        // change what gets reported (the server emits on value change).
        // The OBSERVABLE invariant we pin: the executor accepts the
        // Modified diff, applies it, and the adapter stays Running.
        await ReceiveFromAsync(_adapter!, "Counter", TimeSpan.FromSeconds(5));

        var modifiedConfig = MakeConfig(_adapter!.InstanceId, _fixture!.EndpointUrl, new[]
        {
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"),
                DisplayName = "Counter",
                SamplingIntervalMs = 100,  // change from default (50) → triggers Modified diff
            },
        });

        await _adapter!.ReconfigureAsync(modifiedConfig, CancellationToken.None);
        _adapter.State.Should().Be(AdapterState.Running);

        var health = await _adapter.CheckHealthAsync(CancellationToken.None);
        ((long)health.Metrics!["reconfigureCount"]).Should().Be(1);
        ((int)health.Metrics["lastReconfigureChangeCount"]).Should().Be(1,
            "1 SamplingInterval change → 1 Modified diff entry");

        // Notifications continue (no session teardown).
        await ReceiveFromAsync(_adapter, "Counter", TimeSpan.FromSeconds(5));
    }

    // ─── helpers ───────────────────────────────────────────────────────

    private static OpcUaClientSourceConfiguration MakeConfig(
        string instanceId,
        string endpointUrl,
        IReadOnlyList<MonitoredItemConfig> items) => new()
    {
        InstanceId = instanceId,
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "test-server",
        EndpointUrl = endpointUrl,
        // ApplicationUri MUST be deterministic across MakeConfig calls
        // within a single test. Each test has a unique instanceId; we
        // derive a stable URI from it so the reconfigure-vs-restart
        // check sees no difference and routes through the hot path.
        // A per-call GUID would force the adapter into the restart
        // fall-through, skipping the executor entirely and leaving
        // reconfigureCount at 0.
        ApplicationUri = $"urn:elpis:edgeconnect:test:client:{instanceId}",
        SecurityMode = OpcUaSecurityMode.None,
        AuthMode = OpcUaAuthMode.Anonymous,
        AutoAcceptUntrustedServerCertificate = true,
        MonitoredItems = items,
    };

    private static async Task<CanonicalDataPoint> ReceiveFromAsync(
        OpcUaClientSourceAdapter adapter,
        string expectedTagName,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var cdp in adapter.SubscribeAsync(cts.Token))
            {
                if (cdp.TagName == expectedTagName) return cdp;
            }
        }
        catch (OperationCanceledException)
        {
            // fall through
        }
        throw new TimeoutException(
            $"No notification for tag '{expectedTagName}' arrived within {timeout.TotalSeconds:F1} s.");
    }

    private static async Task<HashSet<string>> CollectDistinctTagsAsync(
        OpcUaClientSourceAdapter adapter,
        int expectedCount,
        TimeSpan timeout)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var cdp in adapter.SubscribeAsync(cts.Token))
            {
                seen.Add(cdp.TagName);
                if (seen.Count >= expectedCount) break;
            }
        }
        catch (OperationCanceledException)
        {
            // fall through to return whatever we accumulated
        }
        return seen;
    }
}
