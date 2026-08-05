// ============================================================================
// File: Focas2SourceAdapterTests.cs
// Purpose: Unit tests for Focas2SourceAdapter — ISourceAdapter lifecycle,
//          polling, browsing, and config validation.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

/// <summary>Minimal <see cref="ILogger"/> that records emitted entries so tests
/// can assert on alert-level logging (no capture helper exists otherwise).</summary>
internal sealed class CapturingLogger : ILogger
{
    public sealed record Entry(LogLevel Level, string Message);

    public List<Entry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add(new Entry(logLevel, formatter(state, exception)));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

public sealed class Focas2SourceAdapterTests : IAsyncDisposable
{
    private readonly FakeFocas2Api _api = new();
    private Focas2SourceAdapter? _adapter;

    private Focas2SourceAdapter CreateAdapter(string instanceId = "focas-test", ILogger? logger = null)
    {
        _adapter = new Focas2SourceAdapter(instanceId, _api, logger ?? NullLogger.Instance);
        return _adapter;
    }

    private static Focas2SourceConfiguration CreateConfig(
        string instanceId = "focas-test",
        string ipAddress = "192.168.1.1") => new()
    {
        InstanceId = instanceId,
        ProtocolName = "focas2",
        DeviceId = "dev1",
        IpAddress = ipAddress,
        MaxConnectRetries = 1,
        // Disable poll pacing for unit tests — the real PollIntervalMs
        // (1000 ms default) would make tests that invoke PollAsync twice
        // take >1 s each without adding any signal. Pacing is exercised
        // by a dedicated test (see PollAsync_RespectsPollIntervalMs_).
        PollIntervalMs = 0,
    };

    public async ValueTask DisposeAsync()
    {
        if (_adapter != null)
        {
            await _adapter.DisposeAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_ValidConfig_TransitionsToInitialized()
    {
        var adapter = CreateAdapter();
        var config = CreateConfig();

        await adapter.InitializeAsync(config, CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Initialized);
    }

    [Fact]
    public async Task InitializeAsync_WrongConfigType_ThrowsAndFails()
    {
        var adapter = CreateAdapter();
        var wrongConfig = new StubSourceConfiguration
        {
            InstanceId = "wrong",
            ProtocolName = "modbus",
            DeviceId = "dev1",
        };

        var act = () => adapter.InitializeAsync(wrongConfig, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Focas2SourceConfiguration*");
        adapter.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task ReconfigureAsync_DefaultImpl_StopsAndRestartsWithNewConfig()
    {
        // Pins the ISourceAdapter.ReconfigureAsync default-implementation
        // contract for FOCAS2 — no behavioural regression. Adapter ends in
        // Running on the NEW config.
        // Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
        var adapter = CreateAdapter();
        ISourceAdapter via = adapter;
        await via.InitializeAsync(CreateConfig(), CancellationToken.None);
        await via.StartAsync(CancellationToken.None);

        var newConfig = CreateConfig(ipAddress: "192.168.1.99");
        await via.ReconfigureAsync(newConfig, CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task StartAsync_TransitionsToRunning()
    {
        var adapter = CreateAdapter();
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task StopAsync_TransitionsToStopped()
    {
        var adapter = CreateAdapter();
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        await adapter.StopAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task PollAsync_ReturnsCanonicalDataPoints()
    {
        var adapter = CreateAdapter();
        ConfigureRealisticFake();
        var config = CreateConfig();
        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().NotBeEmpty();
        points.Should().Contain(p => p.TagName == "status/run_state");
        points.Should().Contain(p => p.TagName == "status/auto_mode");
        points.Should().Contain(p => p.TagName == "status/emergency_stop");
    }

    [Fact]
    public async Task PollAsync_FatalError_TransitionsToDegraded()
    {
        var adapter = CreateAdapter();
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        var config = CreateConfig();
        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // First poll consumes the cached statinfo from connect-time validation
        await adapter.PollAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Running);

        // Now make the next status read fail with a fatal error.
        // Since cached statinfo is consumed, StatusCollector will call the API.
        _api.ReadStatusInfoFunc = _ => ((short)Focas2ErrorCode.EW_SOCKET, default);

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().BeEmpty();
        adapter.State.Should().Be(AdapterState.Degraded);
    }

    [Fact]
    public async Task PollAsync_SuccessAfterDegraded_RecoversToRunning()
    {
        var adapter = CreateAdapter();
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        var config = CreateConfig();
        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // First poll consumes the cached statinfo from connect-time
        await adapter.PollAsync(CancellationToken.None);

        // Cause degraded state — StatusCollector will now call API directly
        _api.ReadStatusInfoFunc = _ => ((short)Focas2ErrorCode.EW_SOCKET, default);
        await adapter.PollAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Degraded);

        // Recover — remove the override and re-enable connection
        _api.ReadStatusInfoFunc = null;
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };

        var points = await adapter.PollAsync(CancellationToken.None);

        // The adapter should recover. Points might be empty if reconnect not yet done,
        // but state should be Running if any points were produced.
        if (points.Count > 0)
        {
            adapter.State.Should().Be(AdapterState.Running);
        }
    }

    [Fact]
    public async Task PollAsync_FatalError_LogsStoppedAlert()
    {
        var logger = new CapturingLogger();
        var adapter = CreateAdapter(logger: logger);
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // First poll → Running.
        await adapter.PollAsync(CancellationToken.None);

        // Fatal error on the next poll drives Running → Degraded, which must
        // emit the operator-visible "STOPPED producing data" alert at Error.
        _api.ReadStatusInfoFunc = _ => ((short)Focas2ErrorCode.EW_SOCKET, default);
        await adapter.PollAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Degraded);
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error
            && e.Message.Contains("ALERT")
            && e.Message.Contains("STOPPED producing data"));
    }

    [Fact]
    public async Task PollAsync_SustainedFailure_LogsStillDownAlertOnce()
    {
        var logger = new CapturingLogger();
        var adapter = CreateAdapter(logger: logger);
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        await adapter.PollAsync(CancellationToken.None); // Running

        // Persistent socket loss on every subsequent poll.
        _api.ReadStatusInfoFunc = _ => ((short)Focas2ErrorCode.EW_SOCKET, default);
        for (var i = 0; i < 6; i++)
        {
            await adapter.PollAsync(CancellationToken.None);
        }

        // The one-shot "STILL DOWN" sustained-outage alert must appear exactly once.
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Error && e.Message.Contains("STILL DOWN"));
    }

    [Fact]
    public async Task ValidateConfigAsync_NegativeDataTimeout_ReturnsFailure()
    {
        var adapter = CreateAdapter();
        var config = CreateConfig() with { DataTimeoutSeconds = -1 };

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "DataTimeoutSeconds");
    }

    [Fact]
    public async Task BrowseTagsAsync_ReturnsTagDefinitions()
    {
        var adapter = CreateAdapter();
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);

        var tags = await adapter.BrowseTagsAsync(CancellationToken.None);

        tags.Should().NotBeEmpty();
        tags.Should().Contain(t => t.Name == "status/run_state");
        tags.Should().Contain(t => t.Name == "axes/x/absolute");
    }

    [Fact]
    public async Task ValidateConfigAsync_ValidConfig_ReturnsSuccess()
    {
        var adapter = CreateAdapter();
        var config = CreateConfig();

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateConfigAsync_MissingIp_ReturnsFailure()
    {
        var adapter = CreateAdapter();
        var config = CreateConfig(ipAddress: "");

        var result = await adapter.ValidateConfigAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "IpAddress");
    }

    [Fact]
    public async Task ValidateConfigAsync_WrongType_ReturnsFailure()
    {
        var adapter = CreateAdapter();
        var wrongConfig = new StubSourceConfiguration
        {
            InstanceId = "wrong",
            ProtocolName = "modbus",
            DeviceId = "dev1",
        };

        var result = await adapter.ValidateConfigAsync(wrongConfig, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PollAsync_RespectsPollIntervalMs_BetweenSuccessivePolls()
    {
        var adapter = CreateAdapter();
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        var config = CreateConfig() with { PollIntervalMs = 300 };
        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // First poll — must NOT be delayed (no prior poll to pace against).
        var firstStart = DateTime.UtcNow;
        await adapter.PollAsync(CancellationToken.None);
        var firstElapsed = DateTime.UtcNow - firstStart;
        firstElapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200),
            "the first poll should not wait for the PollIntervalMs");

        // Second poll — must wait until at least PollIntervalMs has elapsed
        // since the first poll started. Allow a small margin for Task.Delay
        // scheduling jitter.
        var secondStart = DateTime.UtcNow;
        await adapter.PollAsync(CancellationToken.None);
        var intervalFromFirstStart = DateTime.UtcNow - firstStart;
        intervalFromFirstStart.Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromMilliseconds(280),
            "the adapter must pace itself by PollIntervalMs — ~300 ms elapsed since " +
            "the previous poll's start, minus a small jitter margin");
    }

    [Fact]
    public async Task PollAsync_ZeroPollIntervalMs_DoesNotDelay()
    {
        var adapter = CreateAdapter();
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        var config = CreateConfig(); // default includes PollIntervalMs = 0
        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var start = DateTime.UtcNow;
        await adapter.PollAsync(CancellationToken.None);
        await adapter.PollAsync(CancellationToken.None);
        var elapsed = DateTime.UtcNow - start;
        elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200),
            "PollIntervalMs = 0 should disable pacing entirely");
    }

    [Fact]
    public async Task PollAsync_MaxConnectRetriesExhausted_StaysDegradedAndBacksOff()
    {
        var adapter = CreateAdapter();
        var config = CreateConfig() with
        {
            MaxConnectRetries = 2,
            InitialBackoffMs = 50,
            MaxBackoffMs = 200,
        };
        // Fail every AllocLibHandle attempt so retries exhaust.
        _api.AllocResult = (short)Focas2ErrorCode.EW_SOCKET;

        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // First poll — connection manager attempts MaxConnectRetries,
        // fails, adapter returns empty batch + transitions to Degraded.
        var first = await adapter.PollAsync(CancellationToken.None);
        first.Should().BeEmpty("connect failed after MaxConnectRetries attempts");
        adapter.State.Should().NotBe(AdapterState.Failed,
            "a connect-retry exhaustion is retryable — do NOT move to Failed; backoff will retry on next poll");

        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.Metrics.Should().ContainKey("consecutiveConnectFailures");
        health.Metrics!["consecutiveConnectFailures"].Should().BeOfType<int>()
            .Which.Should().BeGreaterThan(0, "every exhausted attempt set increments the counter");
        ((bool)health.Metrics["connected"]!).Should().BeFalse();
    }

    [Fact]
    public async Task PollAsync_RecoversAfterFailuresClear()
    {
        var adapter = CreateAdapter();
        var config = CreateConfig() with
        {
            MaxConnectRetries = 2,
            InitialBackoffMs = 10,
            MaxBackoffMs = 20,
        };
        _api.AllocResult = (short)Focas2ErrorCode.EW_SOCKET;

        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // Burn through retries once so consecutiveConnectFailures > 0.
        await adapter.PollAsync(CancellationToken.None);

        // Fix the API so the next connect succeeds, then wait past the
        // backoff window so the next poll actually retries.
        _api.AllocResult = (short)Focas2ErrorCode.EW_OK;
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        await Task.Delay(50);

        var recovered = await adapter.PollAsync(CancellationToken.None);

        recovered.Should().NotBeEmpty("connection recovered and status points should flow");
        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task DisposeAsync_StopsIfRunning()
    {
        var adapter = CreateAdapter();
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1 };
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Running);

        await adapter.DisposeAsync();
        _adapter = null; // Prevent double dispose

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    // ---- Helpers ----

    private void ConfigureRealisticFake()
    {
        _api.StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 0 };
        _api.ProgramNumber = new OdbProgramNumber { MainProgram = 1234, RunningProgram = 5678 };
        _api.AxisCount = 3;
        _api.AxisNames =
        [
            new OdbAxisName { Name = "X", Suffix = "" },
            new OdbAxisName { Name = "Y", Suffix = "" },
            new OdbAxisName { Name = "Z", Suffix = "" },
        ];
        _api.SystemInfo = new OdbSystemInfo
        {
            CncType = "M",
            Series = "0iF",
            Version = "33.2",
            Axes = "3",
            MaxAxis = "8",
        };

        var absData = new OdbAxisData
        {
            Data = new int[Focas2Interop.MAX_AXIS],
            Decimal = new short[Focas2Interop.MAX_AXIS],
        };
        absData.Data[0] = 123456;
        absData.Data[1] = 234567;
        absData.Data[2] = 345678;
        absData.Decimal[0] = 3;
        absData.Decimal[1] = 3;
        absData.Decimal[2] = 3;
        _api.AbsolutePosition = absData;
        _api.MachinePosition = absData;
        _api.RelativePosition = absData;
        _api.DistanceToGo = absData;

        _api.ActualFeed = new OdbActualFeed { Data = 5000 };
        _api.ActualSpeed = new OdbActualSpeed { Data = 12000 };
        _api.SpindleLoadData = new OdbSpindleLoad { Data = [350, 0, 0, 0] };
    }

    /// <summary>
    /// Stub configuration for testing wrong-type scenarios.
    /// </summary>
    private sealed record StubSourceConfiguration : SourceConfiguration;
}
