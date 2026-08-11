// ============================================================================
// Tests: RoutesUpdateApi — pins the PUT /api/v1/routes/{routeId}
//        Edit-mode save endpoint from M.2d.3 v2 §3. Six locked tests
//        mirroring the SourcesUpdateApiTests pattern:
//
//          1. Put_HappyPath_AppliesDraftAndReturnsApplyResult
//          2. Put_StaleBaseVersionId_Returns409VersionMismatch
//          3. Put_InvalidReference_Returns400   (source/sink missing)
//          4. Put_RouteNotFound_Returns404
//          5. Put_SourcesAndSinksPreservation_ByteIdenticalPostSave
//          6. Put_RouteIdMismatch_Returns400
//
//        Tests target internal DispatchAsync directly — no
//        WebApplicationFactory needed (mirrors SourcesUpdateApiTests).
// Reference: docs/sessions/2026-05-26-m2d3-sink-route-editors-plan-v2.md §3
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

public sealed class RoutesUpdateApiTests
{
    // ── #1 — Happy path ───────────────────────────────────────────────────
    [Fact]
    public async Task Put_HappyPath_AppliesDraftAndReturnsApplyResult()
    {
        var source = MakeSource("focas-1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-1", "focas-1", "mqtt-eremos");
        var config = MakeConfig(
            sources: new[] { source },
            sinks: new[] { sink },
            routes: new[] { route });
        var mgr = new FakeRouteConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateRouteRequestDto
        {
            RouteConfig = route with { Enabled = false },
            BaseVersionId = "v-1",
        };

        var result = await RoutesUpdateApi.DispatchAsync(
            "route-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        var applied = GetJsonValue<ApplyResultDto>(result);
        applied.NewVersionId.Should().NotBe("v-1", "apply produces a fresh version id");
        applied.PreviousVersionId.Should().Be("v-1");
        mgr.AppliedDrafts.Should().HaveCount(1);
        mgr.AppliedDrafts[0].Routes[0].Enabled.Should().BeFalse();
    }

    // ── #2 — Stale BaseVersionId → 409 + ConfigVersionMismatchDto ─────────
    [Fact]
    public async Task Put_StaleBaseVersionId_Returns409VersionMismatch()
    {
        var source = MakeSource("focas-1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-1", "focas-1", "mqtt-eremos");
        var config = MakeConfig(
            sources: new[] { source },
            sinks: new[] { sink },
            routes: new[] { route });
        var mgr = new FakeRouteConfigManager(config, currentVersion: "v-new");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateRouteRequestDto
        {
            RouteConfig = route with { Enabled = false },
            BaseVersionId = "v-old-stale",
        };

        var result = await RoutesUpdateApi.DispatchAsync(
            "route-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(409);
        var dto = GetJsonValue<ConfigVersionMismatchDto>(result);
        dto.ConflictType.Should().Be("VersionMismatch");
        dto.BaseVersionId.Should().Be("v-old-stale");
        dto.CurrentVersionId.Should().Be("v-new");
        mgr.AppliedDrafts.Should().BeEmpty("a stale save must not mutate anything");
    }

    // ── #3 — Invalid reference (source/sink missing) → 400 ───────────────
    [Fact]
    public async Task Put_InvalidReference_Returns400()
    {
        // Config has route-1 pointing at focas-1 + mqtt-eremos.
        // Edit tries to point at ghost-source — merger throws ArgumentException.
        var source = MakeSource("focas-1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-1", "focas-1", "mqtt-eremos");
        var config = MakeConfig(
            sources: new[] { source },
            sinks: new[] { sink },
            routes: new[] { route });
        var mgr = new FakeRouteConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateRouteRequestDto
        {
            RouteConfig = route with { SourceInstanceId = "ghost-source" },
            BaseVersionId = "v-1",
        };

        var result = await RoutesUpdateApi.DispatchAsync(
            "route-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(400);
        mgr.AppliedDrafts.Should().BeEmpty();
    }

    // ── #4 — Route not found → 404 ────────────────────────────────────────
    [Fact]
    public async Task Put_RouteNotFound_Returns404()
    {
        var source = MakeSource("focas-1");
        var sink = MakeSink("mqtt-eremos");
        var existingRoute = MakeRoute("existing-route", "focas-1", "mqtt-eremos");
        var config = MakeConfig(
            sources: new[] { source },
            sinks: new[] { sink },
            routes: new[] { existingRoute });
        var mgr = new FakeRouteConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var ghostRoute = MakeRoute("ghost-route", "focas-1", "mqtt-eremos");
        var body = new UpdateRouteRequestDto
        {
            RouteConfig = ghostRoute,
            BaseVersionId = "v-1",
        };

        var result = await RoutesUpdateApi.DispatchAsync(
            "ghost-route", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(404);
        mgr.AppliedDrafts.Should().BeEmpty();
    }

    // ── #5 — Sources + Sinks preservation pin ────────────────────────────
    [Fact]
    public async Task Put_SourcesAndSinksPreservation_ByteIdenticalPostSave()
    {
        var source = MakeSource("focas-1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-1", "focas-1", "mqtt-eremos");
        var config = MakeConfig(
            sources: new[] { source },
            sinks: new[] { sink },
            routes: new[] { route });
        var mgr = new FakeRouteConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateRouteRequestDto
        {
            RouteConfig = route with { Enabled = false },
            BaseVersionId = "v-1",
        };

        var result = await RoutesUpdateApi.DispatchAsync(
            "route-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        var applied = mgr.AppliedDrafts.Should().ContainSingle().Subject;
        // Sources and Sinks must be preserved by reference (byte-identical).
        ReferenceEquals(applied.Sources, config.Sources).Should().BeTrue(
            "BuildEditedRouteDraft must preserve Sources reference");
        ReferenceEquals(applied.Sinks, config.Sinks).Should().BeTrue(
            "BuildEditedRouteDraft must preserve Sinks reference");
        applied.Routes[0].Enabled.Should().BeFalse("the updated route value is applied");
    }

    // ── #6 — RouteId mismatch (route param vs body) → 400 ─────────────────
    [Fact]
    public async Task Put_RouteIdMismatch_Returns400()
    {
        var source = MakeSource("focas-1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-1", "focas-1", "mqtt-eremos");
        var config = MakeConfig(
            sources: new[] { source },
            sinks: new[] { sink },
            routes: new[] { route });
        var mgr = new FakeRouteConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateRouteRequestDto
        {
            // Body claims route-2, URL says route-1. Route is truth → 400.
            RouteConfig = MakeRoute("route-2", "focas-1", "mqtt-eremos"),
            BaseVersionId = "v-1",
        };

        var result = await RoutesUpdateApi.DispatchAsync(
            "route-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(400);
        mgr.AppliedDrafts.Should().BeEmpty();
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static T GetJsonValue<T>(IResult result) where T : class
    {
        var type = result.GetType();
        var prop = type.GetProperty("Value");
        if (prop is null) throw new InvalidOperationException($"Result type {type.Name} has no Value property.");
        var value = prop.GetValue(result) as T;
        if (value is null) throw new InvalidOperationException($"Result Value is not {typeof(T).Name}.");
        return value;
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
        IReadOnlyList<SourceInstanceConfig>? sources = null,
        IReadOnlyList<SinkInstanceConfig>? sinks = null,
        IReadOnlyList<RouteConfig>? routes = null) => new()
    {
        Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Test" },
        Sources = sources ?? Array.Empty<SourceInstanceConfig>(),
        Sinks = sinks ?? Array.Empty<SinkInstanceConfig>(),
        Routes = routes ?? Array.Empty<RouteConfig>(),
    };

    private static SourceInstanceConfig MakeSource(string id) => new()
    {
        InstanceId = id,
        ProtocolName = "focas2",
        DeviceId = id,
        DeviceName = id,
        DeviceClass = "cnc",
        Enabled = true,
        Polling = new PollingSettings { IntervalMs = 1000 },
    };

    private static SinkInstanceConfig MakeSink(string id, string protocolName = "mqtt") => new()
    {
        InstanceId = id,
        ProtocolName = protocolName,
        Enabled = true,
    };

    private static RouteConfig MakeRoute(string id, string sourceId, string sinkId) => new()
    {
        RouteId = id,
        Name = id,
        SourceInstanceId = sourceId,
        SinkInstanceIds = new[] { sinkId },
        Enabled = true,
    };

    private sealed class FakeRouteConfigManager : IConfigurationManager
    {
        private GatewayConfiguration _current;
        private string _versionValue;
        private readonly Dictionary<string, GatewayConfiguration> _drafts = new(StringComparer.Ordinal);
        private int _draftCounter;
        private int _versionCounter;

        public FakeRouteConfigManager(GatewayConfiguration current, string currentVersion)
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
