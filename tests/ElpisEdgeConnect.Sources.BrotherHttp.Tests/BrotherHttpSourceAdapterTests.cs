// ============================================================================
// Tests: BrotherHttpSourceAdapter — lifecycle, validation, v3.1 §B locks
//        (atomic batch, single timestamp, single-flight, no fire-and-forget),
//        BrowseTagsAsync surface.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

public sealed class BrotherHttpSourceAdapterTests
{
    // ── Lifecycle ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_HappyPath_TransitionsToInitialized()
    {
        var (adapter, _) = BuildAdapter();
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Initialized);
    }

    [Fact]
    public async Task InitializeAsync_WrongConfigType_Throws()
    {
        var (adapter, _) = BuildAdapter();
        var wrongConfig = new DummyConfig();

        Func<Task> act = () => adapter.InitializeAsync(wrongConfig, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsync_HappyPath_TransitionsToRunning()
    {
        var (adapter, api) = BuildAdapter();
        api.MachineInfoResponse = "BRN68E74A6608EA,SXd1,3,01,0,1";
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task StartAsync_FirstProbeFails_TransitionsToRunning_DoesNotThrow()
    {
        // ADR-0003 fail-soft: a missing CNC on first probe must not crash the host.
        // The adapter enters Running (same as FOCAS2) and the polling loop retries.
        var (adapter, api) = BuildAdapter();
        api.MachineInfoResponse = null;  // health endpoint unreachable
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);

        Func<Task> act = () => adapter.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task StartAsync_DoesNotTouchTheDevice()
    {
        // TL-139: StartAsync is awaited by SourceSupervisor.AddAsync, which the
        // hot-reload coordinator awaits per source while applying a config. The old
        // HTTPD_MCNINFO probe therefore stalled an operator's Save for a full HTTP
        // timeout per Brother source pointed at a CNC that is not yet wired up —
        // and decided nothing, since both outcomes ended at Running and PollAsync
        // records the same health on its first pass.
        var (adapter, api) = BuildAdapter();
        var apiCalls = 0;
        api.OnApiEntry = _ => { apiCalls++; return Task.CompletedTask; };
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        apiCalls.Should().Be(0, "starting must not dial the CNC — the first poll owns that");
        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task ReconfigureAsync_DefaultImpl_StopsAndRestartsWithNewConfig()
    {
        // Pins the ISourceAdapter.ReconfigureAsync default-implementation
        // contract for Brother HTTP — no behavioural regression. Adapter
        // ends in Running on the NEW config.
        // Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
        var (adapter, api) = BuildAdapter();
        api.MachineInfoResponse = "BRN68E74A6608EA,SXd1,3,01,0,1";
        ISourceAdapter via = adapter;
        await via.InitializeAsync(BuildConfig(), CancellationToken.None);
        await via.StartAsync(CancellationToken.None);

        await via.ReconfigureAsync(BuildConfig(), CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task BrowseTagsAsync_ReturnsStaticCatalog()
    {
        var (adapter, _) = BuildAdapter();
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);

        var defs = await adapter.BrowseTagsAsync(CancellationToken.None);

        defs.Should().HaveCount(BrotherTagMap.StaticTags.Count);
        defs.Should().Contain(d => d.Path == "MachineInfo/Hostname");
    }

    [Fact]
    public void SubscribeAsync_NotSupported_Throws()
    {
        var (adapter, _) = BuildAdapter();

        Action act = () =>
        {
            var _ = adapter.SubscribeAsync(CancellationToken.None);
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*Subscription*");
    }

    // ── Validation ────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateConfigAsync_HappyPath_IsValid()
    {
        var (adapter, _) = BuildAdapter();
        var config = BuildConfig() with { DataPoints = new[] { "Status/State", "Tools/" } };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateConfigAsync_PollTooFast_EmitsError()
    {
        var (adapter, _) = BuildAdapter();
        var config = BuildConfig() with { PollIntervalMs = 100 };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(i => i.Code == BrotherErrors.PollTooFast);
    }

    [Fact]
    public async Task ValidateConfigAsync_PollWarning_EmitsWarningNotError()
    {
        var (adapter, _) = BuildAdapter();
        var config = BuildConfig() with { PollIntervalMs = 700 };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(i => i.Code == BrotherErrors.PollTooFastWarning);
    }

    [Fact]
    public async Task ValidateConfigAsync_UnknownDataPoint_EmitsError()
    {
        var (adapter, _) = BuildAdapter();
        var config = BuildConfig() with { DataPoints = new[] { "TotallyMadeUp/Path" } };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(i => i.Code == BrotherErrors.UnknownDataPoint);
    }

    // ── v3.1 §B.1 atomic batch + §B.2 single timestamp ────────────────────

    [Fact]
    public async Task PollAsync_AllPointsCarryIdenticalTimestamp()
    {
        var (adapter, api) = BuildAdapter();
        api.MachineInfoResponse = "BRN68E74A6608EA,SXd1,3,01,0,1";
        api.CycleTimeResponse = "Program,(,0926,)\nCycle time,0000:04:56.2\nOperation end counter,0994";
        api.WorkCountersResponse = "100,0,0,0,,0";
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().NotBeEmpty();
        var firstStamp = points[0].DeviceTimestamp;
        points.Should().OnlyContain(p => p.DeviceTimestamp == firstStamp && p.GatewayTimestamp == firstStamp,
            "v3.1 §B.2 single timestamp authority");
    }

    [Fact]
    public async Task PollAsync_EmitsCanonicalPointsForKnownTags()
    {
        var (adapter, api) = BuildAdapter();
        api.MachineInfoResponse = "BRN68E74A6608EA,SXd1,3,01,0,1";
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().Contain(p => p.TagPath == "MachineInfo/Hostname" && (string)p.Value! == "BRN68E74A6608EA");
        points.Should().Contain(p => p.TagPath == "Status/State" && (string)p.Value! == "OPERATE");
    }

    // ── v3.1 §B.3 single-flight ──────────────────────────────────────────

    [Fact]
    public async Task PollAsync_Concurrent_SecondReturnsEmpty()
    {
        var (adapter, api) = BuildAdapter();
        api.MachineInfoResponse = "BRN68E74A6608EA,SXd1,3,01,0,1";
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // Deterministic single-flight test: gate the first poll's API calls
        // via a TaskCompletionSource so the second call definitely starts
        // while the first is in-flight. All six fan-out calls in the first
        // poll await the SAME TCS, so SetResult() releases them at once
        // (a SemaphoreSlim with Release(1) would deadlock the other five).
        var releaseFirstPoll = new TaskCompletionSource();
        var firstPollEnteredApi = new TaskCompletionSource();
        api.OnApiEntry = async ct =>
        {
            firstPollEnteredApi.TrySetResult();
            await releaseFirstPoll.Task.WaitAsync(ct);
        };

        var first = adapter.PollAsync(CancellationToken.None);
        await firstPollEnteredApi.Task;  // first call is now blocked inside the API

        // Disable the gate for the second call so it isn't blocked at the API
        // (it should be gated at the single-flight check, not the API).
        api.OnApiEntry = null;
        var second = await adapter.PollAsync(CancellationToken.None);

        second.Should().BeEmpty("v3.1 §B.3 single-flight — second concurrent call returns empty");

        releaseFirstPoll.SetResult();
        await first;
    }

    // ── §6.2 precedence chain ─────────────────────────────────────────────

    [Fact]
    public async Task PollAsync_ActiveAlarm_ForcesStatusStateAlarm()
    {
        var (adapter, api) = BuildAdapter();
        api.MachineInfoResponse = "BRN68E74A6608EA,SXd1,3,01,0,1";   // running
        api.AlarmsResponse = "0512, *Servo overheat,0926,001186,4";
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().Contain(p => p.TagPath == "Status/State" && (string)p.Value! == "ALARM",
            "active alarm forces Status/State=ALARM regardless of status code per v3 §6.2");
    }

    [Fact]
    public async Task PollAsync_InformationalStandby_ForcesStatusStop()
    {
        var (adapter, api) = BuildAdapter();
        api.MachineInfoResponse = "BRN68E74A6608EA,SXd1,3,01,0,1";
        api.AlarmsResponse = "0501, *Standby mode,0926,001186,3";
        await adapter.InitializeAsync(BuildConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().Contain(p => p.TagPath == "Status/State" && (string)p.Value! == "STOP");
        points.Should().Contain(p => p.TagPath == "Status/Warning" && (string)p.Value! == "*Standby mode");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (BrotherHttpSourceAdapter Adapter, StubBrotherApi Api) BuildAdapter()
    {
        var api = new StubBrotherApi();
        var adapter = new BrotherHttpSourceAdapter("brother-test", api, NullLogger.Instance, gatewayIdentity: null);
        return (adapter, api);
    }

    private static BrotherHttpSourceConfiguration BuildConfig() => new()
    {
        InstanceId = "brother-test",
        ProtocolName = "brother-http",
        DisplayName = "Brother test",
        Enabled = true,
        PollIntervalMs = 3000,
        DeviceId = "Brother-01",
        DeviceName = "Brother test CNC",
        DeviceClass = "cnc",
        Tags = Array.Empty<string>(),
        BaseUrl = "http://test",
    };
}

internal sealed class StubBrotherApi : IBrotherHttpApi
{
    public string? MachineInfoResponse { get; set; } = string.Empty;
    public string? CycleTimeResponse { get; set; } = string.Empty;
    public string? WorkCountersResponse { get; set; } = string.Empty;
    public string? AtcToolsResponse { get; set; } = string.Empty;
    public string? AlarmsResponse { get; set; } = string.Empty;
    public string? MaintenanceResponse { get; set; } = string.Empty;

    /// <summary>
    /// Optional async hook that runs at the start of every endpoint method.
    /// Tests use this to gate the first call deterministically via a
    /// TaskCompletionSource / SemaphoreSlim instead of Task.Delay timing.
    /// </summary>
    public Func<CancellationToken, Task>? OnApiEntry { get; set; }

    public async Task<string?> GetMachineInfoAsync(CancellationToken ct) => await Return(MachineInfoResponse, ct);
    public async Task<string?> GetCycleTimeAsync(CancellationToken ct) => await Return(CycleTimeResponse, ct);
    public async Task<string?> GetWorkCountersAsync(CancellationToken ct) => await Return(WorkCountersResponse, ct);
    public async Task<string?> GetAtcToolsAsync(CancellationToken ct) => await Return(AtcToolsResponse, ct);
    public async Task<string?> GetAlarmsAsync(CancellationToken ct) => await Return(AlarmsResponse, ct);
    public async Task<string?> GetMaintenanceNoticesAsync(CancellationToken ct) => await Return(MaintenanceResponse, ct);

    private async Task<string?> Return(string? response, CancellationToken ct)
    {
        if (OnApiEntry is not null) await OnApiEntry(ct);
        return response;
    }
}

internal sealed record DummyConfig : SourceConfiguration
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DummyConfig()
    {
        InstanceId = "dummy";
        ProtocolName = "dummy";
        DeviceId = "dummy";
    }
}
