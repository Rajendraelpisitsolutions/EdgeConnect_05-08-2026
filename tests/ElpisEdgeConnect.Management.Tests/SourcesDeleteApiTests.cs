// ============================================================================
// Tests: SourcesDeleteApi — pins DELETE /api/v1/sources/{instanceId}.
//        Targets internal DispatchAsync directly (no WebApplicationFactory),
//        mirroring SourcesUpdateApiTests / EnableDisableApiTests.
//
//          1. Delete_HappyPath_RemovesSourceAndItsRoute_Applies
//          2. Delete_CascadesOnlyRoutesReadingFromSource
//          3. Delete_KeepsSinksAndOtherSources
//          4. Delete_SourceNotFound_Returns404
//          5. Delete_StaleBaseVersionId_Returns409
//          6. Delete_NoBaseVersionId_SkipsConcurrencyCheck
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Management.Contracts.Config;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class SourcesDeleteApiTests
{
    // ── #1 — Happy path: source + its route removed, draft applied ────────
    [Fact]
    public async Task Delete_HappyPath_RemovesSourceAndItsRoute_Applies()
    {
        var source = MakeSource("focas-1");
        var sink = MakeSink("mqtt-1");
        var route = MakeRoute("route-1", "focas-1", new[] { "mqtt-1" });
        var config = MakeConfig(new[] { source }, new[] { sink }, new[] { route });
        var mgr = new FakeConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await SourcesDeleteApi.DispatchAsync(
            "focas-1", baseVersionId: "v-1", actor: "studio-ui", mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        var applied = mgr.AppliedDrafts.Should().ContainSingle().Subject;
        applied.Sources.Should().BeEmpty("the deleted source is gone");
        applied.Routes.Should().BeEmpty("the route reading from it is cascaded away");
        applied.Sinks.Should().ContainSingle(s => s.InstanceId == "mqtt-1", "sinks are left in place");
    }

    // ── #2 — Cascade removes ONLY routes reading from the deleted source ──
    [Fact]
    public async Task Delete_CascadesOnlyRoutesReadingFromSource()
    {
        var keep = MakeSource("keep");
        var drop = MakeSource("drop");
        var sink = MakeSink("mqtt-1");
        var routeKeep = MakeRoute("route-keep", "keep", new[] { "mqtt-1" });
        var routeDrop = MakeRoute("route-drop", "drop", new[] { "mqtt-1" });
        var config = MakeConfig(new[] { keep, drop }, new[] { sink }, new[] { routeKeep, routeDrop });
        var mgr = new FakeConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await SourcesDeleteApi.DispatchAsync(
            "drop", baseVersionId: "v-1", actor: null, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        var applied = mgr.AppliedDrafts.Should().ContainSingle().Subject;
        applied.Sources.Should().ContainSingle(s => s.InstanceId == "keep");
        applied.Routes.Should().ContainSingle(r => r.RouteId == "route-keep");
    }

    // ── #3 — Sinks + other sources preserved ─────────────────────────────
    [Fact]
    public async Task Delete_KeepsSinksAndOtherSources()
    {
        var a = MakeSource("a");
        var b = MakeSource("b");
        var config = MakeConfig(new[] { a, b }, new[] { MakeSink("s1"), MakeSink("s2") });
        var mgr = new FakeConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        await SourcesDeleteApi.DispatchAsync(
            "a", baseVersionId: "v-1", actor: null, mgr, services, CancellationToken.None);

        var applied = mgr.AppliedDrafts.Should().ContainSingle().Subject;
        applied.Sources.Should().ContainSingle(s => s.InstanceId == "b");
        applied.Sinks.Should().HaveCount(2);
    }

    // ── #4 — Source not found → 404, nothing applied ─────────────────────
    [Fact]
    public async Task Delete_SourceNotFound_Returns404()
    {
        var config = MakeConfig(new[] { MakeSource("exists") });
        var mgr = new FakeConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await SourcesDeleteApi.DispatchAsync(
            "ghost", baseVersionId: "v-1", actor: null, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(404);
        mgr.AppliedDrafts.Should().BeEmpty();
    }

    // ── #5 — Stale base version → 409, nothing applied ───────────────────
    [Fact]
    public async Task Delete_StaleBaseVersionId_Returns409()
    {
        var config = MakeConfig(new[] { MakeSource("focas-1") });
        var mgr = new FakeConfigManager(config, currentVersion: "v-new");
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await SourcesDeleteApi.DispatchAsync(
            "focas-1", baseVersionId: "v-old-stale", actor: null, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(409);
        var dto = GetJsonValue<ConfigVersionMismatchDto>(result);
        dto.BaseVersionId.Should().Be("v-old-stale");
        dto.CurrentVersionId.Should().Be("v-new");
        mgr.AppliedDrafts.Should().BeEmpty("a stale delete must not mutate anything");
    }

    // ── #6 — No base version → concurrency check skipped, delete proceeds ─
    [Fact]
    public async Task Delete_NoBaseVersionId_SkipsConcurrencyCheck()
    {
        var config = MakeConfig(new[] { MakeSource("focas-1") });
        var mgr = new FakeConfigManager(config, currentVersion: "v-anything");
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await SourcesDeleteApi.DispatchAsync(
            "focas-1", baseVersionId: null, actor: null, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        mgr.AppliedDrafts.Should().ContainSingle().Which.Sources.Should().BeEmpty();
    }

    // ── helpers (mirror SourcesUpdateApiTests) ───────────────────────────

    private static T GetJsonValue<T>(IResult result) where T : class
    {
        var prop = result.GetType().GetProperty("Value")
            ?? throw new InvalidOperationException("Result has no Value property.");
        return prop.GetValue(result) as T
            ?? throw new InvalidOperationException($"Result Value is not {typeof(T).Name}.");
    }

    private static int GetStatusCode(IResult result)
    {
        var prop = result.GetType().GetProperty("StatusCode");
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

    private static SourceInstanceConfig MakeSource(string id, string protocol = "focas2")
    {
        var connection = JsonSerializer.Deserialize<JsonElement>(
            """{ "ipAddress": "10.0.0.1", "port": 8193, "dataPoints": [] }""");
        return new SourceInstanceConfig
        {
            InstanceId = id,
            ProtocolName = protocol,
            DeviceId = id,
            DeviceName = id,
            DeviceClass = "cnc",
            Enabled = true,
            Polling = new PollingSettings { IntervalMs = 1000 },
            Connection = connection,
        };
    }

    private static SinkInstanceConfig MakeSink(string id) => new()
    {
        InstanceId = id,
        ProtocolName = "mqtt",
        Enabled = true,
    };

    private static RouteConfig MakeRoute(string id, string sourceId, string[] sinkIds) => new()
    {
        RouteId = id,
        Name = id,
        SourceInstanceId = sourceId,
        SinkInstanceIds = sinkIds,
        Enabled = true,
    };

    /// <summary>Minimal fake IConfigurationManager — same shape as SourcesUpdateApiTests.</summary>
    private sealed class FakeConfigManager : IConfigurationManager
    {
        private GatewayConfiguration _current;
        private string _versionValue;
        private readonly Dictionary<string, GatewayConfiguration> _drafts = new(StringComparer.Ordinal);
        private int _draftCounter;
        private int _versionCounter;

        public FakeConfigManager(GatewayConfiguration current, string currentVersion)
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
            {
                return Task.FromResult(ConfigurationApplyResult.Failed(ValidationResult.Success()));
            }
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
