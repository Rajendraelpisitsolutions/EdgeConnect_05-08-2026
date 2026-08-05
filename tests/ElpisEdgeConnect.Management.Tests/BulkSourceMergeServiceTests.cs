// ============================================================================
// File: BulkSourceMergeServiceTests.cs
// Purpose: Coverage for BulkSourceMergeService — the v3.1 §10 acceptance
//          suite excluding probe (T39-T45) and handler auth (T46-T47).
//          Implements T01..T17, T31..T38 plus a happy-path Submit.
//
//          Dependencies are stubbed with hand-rolled fakes (FakeConfigurationManager,
//          FakeSchemaValidator). NSubstitute is not on this project; keeping
//          fakes inline avoids a new package dependency for the locked set
//          of behaviors we need to assert.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Api.BulkSourceMerge;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class BulkSourceMergeServiceTests
{
    // ── Fakes ─────────────────────────────────────────────────────────────────
    private sealed class FakeConfigurationManager : ElpisEdgeConnect.Core.Configuration.IConfigurationManager
    {
        private readonly Func<GatewayConfiguration> _currentSupplier;
        public List<GatewayConfiguration> CreatedDrafts { get; } = new();
        public List<DraftId> DraftRegistry { get; } = new();
        private int _draftCounter;

        public FakeConfigurationManager(GatewayConfiguration initial)
            : this(() => initial) { }

        public FakeConfigurationManager(Func<GatewayConfiguration> supplier)
        {
            _currentSupplier = supplier;
        }

        public ConfigurationVersionId CurrentVersionId => new("v1");
        public event EventHandler<ConfigurationChangeEventArgs>? CurrentChanged;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask<GatewayConfiguration> GetCurrentAsync(CancellationToken cancellationToken)
            => new(_currentSupplier());

        public Task<DraftId> CreateDraftAsync(GatewayConfiguration draft, string? actor, CancellationToken cancellationToken)
        {
            CreatedDrafts.Add(draft);
            _draftCounter++;
            var id = new DraftId($"draft-{_draftCounter}");
            DraftRegistry.Add(id);
            return Task.FromResult(id);
        }

        public Task<GatewayConfiguration?> GetDraftAsync(DraftId draftId, CancellationToken cancellationToken)
            => Task.FromResult<GatewayConfiguration?>(null);

        public Task<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DraftId>>(DraftRegistry);

        public Task<ValidationResult> ValidateDraftAsync(DraftId draftId, CancellationToken cancellationToken)
            => Task.FromResult(ValidationResult.Success());

        public Task<ConfigurationApplyResult> ApplyDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task DiscardDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ConfigurationApplyResult> RollbackAsync(ConfigurationVersionId targetVersionId, string? actor, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ConfigurationHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ConfigurationHistoryEntry>>(Array.Empty<ConfigurationHistoryEntry>());

        public async IAsyncEnumerable<ConfigurationAuditEntry> GetAuditLogAsync(bool verifyChain, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask<ConfigurationAuditEntry> AppendRuntimeFaultAsync(ConfigurationFault fault, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public void Raise() => CurrentChanged?.Invoke(this, null!);
    }

    private sealed class FakeSchemaValidator : IConfigurationSchemaValidator
    {
        public Func<string, ValidationResult>? Override { get; set; }

        public ValueTask<ValidationResult> ValidateAsync(string json, CancellationToken cancellationToken)
        {
            var result = Override?.Invoke(json) ?? ValidationResult.Success();
            return new ValueTask<ValidationResult>(result);
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────
    private const string MqttSinkInstanceId = "acme-mqtt";

    private static GatewayConfiguration BaseConfigWithOneSink(string sinkInstanceId = MqttSinkInstanceId) =>
        new()
        {
            Gateway = new GatewaySettings { GatewayId = "GW-001", GatewayName = "Gateway 1" },
            Sinks = new[]
            {
                new SinkInstanceConfig
                {
                    InstanceId = sinkInstanceId,
                    ProtocolName = "mqtt",
                    Enabled = true,
                },
            },
        };

    private static GatewayConfiguration BaseConfigWithSinks(params (string Id, string Protocol, bool Enabled)[] sinks)
    {
        var sinkList = sinks
            .Select(s => new SinkInstanceConfig
            {
                InstanceId = s.Id,
                ProtocolName = s.Protocol,
                Enabled = s.Enabled,
            })
            .ToList();
        return new GatewayConfiguration
        {
            Gateway = new GatewaySettings { GatewayId = "GW-001", GatewayName = "Gateway 1" },
            Sinks = sinkList,
        };
    }

    private static byte[] FanucCsv(params (string DeviceId, string DeviceName, string Host, string Enabled)[] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("deviceId,deviceName,host,enabled");
        foreach (var r in rows)
        {
            sb.Append(r.DeviceId).Append(',').Append(r.DeviceName).Append(',').Append(r.Host).Append(',').AppendLine(r.Enabled);
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static BulkSourceMergePreviewRequest PreviewReq(byte[] csv, string? sink = null) => new()
    {
        Protocol = BulkSourceMergeProtocol.Focas2,
        CsvBytes = csv,
        SelectedSinkInstanceId = sink,
    };

    private static BulkSourceMergeService MakeService(
        GatewayConfiguration current,
        out FakeConfigurationManager mgr,
        out FakeSchemaValidator validator)
    {
        mgr = new FakeConfigurationManager(current);
        validator = new FakeSchemaValidator();
        return new BulkSourceMergeService(mgr, validator);
    }

    // ── T01..T07 — Merge semantics ────────────────────────────────────────────
    [Fact]  // T01
    public async Task Preview_AppendsNSourcesToCurrentSources()
    {
        var svc = MakeService(BaseConfigWithOneSink(), out var mgr, out _);
        var req = PreviewReq(FanucCsv(
            ("cnc-001", "Mill-A", "192.168.10.21", "true"),
            ("cnc-002", "Mill-B", "192.168.10.22", "true"),
            ("cnc-003", "Mill-C", "192.168.10.23", "false")));

        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        preview.CanSubmit.Should().BeTrue();
        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), actor: "tester", CancellationToken.None);

        submitResp.DraftId.Should().NotBeNull();
        var draft = mgr.CreatedDrafts.Should().ContainSingle().Subject;
        draft.Sources.Should().HaveCount(3);
        draft.Sources.Select(s => s.DeviceId).Should().Equal("cnc-001", "cnc-002", "cnc-003");
    }

    [Fact]  // T02
    public async Task Preview_AppendsRoutesPointingAtSelectedSink()
    {
        var svc = MakeService(BaseConfigWithOneSink(), out var mgr, out _);
        var req = PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")));

        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), null, CancellationToken.None);

        submitResp.DraftId.Should().NotBeNull();
        var draft = mgr.CreatedDrafts[0];
        draft.Routes.Should().ContainSingle();
        draft.Routes[0].RouteId.Should().Be("route-cnc-001");
        draft.Routes[0].SourceInstanceId.Should().Be("cnc-001-source");
        draft.Routes[0].SinkInstanceIds.Should().ContainSingle().Which.Should().Be(MqttSinkInstanceId);
    }

    [Fact]  // T03
    public async Task Preview_PreservesGatewaySettingsUnchanged()
    {
        var current = BaseConfigWithOneSink() with
        {
            Gateway = new GatewaySettings { GatewayId = "GW-original", GatewayName = "Original Name" },
        };
        var svc = MakeService(current, out var mgr, out _);
        var req = PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")));

        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), null, CancellationToken.None);

        submitResp.DraftId.Should().NotBeNull();
        var draft = mgr.CreatedDrafts[0];
        draft.Gateway.GatewayId.Should().Be("GW-original");
        draft.Gateway.GatewayName.Should().Be("Original Name");
    }

    [Fact]  // T04
    public async Task Preview_PreservesSinksUnchanged()
    {
        var current = BaseConfigWithSinks(
            ("acme-mqtt",   "mqtt", true),
            ("acme-opcua",  "opcua-server", true));
        var svc = MakeService(current, out var mgr, out _);
        var req = PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")), sink: "acme-mqtt");

        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), null, CancellationToken.None);

        submitResp.DraftId.Should().NotBeNull();
        var draft = mgr.CreatedDrafts[0];
        draft.Sinks.Should().BeEquivalentTo(current.Sinks);
    }

    [Fact]  // T05
    public async Task Preview_PreservesExistingSources_IdentityAndOrder()
    {
        var current = BaseConfigWithOneSink() with
        {
            Sources = new[]
            {
                new SourceInstanceConfig { InstanceId = "pre-existing-source", ProtocolName = "modbus-tcp", DeviceId = "pre-001" },
            },
        };
        var svc = MakeService(current, out var mgr, out _);
        var req = PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")));

        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), null, CancellationToken.None);

        submitResp.DraftId.Should().NotBeNull();
        var draft = mgr.CreatedDrafts[0];
        draft.Sources.Should().HaveCount(2);
        draft.Sources[0].InstanceId.Should().Be("pre-existing-source");
        draft.Sources[1].InstanceId.Should().Be("cnc-001-source");
    }

    [Fact]  // T06
    public async Task Preview_PreservesExistingRoutes_IdentityAndOrder()
    {
        var current = BaseConfigWithOneSink() with
        {
            Routes = new[]
            {
                new RouteConfig
                {
                    RouteId = "pre-existing-route",
                    Name = "Pre-existing",
                    SourceInstanceId = "some-source",
                    SinkInstanceIds = new[] { "acme-mqtt" },
                },
            },
        };
        var svc = MakeService(current, out var mgr, out _);
        var req = PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")));

        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), null, CancellationToken.None);

        submitResp.DraftId.Should().NotBeNull();
        var draft = mgr.CreatedDrafts[0];
        draft.Routes.Should().HaveCount(2);
        draft.Routes[0].RouteId.Should().Be("pre-existing-route");
        draft.Routes[1].RouteId.Should().Be("route-cnc-001");
    }

    [Fact]  // T07
    public async Task Preview_PreservesExtensionDataUnchanged()
    {
        using var doc = JsonDocument.Parse("{\"foo\":42}");
        var current = BaseConfigWithOneSink() with
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["_provisioning"] = doc.RootElement.Clone(),
            },
        };
        var svc = MakeService(current, out var mgr, out _);
        var req = PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")));

        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), null, CancellationToken.None);

        submitResp.DraftId.Should().NotBeNull();
        var draft = mgr.CreatedDrafts[0];
        draft.ExtensionData.Should().ContainKey("_provisioning");
    }

    // ── T08..T11 — Sink selection ─────────────────────────────────────────────
    [Fact]  // T08
    public async Task Preview_BlocksWhenZeroEnabledMqttSinks()
    {
        var current = BaseConfigWithSinks(("disabled-mqtt", "mqtt", false));
        var svc = MakeService(current, out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeFalse();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.NoMqttSink);
    }

    [Fact]  // T09
    public async Task Preview_AutoSelectsWhenOneEnabledMqttSink()
    {
        var svc = MakeService(BaseConfigWithOneSink(sinkInstanceId: "uniq-mqtt"), out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeTrue();
        preview.ChosenSinkInstanceId.Should().Be("uniq-mqtt");
    }

    [Fact]  // T10
    public async Task Preview_RequiresChoiceWhenTwoPlusEnabledMqttSinks()
    {
        var current = BaseConfigWithSinks(
            ("acme-mqtt-1", "mqtt", true),
            ("acme-mqtt-2", "mqtt", true));
        var svc = MakeService(current, out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")), sink: null), CancellationToken.None);

        preview.CanSubmit.Should().BeFalse();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.SinkSelectionRequired);
    }

    [Fact]  // T11
    public async Task Preview_AcceptsExplicitSinkSelectionWhenTwoPlusExist()
    {
        var current = BaseConfigWithSinks(
            ("acme-mqtt-1", "mqtt", true),
            ("acme-mqtt-2", "mqtt", true));
        var svc = MakeService(current, out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")), sink: "acme-mqtt-2"), CancellationToken.None);

        preview.CanSubmit.Should().BeTrue();
        preview.ChosenSinkInstanceId.Should().Be("acme-mqtt-2");
    }

    // ── T12..T17 — Collisions ────────────────────────────────────────────────
    [Fact]  // T12
    public async Task Preview_BlocksDuplicateDeviceIdWithinCsv()
    {
        var svc = MakeService(BaseConfigWithOneSink(), out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(
            ("cnc-001", "Mill-A", "192.168.10.21", "true"),
            ("cnc-001", "Mill-B-dup", "192.168.10.22", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeFalse();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.CsvDuplicateDeviceId);
    }

    [Fact]  // T13
    public async Task Preview_BlocksSourceInstanceIdCollisionAgainstCurrent()
    {
        var current = BaseConfigWithOneSink() with
        {
            Sources = new[]
            {
                new SourceInstanceConfig { InstanceId = "cnc-001-source", ProtocolName = "focas2", DeviceId = "different-deviceid" },
            },
        };
        var svc = MakeService(current, out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeFalse();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.SourceInstanceIdCollision);
    }

    [Fact]  // T14
    public async Task Preview_BlocksSourceDeviceIdCollisionAgainstCurrent()
    {
        var current = BaseConfigWithOneSink() with
        {
            Sources = new[]
            {
                new SourceInstanceConfig { InstanceId = "different-instance", ProtocolName = "focas2", DeviceId = "cnc-001" },
            },
        };
        var svc = MakeService(current, out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeFalse();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.SourceDeviceIdCollision);
    }

    [Fact]  // T15
    public async Task Preview_BlocksRouteIdCollisionAgainstCurrent()
    {
        var current = BaseConfigWithOneSink() with
        {
            Routes = new[]
            {
                new RouteConfig
                {
                    RouteId = "route-cnc-001",
                    Name = "Existing route",
                    SourceInstanceId = "some-source",
                    SinkInstanceIds = new[] { "acme-mqtt" },
                },
            },
        };
        var svc = MakeService(current, out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeFalse();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.RouteIdCollision);
    }

    [Fact]  // T16
    public async Task Preview_WarnsDuplicateDeviceNameWithinCsv()
    {
        var svc = MakeService(BaseConfigWithOneSink(), out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(
            ("cnc-001", "Mill-A", "192.168.10.21", "true"),
            ("cnc-002", "Mill-A", "192.168.10.22", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeTrue();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.DuplicateDeviceName && f.Severity == BulkSourceMergeSeverity.Warning);
    }

    [Fact]  // T17
    public async Task Preview_WarnsDuplicateRouteNameVsExisting()
    {
        var current = BaseConfigWithOneSink() with
        {
            Routes = new[]
            {
                new RouteConfig
                {
                    RouteId = "route-other",
                    Name = "Mill-A to acme-mqtt",
                    SourceInstanceId = "other",
                    SinkInstanceIds = new[] { "acme-mqtt" },
                },
            },
        };
        var svc = MakeService(current, out _, out _);
        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeTrue();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.DuplicateRouteName && f.Severity == BulkSourceMergeSeverity.Warning);
    }

    // ── T26..T29 — value safety (covered indirectly by SubstitutionEngineTests
    // but a quick end-to-end "hostile values survive" check here too) ────────
    [Fact]
    public async Task Preview_HostileDeviceNameSurvivesLiterallyInRenderedConfig()
    {
        var svc = MakeService(BaseConfigWithOneSink(), out var mgr, out _);
        var hostile = "Mill \"A\" \\ {{bad}}";
        // RFC-4180 quote the deviceName: surround in " and escape inner " as ""
        var csvBody =
            "deviceId,deviceName,host,enabled\n" +
            "cnc-001,\"Mill \"\"A\"\" \\ {{bad}}\",192.168.10.21,true\n";
        var req = PreviewReq(Encoding.UTF8.GetBytes(csvBody));
        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        preview.CanSubmit.Should().BeTrue();

        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), null, CancellationToken.None);
        submitResp.DraftId.Should().NotBeNull();
        mgr.CreatedDrafts[0].Sources[0].DeviceName.Should().Be(hostile);
    }

    // ── T33..T38 — Draft concurrency + schema validation ──────────────────────
    [Fact]  // T33
    public async Task Submit_BlocksWhenBaseConfigHashMismatchesCurrent()
    {
        var current = BaseConfigWithOneSink();
        var svc = MakeService(current, out _, out _);
        var req = PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")));
        var preview = await svc.PreviewAsync(req, CancellationToken.None);

        var submit = new BulkSourceMergeSubmitRequest
        {
            Protocol = req.Protocol,
            CsvBytes = req.CsvBytes,
            SelectedSinkInstanceId = req.SelectedSinkInstanceId,
            BaseConfigHash = "0000000000000000000000000000000000000000000000000000000000000000",
        };
        var submitResp = await svc.SubmitAsync(submit, null, CancellationToken.None);

        submitResp.DraftId.Should().BeNull();
        submitResp.Findings.Should().ContainSingle()
            .Which.Code.Should().Be(BulkSourceMergeErrorCode.BaseConfigHashMismatch);
        _ = preview;
    }

    [Fact]  // T34
    public async Task Preview_SurfacesWarningWhenUnappliedDraftExists()
    {
        var current = BaseConfigWithOneSink();
        var mgr = new FakeConfigurationManager(current);
        mgr.DraftRegistry.Add(new DraftId("preexisting-draft"));
        var validator = new FakeSchemaValidator();
        var svc = new BulkSourceMergeService(mgr, validator);

        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeTrue();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.UnappliedDraftExists && f.Severity == BulkSourceMergeSeverity.Warning);
    }

    [Fact]  // T35
    public async Task Submit_ReparsesCsvBytesFromScratchEvenIfPreviewBuiltSources()
    {
        // The preview/submit DTO carries CSV bytes, NOT generated objects.
        // Verifying the submit path's draft has 2 sources derived from CSV
        // bytes (not from any imagined preview-supplied object).
        var svc = MakeService(BaseConfigWithOneSink(), out var mgr, out _);
        var csv = FanucCsv(
            ("cnc-001", "Mill-A", "192.168.10.21", "true"),
            ("cnc-002", "Mill-B", "192.168.10.22", "true"));
        var req = PreviewReq(csv);
        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        await svc.SubmitAsync(SubmitFromPreview(req, preview), null, CancellationToken.None);

        mgr.CreatedDrafts[0].Sources.Should().HaveCount(2);
    }

    [Fact]  // T36
    public async Task Submit_RunsSchemaValidationAgainstMergedConfigBeforeCreatingDraft()
    {
        var schemaCalls = 0;
        var validator = new FakeSchemaValidator
        {
            Override = _ =>
            {
                schemaCalls++;
                return ValidationResult.Success();
            },
        };
        var mgr = new FakeConfigurationManager(BaseConfigWithOneSink());
        var svc = new BulkSourceMergeService(mgr, validator);
        var req = PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")));

        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), null, CancellationToken.None);

        submitResp.DraftId.Should().NotBeNull();
        schemaCalls.Should().BeGreaterThanOrEqualTo(2);  // preview + submit
    }

    [Fact]  // T37
    public async Task Preview_BlocksWhenMergedConfigFailsSchemaValidation()
    {
        var validator = new FakeSchemaValidator
        {
            Override = _ => ValidationResult.Failure("CORE.SCHEMA_BROKEN", "synthetic schema violation"),
        };
        var mgr = new FakeConfigurationManager(BaseConfigWithOneSink());
        var svc = new BulkSourceMergeService(mgr, validator);

        var preview = await svc.PreviewAsync(PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true"))), CancellationToken.None);

        preview.CanSubmit.Should().BeFalse();
        preview.Findings.Should().Contain(f => f.Code == BulkSourceMergeErrorCode.MergedConfigSchemaViolation);
    }

    [Fact]  // T38
    public async Task Submit_HappyPathReturnsDraftId()
    {
        var svc = MakeService(BaseConfigWithOneSink(), out _, out _);
        var req = PreviewReq(FanucCsv(("cnc-001", "Mill-A", "192.168.10.21", "true")));
        var preview = await svc.PreviewAsync(req, CancellationToken.None);
        var submitResp = await svc.SubmitAsync(SubmitFromPreview(req, preview), actor: "tester", CancellationToken.None);

        submitResp.DraftId.Should().NotBeNullOrEmpty();
        submitResp.Succeeded.Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static BulkSourceMergeSubmitRequest SubmitFromPreview(
        BulkSourceMergePreviewRequest preview,
        BulkSourceMergePreviewResponse response) => new()
    {
        Protocol = preview.Protocol,
        CsvBytes = preview.CsvBytes,
        SelectedSinkInstanceId = preview.SelectedSinkInstanceId,
        BaseConfigHash = response.BaseConfigHash,
    };
}
