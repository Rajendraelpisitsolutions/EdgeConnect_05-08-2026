// ============================================================================
// Tests: MqttTestConnectionService — pins the MQTT Test Connection
//        probe behaviour (M.2b.6). Key invariants:
//
//          * Locked H: the service NEVER calls PublishAsync. A counter
//            on the fake IMqttClient asserts the call count remains zero
//            across every probe outcome — happy path AND failure paths.
//          * License gate: when the `sink-mqtt` module is disabled, the
//            probe returns LicenseDisabled WITHOUT touching the client
//            factory.
//          * Single-flight per host:port: a second probe against the
//            same broker while the first is in flight returns Busy.
//          * Timeout: the probe budget bounds the connect call; expired
//            budget surfaces MQTT.PROBE_TIMEOUT.
//          * Connect failures map to structured error codes.
//          * ProbeId on every result for log correlation.
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
//            docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v2.md §3 (Locked H)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Management.Api;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Client;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class MqttTestConnectionServiceTests
{
    // ─── Locked H — no Publish, ever ────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_HappyPath_NeverCallsPublishAsync()
    {
        // Locked H — the literal architectural contract. The probe is
        // verification, not a write. If this test ever fires, the probe
        // is polluting customer brokers with one-off test topics.
        var fake = new RecordingMqttClient(connectResult: MqttClientConnectResultCode.Success);
        var service = MakeService(() => fake);

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(MqttTestConnectionStatus.Success);
        outcome.Result.Success.Should().BeTrue();
        fake.PublishCallCount.Should().Be(0, "Locked H — the probe must never publish");
        fake.ConnectCallCount.Should().Be(1);
        fake.DisconnectCallCount.Should().Be(1, "the probe must disconnect cleanly after CONNECT");
    }

    [Fact]
    public async Task ProbeAsync_AuthFailure_NeverCallsPublishAsync()
    {
        // Even on failure paths the no-Publish contract holds. A buggy
        // refactor that "tries publishing as a fallback verification"
        // would fail this test.
        var fake = new RecordingMqttClient(connectResult: MqttClientConnectResultCode.BadUserNameOrPassword);
        var service = MakeService(() => fake);

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(MqttTestConnectionStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("MQTT.PROBE_AUTH_FAILED");
        fake.PublishCallCount.Should().Be(0, "Locked H — no Publish even on auth failure");
    }

    [Fact]
    public async Task ProbeAsync_ConnectThrows_NeverCallsPublishAsync()
    {
        var fake = new RecordingMqttClient(connectThrows: new InvalidOperationException("socket refused"));
        var service = MakeService(() => fake);

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(MqttTestConnectionStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("MQTT.PROBE_REFUSED");
        outcome.Result.ErrorMessage.Should().Contain("socket refused");
        fake.PublishCallCount.Should().Be(0);
    }

    // ─── License gate ───────────────────────────────────────────────────

    [Fact]
    public void BuildLicenseGate_NullManager_Permissive()
    {
        // When no ILicenseManager is registered in DI, the gate must
        // allow everything (matches the existing source-wizard semantic).
        var gate = MqttTestConnectionService.BuildLicenseGate(license: null);
        gate("sink-mqtt").Should().BeTrue();
    }

    [Fact]
    public void BuildLicenseGate_LicenseManagerWithoutLoadedLicense_Permissive()
    {
        // REGRESSION: an ILicenseManager IS in DI (Host always registers
        // one) but no license file has been loaded — `Current` is null.
        // Earlier bug returned MQTT.PROBE_LICENSE_DISABLED on the first
        // Test Connection click after a fresh `dotnet run` of the Studio
        // (the most common dev workflow). Fix: BuildLicenseGate treats
        // Current==null as permissive, mirroring Focas2BrowseService.
        var license = new NoLicenseLoadedManager();
        var gate = MqttTestConnectionService.BuildLicenseGate(license);

        gate("sink-mqtt").Should().BeTrue("dev / no-license-loaded mode must be permissive");
    }

    /// <summary>
    /// Fake ILicenseManager that reports no license loaded (the dev-mode
    /// case the M.2b.6 license-gate fix protects).
    /// </summary>
    private sealed class NoLicenseLoadedManager : ILicenseManager
    {
        public LicenseInfo? Current => null;
        public LicenseStatus Status => default;
        public TimeSpan? RemainingGrace => null;
        public event EventHandler<LicenseWarning>? WarningRaised;
        public Task LoadFromFileAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadAsync(System.IO.Stream licenseJson, CancellationToken cancellationToken) => Task.CompletedTask;
        public bool IsModuleEnabled(string moduleKey) => false;  // No license → no module enabled
        public bool IsFeatureEnabled(string featureKey) => false;
        public LicenseEvaluationResult CheckInstanceLimit(string moduleKey, int proposedCount) =>
            throw new NotImplementedException();
        public void Tick() { _ = WarningRaised; }
        public void Unload() { }
    }

    [Fact]
    public async Task ProbeAsync_LicenseDisabled_ReturnsLicenseDisabled_AndDoesNotTouchClient()
    {
        var fake = new RecordingMqttClient(connectResult: MqttClientConnectResultCode.Success);
        var service = MakeService(() => fake, isModuleEnabled: _ => false);

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(MqttTestConnectionStatus.LicenseDisabled);
        outcome.Result.ErrorCode.Should().Be("MQTT.PROBE_LICENSE_DISABLED");
        fake.ConnectCallCount.Should().Be(0, "the gate must short-circuit before the factory runs");
    }

    // ─── Single-flight ──────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_SecondProbeAgainstSameBroker_ReturnsBusy()
    {
        // Two probes against the same broker:port — the second sees the
        // first still holding the lease and returns Busy. Protects the
        // broker from a "click Test 5 times" hammer.
        var slowGate = new TaskCompletionSource();
        var slowClient = new RecordingMqttClient(
            connectResult: MqttClientConnectResultCode.Success,
            connectGate: slowGate.Task);
        var fastClient = new RecordingMqttClient(connectResult: MqttClientConnectResultCode.Success);

        var clients = new System.Collections.Generic.Queue<IMqttProbeClient>();
        clients.Enqueue(slowClient);
        clients.Enqueue(fastClient);

        var service = MakeService(() => clients.Dequeue());

        var first = service.ProbeAsync(MakeRequest(), CancellationToken.None);
        // Yield once so the first probe acquires the lease.
        await Task.Delay(50);

        var second = await service.ProbeAsync(MakeRequest(), CancellationToken.None);
        second.Status.Should().Be(MqttTestConnectionStatus.Busy);
        second.Result.ErrorCode.Should().Be("MQTT.PROBE_BUSY");

        // Let the first one finish.
        slowGate.SetResult();
        var firstOutcome = await first;
        firstOutcome.Status.Should().Be(MqttTestConnectionStatus.Success);
    }

    [Fact]
    public async Task ProbeAsync_DifferentBrokers_BothSucceed()
    {
        // Single-flight is keyed per host:port — two probes against
        // DIFFERENT brokers must both succeed in parallel.
        var clientA = new RecordingMqttClient(connectResult: MqttClientConnectResultCode.Success);
        var clientB = new RecordingMqttClient(connectResult: MqttClientConnectResultCode.Success);
        var clients = new System.Collections.Generic.Queue<IMqttProbeClient>();
        clients.Enqueue(clientA);
        clients.Enqueue(clientB);

        var service = MakeService(() => clients.Dequeue());

        var a = await service.ProbeAsync(MakeRequest("broker-a", 1883), CancellationToken.None);
        var b = await service.ProbeAsync(MakeRequest("broker-b", 1883), CancellationToken.None);

        a.Status.Should().Be(MqttTestConnectionStatus.Success);
        b.Status.Should().Be(MqttTestConnectionStatus.Success);
    }

    // ─── Timeout ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_ConnectExceedsProbeBudget_ReturnsTimeout()
    {
        // The fake's connect hangs indefinitely; the service's CTS cancels
        // it after the probe budget, surfacing MQTT.PROBE_TIMEOUT.
        var hangForever = new TaskCompletionSource();
        var fake = new RecordingMqttClient(
            connectResult: MqttClientConnectResultCode.Success,
            connectGate: hangForever.Task);

        var service = MakeService(() => fake, probeBudget: TimeSpan.FromMilliseconds(100));

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(MqttTestConnectionStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("MQTT.PROBE_TIMEOUT");
        outcome.Result.ErrorMessage.Should().Contain("did not respond");
    }

    // ─── ProbeId ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_ResultCarriesProbeId()
    {
        var fake = new RecordingMqttClient(connectResult: MqttClientConnectResultCode.Success);
        var service = MakeService(() => fake);

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Result.ProbeId.Should().StartWith("probe-", "ProbeId convention for log correlation");
        outcome.Result.ProbeId.Length.Should().BeGreaterThan(6);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static MqttTestConnectionService MakeService(
        Func<IMqttProbeClient> clientFactory,
        Func<string, bool>? isModuleEnabled = null,
        TimeSpan? probeBudget = null) =>
        new(
            clientFactory: clientFactory,
            isModuleEnabled: isModuleEnabled ?? (_ => true),
            loggerFactory: NullLoggerFactory.Instance,
            probeBudget: probeBudget ?? TimeSpan.FromSeconds(5));

    private static MqttTestConnectionRequest MakeRequest(string host = "localhost", int port = 1883) => new()
    {
        BrokerHost = host,
        BrokerPort = port,
    };

    // ─── Fake IMqttProbeClient ──────────────────────────────────────────

    /// <summary>
    /// Records calls to Connect / Disconnect / Publish. The Publish counter
    /// is the load-bearing assertion for Locked H — the production service
    /// must NEVER increment it.
    /// </summary>
    private sealed class RecordingMqttClient : IMqttProbeClient
    {
        private readonly MqttClientConnectResultCode _connectResult;
        private readonly Exception? _connectThrows;
        private readonly Task? _connectGate;

        public RecordingMqttClient(
            MqttClientConnectResultCode connectResult = MqttClientConnectResultCode.Success,
            Exception? connectThrows = null,
            Task? connectGate = null)
        {
            _connectResult = connectResult;
            _connectThrows = connectThrows;
            _connectGate = connectGate;
        }

        public int ConnectCallCount { get; private set; }
        public int DisconnectCallCount { get; private set; }
        public int PublishCallCount { get; private set; }

        public async Task<MqttProbeConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken)
        {
            ConnectCallCount++;
            if (_connectGate is not null)
            {
                await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (_connectThrows is not null) throw _connectThrows;
            return new MqttProbeConnectResult(_connectResult);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCallCount++;
            return Task.CompletedTask;
        }

        public Task PublishAsync(MqttApplicationMessage message, CancellationToken cancellationToken)
        {
            // The very fact this method was called is the test failure —
            // the counter increment is all this fake needs to do.
            PublishCallCount++;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
