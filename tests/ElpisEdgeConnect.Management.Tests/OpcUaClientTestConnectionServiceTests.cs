// ============================================================================
// Tests: OpcUaClientTestConnectionService — pin the PR 7c-2 probe
//        contract (license gate, single-flight, budget, ProbeId, no
//        side-effects beyond the adapter's TestConnectAsync surface).
//
//        Mirrors the MqttTestConnectionService test shape: a recording
//        prober substitutes for the throwaway adapter so the service
//        contract is testable without an OPC stack endpoint.
// Reference: PR 7c-2 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class OpcUaClientTestConnectionServiceTests
{
    // ─── Happy path ───────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_AdapterReportsSuccess_ReturnsSuccessOutcome()
    {
        var prober = new RecordingProber(
            result: new TestConnectResult
            {
                Success = true,
                EndpointUrl = "opc.tcp://localhost:4840",
                ServerState = "Running",
                Message = "ok",
            });
        var service = MakeService(_ => prober);

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientTestConnectionStatus.Success);
        outcome.Result.Success.Should().BeTrue();
        outcome.Result.ServerState.Should().Be("Running");
        outcome.Result.EndpointUrl.Should().Be("opc.tcp://localhost:4840");
        prober.TestConnectCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ProbeAsync_AdapterReportsFailure_ReturnsFailureOutcomeWithRemediationHint()
    {
        var prober = new RecordingProber(
            result: new TestConnectResult
            {
                Success = false,
                EndpointUrl = "opc.tcp://localhost:4840",
                Message = "Server certificate untrusted.",
                ErrorCode = "OPCUA.SERVER_CERT_UNTRUSTED",
            });
        var service = MakeService(_ => prober);

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientTestConnectionStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("OPCUA.SERVER_CERT_UNTRUSTED");
        outcome.Result.RemediationHint.Should().NotBeNullOrEmpty(
            "every classified error code must carry an operator-actionable hint");
    }

    // ─── License gate ─────────────────────────────────────────────────

    [Fact]
    public void BuildLicenseGate_NullManager_Permissive()
    {
        var gate = OpcUaClientTestConnectionService.BuildLicenseGate(license: null);
        gate("source-opcua-client").Should().BeTrue();
    }

    [Fact]
    public void BuildLicenseGate_LicenseManagerWithoutLoadedLicense_Permissive()
    {
        // REGRESSION: an ILicenseManager IS in DI but no license file
        // loaded — Current is null. Must remain permissive so dev runs
        // don't get spurious LICENSE.MODULE_DISABLED on first Test
        // Connection click. Mirrors MqttTestConnectionService's gate.
        var license = new NoLicenseLoadedManager();
        var gate = OpcUaClientTestConnectionService.BuildLicenseGate(license);
        gate("source-opcua-client").Should().BeTrue("dev / no-license-loaded mode must be permissive");
    }

    [Fact]
    public async Task ProbeAsync_LicenseDisabled_ReturnsLicenseDisabled_DoesNotInvokeProber()
    {
        var prober = new RecordingProber(result: SuccessResult());
        var service = MakeService(_ => prober, isModuleEnabled: _ => false);

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientTestConnectionStatus.LicenseDisabled);
        outcome.Result.ErrorCode.Should().Be("OPCUA.PROBE_LICENSE_DISABLED");
        prober.TestConnectCallCount.Should().Be(0,
            "the gate must short-circuit before the prober factory runs");
    }

    // ─── Config validation ────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_MissingEndpointUrl_ReturnsConfigInvalid()
    {
        var prober = new RecordingProber(result: SuccessResult());
        var service = MakeService(_ => prober);

        var outcome = await service.ProbeAsync(new OpcUaClientTestConnectionRequest { EndpointUrl = null },
            CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientTestConnectionStatus.ConfigInvalid);
        outcome.Result.ErrorCode.Should().Be("OPCUA.PROBE_CONFIG_INVALID");
        prober.TestConnectCallCount.Should().Be(0);
    }

    // ─── Single-flight ────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_SecondProbeAgainstSameEndpoint_ReturnsBusy()
    {
        var slowGate = new TaskCompletionSource();
        var slowProber = new RecordingProber(result: SuccessResult(), gate: slowGate.Task);
        var fastProber = new RecordingProber(result: SuccessResult());

        var probers = new Queue<IOpcUaClientTestConnectionProber>();
        probers.Enqueue(slowProber);
        probers.Enqueue(fastProber);
        var service = MakeService(_ => probers.Dequeue());

        var first = service.ProbeAsync(MakeRequest(), CancellationToken.None);
        await Task.Delay(50);  // let the first acquire the lease

        var second = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        second.Status.Should().Be(OpcUaClientTestConnectionStatus.Busy);
        second.Result.ErrorCode.Should().Be("OPCUA.PROBE_BUSY");

        slowGate.SetResult();
        (await first).Status.Should().Be(OpcUaClientTestConnectionStatus.Success);
    }

    [Fact]
    public async Task ProbeAsync_DifferentEndpoints_BothSucceedInParallel()
    {
        var proberA = new RecordingProber(result: SuccessResult());
        var proberB = new RecordingProber(result: SuccessResult());
        var queue = new Queue<IOpcUaClientTestConnectionProber>();
        queue.Enqueue(proberA);
        queue.Enqueue(proberB);
        var service = MakeService(_ => queue.Dequeue());

        var a = await service.ProbeAsync(MakeRequest("opc.tcp://a.local:4840"), CancellationToken.None);
        var b = await service.ProbeAsync(MakeRequest("opc.tcp://b.local:4840"), CancellationToken.None);

        a.Status.Should().Be(OpcUaClientTestConnectionStatus.Success);
        b.Status.Should().Be(OpcUaClientTestConnectionStatus.Success);
    }

    // ─── Timeout ──────────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_ProberHangsBeyondBudget_ReturnsTimeout()
    {
        var hangForever = new TaskCompletionSource();
        var prober = new RecordingProber(result: SuccessResult(), gate: hangForever.Task);
        var service = MakeService(_ => prober, probeBudget: TimeSpan.FromMilliseconds(100));

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientTestConnectionStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("OPCUA.PROBE_TIMEOUT");
    }

    // ─── ProbeId ──────────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_ResultCarriesProbeId()
    {
        var service = MakeService(_ => new RecordingProber(result: SuccessResult()));

        var outcome = await service.ProbeAsync(MakeRequest(), CancellationToken.None);

        outcome.Result.ProbeId.Should().StartWith("probe-");
        outcome.Result.ProbeId.Length.Should().BeGreaterThan(6);
    }

    // ─── Status-code mapping ──────────────────────────────────────────

    [Theory]
    [InlineData(OpcUaClientTestConnectionStatus.Success, 200)]
    [InlineData(OpcUaClientTestConnectionStatus.Failure, 200)]   // body has Success=false
    [InlineData(OpcUaClientTestConnectionStatus.ConfigInvalid, 400)]
    [InlineData(OpcUaClientTestConnectionStatus.LicenseDisabled, 403)]
    [InlineData(OpcUaClientTestConnectionStatus.Busy, 409)]
    public void StatusCodeMapping_MatchesLockedTable(OpcUaClientTestConnectionStatus status, int expected)
    {
        OpcUaClientTestConnectionStatusMapping.StatusCodeFor(status).Should().Be(expected);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static OpcUaClientTestConnectionService MakeService(
        Func<string, IOpcUaClientTestConnectionProber> proberFactory,
        Func<string, bool>? isModuleEnabled = null,
        TimeSpan? probeBudget = null) =>
        new(
            proberFactory: proberFactory,
            isModuleEnabled: isModuleEnabled ?? (_ => true),
            loggerFactory: NullLoggerFactory.Instance,
            probeBudget: probeBudget ?? TimeSpan.FromSeconds(5));

    private static OpcUaClientTestConnectionRequest MakeRequest(string endpoint = "opc.tcp://localhost:4840") => new()
    {
        EndpointUrl = endpoint,
        SecurityMode = OpcUaSecurityMode.None,
        AuthMode = OpcUaAuthMode.Anonymous,
    };

    private static TestConnectResult SuccessResult() => new()
    {
        Success = true,
        EndpointUrl = "opc.tcp://localhost:4840",
        ServerState = "Running",
        Message = "ok",
    };

    /// <summary>Fake prober that records call count + optionally hangs on a gate task.</summary>
    private sealed class RecordingProber : IOpcUaClientTestConnectionProber
    {
        private readonly TestConnectResult _result;
        private readonly Task? _gate;

        public RecordingProber(TestConnectResult result, Task? gate = null)
        {
            _result = result;
            _gate = gate;
        }

        public int TestConnectCallCount { get; private set; }

        public async Task<TestConnectResult> TestConnectAsync(
            OpcUaClientSourceConfiguration config, CancellationToken ct)
        {
            TestConnectCallCount++;
            if (_gate is not null)
            {
                await _gate.WaitAsync(ct).ConfigureAwait(false);
            }
            return _result;
        }
    }

    /// <summary>Fake ILicenseManager — reports no license loaded (Current is null).</summary>
    private sealed class NoLicenseLoadedManager : ILicenseManager
    {
        public LicenseInfo? Current => null;
        public LicenseStatus Status => default;
        public TimeSpan? RemainingGrace => null;
        public event EventHandler<LicenseWarning>? WarningRaised;
        public Task LoadFromFileAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadAsync(System.IO.Stream licenseJson, CancellationToken cancellationToken) => Task.CompletedTask;
        public bool IsModuleEnabled(string moduleKey) => false;
        public bool IsFeatureEnabled(string featureKey) => false;
        public LicenseEvaluationResult CheckInstanceLimit(string moduleKey, int proposedCount) =>
            throw new NotImplementedException();
        public void Tick() { _ = WarningRaised; }
        public void Unload() { }
    }
}
