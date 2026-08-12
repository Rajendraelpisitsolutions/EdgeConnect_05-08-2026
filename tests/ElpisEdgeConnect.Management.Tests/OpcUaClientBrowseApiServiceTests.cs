// ============================================================================
// Tests: OpcUaClientBrowseApiService — pin the PR 7c-2 browse-side
//        probe contract (license gate, single-flight per endpoint URL,
//        budget, malformed config rejection, status-code mapping).
//
//        The protocol-side OpcUaClientBrowseService (Sources.OpcUaClient,
//        PR 5) is substituted via the ITagBrowseService seam so these
//        tests verify the Management-layer orchestration without needing
//        a real OPC stack endpoint.
// Reference: PR 7c-2 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Browse;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Management.Api;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class OpcUaClientBrowseApiServiceTests
{
    private const string SampleEndpointUrl = "opc.tcp://localhost:4840";

    // ─── Happy path ───────────────────────────────────────────────────

    [Fact]
    public async Task BrowseAsync_ProtocolReturnsResult_ReturnsSuccessWithTreeAttached()
    {
        var tree = SampleBrowseResult();
        var browseService = new RecordingBrowseService(result: tree);
        var service = MakeService(() => browseService);

        var outcome = await service.BrowseAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientBrowseStatus.Success);
        outcome.Result.Success.Should().BeTrue();
        outcome.Result.Result.Should().BeSameAs(tree,
            "the API service must pass through the protocol-side BrowseResult unchanged");
        browseService.BrowseCallCount.Should().Be(1);
    }

    // ─── License gate ─────────────────────────────────────────────────

    [Fact]
    public async Task BrowseAsync_LicenseDisabled_ReturnsLicenseDisabled_DoesNotInvokeProtocolService()
    {
        var browseService = new RecordingBrowseService(result: SampleBrowseResult());
        var service = MakeService(() => browseService, isModuleEnabled: _ => false);

        var outcome = await service.BrowseAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientBrowseStatus.LicenseDisabled);
        outcome.Result.ErrorCode.Should().Be("OPCUA.PROBE_LICENSE_DISABLED");
        browseService.BrowseCallCount.Should().Be(0,
            "gate must short-circuit before the protocol service runs");
    }

    [Fact]
    public void BuildLicenseGate_NullManagerOrCurrentNull_Permissive()
    {
        OpcUaClientBrowseApiService.BuildLicenseGate(license: null)("source-opcua-client").Should().BeTrue();
        OpcUaClientBrowseApiService.BuildLicenseGate(new NoLicenseLoadedManager())("source-opcua-client").Should().BeTrue();
    }

    // ─── Config validation ────────────────────────────────────────────

    [Fact]
    public async Task BrowseAsync_MalformedConfigJson_ReturnsConfigInvalid()
    {
        var browseService = new RecordingBrowseService(result: SampleBrowseResult());
        var service = MakeService(() => browseService);

        var outcome = await service.BrowseAsync(new OpcUaClientBrowseRequest
        {
            SourceConfigJson = "{not valid json",
        }, CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientBrowseStatus.ConfigInvalid);
        outcome.Result.ErrorCode.Should().Be("OPCUA.BROWSE_CONFIG_INVALID");
        browseService.BrowseCallCount.Should().Be(0);
    }

    [Fact]
    public async Task BrowseAsync_ValidJsonButMissingEndpointUrl_ReturnsConfigIncomplete()
    {
        var browseService = new RecordingBrowseService(result: SampleBrowseResult());
        var service = MakeService(() => browseService);

        var outcome = await service.BrowseAsync(new OpcUaClientBrowseRequest
        {
            SourceConfigJson = "{\"protocolName\":\"opcua-client\"}",
        }, CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientBrowseStatus.ConfigInvalid);
        outcome.Result.ErrorCode.Should().Be("OPCUA.BROWSE_CONFIG_INCOMPLETE");
    }

    // ─── Single-flight ────────────────────────────────────────────────

    [Fact]
    public async Task BrowseAsync_SecondBrowseAgainstSameEndpoint_ReturnsBusy()
    {
        var gate = new TaskCompletionSource();
        var slow = new RecordingBrowseService(result: SampleBrowseResult(), gate: gate.Task);
        var fast = new RecordingBrowseService(result: SampleBrowseResult());
        var queue = new Queue<ITagBrowseService>();
        queue.Enqueue(slow);
        queue.Enqueue(fast);
        var service = MakeService(() => queue.Dequeue());

        var first = service.BrowseAsync(MakeRequest(), CancellationToken.None);
        await Task.Delay(50);
        var second = await service.BrowseAsync(MakeRequest(), CancellationToken.None);

        second.Status.Should().Be(OpcUaClientBrowseStatus.Busy);
        second.Result.ErrorCode.Should().Be("OPCUA.BROWSE_IN_FLIGHT");

        gate.SetResult();
        (await first).Status.Should().Be(OpcUaClientBrowseStatus.Success);
    }

    [Fact]
    public async Task BrowseAsync_DifferentEndpoints_BothSucceed()
    {
        var a = new RecordingBrowseService(result: SampleBrowseResult());
        var b = new RecordingBrowseService(result: SampleBrowseResult());
        var queue = new Queue<ITagBrowseService>();
        queue.Enqueue(a);
        queue.Enqueue(b);
        var service = MakeService(() => queue.Dequeue());

        var ra = await service.BrowseAsync(MakeRequest("opc.tcp://a.local:4840"), CancellationToken.None);
        var rb = await service.BrowseAsync(MakeRequest("opc.tcp://b.local:4840"), CancellationToken.None);

        ra.Status.Should().Be(OpcUaClientBrowseStatus.Success);
        rb.Status.Should().Be(OpcUaClientBrowseStatus.Success);
    }

    // ─── Timeout ──────────────────────────────────────────────────────

    [Fact]
    public async Task BrowseAsync_ProtocolHangsBeyondBudget_ReturnsTimeout()
    {
        var hang = new TaskCompletionSource();
        var browseService = new RecordingBrowseService(result: SampleBrowseResult(), gate: hang.Task);
        var service = MakeService(() => browseService, probeBudget: TimeSpan.FromMilliseconds(100));

        var outcome = await service.BrowseAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientBrowseStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("OPCUA.BROWSE_TIMEOUT");
    }

    // ─── Protocol-side error propagation ──────────────────────────────

    [Fact]
    public async Task BrowseAsync_ProtocolThrowsClassifiedError_PassesCodeThrough()
    {
        var browseService = new RecordingBrowseService(
            throws: new InvalidOperationException("OPCUA.BROWSE_CONFIG_INCOMPLETE: ApplicationUri missing"));
        var service = MakeService(() => browseService);

        var outcome = await service.BrowseAsync(MakeRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OpcUaClientBrowseStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("OPCUA.BROWSE_CONFIG_INCOMPLETE");
    }

    // ─── ProbeId ──────────────────────────────────────────────────────

    [Fact]
    public async Task BrowseAsync_ResultCarriesProbeId()
    {
        var service = MakeService(() => new RecordingBrowseService(result: SampleBrowseResult()));

        var outcome = await service.BrowseAsync(MakeRequest(), CancellationToken.None);

        outcome.Result.ProbeId.Should().StartWith("probe-");
    }

    // ─── Status-code mapping ──────────────────────────────────────────

    [Theory]
    [InlineData(OpcUaClientBrowseStatus.Success, 200)]
    [InlineData(OpcUaClientBrowseStatus.Failure, 200)]
    [InlineData(OpcUaClientBrowseStatus.ConfigInvalid, 400)]
    [InlineData(OpcUaClientBrowseStatus.LicenseDisabled, 403)]
    [InlineData(OpcUaClientBrowseStatus.Busy, 409)]
    public void StatusCodeMapping_MatchesLockedTable(OpcUaClientBrowseStatus status, int expected)
    {
        OpcUaClientBrowseStatusMapping.StatusCodeFor(status).Should().Be(expected);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static OpcUaClientBrowseApiService MakeService(
        Func<ITagBrowseService> browseServiceFactory,
        Func<string, bool>? isModuleEnabled = null,
        TimeSpan? probeBudget = null) =>
        new(
            browseServiceFactory: browseServiceFactory,
            isModuleEnabled: isModuleEnabled ?? (_ => true),
            loggerFactory: NullLoggerFactory.Instance,
            probeBudget: probeBudget ?? TimeSpan.FromSeconds(5));

    private static OpcUaClientBrowseRequest MakeRequest(string endpoint = SampleEndpointUrl) => new()
    {
        SourceConfigJson = JsonSerializer.Serialize(new { endpointUrl = endpoint, protocolName = "opcua-client" }),
        MaxDepth = 1,
        MaxNodes = 100,
    };

    private static BrowseResult SampleBrowseResult() => new()
    {
        Root = new BrowseNode
        {
            NodeId = "ns=0;i=85",
            DisplayName = "Objects",
            Kind = BrowseNodeKind.Folder,
        },
        Truncated = false,
    };

    private sealed class RecordingBrowseService : ITagBrowseService
    {
        private readonly BrowseResult? _result;
        private readonly Exception? _throws;
        private readonly Task? _gate;

        public RecordingBrowseService(BrowseResult? result = null, Exception? throws = null, Task? gate = null)
        {
            _result = result;
            _throws = throws;
            _gate = gate;
        }

        public int BrowseCallCount { get; private set; }

        public async Task<BrowseResult> BrowseAsync(BrowseRequest request, CancellationToken ct)
        {
            BrowseCallCount++;
            if (_gate is not null) await _gate.WaitAsync(ct).ConfigureAwait(false);
            if (_throws is not null) throw _throws;
            return _result ?? throw new InvalidOperationException("RecordingBrowseService misconfigured");
        }
    }

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
