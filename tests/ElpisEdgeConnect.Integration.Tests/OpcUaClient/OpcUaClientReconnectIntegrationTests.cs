// ============================================================================
// File: OpcUaClientReconnectIntegrationTests.cs
// Purpose: End-to-end validation of the PR 6a reconnect coordinator
//          against the in-process server fixture. Exercises:
//
//            * Server restart on same port → adapter detects bad
//              keep-alive, transitions Running → Degraded, then
//              recovers back to Running once the reconnect handler
//              re-establishes the session
//            * Reconnect counter increments
//            * Notifications resume after recovery (proves the
//              FastDataChangeCallback re-wire path actually fires
//              on the new session's subscriptions)
//            * Server stays down → adapter STAYS Degraded (the
//              SessionReconnectHandler retries indefinitely; this
//              behaviour is correct for v1 — future hardening could
//              add an explicit retry-budget cap)
//
//          Each test owns a fresh fixture + adapter via
//          IAsyncLifetime — restart scenarios mutate fixture state in
//          ways that would interfere across tests if a class fixture
//          were shared.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3, §1.3.5
//            PR 6a + PR 7b plans (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
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
public sealed class OpcUaClientReconnectIntegrationTests : IAsyncLifetime
{
    private OpcUaClientInProcessServerFixture? _fixture;
    private OpcUaClientSourceAdapter? _adapter;

    public async Task InitializeAsync()
    {
        _fixture = await OpcUaClientInProcessServerFixture.StartAsync();
        _adapter = new OpcUaClientSourceAdapter(
            $"opcua-reconnect-{Guid.NewGuid():N}",
            NullLogger<OpcUaClientSourceAdapter>.Instance);

        var config = MakeConfig(_adapter.InstanceId, _fixture.EndpointUrl);
        await _adapter.InitializeAsync(config, CancellationToken.None);
        await _adapter.StartAsync(CancellationToken.None);
        _adapter.State.Should().Be(AdapterState.Running);
    }

    public async Task DisposeAsync()
    {
        if (_adapter is not null) await _adapter.DisposeAsync();
        if (_fixture is not null) await _fixture.DisposeAsync();
    }

    // Downtime tuning — the OPC Foundation stack's default session
    // KeepAliveInterval is ~5 s with ~2 missed keep-alives before
    // KeepAlive event fires bad. A 10 s downtime guarantees detection
    // across CI timing variance.
    private static readonly TimeSpan RestartDownTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DegradedDetectionWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Restart_AdapterTransitionsThroughDegradedAndRecovers()
    {
        // Warm up — receive a few notifications so we know the
        // subscription is wired and the publish thread is firing.
        await ReceiveAtLeastOneAsync(_adapter!, TimeSpan.FromSeconds(5));

        await _fixture!.RestartPreservingSessionsAsync(downTime: RestartDownTime);

        // Adapter should transition Running → Degraded → Running.
        await WaitForStateAsync(_adapter!, AdapterState.Degraded, DegradedDetectionWindow);
        await WaitForStateAsync(_adapter!, AdapterState.Running, RecoveryWindow);
    }

    [Fact]
    public async Task Restart_ReconnectCounterIncrementsAfterRecovery()
    {
        await ReceiveAtLeastOneAsync(_adapter!, TimeSpan.FromSeconds(5));

        var before = await _adapter!.CheckHealthAsync(CancellationToken.None);
        var transfersBefore = (long)before.Metrics!["reconnectsViaTransfer"];
        var recreatesBefore = (long)before.Metrics["reconnectsViaRecreate"];

        await _fixture!.RestartPreservingSessionsAsync(downTime: RestartDownTime);

        // Explicit Degraded gate first — proves the keep-alive actually
        // detected the dropout. Without this, WaitForStateAsync(Running)
        // would return immediately because state never left Running,
        // masking the case where the reconnect path didn't fire.
        await WaitForStateAsync(_adapter!, AdapterState.Degraded, DegradedDetectionWindow);
        await WaitForStateAsync(_adapter!, AdapterState.Running, RecoveryWindow);

        var after = await _adapter!.CheckHealthAsync(CancellationToken.None);
        var transfersAfter = (long)after.Metrics!["reconnectsViaTransfer"];
        var recreatesAfter = (long)after.Metrics["reconnectsViaRecreate"];

        // The OPC stack's discrimination between Transfer and Recreate
        // depends on whether the server preserved session state — our
        // fixture drops it on Stop(), so the actual path is stack-
        // dependent. Test the OBSERVABLE invariant: at least one
        // successful reconnect counter ticked.
        var totalIncrease = (transfersAfter - transfersBefore) + (recreatesAfter - recreatesBefore);
        totalIncrease.Should().BeGreaterThanOrEqualTo(1,
            "the coordinator must observe a successful reconnect after the server restart");

        after.Metrics["lastSuccessfulReconnectUtc"].Should().NotBe("never",
            "amendment #1 — the timestamp should populate on first successful reconnect");
        after.Metrics["lastReconnectMode"].Should().NotBe("Unknown",
            "amendment #2 — the mode should reflect the path taken");
    }

    [Fact]
    public async Task Restart_NotificationsResumeAfterRecovery()
    {
        // Drain at least one notification pre-restart to confirm wiring.
        await ReceiveAtLeastOneAsync(_adapter!, TimeSpan.FromSeconds(5));

        await _fixture!.RestartPreservingSessionsAsync(downTime: RestartDownTime);

        // Same explicit-Degraded gate as the counter test — without it,
        // this test would PASS trivially in scenarios where the
        // reconnect coordinator never fires (notifications would just
        // keep flowing through the un-disturbed session), masking the
        // actual recovery-path coverage gap.
        await WaitForStateAsync(_adapter!, AdapterState.Degraded, DegradedDetectionWindow);
        await WaitForStateAsync(_adapter!, AdapterState.Running, RecoveryWindow);

        // After recovery, the FastDataChangeCallback re-wire path
        // (PR 6a OnCoordinatorStateChanged → RewireForReconnectedSession)
        // should restore the notification stream end-to-end.
        await ReceiveAtLeastOneAsync(_adapter!, TimeSpan.FromSeconds(15),
            because: "notifications must resume after reconnect; the dispatcher "
                + "survived the reconnect (locked invariant #4) and the subscriptions "
                + "have FastDataChangeCallback re-wired");
    }

    [Fact]
    public async Task ServerStaysDown_AdapterRemainsDegraded()
    {
        await ReceiveAtLeastOneAsync(_adapter!, TimeSpan.FromSeconds(5));

        // Stop the server without restart. SessionReconnectHandler will
        // retry indefinitely; adapter stays in Degraded.
        await _fixture!.StopAsync();
        await WaitForStateAsync(_adapter!, AdapterState.Degraded, DegradedDetectionWindow);

        // Hold and confirm the adapter does NOT spontaneously recover
        // or fail-out — the stack's retry loop keeps it in Degraded.
        await Task.Delay(TimeSpan.FromSeconds(3));
        _adapter!.State.Should().Be(AdapterState.Degraded,
            "with no server to reconnect to, the adapter must stay in Degraded — "
            + "the OPC stack's SessionReconnectHandler retries indefinitely by design "
            + "(future hardening can add an explicit retry-budget cap if needed)");

        var health = await _adapter.CheckHealthAsync(CancellationToken.None);
        health.Metrics!["currentlyReconnecting"].Should().Be(true);
    }

    // ─── helpers ───────────────────────────────────────────────────────

    private static OpcUaClientSourceConfiguration MakeConfig(string instanceId, string endpointUrl) => new()
    {
        InstanceId = instanceId,
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "test-server",
        EndpointUrl = endpointUrl,
        ApplicationUri = $"urn:elpis:edgeconnect:test:client:{Guid.NewGuid():N}",
        SecurityMode = OpcUaSecurityMode.None,
        AuthMode = OpcUaAuthMode.Anonymous,
        AutoAcceptUntrustedServerCertificate = true,
        MonitoredItems = new[]
        {
            new MonitoredItemConfig
            {
                NodeId = OpcUaClientInProcessServerFixture.TagNodeIdString("Counter"),
                DisplayName = "Counter",
            },
        },
    };

    private static async Task WaitForStateAsync(
        OpcUaClientSourceAdapter adapter,
        AdapterState target,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (adapter.State == target) return;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"Adapter did not reach state {target} within {timeout.TotalSeconds:F1} s; "
            + $"current state is {adapter.State}.");
    }

    private static async Task<CanonicalDataPoint> ReceiveAtLeastOneAsync(
        OpcUaClientSourceAdapter adapter,
        TimeSpan timeout,
        string? because = null)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var cdp in adapter.SubscribeAsync(cts.Token))
            {
                return cdp;
            }
        }
        catch (OperationCanceledException)
        {
            // fall through to throw
        }
        throw new TimeoutException(
            $"No notification arrived within {timeout.TotalSeconds:F1} s"
            + (because is null ? "." : $" — {because}"));
    }
}
