// ============================================================================
// Tests: SourcesUpdateApiIntegrationTests — pins the Edit-mode save path
//        against a REAL ConfigurationManager + FileSystemConfigurationStore
//        (temp dir), not the unit-level fake. Closes M.2d.2 step 12
//        per the v2 §5.6 "Reframe" verdict from the 2026-05-23 review pass.
//
//        Background: v2 §5.6 originally specified an
//        EditMode_CosmeticOnlyChange_DoesNotRestartSource assertion against
//        ReloadOutcomeDto. ADR-0009 Decision 3 locks "every Modified entity
//        resolves to Restart" and ConfigurationDiffer treats DeviceName-only
//        changes as Modified — so the no-restart assertion would fail
//        without amending ADR-0009 (out of M.2d.2 scope). The reframed test
//        below pins what the architecture DOES guarantee end-to-end:
//
//          1. PUT /api/v1/sources/{id} round-trips through the real
//             draft → validate → apply pipeline successfully.
//          2. Post-apply RouteConfig[] is byte-identical to pre-apply
//             (v2 §5.5 route-preservation invariant — also pinned at
//             the merger layer in WizardConfigMergerTests and at the
//             HTTP-layer in SourcesUpdateApiTests test #5; this test
//             closes the gap end-to-end including audit-chain persistence).
//          3. Post-apply current config carries the new DeviceName.
//          4. Post-apply current config version id differs from the
//             pre-apply BaseVersionId (apply produced a fresh version).
//
//        The "no-restart" architectural gap is captured as a follow-up
//        chip / ADR-0009 amendment proposal, not pinned here.
//
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §5.6,
//            docs/sessions/2026-05-22-m2d2-steps-8-10-plan-v2.md §0.3
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Management.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class SourcesUpdateApiIntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _root;
    private readonly ConfigurationStorageLayout _layout;
    private readonly FileSystemConfigurationStore _store;

    public SourcesUpdateApiIntegrationTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "edgeconnect-m2d2-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _layout = new ConfigurationStorageLayout(_root);
        _store = new FileSystemConfigurationStore(_layout);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; tests must not fail on stale temp dirs.
        }
    }

    // ── Reframed test (v2 §5.6 verdict: pin what's true today) ────────────
    [Fact]
    public async Task EditMode_DeviceNameChange_PreservesRoutesAndAppliesNewName_EndToEnd()
    {
        // ── Arrange — seed disk with a one-source-one-route config ────
        var initial = BuildInitialConfig();
        await _store.WriteCurrentAsync(
            JsonSerializer.Serialize(initial, JsonOptions),
            CancellationToken.None);
        await using var mgr = new ConfigurationManager(_store);
        await mgr.InitializeAsync(CancellationToken.None);

        var beforeVersion = mgr.CurrentVersionId;
        var beforeConfig = await mgr.GetCurrentAsync(CancellationToken.None);
        beforeConfig.Sources.Should().ContainSingle();
        beforeConfig.Routes.Should().ContainSingle();
        beforeConfig.Sources[0].DeviceName.Should().Be("Cell A — original");

        // Snapshot pre-apply route shape for byte-equivalence comparison.
        var beforeRoutesJson = JsonSerializer.Serialize(beforeConfig.Routes, JsonOptions);

        // ── Act — Edit-mode save through the real DispatchAsync flow ──
        var updatedSource = beforeConfig.Sources[0] with
        {
            DeviceName = "Cell A — renamed",
        };
        var body = new UpdateSourceRequestDto
        {
            SourceConfig = updatedSource,
            BaseVersionId = beforeVersion.Value,
        };
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await SourcesUpdateApi.DispatchAsync(
            "focas-cell-a", body, mgr, services, CancellationToken.None);

        // ── Assert — 200 OK and a fresh ApplyResultDto ─────────────────
        GetStatusCode(result).Should().Be(200);

        // ── Assert — post-apply state has the new DeviceName ──────────
        var afterConfig = await mgr.GetCurrentAsync(CancellationToken.None);
        afterConfig.Sources.Should().ContainSingle();
        afterConfig.Sources[0].DeviceName.Should().Be("Cell A — renamed");

        // ── Assert — version id advanced ──────────────────────────────
        mgr.CurrentVersionId.Value.Should().NotBe(
            beforeVersion.Value,
            "the apply pipeline must produce a fresh version id");

        // ── Assert — RouteConfig[] is byte-identical end-to-end ───────
        // v2 §5.5 route-preservation invariant. The merger preserves
        // routes by reference, the apply persists them through the
        // FileSystemConfigurationStore, and the post-apply read reproduces
        // the identical JSON shape. Reference-equality doesn't survive
        // store round-trip; serialize and compare.
        var afterRoutesJson = JsonSerializer.Serialize(afterConfig.Routes, JsonOptions);
        afterRoutesJson.Should().Be(
            beforeRoutesJson,
            "M.2d.2 v2 §5.5 — Edit mode must never mutate routes, " +
            "even when the source's body fields change");

        // ── Assert — the route still references the source by id ──────
        afterConfig.Routes[0].SourceInstanceId.Should().Be("focas-cell-a");
        afterConfig.Routes[0].SinkInstanceIds.Should().BeEquivalentTo(new[] { "mqtt-primary" });
    }

    // ── Negative pin: a stale BaseVersionId still 409s end-to-end ─────────
    [Fact]
    public async Task EditMode_StaleBaseVersionId_Returns409_EvenAgainstRealManager()
    {
        // Belt-and-braces over SourcesUpdateApiTests test #2. The fake
        // manager's stale-detection might mask a CurrentVersionId
        // initialization quirk in the real manager; this test catches that.
        var initial = BuildInitialConfig();
        await _store.WriteCurrentAsync(
            JsonSerializer.Serialize(initial, JsonOptions),
            CancellationToken.None);
        await using var mgr = new ConfigurationManager(_store);
        await mgr.InitializeAsync(CancellationToken.None);

        var beforeConfig = await mgr.GetCurrentAsync(CancellationToken.None);
        var updatedSource = beforeConfig.Sources[0] with { DeviceName = "Late edit" };
        var body = new UpdateSourceRequestDto
        {
            SourceConfig = updatedSource,
            BaseVersionId = "v-deliberately-stale",
        };
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await SourcesUpdateApi.DispatchAsync(
            "focas-cell-a", body, mgr, services, CancellationToken.None);

        GetStatusCode(result).Should().Be(409);
        var afterConfig = await mgr.GetCurrentAsync(CancellationToken.None);
        afterConfig.Sources[0].DeviceName.Should().Be(
            "Cell A — original",
            "a stale save must not mutate disk-backed state");
    }

    // ── Fixture helper ────────────────────────────────────────────────────

    private static GatewayConfiguration BuildInitialConfig()
    {
        var connection = JsonSerializer.Deserialize<JsonElement>(
            $$"""{ "ipAddress": "10.0.5.7", "port": 8193, "dataPoints": [] }""");

        return new GatewayConfiguration
        {
            Gateway = new GatewaySettings
            {
                GatewayId = "gw-m2d2-int",
                GatewayName = "M.2d.2 integration",
            },
            Sources = new[]
            {
                new SourceInstanceConfig
                {
                    InstanceId = "focas-cell-a",
                    ProtocolName = "focas2",
                    DeviceId = "F31i-A",
                    DeviceName = "Cell A — original",
                    DeviceClass = "cnc",
                    Enabled = true,
                    Polling = new PollingSettings { IntervalMs = 1000 },
                    Connection = connection,
                },
            },
            Sinks = new[]
            {
                new SinkInstanceConfig
                {
                    InstanceId = "mqtt-primary",
                    ProtocolName = "mqtt",
                    Enabled = true,
                },
            },
            Routes = new[]
            {
                new RouteConfig
                {
                    RouteId = "route-focas-cell-a",
                    Name = "Cell A → MQTT",
                    SourceInstanceId = "focas-cell-a",
                    SinkInstanceIds = new[] { "mqtt-primary" },
                    Enabled = true,
                },
            },
        };
    }

    private static int GetStatusCode(IResult result)
    {
        var type = result.GetType();
        var prop = type.GetProperty("StatusCode");
        if (prop is null) return 0;
        var value = prop.GetValue(result);
        return value is int i ? i : value is null ? 0 : (int)value;
    }
}
