// ============================================================================
// Tests: SourcesUpdateApi — pins the PUT /api/v1/sources/{instanceId}
//        Edit-mode save endpoint from M.2d.2 v2 §2. Six locked tests
//        per v2 §0.3:
//
//          1. Put_HappyPath_AppliesDraftAndReturnsApplyResult
//          2. Put_StaleBaseVersionId_Returns409VersionMismatch
//          3. Put_ProtocolNameChanged_Returns400
//          4. Put_SourceNotFound_Returns404
//          5. Put_RoutePreservation_RoutesAreByteIdenticalPostSave
//          6. Put_InstanceIdMismatch_Returns400  (v2 §0.3 NEW)
//
//        Tests target internal DispatchAsync directly — matches the
//        EnableDisableApiTests pattern (no WebApplicationFactory).
// Reference: docs/sessions/2026-05-22-m2d2-steps-8-10-plan-v2.md §2
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
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Contracts.Config;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class SourcesUpdateApiTests
{
    // ── #1 — Happy path ───────────────────────────────────────────────────
    [Fact]
    public async Task Put_HappyPath_AppliesDraftAndReturnsApplyResult()
    {
        var source = MakeSource("focas-1", deviceName: "Original");
        var config = MakeConfig(sources: new[] { source });
        var mgr = new FakeConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateSourceRequestDto
        {
            SourceConfig = source with { DeviceName = "Renamed" },
            BaseVersionId = "v-1",
        };

        var result = await SourcesUpdateApi.DispatchAsync(
            "focas-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        var applied = GetJsonValue<ApplyResultDto>(result);
        applied.NewVersionId.Should().NotBe("v-1", "apply produces a fresh version id");
        applied.PreviousVersionId.Should().Be("v-1");
        mgr.AppliedDrafts.Should().HaveCount(1);
        mgr.AppliedDrafts[0].Sources[0].DeviceName.Should().Be("Renamed");
    }

    // ── #2 — Stale BaseVersionId → 409 + ConfigVersionMismatchDto ─────────
    [Fact]
    public async Task Put_StaleBaseVersionId_Returns409VersionMismatch()
    {
        var source = MakeSource("focas-1");
        var config = MakeConfig(sources: new[] { source });
        var mgr = new FakeConfigManager(config, currentVersion: "v-new");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateSourceRequestDto
        {
            SourceConfig = source with { DeviceName = "Late" },
            BaseVersionId = "v-old-stale",
        };

        var result = await SourcesUpdateApi.DispatchAsync(
            "focas-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(409);
        var dto = GetJsonValue<ConfigVersionMismatchDto>(result);
        dto.ConflictType.Should().Be(
            "VersionMismatch",
            "the explicit discriminator from v2 §0.2 must be on the wire");
        dto.BaseVersionId.Should().Be("v-old-stale");
        dto.CurrentVersionId.Should().Be("v-new");
        mgr.AppliedDrafts.Should().BeEmpty("a stale save must not mutate anything");
    }

    // ── #3 — ProtocolName changed → 400 ───────────────────────────────────
    [Fact]
    public async Task Put_ProtocolNameChanged_Returns400()
    {
        var source = MakeSource("focas-1", protocol: "focas2");
        var config = MakeConfig(sources: new[] { source });
        var mgr = new FakeConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateSourceRequestDto
        {
            // ProtocolName flipped — v2 §5.4 immutable; merger throws ArgumentException.
            SourceConfig = source with { ProtocolName = "modbustcp" },
            BaseVersionId = "v-1",
        };

        var result = await SourcesUpdateApi.DispatchAsync(
            "focas-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(400);
        mgr.AppliedDrafts.Should().BeEmpty();
    }

    // ── #4 — Source not found → 404 ───────────────────────────────────────
    [Fact]
    public async Task Put_SourceNotFound_Returns404()
    {
        var existing = MakeSource("existing-id");
        var config = MakeConfig(sources: new[] { existing });
        var mgr = new FakeConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateSourceRequestDto
        {
            SourceConfig = MakeSource("ghost-id"),
            BaseVersionId = "v-1",
        };

        var result = await SourcesUpdateApi.DispatchAsync(
            "ghost-id", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(404);
        mgr.AppliedDrafts.Should().BeEmpty();
    }

    // ── #5 — Route preservation — HTTP-layer pin of v2 §5.5 invariant ─────
    [Fact]
    public async Task Put_RoutePreservation_RoutesAreByteIdenticalPostSave()
    {
        var source = MakeSource("focas-1", deviceName: "Before");
        var sink = MakeSink("mqtt-1");
        var route = MakeRoute("route-1", "focas-1", new[] { "mqtt-1" });
        var config = MakeConfig(
            sources: new[] { source },
            sinks: new[] { sink },
            routes: new[] { route });
        var mgr = new FakeConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateSourceRequestDto
        {
            SourceConfig = source with { DeviceName = "After cosmetic edit" },
            BaseVersionId = "v-1",
        };

        var result = await SourcesUpdateApi.DispatchAsync(
            "focas-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(200);
        // The applied draft must carry the SAME Routes reference (or an
        // equivalent collection) as the original — Edit mode never mutates
        // routes per v2 §5.5.
        var applied = mgr.AppliedDrafts.Should().ContainSingle().Subject;
        applied.Routes.Should().HaveCount(1);
        applied.Routes[0].RouteId.Should().Be("route-1");
        applied.Routes[0].SourceInstanceId.Should().Be("focas-1");
        applied.Routes[0].SinkInstanceIds.Should().BeEquivalentTo(new[] { "mqtt-1" });
        applied.Routes[0].Enabled.Should().BeTrue();
        // Reference equality is the strict v2 §5.5 invariant — the merger
        // promises Routes-by-reference identical to the pre-merge config.
        ReferenceEquals(applied.Routes, config.Routes).Should().BeTrue(
            "BuildUpdatedSourceDraft must preserve the Routes reference byte-identically");
    }

    // ── #6 — InstanceId mismatch (route vs body) → 400 (v2 §0.3 NEW) ──────
    [Fact]
    public async Task Put_InstanceIdMismatch_Returns400()
    {
        var source = MakeSource("focas-1");
        var config = MakeConfig(sources: new[] { source });
        var mgr = new FakeConfigManager(config, currentVersion: "v-1");
        var services = new ServiceCollection().BuildServiceProvider();

        var body = new UpdateSourceRequestDto
        {
            // Body claims focas-2, route says focas-1. v2 §2.7 verdict:
            // route is truth; mismatch = programming error → 400.
            SourceConfig = MakeSource("focas-2"),
            BaseVersionId = "v-1",
        };

        var result = await SourcesUpdateApi.DispatchAsync(
            "focas-1", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(400);
        mgr.AppliedDrafts.Should().BeEmpty();
    }

    // ── helpers (mirror EnableDisableApiTests pattern) ────────────────────

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

    private static SourceInstanceConfig MakeSource(
        string id,
        string protocol = "focas2",
        string? deviceName = null)
    {
        var connection = JsonSerializer.Deserialize<JsonElement>(
            $$"""{ "ipAddress": "10.0.0.1", "port": 8193, "dataPoints": [] }""");
        return new SourceInstanceConfig
        {
            InstanceId = id,
            ProtocolName = protocol,
            DeviceId = id,
            DeviceName = deviceName ?? id,
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

    /// <summary>
    /// Hand-rolled fake IConfigurationManager covering the methods
    /// SourcesUpdateApi.DispatchAsync uses. Methods unused by the API
    /// throw NotImplementedException so a refactor that calls them is
    /// surfaced by the test rather than silently passing.
    /// </summary>
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

        /// <summary>Each draft that successfully applied (in order). Tests inspect to confirm route preservation, payload shape, etc.</summary>
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
            // Empty history is fine — TryGetCurrentVersionAppliedAtUtcAsync
            // falls back to DateTime.UtcNow, which is acceptable for the
            // version-mismatch test (#2) since the test asserts the
            // CurrentVersionId, not the timestamp precision.
            Task.FromResult<IReadOnlyList<ConfigurationHistoryEntry>>(Array.Empty<ConfigurationHistoryEntry>());

        // ── Not called by DispatchAsync; throw to surface accidental use ──
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
