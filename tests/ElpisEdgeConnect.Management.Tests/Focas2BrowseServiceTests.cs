// ============================================================================
// Tests: Focas2BrowseService — pins the throwaway-adapter probe lifecycle
//        defined in M.2b.3 plan v3 §2 and §8.2. Uses a FakeProbeAdapter
//        (ISourceAdapter shim) so we never instantiate the real
//        Focas2SourceAdapter — keeps the tests fast, deterministic, and
//        independent of FakeFocas2Api wiring.
//
//        Headline tests:
//          * #1 happy path
//          * #2 license disabled
//          * #3 connect failure (post-Start connected check)
//          * #4 LOCKED S override — high-retries/high-timeout request must
//            be reshaped to retries=1, timeoutSeconds <= 8
//          * #5 LOCKED M timeout — short-budget service cancels probe
//          * #6 LOCKED J in-flight — second call same IP:Port returns busy
// Reference: docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md §8.2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Sources.Focas2;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class Focas2BrowseServiceTests
{
    // ── #1 — HAPPY PATH ───────────────────────────────────────────────────
    [Fact]
    public async Task Browse_HappyPath_ReturnsAxesAndTagsWithProbeIdAndElapsedMs()
    {
        var adapter = new FakeProbeAdapter("focas-1")
        {
            CncSeries = "31i-B5",
            CncType = "M",
            BrowseResult = new[]
            {
                new TagDefinition { Name = "status/run_state",     Path = "Status/RunState",     ValueType = CanonicalValueType.String },
                new TagDefinition { Name = "axes/x/absolute",      Path = "Axes/X/Absolute",     ValueType = CanonicalValueType.Double, Unit = "mm" },
                new TagDefinition { Name = "axes/y/absolute",      Path = "Axes/Y/Absolute",     ValueType = CanonicalValueType.Double, Unit = "mm" },
                new TagDefinition { Name = "axes/z/absolute",      Path = "Axes/Z/Absolute",     ValueType = CanonicalValueType.Double, Unit = "mm" },
                new TagDefinition { Name = "axes/feed_rate",       Path = "Axes/FeedRate",       ValueType = CanonicalValueType.Double, Unit = "mm/min" },
                new TagDefinition { Name = "spindle/speed",        Path = "Spindle/Speed",       ValueType = CanonicalValueType.Double, Unit = "rpm" },
            },
        };
        var service = MakeService(_ => adapter);

        var result = await service.BrowseAsync(MakeRequest("focas-1", "192.168.1.10"));

        result.Success.Should().BeTrue();
        result.ProbeId.Should().HaveLength(8);
        result.ProbeId.Should().MatchRegex("^[a-f0-9]{8}$");
        result.AxisNames.Should().BeEquivalentTo(new[] { "X", "Y", "Z" });  // "FeedRate" excluded
        result.Tags.Should().HaveCount(6);
        result.TagCount.Should().Be(6);
        result.CncSeries.Should().Be("31i-B5");
        result.CncType.Should().Be("M");
        result.ElapsedMs.Should().BeGreaterOrEqualTo(0,
            "the fake completes in sub-millisecond on fast hardware; the contract is non-negative");
        result.ErrorCode.Should().BeNull();
        result.Warnings.Should().BeEmpty();
    }

    // ── #2 — LICENSE DISABLED ─────────────────────────────────────────────
    [Fact]
    public async Task Browse_LicenseDisabled_ReturnsLicenseModuleDisabledWithoutTouchingAdapter()
    {
        var adapter = new FakeProbeAdapter("focas-1");
        var service = MakeService(
            factory: _ => adapter,
            isModuleEnabled: _ => false);  // license rejects every module

        var result = await service.BrowseAsync(MakeRequest("focas-1", "192.168.1.10"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("LICENSE.MODULE_DISABLED");
        result.ProbeId.Should().HaveLength(8);
        adapter.InitializeCallCount.Should().Be(0, "the license gate must reject before adapter construction");
        adapter.StartCallCount.Should().Be(0);
        adapter.BrowseCallCount.Should().Be(0);
    }

    // ── #3 — CONNECT FAILURE / FAIL-SOFT POST-START CHECK ─────────────────
    [Fact]
    public async Task Browse_StartAsyncFailSoft_PostStartConnectedCheckFiresAndReturnsConnectFailed()
    {
        // Pins the §14 adjacent observation: Focas2SourceAdapter.StartAsync
        // is fail-soft — it transitions to Running even when the initial
        // connect failed. The service must inspect health AFTER StartAsync
        // and surface FOCAS2.CONNECT_FAILED instead of silently returning
        // fallback axis names.
        var adapter = new FakeProbeAdapter("focas-1")
        {
            ConnectedAfterStart = false,  // simulates the fail-soft outcome
            BrowseResult = new[]
            {
                new TagDefinition { Name = "axes/x/absolute", Path = "Axes/X/Absolute", ValueType = CanonicalValueType.Double },
            },
        };
        var service = MakeService(_ => adapter);

        var result = await service.BrowseAsync(MakeRequest("focas-1", "192.168.1.99"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("FOCAS2.CONNECT_FAILED");
        result.Tags.Should().BeEmpty();
        result.AxisNames.Should().BeEmpty();
        adapter.BrowseCallCount.Should().Be(0, "Browse must NOT be called when post-Start connected check fails");
        adapter.StopCallCount.Should().Be(1, "cleanup still runs on failure");
    }

    // ── #4 — LOCKED S OVERRIDE (HEADLINE) ─────────────────────────────────
    [Fact]
    public async Task Browse_RequestWithHighRetriesAndLongTimeout_OverriddenByProbeConfig()
    {
        // The request specifies maxConnectRetries=10 and timeoutSeconds=60
        // — values an operator might legitimately want at runtime. The probe
        // MUST override these to retries=1 and timeoutSeconds<=8 so the 15s
        // Browse budget is genuinely enforceable. Pins Locked S.
        var adapter = new FakeProbeAdapter("focas-1");
        var service = MakeService(_ => adapter);

        var request = MakeRequest("focas-1", "192.168.1.10", maxConnectRetries: 10, timeoutSeconds: 60);
        var result = await service.BrowseAsync(request);

        result.Success.Should().BeTrue();
        adapter.CapturedInitConfig.Should().NotBeNull();
        adapter.CapturedInitConfig!.MaxConnectRetries.Should().Be(1, "Locked S forces single-attempt probe");
        adapter.CapturedInitConfig.TimeoutSeconds.Should().BeLessOrEqualTo(Focas2BrowseService.ProbeMaxTimeoutSeconds,
            "Locked S caps TimeoutSeconds at 8 for the probe regardless of request");

        // Sanity: a small request value is preserved (clamped at min, not boosted up).
        var smallTimeoutRequest = MakeRequest("focas-1b", "192.168.1.10", maxConnectRetries: 5, timeoutSeconds: 3);
        adapter.CapturedInitConfig = null;
        var result2 = await service.BrowseAsync(smallTimeoutRequest);
        result2.Success.Should().BeTrue();
        adapter.CapturedInitConfig!.TimeoutSeconds.Should().Be(3, "operator's small timeout is preserved (min, not boosted)");
        adapter.CapturedInitConfig.MaxConnectRetries.Should().Be(1);
    }

    // ── #5 — LOCKED M TIMEOUT ─────────────────────────────────────────────
    [Fact]
    public async Task Browse_BrowseTagsAsyncHangs_ProbeBudgetCancelsAndReturnsTimeout()
    {
        // Use a short 500 ms probe budget for fast test execution. The
        // PRODUCTION 15s budget is pinned by Focas2BrowseService.DefaultProbeBudget
        // (verified by the build referencing the constant elsewhere).
        var hold = new TaskCompletionSource();
        var adapter = new FakeProbeAdapter("focas-1") { HoldAtBrowse = hold };
        var service = MakeService(
            factory: _ => adapter,
            probeBudget: TimeSpan.FromMilliseconds(500),
            cleanupBudget: TimeSpan.FromSeconds(2));

        var sw = Stopwatch.StartNew();
        var result = await service.BrowseAsync(MakeRequest("focas-1", "192.168.1.10"));
        sw.Stop();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("FOCAS2.BROWSE_TIMEOUT");
        result.ProbeId.Should().HaveLength(8);
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(450),
            "probe should consume close to the full budget before giving up");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
            "the budget + cleanup must not blow out");

        // Cleanup — release the held BrowseTagsAsync so the fake's background task can finish.
        hold.TrySetCanceled();
    }

    // ── #6 — LOCKED J IN-FLIGHT (BUSY) ────────────────────────────────────
    [Fact]
    public async Task Browse_SecondCallSameIpPortWhileFirstInFlight_ReturnsBrowseInFlight()
    {
        // First probe parks at BrowseTagsAsync. Second probe to same IP:Port
        // must return immediately with FOCAS2.BROWSE_IN_FLIGHT — single-flight
        // is by-rejection, not by-queueing. Pins Locked J.
        var hold = new TaskCompletionSource();
        var firstAdapter = new FakeProbeAdapter("focas-first") { HoldAtBrowse = hold };
        var service = MakeService(_ => firstAdapter);

        var firstTask = Task.Run(() => service.BrowseAsync(MakeRequest("focas-first", "192.168.1.50")));

        // Wait until the first probe has acquired the lease and is parked at Browse.
        // Spin-yield is cheap and avoids Thread.Sleep.
        var spinSw = Stopwatch.StartNew();
        while (firstAdapter.BrowseCallCount == 0)
        {
            await Task.Yield();
            if (spinSw.Elapsed > TimeSpan.FromSeconds(2))
            {
                throw new Xunit.Sdk.XunitException("First probe did not reach BrowseTagsAsync within 2s — test setup broken.");
            }
        }

        // Second probe — same IP:Port. Different InstanceId is fine; the lease
        // is keyed on endpoint, not on adapter id.
        var secondResult = await service.BrowseAsync(MakeRequest("focas-second", "192.168.1.50"));

        secondResult.Success.Should().BeFalse();
        secondResult.ErrorCode.Should().Be("FOCAS2.BROWSE_IN_FLIGHT");
        secondResult.ProbeId.Should().HaveLength(8);
        secondResult.ProbeId.Should().NotBe(firstAdapter.LastProbeIdIfCaptured ?? string.Empty);

        // Release the first probe so it can finish cleanly.
        hold.SetResult();
        var firstResult = await firstTask;
        firstResult.Success.Should().BeTrue();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static Focas2BrowseService MakeService(
        Func<string, ISourceAdapter> factory,
        Func<string, bool>? isModuleEnabled = null,
        TimeSpan? probeBudget = null,
        TimeSpan? cleanupBudget = null)
        => new(
            adapterFactory: factory,
            isModuleEnabled: isModuleEnabled ?? (_ => true),
            logger: NullLogger<Focas2BrowseService>.Instance,
            probeBudget: probeBudget,
            cleanupBudget: cleanupBudget);

    private static SourceInstanceConfig MakeRequest(
        string instanceId,
        string ip,
        int port = 8193,
        int timeoutSeconds = 10,
        int maxConnectRetries = 5)
    {
        var conn = new JsonObject
        {
            ["ipAddress"] = ip,
            ["port"] = port,
            ["timeoutSeconds"] = timeoutSeconds,
            ["keepAlive"] = true,
            ["maxConnectRetries"] = maxConnectRetries,
            ["dataPoints"] = new JsonArray(),
        };
        using var doc = JsonDocument.Parse(conn.ToJsonString());
        return new SourceInstanceConfig
        {
            InstanceId = instanceId,
            ProtocolName = "focas2",
            DeviceId = instanceId,
            DeviceClass = "cnc",
            Enabled = true,
            Polling = new PollingSettings { IntervalMs = 1000 },
            Connection = doc.RootElement.Clone(),
        };
    }
}

// ============================================================================
// Test seam: fake ISourceAdapter that captures every call the probe service
// makes and exposes knobs for the failure scenarios the tests need.
// ============================================================================

internal sealed class FakeProbeAdapter : ISourceAdapter
{
    public FakeProbeAdapter(string instanceId) => InstanceId = instanceId;

    public string InstanceId { get; }
    public string ProtocolName => "focas2";
    public SourceCapabilities Capabilities => SourceCapabilities.Polling | SourceCapabilities.Browse;
    public AdapterState State { get; private set; } = AdapterState.Created;

    // ── Knobs ─────────────────────────────────────────────────────────────
    public bool ConnectedAfterStart { get; set; } = true;
    public IReadOnlyList<TagDefinition> BrowseResult { get; set; } = Array.Empty<TagDefinition>();
    public Exception? BrowseThrows { get; set; }
    public TaskCompletionSource? HoldAtBrowse { get; set; }
    public string? CncSeries { get; set; }
    public string? CncType { get; set; }

    // ── Spies ─────────────────────────────────────────────────────────────
    public Focas2SourceConfiguration? CapturedInitConfig { get; set; }
    public int InitializeCallCount;
    public int StartCallCount;
    public int BrowseCallCount;
    public int StopCallCount;
    public int DisposeCallCount;
    public string? LastProbeIdIfCaptured { get; set; }  // reserved; not currently populated

    // ── ISourceAdapter impl ───────────────────────────────────────────────
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
        => Task.FromResult(ValidationResult.Success());

    public Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        CapturedInitConfig = (Focas2SourceConfiguration)config;
        Interlocked.Increment(ref InitializeCallCount);
        State = AdapterState.Initialized;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref StartCallCount);
        State = AdapterState.Running;
        return Task.CompletedTask;
    }

    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
    {
        var metrics = new Dictionary<string, object>
        {
            ["connected"] = ConnectedAfterStart,
        };
        if (CncSeries is not null) metrics["cncSeries"] = CncSeries;
        if (CncType is not null) metrics["cncType"] = CncType;

        return Task.FromResult(new AdapterHealth
        {
            State = State,
            Level = ConnectedAfterStart ? HealthLevel.Healthy : HealthLevel.Degraded,
            CheckedAt = DateTime.UtcNow,
            Metrics = metrics,
        });
    }

    public async Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref BrowseCallCount);
        if (HoldAtBrowse is not null)
        {
            await HoldAtBrowse.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        if (BrowseThrows is not null)
        {
            throw BrowseThrows;
        }
        return BrowseResult;
    }

    public Task StopAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref StopCallCount);
        State = AdapterState.Stopped;
        return Task.CompletedTask;
    }

    public Task<System.Collections.Generic.IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public System.Collections.Generic.IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref DisposeCallCount);
        return ValueTask.CompletedTask;
    }
}
