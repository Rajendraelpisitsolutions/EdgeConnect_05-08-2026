// ============================================================================
// File: MTConnectSourceAdapterTests.cs
// Purpose: Lifecycle + behavior tests for MTConnectSourceAdapter against a
//          FakeMTConnectClient. No live Agent required.
// ============================================================================

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.MTConnect.Tests;

public sealed class MTConnectSourceAdapterTests : IAsyncDisposable
{
    private readonly FakeMTConnectClient _fake = new();
    private MTConnectSourceAdapter? _adapter;

    private MTConnectSourceAdapter CreateAdapter(string instanceId = "mtc-test")
    {
        _adapter = new MTConnectSourceAdapter(
            instanceId,
            NullLogger<MTConnectSourceAdapter>.Instance,
            gatewayIdentity: null,
            clientFactory: _ => _fake);
        return _adapter;
    }

    private static MTConnectSourceConfiguration CreateConfig(
        string instanceId = "mtc-test",
        string agentBaseUrl = "http://localhost:5000/") => new()
    {
        InstanceId = instanceId,
        ProtocolName = "mtconnect",
        DeviceId = "cnc1",
        AgentBaseUrl = agentBaseUrl,
        // Disable pacing for unit tests. Dedicated tests cover pacing.
        PollIntervalMs = 0,
        InitialBackoffMs = 10,
        MaxBackoffMs = 40,
        DegradeAfterConsecutiveFailures = 2,
    };

    public async ValueTask DisposeAsync()
    {
        if (_adapter is not null)
        {
            await _adapter.DisposeAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_ValidConfig_TransitionsToInitialized()
    {
        var adapter = CreateAdapter();
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Initialized);
    }

    [Fact]
    public async Task InitializeAsync_WrongConfigType_FailsAndThrows()
    {
        var adapter = CreateAdapter();
        var wrong = new StubSourceConfiguration { InstanceId = "x", ProtocolName = "focas2", DeviceId = "x" };

        var act = () => adapter.InitializeAsync(wrong, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MTConnectSourceConfiguration*");
        adapter.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task ReconfigureAsync_DefaultImpl_StopsAndRestartsWithNewConfig()
    {
        // Pins the ISourceAdapter.ReconfigureAsync default-implementation
        // contract for MTConnect — no behavioural regression. Adapter ends
        // in Running on the NEW config.
        // Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
        var adapter = CreateAdapter();
        ISourceAdapter via = adapter;
        await via.InitializeAsync(CreateConfig(), CancellationToken.None);
        await via.StartAsync(CancellationToken.None);

        var newConfig = CreateConfig(agentBaseUrl: "http://localhost:5099/");
        await via.ReconfigureAsync(newConfig, CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task StartAsync_TransitionsToRunning()
    {
        var adapter = CreateAdapter();
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task PollAsync_HappyPath_EmitsCanonicalPoints()
    {
        var adapter = CreateAdapter();
        _fake.CurrentResponse = File.ReadAllText(Path.Combine("TestData", "sample-current.xml"));
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().NotBeEmpty();
        points.Should().Contain(p => p.TagName == "status/run_state");
        points.Should().Contain(p => p.TagName == "axes/x/absolute");
        adapter.State.Should().Be(AdapterState.Running);
        _fake.GetProbeCallCount.Should().Be(1, "probe runs once on the first successful poll");
    }

    [Fact]
    public async Task PollAsync_ProbeRunsOnce_NotOnEverySubsequentPoll()
    {
        var adapter = CreateAdapter();
        _fake.CurrentResponse = File.ReadAllText(Path.Combine("TestData", "sample-current.xml"));
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        await adapter.PollAsync(CancellationToken.None);
        await adapter.PollAsync(CancellationToken.None);
        await adapter.PollAsync(CancellationToken.None);

        _fake.GetProbeCallCount.Should().Be(1);
        _fake.GetCurrentCallCount.Should().Be(3);
    }

    [Fact]
    public async Task PollAsync_HttpFailure_RecordsFailureAndReturnsEmpty()
    {
        var adapter = CreateAdapter();
        _fake.CurrentException = new HttpRequestException("connection refused");
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().BeEmpty();
        adapter.State.Should().BeOneOf(AdapterState.Running, AdapterState.Degraded);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        ((long)health.Metrics!["pollFailures"]!).Should().Be(1);
        ((int)health.Metrics!["consecutiveFailures"]!).Should().Be(1);
    }

    [Fact]
    public async Task PollAsync_ConsecutiveFailures_TransitionToDegraded()
    {
        var adapter = CreateAdapter();
        _fake.CurrentException = new HttpRequestException("connection refused");
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // DegradeAfter=2 in CreateConfig — two failures should trip degradation.
        await adapter.PollAsync(CancellationToken.None);
        await Task.Delay(50);   // wait out backoff
        await adapter.PollAsync(CancellationToken.None);
        await Task.Delay(50);
        await adapter.PollAsync(CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Degraded);
    }

    [Fact]
    public async Task PollAsync_RecoveryAfterFailures_ReturnsToRunning()
    {
        var adapter = CreateAdapter();
        _fake.CurrentException = new HttpRequestException("down");
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // Drive to Degraded.
        for (var i = 0; i < 3; i++)
        {
            await adapter.PollAsync(CancellationToken.None);
            await Task.Delay(60);
        }
        adapter.State.Should().Be(AdapterState.Degraded);

        // Heal the fake and wait out backoff.
        _fake.CurrentException = null;
        _fake.CurrentResponse = File.ReadAllText(Path.Combine("TestData", "sample-current.xml"));
        await Task.Delay(100);

        var points = await adapter.PollAsync(CancellationToken.None);
        points.Should().NotBeEmpty();
        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task PollAsync_RespectsPollIntervalMs()
    {
        var adapter = CreateAdapter();
        _fake.CurrentResponse = File.ReadAllText(Path.Combine("TestData", "sample-current.xml"));
        var config = CreateConfig() with { PollIntervalMs = 250 };
        await adapter.InitializeAsync(config, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var firstStart = DateTime.UtcNow;
        await adapter.PollAsync(CancellationToken.None);
        await adapter.PollAsync(CancellationToken.None);

        var elapsed = DateTime.UtcNow - firstStart;
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(230),
            "second poll waits ~250 ms after the first poll started, minus a jitter margin");
    }

    [Fact]
    public async Task ValidateConfigAsync_ValidHttpsUrl_ReturnsSuccess()
    {
        var adapter = CreateAdapter();
        var cfg = CreateConfig(agentBaseUrl: "https://agent.example.com:5001/");

        var result = await adapter.ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateConfigAsync_FileScheme_ReturnsFailure()
    {
        var adapter = CreateAdapter();
        var cfg = CreateConfig(agentBaseUrl: "file:///tmp/agent");

        var result = await adapter.ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "AgentBaseUrl");
    }

    [Fact]
    public async Task ValidateConfigAsync_WrongType_ReturnsFailure()
    {
        var adapter = CreateAdapter();
        var wrong = new StubSourceConfiguration { InstanceId = "x", ProtocolName = "focas2", DeviceId = "x" };

        var result = await adapter.ValidateConfigAsync(wrong, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task BrowseTagsAsync_ReturnsTheKnownTagSet()
    {
        var adapter = CreateAdapter();
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);

        var tags = await adapter.BrowseTagsAsync(CancellationToken.None);

        tags.Should().NotBeEmpty();
        tags.Should().Contain(t => t.Name == "status/run_state");
        tags.Should().Contain(t => t.Name == "axes/x/absolute");
    }

    [Fact]
    public async Task DisposeAsync_StopsIfRunning()
    {
        var adapter = CreateAdapter();
        await adapter.InitializeAsync(CreateConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Running);

        await adapter.DisposeAsync();
        _adapter = null;

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    /// <summary>Stub for wrong-type configuration scenarios.</summary>
    private sealed record StubSourceConfiguration : SourceConfiguration;
}
