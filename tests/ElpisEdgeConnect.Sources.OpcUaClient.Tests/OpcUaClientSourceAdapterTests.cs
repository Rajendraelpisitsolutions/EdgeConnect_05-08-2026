// ============================================================================
// Tests: OpcUaClientSourceAdapterTests — pin the adapter spine's
//        lifecycle + ValidateConfigAsync surface for PR 1.
//
//        PR 1 invariants:
//          * Capabilities flags advertise Subscription | Browse |
//            Discovery | Quality | TestConnect (NOT Polling, NOT WriteBack)
//          * InitializeAsync rejects wrong config type → Failed state +
//            throws
//          * InitializeAsync rejects mismatched InstanceId → Failed
//          * Coherence checks fire at Initialize: UserName + None,
//            UserName without credentials, Certificate without path
//          * StartAsync requires prior Initialize
//          * StartAsync transitions to Running (PR 2+ will wire the
//            actual Session)
//          * StopAsync is idempotent from Stopped / Created
//          * CheckHealthAsync surfaces State + monitoredItemsConfigured
//          * PollAsync throws NotSupported (subscription-only adapter)
//          * SubscribeAsync returns empty stream for PR 1
//          * BrowseTagsAsync returns empty list for PR 1
//          * ValidateConfigAsync covers: wrong type, invalid endpoint,
//            invalid intervals, lifetime-too-short, coherence rejections
//          * ReconfigureAsync default-impl works (no override yet —
//            PR 6 adds it)
//
//        Subsequent PRs (2-6) add real Session/Subscription/Browse logic;
//        these tests update accordingly.
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §5.1
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Opc.Ua;
using Opc.Ua.Client;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaClientSourceAdapterTests : IAsyncDisposable
{
    private OpcUaClientSourceAdapter? _adapter;

    private OpcUaClientSourceAdapter CreateAdapter(string instanceId = "opcua-test")
    {
        _adapter = new OpcUaClientSourceAdapter(instanceId, NullLogger.Instance);
        return _adapter;
    }

    /// <summary>
    /// Create an adapter wired to a substituted connection establisher
    /// + substituted subscription factory. Both return cleanly so
    /// StartAsync reaches Running. Used by happy-path lifecycle tests.
    /// </summary>
    private (OpcUaClientSourceAdapter Adapter, IOpcUaClientSubscriptionFactory Factory) CreateAdapterWithFakeSessionAndFactory(
        string instanceId = "opcua-test",
        IReadOnlyList<Subscription>? subscriptions = null)
    {
        var fakeSession = Substitute.For<ISession>();
        fakeSession.CloseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((StatusCode)StatusCodes.Good));

        var establisher = Substitute.For<IOpcUaClientConnectionEstablisher>();
        establisher.EstablishAsync(
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fakeSession));

        var factory = Substitute.For<IOpcUaClientSubscriptionFactory>();
        factory.CreateSubscriptionsAsync(
            Arg.Any<ISession>(),
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Subscription>>(
                subscriptions ?? System.Array.Empty<Subscription>()));

        _adapter = new OpcUaClientSourceAdapter(instanceId, NullLogger.Instance, establisher, factory);
        return (_adapter, factory);
    }

    /// <summary>Convenience overload returning just the adapter.</summary>
    private OpcUaClientSourceAdapter CreateAdapterWithFakeSession(string instanceId = "opcua-test")
    {
        var (adapter, _) = CreateAdapterWithFakeSessionAndFactory(instanceId);
        return adapter;
    }

    /// <summary>
    /// Create an adapter wired to a substituted connection establisher
    /// that THROWS the given exception. Used by fail-soft tests.
    /// </summary>
    private OpcUaClientSourceAdapter CreateAdapterWithFailingEstablisher(Exception toThrow, string instanceId = "opcua-test")
    {
        var establisher = Substitute.For<IOpcUaClientConnectionEstablisher>();
        establisher.EstablishAsync(
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns<Task<ISession>>(_ => throw toThrow);

        // Subscription factory is irrelevant when the establisher throws.
        var factory = Substitute.For<IOpcUaClientSubscriptionFactory>();

        _adapter = new OpcUaClientSourceAdapter(instanceId, NullLogger.Instance, establisher, factory);
        return _adapter;
    }

    /// <summary>
    /// Create an adapter where the establisher succeeds but the subscription
    /// factory throws. Used by fail-soft tests for subscription-create failures.
    /// </summary>
    private OpcUaClientSourceAdapter CreateAdapterWithFailingSubscriptionFactory(
        Exception toThrow, string instanceId = "opcua-test")
    {
        var fakeSession = Substitute.For<ISession>();
        fakeSession.CloseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((StatusCode)StatusCodes.Good));

        var establisher = Substitute.For<IOpcUaClientConnectionEstablisher>();
        establisher.EstablishAsync(
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fakeSession));

        var factory = Substitute.For<IOpcUaClientSubscriptionFactory>();
        factory.CreateSubscriptionsAsync(
            Arg.Any<ISession>(),
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<Subscription>>>(_ => throw toThrow);

        _adapter = new OpcUaClientSourceAdapter(instanceId, NullLogger.Instance, establisher, factory);
        return _adapter;
    }

    private static OpcUaClientSourceConfiguration ValidConfig(
        string instanceId = "opcua-test",
        OpcUaSecurityMode securityMode = OpcUaSecurityMode.SignAndEncrypt,
        OpcUaAuthMode authMode = OpcUaAuthMode.Anonymous,
        OpcUaClientCredentials? credentials = null) => new()
    {
        InstanceId = instanceId,
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "factorytalk",
        EndpointUrl = "opc.tcp://factorytalk.pilot.local:4840",
        SecurityMode = securityMode,
        AuthMode = authMode,
        Credentials = credentials,
    };

    public async ValueTask DisposeAsync()
    {
        if (_adapter is not null)
        {
            await _adapter.DisposeAsync();
        }
    }

    // ─── Capabilities ─────────────────────────────────────────────────

    [Fact]
    public void Capabilities_SubscriptionPlusBrowsePlusDiscoveryPlusQualityPlusTestConnect()
    {
        var adapter = CreateAdapter();

        adapter.Capabilities.Should().HaveFlag(SourceCapabilities.Subscription);
        adapter.Capabilities.Should().HaveFlag(SourceCapabilities.Browse);
        adapter.Capabilities.Should().HaveFlag(SourceCapabilities.Discovery);
        adapter.Capabilities.Should().HaveFlag(SourceCapabilities.Quality);
        adapter.Capabilities.Should().HaveFlag(SourceCapabilities.TestConnect);

        adapter.Capabilities.Should().NotHaveFlag(SourceCapabilities.Polling);
        adapter.Capabilities.Should().NotHaveFlag(SourceCapabilities.WriteBack);
    }

    [Fact]
    public void ProtocolName_IsOpcUaClient()
    {
        CreateAdapter().ProtocolName.Should().Be("opcua-client");
    }

    // ─── InitializeAsync ──────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_ValidConfig_TransitionsToInitialized()
    {
        var adapter = CreateAdapter();

        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Initialized);
    }

    [Fact]
    public async Task InitializeAsync_WrongConfigType_FailsAndThrows()
    {
        var adapter = CreateAdapter();
        var stub = new StubSourceConfiguration { InstanceId = "stub", ProtocolName = "stub", DeviceId = "stub" };

        var act = () => adapter.InitializeAsync(stub, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OpcUaClientSourceConfiguration*");
        adapter.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task InitializeAsync_MismatchedInstanceId_FailsAndThrows()
    {
        var adapter = CreateAdapter("real-id");

        var wrongIdConfig = ValidConfig(instanceId: "different-id");
        var act = () => adapter.InitializeAsync(wrongIdConfig, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*InstanceId*");
        adapter.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task InitializeAsync_UsernameWithNone_FailsAndThrows()
    {
        // OPCUA.UNSAFE_USERNAME_OVER_NONE — credentials cleartext on the wire.
        var adapter = CreateAdapter();
        var config = ValidConfig(
            securityMode: OpcUaSecurityMode.None,
            authMode: OpcUaAuthMode.UserName,
            credentials: new OpcUaClientCredentials { Username = "u", Password = "p" });

        var act = () => adapter.InitializeAsync(config, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNSAFE_USERNAME_OVER_NONE*");
    }

    [Fact]
    public async Task InitializeAsync_UsernameWithoutCredentials_FailsAndThrows()
    {
        var adapter = CreateAdapter();
        var config = ValidConfig(
            securityMode: OpcUaSecurityMode.SignAndEncrypt,
            authMode: OpcUaAuthMode.UserName,
            credentials: null);

        var act = () => adapter.InitializeAsync(config, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*USERNAME_CREDENTIALS_MISSING*");
    }

    [Fact]
    public async Task InitializeAsync_CertificateWithoutPath_FailsAndThrows()
    {
        var adapter = CreateAdapter();
        var config = ValidConfig(
            securityMode: OpcUaSecurityMode.SignAndEncrypt,
            authMode: OpcUaAuthMode.Certificate,
            credentials: new OpcUaClientCredentials { CertificatePath = null });

        var act = () => adapter.InitializeAsync(config, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CERT_PATH_MISSING*");
    }

    // ─── StartAsync / StopAsync ───────────────────────────────────────

    [Fact]
    public async Task StartAsync_BeforeInitialize_Throws()
    {
        var adapter = CreateAdapter();

        var act = () => adapter.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsync_AfterInitialize_OpensSessionAndTransitionsToRunning()
    {
        var adapter = CreateAdapterWithFakeSession();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task StartAsync_FactoryThrows_FailSoftToFailedWithLastError()
    {
        // PR 2 fail-soft pattern: initial connect failures DON'T throw
        // from StartAsync — they transition to Failed + capture
        // _lastError so the reconnect coordinator (PR 6) can retry.
        var ex = new System.Net.Sockets.SocketException();
        var adapter = CreateAdapterWithFailingEstablisher(ex);
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Failed);
        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.LastError.Should().NotBeNull();
        health.LastError!.Code.Should().Be("OPCUA.CONNECT_NETWORK_ERROR");
    }

    [Fact]
    public async Task StartAsync_ServiceResultUntrusted_ClassifiesAsServerCertUntrusted()
    {
        var ex = new ServiceResultException(StatusCodes.BadCertificateUntrusted);
        var adapter = CreateAdapterWithFailingEstablisher(ex);
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Failed);
        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.LastError!.Code.Should().Be("OPCUA.SERVER_CERT_UNTRUSTED");
    }

    [Fact]
    public async Task StartAsync_ServiceResultAccessDenied_ClassifiesAsAuthDenied()
    {
        var ex = new ServiceResultException(StatusCodes.BadUserAccessDenied);
        var adapter = CreateAdapterWithFailingEstablisher(ex);
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.LastError!.Code.Should().Be("OPCUA.AUTH_DENIED");
    }

    [Fact]
    public async Task StopAsync_FromCreated_IsNoOp()
    {
        var adapter = CreateAdapter();

        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Created);
    }

    [Fact]
    public async Task StopAsync_FromRunning_ClosesSessionAndTransitionsToStopped()
    {
        var adapter = CreateAdapterWithFakeSession();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task StopAsync_FromFailed_StillTransitionsToStopped()
    {
        // After a fail-soft StartAsync, StopAsync MUST still complete
        // cleanly so the runtime can dispose / restart the adapter.
        var adapter = CreateAdapterWithFailingEstablisher(new System.Net.Sockets.SocketException());
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Failed);

        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    // ─── CheckHealthAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_AfterStart_ReportsHealthyWithLastSuccess()
    {
        var adapter = CreateAdapterWithFakeSession();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        health.State.Should().Be(AdapterState.Running);
        health.Level.Should().Be(HealthLevel.Healthy);
        health.LastSuccessAt.Should().NotBeNull("StartAsync populates _lastSuccessAtUtc on successful connect");
        health.Metrics.Should().NotBeNull();
        var metrics = health.Metrics!;
        // PR 3 amendment #2 — configured-vs-active metrics (user lock 2026-05-29).
        metrics.Should().ContainKey("configuredMonitoredItems")
            .WhoseValue.Should().Be(0);
        metrics.Should().ContainKey("configuredSubscriptions")
            .WhoseValue.Should().Be(0);
        metrics.Should().ContainKey("monitoredItemsActive")
            .WhoseValue.Should().Be(0);
        metrics.Should().ContainKey("subscriptionsActive")
            .WhoseValue.Should().Be(0);
        // PR 4 amendment #4 — hot-path counters (real dispatcher in
        // happy-path config; substituted fake in tests that exercise
        // dispatcher seam).
        metrics.Should().ContainKey("notificationsReceived");
        metrics.Should().ContainKey("notificationsDispatched");
        metrics.Should().ContainKey("notificationsDroppedDueToBackpressure");
        metrics.Should().ContainKey("notificationsDroppedAtShutdown");
        // PR 4 amendment #3 — queue-depth probe capability + value.
        metrics.Should().ContainKey("notificationQueueDepthAvailable");
        metrics.Should().ContainKey("notificationQueueDepth");
    }

    // ─── PollAsync / SubscribeAsync / BrowseTagsAsync ─────────────────

    [Fact]
    public async Task PollAsync_AlwaysThrows_SubscriptionOnlyAdapter()
    {
        var adapter = CreateAdapter();

        var act = () => adapter.PollAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task SubscribeAsync_PR2_ReturnsEmptyStream()
    {
        var adapter = CreateAdapterWithFakeSession();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var collected = new System.Collections.Generic.List<Core.Model.CanonicalDataPoint>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await foreach (var point in adapter.SubscribeAsync(cts.Token))
            {
                collected.Add(point);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected — empty stream completes immediately; cancellation
            // catches any future behaviour where the stream blocks.
        }

        collected.Should().BeEmpty(
            "PR 1 lifecycle skeleton emits no data; real subscription wiring lands in PR 3.");
    }

    [Fact]
    public async Task BrowseTagsAsync_BeforeInitialize_ReturnsEmptyList()
    {
        // No config = no configured items to enumerate. Defensive
        // emptyness rather than throwing.
        var adapter = CreateAdapter();
        var defs = await adapter.BrowseTagsAsync(CancellationToken.None);

        defs.Should().BeEmpty();
    }

    [Fact]
    public async Task BrowseTagsAsync_AfterInitialize_ReturnsConfiguredItemsAsTagDefinitions()
    {
        // PR 5 — BrowseTagsAsync is the RUNTIME-facing surface (distinct
        // from the wizard-facing ITagBrowseService at OpcUaClientBrowseService).
        // It returns the configured monitored items so the route engine
        // + tag inventory tooling can enumerate what this adapter watches.
        var adapter = CreateAdapter();
        var config = ValidConfig() with
        {
            MonitoredItems = new[]
            {
                new MonitoredItemConfig { NodeId = "ns=2;i=1", DisplayName = "Speed" },
                new MonitoredItemConfig { NodeId = "ns=2;i=2", DisplayName = "Status" },
            },
        };
        await adapter.InitializeAsync(config, CancellationToken.None);

        var defs = await adapter.BrowseTagsAsync(CancellationToken.None);

        defs.Should().HaveCount(2);
        defs[0].Name.Should().Be("Speed");
        defs[0].Description.Should().Be("ns=2;i=1");
        defs[1].Name.Should().Be("Status");
        defs[1].Description.Should().Be("ns=2;i=2");
    }

    // ─── ValidateConfigAsync ──────────────────────────────────────────

    [Fact]
    public async Task ValidateConfigAsync_HappyPath_Succeeds()
    {
        var adapter = CreateAdapter();
        var result = await adapter.ValidateConfigAsync(ValidConfig(), CancellationToken.None);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateConfigAsync_WrongType_Fails()
    {
        var adapter = CreateAdapter();
        var stub = new StubSourceConfiguration { InstanceId = "stub", ProtocolName = "stub", DeviceId = "stub" };
        var result = await adapter.ValidateConfigAsync(stub, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "OPCUA.CONFIG_WRONG_TYPE");
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("http://factorytalk.pilot.local:4840")]  // wrong scheme
    [InlineData("")]
    public async Task ValidateConfigAsync_InvalidEndpoint_Fails(string endpoint)
    {
        var adapter = CreateAdapter();
        var config = ValidConfig() with { EndpointUrl = endpoint };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "OPCUA.CONFIG_INVALID_ENDPOINT");
    }

    [Fact]
    public async Task ValidateConfigAsync_ZeroIntervals_Fails()
    {
        var adapter = CreateAdapter();
        var config = ValidConfig() with { PublishingIntervalMs = 0 };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "OPCUA.CONFIG_INVALID_INTERVALS");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]                                                                   // below MinimumNotificationChannelCapacity
    [InlineData(10_001)]                                                               // above MaximumNotificationChannelCapacity
    [InlineData(int.MaxValue)]
    public async Task ValidateConfigAsync_ChannelCapacityOutOfRange_Fails(int outOfRange)
    {
        // PR 4 amendment #1 — guards against pathological capacities.
        var adapter = CreateAdapter();
        var config = ValidConfig() with { NotificationChannelCapacity = outOfRange };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "OPCUA.CONFIG_CHANNEL_CAPACITY_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData(100)]                                                                  // exactly Minimum
    [InlineData(1_000)]                                                                // Default
    [InlineData(10_000)]                                                               // exactly Maximum
    public async Task ValidateConfigAsync_ChannelCapacityInRange_Succeeds(int inRange)
    {
        var adapter = CreateAdapter();
        var config = ValidConfig() with { NotificationChannelCapacity = inRange };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateConfigAsync_LifetimeShorterThan3xKeepAlive_Fails()
    {
        var adapter = CreateAdapter();
        var config = ValidConfig() with { KeepAliveCount = 20, LifetimeCount = 30 };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "OPCUA.CONFIG_LIFETIME_TOO_SHORT");
    }

    [Fact]
    public async Task ValidateConfigAsync_UserNameOverNone_Fails()
    {
        var adapter = CreateAdapter();
        var config = ValidConfig(
            securityMode: OpcUaSecurityMode.None,
            authMode: OpcUaAuthMode.UserName,
            credentials: new OpcUaClientCredentials { Username = "u", Password = "p" });

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "OPCUA.UNSAFE_USERNAME_OVER_NONE");
    }

    // ─── ReconfigureAsync default-impl ────────────────────────────────

    [Fact]
    public async Task ReconfigureAsync_DefaultImpl_RestartsWithNewConfig()
    {
        // PR 1/2 inherit the safe Stop+Initialize+Start default-impl from
        // ISourceAdapter (per Stream 3, PR #49). PR 6 adds the override
        // with active-set-snapshot semantics.
        var adapter = CreateAdapterWithFakeSession();
        ISourceAdapter via = adapter;
        await via.InitializeAsync(ValidConfig(), CancellationToken.None);
        await via.StartAsync(CancellationToken.None);

        var newConfig = ValidConfig() with
        {
            EndpointUrl = "opc.tcp://factorytalk.pilot.local:4841",
        };
        await via.ReconfigureAsync(newConfig, CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    // ─── TestConnectAsync ─────────────────────────────────────────────

    [Fact]
    public async Task TestConnectAsync_SessionFactoryThrows_ReturnsFailureWithErrorCode()
    {
        var ex = new ServiceResultException(StatusCodes.BadCertificateUntrusted);
        var adapter = CreateAdapterWithFailingEstablisher(ex);

        var result = await adapter.TestConnectAsync(ValidConfig(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.EndpointUrl.Should().Be("opc.tcp://factorytalk.pilot.local:4840");
        result.ErrorCode.Should().Be("OPCUA.SERVER_CERT_UNTRUSTED");
        result.Message.Should().Contain("Connect failed");
    }

    [Fact]
    public async Task TestConnectAsync_DoesNotMutateRunningSessionState()
    {
        // ADR-0015 Rule 6 — Test Connection is read-only. Running adapter's
        // _session must NOT be touched by the probe.
        var runningAdapter = CreateAdapterWithFakeSession();
        await runningAdapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await runningAdapter.StartAsync(CancellationToken.None);
        runningAdapter.State.Should().Be(AdapterState.Running);

        // Probe a different endpoint (still using the fake factory's
        // session, but the adapter's State remains Running).
        var probeConfig = ValidConfig() with { EndpointUrl = "opc.tcp://other.pilot.local:4840" };
        _ = await runningAdapter.TestConnectAsync(probeConfig, CancellationToken.None);

        runningAdapter.State.Should().Be(AdapterState.Running,
            "TestConnectAsync MUST NOT mutate running adapter state per ADR-0015 Rule 6.");
    }

    // ─── Subscription lifecycle (PR 3) ───────────────────────────────

    [Fact]
    public async Task StartAsync_CallsSubscriptionFactoryWithOpenedSession()
    {
        var (adapter, factory) = CreateAdapterWithFakeSessionAndFactory();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        // Verify the factory was invoked exactly once with the
        // ISession the establisher returned and the configured config.
        await factory.Received(1).CreateSubscriptionsAsync(
            Arg.Any<ISession>(),
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ZeroMonitoredItems_FactoryStillCalledNoException()
    {
        // Operator-facing rule: an empty MonitoredItems list is a VALID
        // configuration state (operator hasn't picked tags yet). StartAsync
        // must not throw.
        var (adapter, factory) = CreateAdapterWithFakeSessionAndFactory();
        var config = ValidConfig();  // MonitoredItems defaults to empty.
        await adapter.InitializeAsync(config, CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
        await factory.Received(1).CreateSubscriptionsAsync(
            Arg.Any<ISession>(),
            Arg.Is<OpcUaClientSourceConfiguration>(c => c.MonitoredItems.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_SubscriptionFactoryThrows_FailSoftToFailed()
    {
        // Subscription create failures follow the same fail-soft pattern
        // as Session create failures — capture _lastError, transition to
        // Failed, no throw. Reconnect coordinator (PR 6) retries.
        var ex = new InvalidOperationException(
            "OPCUA.TOO_MANY_MONITORED_ITEMS: configured 150000 monitored items but the per-session ceiling is 100000.");
        var adapter = CreateAdapterWithFailingSubscriptionFactory(ex);
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Failed);
        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.LastError.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_AfterStart_SurfacesConfiguredAndActiveMetrics()
    {
        // PR 3 amendment #2 — operator must distinguish "configured but
        // not active" from "configured and active" during reconnect
        // troubleshooting.
        var (adapter, _) = CreateAdapterWithFakeSessionAndFactory();
        var config = ValidConfig() with
        {
            MonitoredItems = new[]
            {
                new MonitoredItemConfig { NodeId = "ns=2;i=1", DisplayName = "T1" },
                new MonitoredItemConfig { NodeId = "ns=2;i=2", DisplayName = "T2" },
            },
        };
        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        health.Metrics.Should().NotBeNull();
        var metrics = health.Metrics!;
        metrics.Should().ContainKey("configuredMonitoredItems")
            .WhoseValue.Should().Be(2);
        metrics.Should().ContainKey("configuredSubscriptions")
            .WhoseValue.Should().Be(1, "2 items × 1000-per-sub = 1 subscription");
        // Active counts are 0 because the substituted factory returned
        // an empty subscription list — exactly the kind of "configured
        // != active" delta operators use to diagnose reconnect issues.
        metrics.Should().ContainKey("monitoredItemsActive")
            .WhoseValue.Should().Be(0);
        metrics.Should().ContainKey("subscriptionsActive")
            .WhoseValue.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_DisposesSubscriptionsBeforeClosingSession()
    {
        // Ordering verification — subscriptions MUST be cleaned up
        // before session.Close per OPC stack ownership semantics. We
        // verify call order via NSubstitute Received().
        var (adapter, _) = CreateAdapterWithFakeSessionAndFactory();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // Act: stop. The substituted ISession's CloseAsync is the
        // observable. The substituted factory returns an empty list of
        // subscriptions in this fixture — so we can't observe a
        // per-subscription Delete call. The integration test in PR 7
        // covers real subscription-then-session ordering against UA
        // Sample Server.
        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    // ─── Reconnect coordinator (PR 6a) ────────────────────────────────

    /// <summary>
    /// Build an adapter with substituted establisher, subscription factory
    /// AND reconnect handler wrapper. The wrapper is returned so tests can
    /// raise its <see cref="IOpcUaReconnectHandlerWrapper.ReconnectCompleted"/>
    /// event to simulate the OPC stack finishing a reconnect attempt. The
    /// session is also returned so tests can raise its KeepAlive event to
    /// simulate the stack detecting a bad keep-alive.
    /// </summary>
    private (OpcUaClientSourceAdapter Adapter, ISession Session, IOpcUaReconnectHandlerWrapper Wrapper)
        CreateAdapterWithSubstitutedReconnectWrapper(string instanceId = "opcua-test")
    {
        var fakeSession = Substitute.For<ISession>();
        fakeSession.CloseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((StatusCode)StatusCodes.Good));

        var establisher = Substitute.For<IOpcUaClientConnectionEstablisher>();
        establisher.EstablishAsync(
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fakeSession));

        var factory = Substitute.For<IOpcUaClientSubscriptionFactory>();
        factory.CreateSubscriptionsAsync(
            Arg.Any<ISession>(),
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Subscription>>(System.Array.Empty<Subscription>()));

        var wrapper = Substitute.For<IOpcUaReconnectHandlerWrapper>();
        wrapper.BeginReconnectAsync(Arg.Any<ISession>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _adapter = new OpcUaClientSourceAdapter(
            instanceId,
            NullLogger.Instance,
            establisher,
            factory,
            dispatcherFactory: null,
            reconnectWrapperFactory: _ => wrapper);
        return (_adapter, fakeSession, wrapper);
    }

    [Fact]
    public async Task BadKeepAlive_TransitionsAdapterRunningToDegraded()
    {
        // PR 6a — bad keep-alive on the session triggers the coordinator,
        // which raises StateChanged(EnteredReconnect=true). The adapter
        // transitions Running → Degraded so health probes surface the
        // recovering-but-not-yet-failed state.
        var (adapter, session, wrapper) = CreateAdapterWithSubstitutedReconnectWrapper();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Running);

        var badKeepAlive = new KeepAliveEventArgs(
            new ServiceResult(StatusCodes.BadCommunicationError),
            ServerState.Unknown,
            DateTime.UtcNow);
        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, badKeepAlive);

        adapter.State.Should().Be(AdapterState.Degraded);
        await wrapper.Received(1).BeginReconnectAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransferReconnect_TransitionsBackToRunning_AndUpdatesSession()
    {
        // PR 6a — successful Transfer reconnect transitions adapter back
        // to Running and replaces _session with the wrapper-supplied
        // session. The dispatcher SURVIVES the reconnect (locked
        // invariant #4 in OpcUaReconnectCoordinator).
        var (adapter, session, wrapper) = CreateAdapterWithSubstitutedReconnectWrapper();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var badKeepAlive = new KeepAliveEventArgs(
            new ServiceResult(StatusCodes.BadCommunicationError),
            ServerState.Unknown,
            DateTime.UtcNow);
        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, badKeepAlive);
        adapter.State.Should().Be(AdapterState.Degraded);

        var newSession = Substitute.For<ISession>();
        newSession.Subscriptions.Returns(System.Array.Empty<Subscription>());
        wrapper.ReconnectCompleted += Raise.Event<Action<ReconnectResult>>(new ReconnectResult
        {
            Mode = ReconnectMode.Transfer,
            NewSession = newSession,
        });

        adapter.State.Should().Be(AdapterState.Running);
        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.LastError.Should().BeNull();
        health.LastSuccessAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FailedReconnect_TransitionsAdapterToFailedWithLastError()
    {
        // PR 6a — terminal reconnect failure (retry budget exhausted)
        // transitions the adapter to Failed and surfaces a structured
        // OPCUA.RECONNECT_EXHAUSTED error code so operators can
        // distinguish initial-connect failures from reconnect-budget
        // exhaustion.
        var (adapter, session, wrapper) = CreateAdapterWithSubstitutedReconnectWrapper();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var badKeepAlive = new KeepAliveEventArgs(
            new ServiceResult(StatusCodes.BadCommunicationError),
            ServerState.Unknown,
            DateTime.UtcNow);
        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, badKeepAlive);

        wrapper.ReconnectCompleted += Raise.Event<Action<ReconnectResult>>(new ReconnectResult
        {
            Mode = ReconnectMode.Failed,
            Error = new InvalidOperationException("retry budget exhausted"),
        });

        adapter.State.Should().Be(AdapterState.Failed);
        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.LastError.Should().NotBeNull();
        health.LastError!.Code.Should().Be("OPCUA.RECONNECT_EXHAUSTED");
    }

    [Fact]
    public async Task CheckHealthAsync_AfterStart_SurfacesAllReconnectMetrics()
    {
        // PR 6a — operator-facing metrics. Must include all 6 counters
        // (transfer, recreate, failed, currentlyReconnecting,
        // lastSuccessfulReconnectUtc per amendment #1, lastReconnectMode
        // per amendment #2) so the health rollup shape stays stable
        // regardless of reconnect history.
        var (adapter, _, _) = CreateAdapterWithSubstitutedReconnectWrapper();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        health.Metrics.Should().NotBeNull();
        var metrics = health.Metrics!;
        metrics.Should().ContainKey("reconnectsViaTransfer").WhoseValue.Should().Be(0L);
        metrics.Should().ContainKey("reconnectsViaRecreate").WhoseValue.Should().Be(0L);
        metrics.Should().ContainKey("reconnectsFailed").WhoseValue.Should().Be(0L);
        metrics.Should().ContainKey("currentlyReconnecting").WhoseValue.Should().Be(false);
        metrics.Should().ContainKey("lastSuccessfulReconnectUtc")
            .WhoseValue.Should().Be("never", "no successful reconnect yet → human-readable sentinel");
        metrics.Should().ContainKey("lastReconnectMode").WhoseValue.Should().Be("Unknown");
    }

    // ─── ReconfigureAsync override (PR 6b) ────────────────────────────

    /// <summary>
    /// Build an adapter with substituted reconnect wrapper AND
    /// reconfigure executor — the latter lets adapter tests verify the
    /// integration shell (single-flight guards, metric counters,
    /// FastDataChangeCallback re-wire) without exercising the heavy
    /// Subscription mutation machinery (covered at the executor
    /// pre-validation test + PR 7 integration tests).
    /// </summary>
    private (OpcUaClientSourceAdapter Adapter,
             ISession Session,
             IOpcUaReconnectHandlerWrapper ReconnectWrapper,
             IOpcUaReconfigureExecutor Executor)
        CreateAdapterWithSubstitutedReconfigureExecutor(string instanceId = "opcua-test")
    {
        var fakeSession = Substitute.For<ISession>();
        fakeSession.CloseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((StatusCode)StatusCodes.Good));

        var establisher = Substitute.For<IOpcUaClientConnectionEstablisher>();
        establisher.EstablishAsync(
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fakeSession));

        var factory = Substitute.For<IOpcUaClientSubscriptionFactory>();
        factory.CreateSubscriptionsAsync(
            Arg.Any<ISession>(),
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Subscription>>(System.Array.Empty<Subscription>()));

        var reconnectWrapper = Substitute.For<IOpcUaReconnectHandlerWrapper>();
        reconnectWrapper.BeginReconnectAsync(Arg.Any<ISession>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var executor = Substitute.For<IOpcUaReconfigureExecutor>();
        executor.ApplyAsync(
            Arg.Any<ISession>(),
            Arg.Any<IReadOnlyList<Subscription>>(),
            Arg.Any<OpcUaMonitoredItemDiffResult>(),
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OpcUaReconfigureExecutionResult
            {
                FinalSubscriptions = System.Array.Empty<Subscription>(),
                NewSubscriptions = System.Array.Empty<Subscription>(),
                RemovedSubscriptions = System.Array.Empty<Subscription>(),
                ItemsAdded = 0,
                ItemsRemoved = 0,
                ItemsModified = 0,
            }));

        _adapter = new OpcUaClientSourceAdapter(
            instanceId,
            NullLogger.Instance,
            establisher,
            factory,
            dispatcherFactory: null,
            reconnectWrapperFactory: _ => reconnectWrapper,
            reconfigureExecutorFactory: _ => executor);
        return (_adapter, fakeSession, reconnectWrapper, executor);
    }

    [Fact]
    public async Task ReconfigureAsync_NotRunning_ReturnsSpecificError()
    {
        // Amendment (user lock 2026-05-29): hot reconfigure requires a
        // live session. Adapter in Created / Initialized / Stopped /
        // Failed has no live session to mutate — must surface a
        // dedicated error code so the management API renders a clear
        // remediation rather than a generic null-ref.
        var (adapter, _, _, _) = CreateAdapterWithSubstitutedReconfigureExecutor();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Initialized);

        var act = () => adapter.ReconfigureAsync(ValidConfig(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OPCUA.RECONFIGURE_NOT_RUNNING*");
    }

    [Fact]
    public async Task ReconfigureAsync_Idempotent_DoesNotInvokeExecutor()
    {
        // Amendment #5 (user lock 2026-05-29) — same config in/out:
        // executor must NOT be called, success returned, counters tick
        // with changeCount=0.
        var (adapter, _, _, executor) = CreateAdapterWithSubstitutedReconfigureExecutor();
        var config = ValidConfig() with
        {
            MonitoredItems = new[]
            {
                new MonitoredItemConfig { NodeId = "ns=2;i=1", DisplayName = "T1" },
                new MonitoredItemConfig { NodeId = "ns=2;i=2", DisplayName = "T2" },
            },
        };
        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        await adapter.ReconfigureAsync(config, CancellationToken.None);

        await executor.DidNotReceiveWithAnyArgs().ApplyAsync(
            default!, default!, default!, default!, default);
        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.Metrics!["reconfigureCount"].Should().Be(1L);
        health.Metrics["lastReconfigureChangeCount"].Should().Be(0);
    }

    [Fact]
    public async Task ReconfigureAsync_HotApplicableChange_InvokesExecutorAndUpdatesConfig()
    {
        var (adapter, _, _, executor) = CreateAdapterWithSubstitutedReconfigureExecutor();
        var original = ValidConfig() with
        {
            MonitoredItems = new[]
            {
                new MonitoredItemConfig { NodeId = "ns=2;i=1", DisplayName = "T1" },
            },
        };
        await adapter.InitializeAsync(original, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var newConfig = ValidConfig() with
        {
            MonitoredItems = new[]
            {
                new MonitoredItemConfig { NodeId = "ns=2;i=1", DisplayName = "T1" },
                new MonitoredItemConfig { NodeId = "ns=2;i=2", DisplayName = "T2" },
            },
        };
        await adapter.ReconfigureAsync(newConfig, CancellationToken.None);

        // Executor was invoked exactly once with a diff that has 1 Added.
        await executor.Received(1).ApplyAsync(
            Arg.Any<ISession>(),
            Arg.Any<IReadOnlyList<Subscription>>(),
            Arg.Is<OpcUaMonitoredItemDiffResult>(d => d.Added.Count == 1 && d.Removed.Count == 0),
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<CancellationToken>());

        adapter.State.Should().Be(AdapterState.Running);
        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.Metrics!["reconfigureCount"].Should().Be(1L);
        health.Metrics["lastReconfigureChangeCount"].Should().Be(1);
    }

    [Fact]
    public async Task ReconfigureAsync_WhileReconnecting_ReturnsRetryFriendlyError()
    {
        // Amendment #4 (user lock 2026-05-29) — when the coordinator is
        // currently reconnecting, hot reconfigure must reject with
        // OPCUA.RECONFIGURE_WHILE_RECONNECTING + retry-friendly metadata
        // so the management API can surface a friendly "retry in ~2s" UX.
        var (adapter, session, reconnectWrapper, _) = CreateAdapterWithSubstitutedReconfigureExecutor();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // Trigger a bad keep-alive so coordinator.CurrentlyReconnecting=true.
        var badKeepAlive = new KeepAliveEventArgs(
            new ServiceResult(StatusCodes.BadCommunicationError),
            ServerState.Unknown,
            DateTime.UtcNow);
        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, badKeepAlive);
        adapter.State.Should().Be(AdapterState.Degraded);

        var act = () => adapter.ReconfigureAsync(ValidConfig(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OPCUA.RECONFIGURE_WHILE_RECONNECTING*");
        ex.Which.Message.Should().Contain("retryable=true");
        ex.Which.Message.Should().Contain("suggestedBackoffMs");
    }

    [Fact]
    public async Task ReconfigureAsync_RestartRequired_FallsThroughToStopInitStart()
    {
        // Endpoint URL change cannot be hot-applied — the adapter must
        // fall through to the safe Stop+Initialize+Start sequence, NOT
        // call the hot executor.
        var (adapter, _, _, executor) = CreateAdapterWithSubstitutedReconfigureExecutor();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var newConfig = ValidConfig() with
        {
            EndpointUrl = "opc.tcp://different-host.pilot.local:4840",
        };
        await adapter.ReconfigureAsync(newConfig, CancellationToken.None);

        // Hot executor must NOT have been called.
        await executor.DidNotReceiveWithAnyArgs().ApplyAsync(
            default!, default!, default!, default!, default);
        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task CheckHealthAsync_SurfacesAllReconfigureMetrics()
    {
        // Amendment #6 (user lock 2026-05-29) — currentlyReconfiguring
        // + counters surfaced through AdapterHealth.Metrics.
        var (adapter, _, _, _) = CreateAdapterWithSubstitutedReconfigureExecutor();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        health.Metrics.Should().NotBeNull();
        var metrics = health.Metrics!;
        metrics.Should().ContainKey("currentlyReconfiguring").WhoseValue.Should().Be(false);
        metrics.Should().ContainKey("reconfigureCount").WhoseValue.Should().Be(0L);
        metrics.Should().ContainKey("lastReconfigureUtc")
            .WhoseValue.Should().Be("never", "no reconfigure has run yet → sentinel");
        metrics.Should().ContainKey("lastReconfigureChangeCount").WhoseValue.Should().Be(0);
    }

    [Fact]
    public async Task ReconfigureAsync_WrongConfigType_Throws()
    {
        var (adapter, _, _, _) = CreateAdapterWithSubstitutedReconfigureExecutor();
        await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var stub = new StubSourceConfiguration { InstanceId = "opcua-test", ProtocolName = "stub", DeviceId = "stub" };
        var act = () => adapter.ReconfigureAsync(stub, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OPCUA.RECONFIGURE_CONFIG_WRONG_TYPE*");
    }

    /// <summary>Stub config type used to exercise wrong-type validation paths.</summary>
    private sealed record StubSourceConfiguration : SourceConfiguration;
}
