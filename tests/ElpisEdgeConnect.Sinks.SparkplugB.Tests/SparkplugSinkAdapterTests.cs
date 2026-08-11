// ============================================================================
// File: SparkplugSinkAdapterTests.cs
// Purpose: Locks the K3 slice-1 SparkplugSinkAdapter façade + SparkplugSessionActor:
//          identity, Push + LocalTransport capabilities, InitializeAsync semantic
//          validation (review B1), the adapter-local lifecycle state machine with
//          its guards (incl. stop-before-start and best-effort fault-on-illegal-start),
//          base-path/pull fail-closed, the deferred replay-lifecycle boundary (state
//          unchanged), cancellation, and single-gate serialization.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Configuration;
using ElpisEdgeConnect.Sinks.SparkplugB.Session;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests;

public sealed class SparkplugSinkAdapterTests
{
    private const string SecretSentinel = "SUPERSECRET-do-not-log-123";

    // ==== Identity & advertised capabilities ====

    [Fact]
    public void ProtocolName_IsSparkplugB()
    {
        NewAdapter().ProtocolName.Should().Be("sparkplug-b");
    }

    [Fact]
    public void Capabilities_IsPushOnly()
    {
        NewAdapter().Capabilities.Should().Be(SinkCapabilities.Push);
    }

    [Fact]
    public void AdvertisedDeliveryCapabilities_IsStoreAndForwardLocalTransport()
    {
        SparkplugSinkAdapter.AdvertisedDeliveryCapabilities.SupportsStoreAndForward.Should().BeTrue();
        SparkplugSinkAdapter.AdvertisedDeliveryCapabilities.AcknowledgementBoundary
            .Should().Be(DeliveryAcknowledgementBoundary.LocalTransport);
        SparkplugSinkAdapter.AdvertisedDeliveryCapabilities.SupportsBrokerAcknowledgedAtLeastOnce
            .Should().BeFalse();
    }

    [Fact]
    public void State_StartsCreated()
    {
        NewAdapter().State.Should().Be(AdapterState.Created);
    }

    // ==== Initialize + semantic validation (review B1) ====

    [Fact]
    public async Task Initialize_ValidConfig_TransitionsToInitialized()
    {
        var adapter = NewAdapter();

        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Initialized);
    }

    [Fact]
    public async Task Initialize_WrongConfigType_ThrowsTypedAndFaults()
    {
        var adapter = NewAdapter();

        var act = async () => await adapter.InitializeAsync(
            new OtherSinkConfiguration { InstanceId = "x", ProtocolName = "other" }, CancellationToken.None);

        (await act.Should().ThrowAsync<AdapterException>())
            .Which.Error.Code.Should().Be(SparkplugErrors.ConfigWrongType);
        adapter.State.Should().Be(AdapterState.Failed);
        adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
    }

    [Theory]
    [InlineData("blankHost", SparkplugErrors.ConfigMissingBrokerHost)]
    [InlineData("badPort", SparkplugErrors.ConfigInvalidBrokerPort)]
    [InlineData("badGroup", SparkplugErrors.ConfigInvalidGroupId)]
    [InlineData("badNode", SparkplugErrors.ConfigInvalidEdgeNodeId)]
    [InlineData("badKeepAlive", SparkplugErrors.ConfigInvalidKeepAlive)]
    [InlineData("badBudget", SparkplugErrors.ConfigInvalidRecoveryBudget)]
    [InlineData("incompleteAuth", SparkplugErrors.ConfigAuthIncomplete)]
    public async Task Initialize_SemanticallyInvalidConfig_ThrowsTypedAndFaults(string mutation, string expectedCode)
    {
        var cfg = mutation switch
        {
            "blankHost" => ValidConfig() with { BrokerHost = "" },
            "badPort" => ValidConfig() with { BrokerPort = 0 },
            "badGroup" => ValidConfig() with { GroupId = "a/b" },
            "badNode" => ValidConfig() with { EdgeNodeId = "a+b" },
            "badKeepAlive" => ValidConfig() with { KeepAliveSeconds = 0 },
            "badBudget" => ValidConfig() with { TransportRecoveryMaxAttempts = 0 },
            "incompleteAuth" => ValidConfig() with { Username = "u", Password = null },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        var adapter = NewAdapter();

        var act = async () => await adapter.InitializeAsync(cfg, CancellationToken.None);

        (await act.Should().ThrowAsync<AdapterException>()).Which.Error.Code.Should().Be(expectedCode);
        adapter.State.Should().Be(AdapterState.Failed);
        adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
    }

    [Fact]
    public async Task Initialize_InvalidConfigWithSecret_ExceptionDoesNotLeakPassword()
    {
        var cfg = ValidConfig() with { Username = "u", Password = SecretSentinel, BrokerPort = 0 };
        var adapter = NewAdapter();

        var act = async () => await adapter.InitializeAsync(cfg, CancellationToken.None);

        (await act.Should().ThrowAsync<AdapterException>()).Which.Message.Should().NotContain(SecretSentinel);
    }

    [Fact]
    public async Task Initialize_CanceledToken_ThrowsAndLeavesStateCreated()
    {
        var adapter = NewAdapter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await adapter.InitializeAsync(ValidConfig(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        adapter.State.Should().Be(AdapterState.Created);
    }

    // ==== Start / Stop lifecycle ====

    [Fact]
    public async Task Start_AfterInitialize_TransitionsToRunning()
    {
        var adapter = await InitializedAdapter();

        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task Start_FromCreated_ThrowsAndFaults()
    {
        var adapter = NewAdapter();

        var act = async () => await adapter.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        adapter.State.Should().Be(AdapterState.Failed);
        adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
    }

    [Fact]
    public async Task Start_FromRunning_ThrowsAndFaults()
    {
        var adapter = await RunningAdapter();

        var act = async () => await adapter.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        adapter.State.Should().Be(AdapterState.Failed);
        adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
    }

    [Fact]
    public async Task Start_FromFailed_ThrowsAndStaysFaulted()
    {
        var adapter = await FailedAdapter();

        var act = async () => await adapter.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        adapter.State.Should().Be(AdapterState.Failed);
        adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
    }

    [Fact]
    public async Task Start_CanceledDuringStarting_FaultsAndGateRemainsUsable()
    {
        // Contract (locked): cancellation BEFORE Start mutates state → remain Initialized
        // (covered elsewhere); cancellation AFTER Start enters Starting → Failed/Faulted.
        var actor = new SparkplugSessionActor("spb-1");
        var probeEntered = new TaskCompletionSource();
        actor.GateHeldProbe = async ct =>
        {
            probeEntered.SetResult();
            await Task.Delay(Timeout.Infinite, ct); // observe the operation token
        };
        var adapter = new SparkplugSinkAdapter("spb-1", actor);
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var startTask = adapter.StartAsync(cts.Token); // enters Starting, blocks in the probe
        await probeEntered.Task;
        await cts.CancelAsync();

        var act = async () => await startTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
        adapter.State.Should().Be(AdapterState.Failed);
        adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);

        // The gate is released on the way out, so cleanup still works.
        await adapter.StopAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Stopped);
        adapter.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
    }

    [Fact]
    public async Task Start_FromStopped_RestartsToRunning()
    {
        var adapter = await RunningAdapter();
        await adapter.StopAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Stopped);

        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task StartThenStop_EndsStopped()
    {
        var adapter = await RunningAdapter();

        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task Stop_FromInitialized_ReachesStopped()
    {
        var adapter = await InitializedAdapter();

        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task Stop_FromFaulted_ResetsBothToStopped()
    {
        var adapter = await FailedAdapter();
        adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);

        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Stopped);
        adapter.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
    }

    [Fact]
    public async Task Stop_FromCreated_IsNoOp()
    {
        var adapter = NewAdapter();

        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Created);
    }

    [Fact]
    public async Task Stop_WhenAlreadyStopped_IsIdempotent()
    {
        var adapter = await RunningAdapter();
        await adapter.StopAsync(CancellationToken.None);

        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    // ==== Health ====

    [Fact]
    public async Task CheckHealth_WhenRunning_IsHealthyWithNoSession()
    {
        var adapter = await RunningAdapter();

        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        health.State.Should().Be(AdapterState.Running);
        health.Level.Should().Be(HealthLevel.Healthy);
        health.Metrics.Should().ContainKey("hasSession").WhoseValue.Should().Be(false);
    }

    [Fact]
    public async Task CheckHealth_CanceledToken_ReturnsCanceled()
    {
        var adapter = await RunningAdapter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await adapter.CheckHealthAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CheckHealth_AfterFault_ReportsUnhealthyAndFaulted()
    {
        var adapter = await FailedAdapter();

        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        health.State.Should().Be(AdapterState.Failed);
        health.Level.Should().Be(HealthLevel.Unhealthy);
        health.Metrics.Should().ContainKey("protocolState")
            .WhoseValue.Should().Be(SparkplugProtocolState.Faulted.ToString());
    }

    // ==== Fail-closed surfaces ====

    [Fact]
    public void BasePublish_FailsClosed()
    {
        var adapter = NewAdapter();

        Action act = () => _ = adapter.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void UpdateCurrentValues_FailsClosed()
    {
        var adapter = NewAdapter();

        Action act = () => _ = adapter.UpdateCurrentValuesAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);

        act.Should().Throw<NotSupportedException>();
    }

    // ==== Config validation delegation ====

    [Fact]
    public async Task ValidateConfig_Valid_IsValid()
    {
        (await NewAdapter().ValidateConfigAsync(ValidConfig(), CancellationToken.None)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateConfig_WrongType_FailsWithConfigWrongType()
    {
        var result = await NewAdapter().ValidateConfigAsync(
            new OtherSinkConfiguration { InstanceId = "x", ProtocolName = "other" }, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigWrongType);
    }

    [Fact]
    public async Task ValidateConfig_CanceledToken_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await NewAdapter().ValidateConfigAsync(ValidConfig(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ==== Deferred replay lifecycle (slices 4-6): throws, leaves state untouched ====

    [Fact]
    public async Task EndSession_WithNoActiveSession_IsNoOp()
    {
        // The full replay lifecycle (Rebirth/PublishContext/CompleteCatchUp/EndSession) is implemented;
        // EndSession with no active session is a harmless no-op that leaves state unchanged.
        var adapter = await RunningAdapter();
        var state = adapter.State;

        await adapter.EndSessionAsync(
            ReplaySessionEnd.Create(ReplaySessionId.Create(1), "route-1", ReplaySessionEndReason.Stop), CancellationToken.None);

        adapter.State.Should().Be(state);
    }

    [Fact]
    public async Task DeferredReplayCall_DoesNotBlockSubsequentLifecycle()
    {
        var adapter = NewAdapter();
        try { _ = adapter.RebirthAsync(null!, CancellationToken.None); }
        catch (NotImplementedException) { /* expected */ }

        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    // ==== Single-gate serialization (deterministic, no Thread.Sleep) ====

    [Fact]
    public async Task Gate_SerializesConcurrentLifecycleCalls()
    {
        var actor = new SparkplugSessionActor("spb-1");
        var probeEntered = new TaskCompletionSource();
        var releaseProbe = new TaskCompletionSource();
        actor.GateHeldProbe = async _ =>
        {
            probeEntered.SetResult();
            await releaseProbe.Task;
        };
        var adapter = new SparkplugSinkAdapter("spb-1", actor);
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        var startTask = adapter.StartAsync(CancellationToken.None); // enters gate, awaits the probe
        await probeEntered.Task;                                    // Start is now holding the gate

        var stopTask = adapter.StopAsync(CancellationToken.None);   // must block on the gate
        stopTask.IsCompleted.Should().BeFalse();                    // serialized behind Start

        releaseProbe.SetResult();                                   // let Start complete + release
        await startTask;
        await stopTask;

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var adapter = NewAdapter();

        await adapter.DisposeAsync();
        var act = async () => await adapter.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    // ==== Helpers ====

    private static SparkplugSinkAdapter NewAdapter() => new("spb-1");

    private static async Task<SparkplugSinkAdapter> InitializedAdapter()
    {
        var adapter = NewAdapter();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        return adapter;
    }

    private static async Task<SparkplugSinkAdapter> RunningAdapter()
    {
        var adapter = await InitializedAdapter();
        await adapter.StartAsync(CancellationToken.None);
        return adapter;
    }

    private static async Task<SparkplugSinkAdapter> FailedAdapter()
    {
        var adapter = NewAdapter();
        try
        {
            await adapter.InitializeAsync(
                new OtherSinkConfiguration { InstanceId = "x", ProtocolName = "other" }, CancellationToken.None);
        }
        catch (AdapterException) { /* expected — leaves the actor Failed */ }
        adapter.State.Should().Be(AdapterState.Failed);
        return adapter;
    }

    private static SparkplugSinkConfiguration ValidConfig() => new()
    {
        InstanceId = "spb-1",
        ProtocolName = SparkplugBProtocol.ProtocolName,
        BrokerHost = "localhost",
        GroupId = "PlantA",
        EdgeNodeId = "gw-1",
    };

    private sealed record OtherSinkConfiguration : SinkConfiguration;
}
