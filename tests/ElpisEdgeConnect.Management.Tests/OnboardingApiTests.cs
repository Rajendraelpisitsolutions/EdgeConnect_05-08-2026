// ============================================================================
// Tests: OnboardingApi — pins POST /api/v1/onboarding/apply (ADR-0016 R6).
//
// Six tests cover the dispatch surface without standing up a
// WebApplicationFactory:
//   1. Post_HappyPath_AppliesBundledDraftAndReturnsApplyResult
//   2. Post_NullBody_Returns400
//   3. Post_MergerInvariantViolated_Returns400 (duplicate source id)
//   4. Post_RouteSourceMismatch_Returns400
//   5. Post_GatewayIdentityOverride_AppliesToCurrentGateway
//   6. Post_MultiSinkRoute_PreservesAllSinkReferences
//
// Mirrors the test-style established by SinksUpdateApiTests / RoutesUpdateApiTests.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Contracts.Config;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class OnboardingApiTests
{
    [Fact]
    public async Task Post_HappyPath_AppliesBundledDraftAndReturnsApplyResult()
    {
        var config = MakeConfig();
        var mgr = new FakeOnboardingConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new OnboardingApplyRequestDto
        {
            Source = MakeSource("modbus-line1"),
            Sink = MakeSink("mqtt-eremos"),
            Route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos"),
        };

        var result = await OnboardingApi.DispatchAsync(body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        var applied = GetJsonValue<ApplyResultDto>(result);
        applied.NewVersionId.Should().NotBe("v-1", "apply produces a fresh version id");
        applied.PreviousVersionId.Should().Be("v-1");
        mgr.AppliedDrafts.Should().HaveCount(1);
        // Bundled draft contains all three entities
        var draft = mgr.AppliedDrafts[0];
        draft.Sources.Should().HaveCount(1);
        draft.Sources[0].InstanceId.Should().Be("modbus-line1");
        draft.Sinks.Should().HaveCount(1);
        draft.Sinks[0].InstanceId.Should().Be("mqtt-eremos");
        draft.Routes.Should().HaveCount(1);
        draft.Routes[0].RouteId.Should().Be("route-line1");
    }

    [Fact]
    public async Task Post_NullBody_Returns400()
    {
        var mgr = new FakeOnboardingConfigManager(MakeConfig(), currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await OnboardingApi.DispatchAsync(null, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(400);
        mgr.AppliedDrafts.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_MergerInvariantViolated_Returns400_DuplicateSourceId()
    {
        var existing = MakeSource("modbus-1");
        var config = MakeConfig(sources: new[] { existing });
        var mgr = new FakeOnboardingConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new OnboardingApplyRequestDto
        {
            Source = MakeSource("modbus-1"), // duplicate
            Sink = MakeSink("mqtt-1"),
            Route = MakeRoute("route-1", "modbus-1", "mqtt-1"),
        };

        var result = await OnboardingApi.DispatchAsync(body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(400);
        mgr.AppliedDrafts.Should().BeEmpty("merger rejection must not produce any apply");
    }

    [Fact]
    public async Task Post_RouteSourceMismatch_Returns400()
    {
        var mgr = new FakeOnboardingConfigManager(MakeConfig(), currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new OnboardingApplyRequestDto
        {
            Source = MakeSource("modbus-actual"),
            Sink = MakeSink("mqtt-1"),
            Route = MakeRoute("route-1", "modbus-OTHER", "mqtt-1"), // wrong source id
        };

        var result = await OnboardingApi.DispatchAsync(body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(400);
        mgr.AppliedDrafts.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_GatewayIdentityOverride_AppliesToCurrentGateway()
    {
        var config = MakeConfig(gatewayId: "gw-old", gatewayName: "Old");
        var mgr = new FakeOnboardingConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new OnboardingApplyRequestDto
        {
            Source = MakeSource("modbus-1"),
            Sink = MakeSink("mqtt-1"),
            Route = MakeRoute("route-1", "modbus-1", "mqtt-1"),
            GatewayIdOverride = "gw-line1",
            GatewayNameOverride = "Line 1 Edge",
        };

        var result = await OnboardingApi.DispatchAsync(body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        mgr.AppliedDrafts[0].Gateway.GatewayId.Should().Be("gw-line1");
        mgr.AppliedDrafts[0].Gateway.GatewayName.Should().Be("Line 1 Edge");
    }

    [Fact]
    public async Task Post_MultiSinkRoute_PreservesAllSinkReferences()
    {
        var existingSink = MakeSink("mqtt-existing");
        var config = MakeConfig(sinks: new[] { existingSink });
        var mgr = new FakeOnboardingConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new OnboardingApplyRequestDto
        {
            Source = MakeSource("modbus-1"),
            Sink = MakeSink("opcua-new"),
            Route = new RouteConfig
            {
                RouteId = "route-1",
                Name = "route-1",
                SourceInstanceId = "modbus-1",
                SinkInstanceIds = new[] { "mqtt-existing", "opcua-new" },
            },
        };

        var result = await OnboardingApi.DispatchAsync(body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        mgr.AppliedDrafts[0].Routes[0].SinkInstanceIds
            .Should().BeEquivalentTo("mqtt-existing", "opcua-new");
    }

    // ═══ Helpers ═════════════════════════════════════════════════════════

    private static T GetJsonValue<T>(IResult result)
    {
        var type = result.GetType();
        var prop = type.GetProperty("Value");
        var raw = prop?.GetValue(result);
        return (T)raw!;
    }

    private static int GetStatusCode(IResult result)
    {
        var type = result.GetType();
        var prop = type.GetProperty("StatusCode");
        if (prop is null) return 0;
        var value = prop.GetValue(result);
        return value is int i ? i : value is null ? 0 : (int)value;
    }

    private static GatewayConfiguration MakeConfig(
        string gatewayId = "gw-test",
        string gatewayName = "Test",
        IReadOnlyList<SourceInstanceConfig>? sources = null,
        IReadOnlyList<SinkInstanceConfig>? sinks = null,
        IReadOnlyList<RouteConfig>? routes = null) => new()
    {
        Gateway = new GatewaySettings { GatewayId = gatewayId, GatewayName = gatewayName },
        Sources = sources ?? Array.Empty<SourceInstanceConfig>(),
        Sinks = sinks ?? Array.Empty<SinkInstanceConfig>(),
        Routes = routes ?? Array.Empty<RouteConfig>(),
    };

    private static SourceInstanceConfig MakeSource(string instanceId) => new()
    {
        InstanceId = instanceId,
        ProtocolName = "modbustcp",
        DeviceId = instanceId,
        DeviceName = instanceId,
        DeviceClass = "plc",
        Enabled = true,
        Polling = new PollingSettings { IntervalMs = 200 },
    };

    private static SinkInstanceConfig MakeSink(string instanceId, string protocolName = "mqtt") => new()
    {
        InstanceId = instanceId,
        ProtocolName = protocolName,
        Enabled = true,
    };

    private static RouteConfig MakeRoute(string routeId, string sourceId, string sinkId) => new()
    {
        RouteId = routeId,
        Name = routeId,
        SourceInstanceId = sourceId,
        SinkInstanceIds = new[] { sinkId },
        Enabled = true,
    };

    private sealed class FakeOnboardingConfigManager : IConfigurationManager
    {
        private GatewayConfiguration _current;
        private string _versionValue;
        private readonly Dictionary<string, GatewayConfiguration> _drafts = new(StringComparer.Ordinal);
        private int _draftCounter;
        private int _versionCounter;

        public FakeOnboardingConfigManager(GatewayConfiguration current, string currentVersion)
        {
            _current = current;
            _versionValue = currentVersion;
            CurrentVersionId = new ConfigurationVersionId(currentVersion);
        }

        public ConfigurationVersionId CurrentVersionId { get; private set; }
        public List<GatewayConfiguration> AppliedDrafts { get; } = new();

        public ValueTask<GatewayConfiguration> GetCurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(_current);

        public Task<DraftId> CreateDraftAsync(GatewayConfiguration draft, string? actor, CancellationToken cancellationToken)
        {
            var id = new DraftId($"draft-{++_draftCounter:D3}");
            _drafts[id.Value] = draft;
            return Task.FromResult(id);
        }

        public Task<ValidationResult> ValidateDraftAsync(DraftId draftId, CancellationToken cancellationToken) =>
            Task.FromResult(ValidationResult.Success());

        public Task<ConfigurationApplyResult> ApplyDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken)
        {
            if (!_drafts.TryGetValue(draftId.Value, out var draft))
                return Task.FromResult(ConfigurationApplyResult.Failed(ValidationResult.Success()));
            var previousVersionId = CurrentVersionId;
            _current = draft;
            AppliedDrafts.Add(draft);
            _versionValue = $"v-applied-{++_versionCounter}";
            CurrentVersionId = new ConfigurationVersionId(_versionValue);
            return Task.FromResult(new ConfigurationApplyResult
            {
                Success = true,
                VersionId = CurrentVersionId,
                ValidationResult = ValidationResult.Success(),
                AuditEntry = new ConfigurationAuditEntry
                {
                    Timestamp = DateTime.UtcNow,
                    VersionId = CurrentVersionId,
                    PreviousVersionId = previousVersionId,
                    Action = ConfigurationAuditAction.Applied,
                    Actor = actor ?? "system",
                    Summary = "fake apply",
                    PreviousHash = "0000000000000000000000000000000000000000000000000000000000000000",
                },
            });
        }

        public Task<IReadOnlyList<ConfigurationHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConfigurationHistoryEntry>>(Array.Empty<ConfigurationHistoryEntry>());

        public Task InitializeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<GatewayConfiguration?> GetDraftAsync(DraftId draftId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DiscardDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ConfigurationApplyResult> RollbackAsync(ConfigurationVersionId targetVersionId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public IAsyncEnumerable<ConfigurationAuditEntry> GetAuditLogAsync(bool verifyChain, CancellationToken cancellationToken) => throw new NotImplementedException();
        public ValueTask<ConfigurationAuditEntry> AppendRuntimeFaultAsync(ConfigurationFault fault, CancellationToken cancellationToken) => throw new NotImplementedException();
        public event EventHandler<ConfigurationChangeEventArgs>? CurrentChanged { add { } remove { } }
    }
}
