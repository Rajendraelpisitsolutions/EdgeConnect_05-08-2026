using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

public class EthernetIpConnectionManagerTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    private static EthernetIpSourceConfiguration Config(int threshold = 3, int initialBackoff = 100, int maxBackoff = 100, int resetMs = 1000) =>
        new()
        {
            InstanceId = "eip-1",
            ProtocolName = "ethernetip",
            DeviceId = "dev-1",
            DeviceClass = "plc",
            Host = "10.0.0.1",
            CpuFamily = EthernetIpCpuFamily.ControlLogix,
            CircuitBreakerThreshold = threshold,
            InitialBackoffMs = initialBackoff,
            MaxBackoffMs = maxBackoff,
            CircuitBreakerResetMs = resetMs,
        };

    [Fact]
    public async Task EnsureConnected_Success_ReturnsTrue()
    {
        var fake = new FakeEthernetIpClient();
        var mgr = new EthernetIpConnectionManager(fake, Config(), "eip-1", NullLogger.Instance, new ManualTimeProvider(Start));

        (await mgr.EnsureConnectedAsync(CancellationToken.None)).Should().BeTrue();
        mgr.IsConnected.Should().BeTrue();
        mgr.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task EnsureConnected_RepeatedFailures_OpensBreaker()
    {
        var fake = new FakeEthernetIpClient { ConnectShouldFail = true };
        var time = new ManualTimeProvider(Start);
        var mgr = new EthernetIpConnectionManager(fake, Config(threshold: 3), "eip-1", NullLogger.Instance, time);

        for (var i = 0; i < 3; i++)
        {
            (await mgr.EnsureConnectedAsync(CancellationToken.None)).Should().BeFalse();
            time.Advance(TimeSpan.FromMilliseconds(200)); // past the 100ms backoff window
        }

        mgr.ConsecutiveFailures.Should().BeGreaterThanOrEqualTo(3);
        mgr.BreakerState.Should().Be(EthernetIpBreakerState.Open);
    }

    [Fact]
    public async Task Breaker_Open_SuppressesConnectWithinResetWindow()
    {
        var fake = new FakeEthernetIpClient { ConnectShouldFail = true };
        var time = new ManualTimeProvider(Start);
        var mgr = new EthernetIpConnectionManager(fake, Config(threshold: 2, resetMs: 5000), "eip-1", NullLogger.Instance, time);

        for (var i = 0; i < 2; i++)
        {
            await mgr.EnsureConnectedAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMilliseconds(200));
        }
        mgr.BreakerState.Should().Be(EthernetIpBreakerState.Open);

        var connectsBefore = fake.ConnectCount;
        // Within the 5s reset window — connect attempt is suppressed (no new attempt).
        (await mgr.EnsureConnectedAsync(CancellationToken.None)).Should().BeFalse();
        fake.ConnectCount.Should().Be(connectsBefore);
    }

    [Fact]
    public async Task Breaker_AfterResetWindow_RecoversOnSuccessfulProbe()
    {
        var fake = new FakeEthernetIpClient { ConnectShouldFail = true };
        var time = new ManualTimeProvider(Start);
        var mgr = new EthernetIpConnectionManager(fake, Config(threshold: 2, resetMs: 1000), "eip-1", NullLogger.Instance, time);

        for (var i = 0; i < 2; i++)
        {
            await mgr.EnsureConnectedAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMilliseconds(200));
        }
        mgr.BreakerState.Should().Be(EthernetIpBreakerState.Open);

        // Peer recovers; advance past the reset window → half-open probe succeeds.
        fake.ConnectShouldFail = false;
        time.Advance(TimeSpan.FromMilliseconds(1200));

        (await mgr.EnsureConnectedAsync(CancellationToken.None)).Should().BeTrue();
        mgr.BreakerState.Should().Be(EthernetIpBreakerState.Closed);
        mgr.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task HandleFatalTransportError_DropsConnectionAndCountsFailure()
    {
        var fake = new FakeEthernetIpClient();
        var time = new ManualTimeProvider(Start);
        var mgr = new EthernetIpConnectionManager(fake, Config(), "eip-1", NullLogger.Instance, time);
        await mgr.EnsureConnectedAsync(CancellationToken.None);
        mgr.IsConnected.Should().BeTrue();

        mgr.HandleFatalTransportError(new EthernetIpFatalException("ETHERNETIP.READ_FAILED", "lost"));

        mgr.IsConnected.Should().BeFalse();
        mgr.ConsecutiveFailures.Should().Be(1);
    }
}
