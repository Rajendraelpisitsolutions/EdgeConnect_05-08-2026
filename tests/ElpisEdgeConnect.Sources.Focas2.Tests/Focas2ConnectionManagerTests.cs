// ============================================================================
// File: Focas2ConnectionManagerTests.cs
// Purpose: Unit tests for Focas2ConnectionManager — handle lifecycle, backoff,
//          system info caching.
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

public sealed class Focas2ConnectionManagerTests
{
    private static Focas2SourceConfiguration CreateConfig(
        bool keepAlive = true,
        int maxRetries = 1,
        int initialBackoffMs = 60_000,
        int dataTimeoutSeconds = 30) => new()
    {
        InstanceId = "cm-test",
        ProtocolName = "focas2",
        DeviceId = "dev1",
        IpAddress = "192.168.1.1",
        Port = 8193,
        KeepAlive = keepAlive,
        MaxConnectRetries = maxRetries,
        InitialBackoffMs = initialBackoffMs,
        MaxBackoffMs = 120_000,
        DataTimeoutSeconds = dataTimeoutSeconds,
    };

    [Theory]
    [InlineData(0.0, 5_000)]      // jitter floor → exactly half the base delay
    [InlineData(0.999, 9_995)]    // jitter ceiling → ~full base delay
    public void IncrementBackoff_AppliesEqualJitter_WithinHalfToFullBand(double jitter, int expectedMs)
    {
        // Alloc fails → connect exhausts → IncrementBackoff with injected jitter.
        var api = new FakeFocas2Api { AllocResult = (short)Focas2ErrorCode.EW_SOCKET };
        var config = CreateConfig(initialBackoffMs: 10_000); // base = 10s on first failure
        var mgr = new Focas2ConnectionManager(api, config, "test", NullLogger.Instance, () => jitter);

        mgr.EnsureConnected().Should().BeFalse();

        // Equal jitter: backoff = base/2 + jitter*(base/2), so always in [5s, 10s).
        mgr.LastBackoffMs.Should().Be(expectedMs);
        mgr.LastBackoffMs.Should().BeInRange(5_000, 10_000);
    }

    [Fact]
    public void IncrementBackoff_DifferentJitter_ProducesDifferentDelays()
    {
        var config = CreateConfig(initialBackoffMs: 10_000);
        var low = new Focas2ConnectionManager(
            new FakeFocas2Api { AllocResult = (short)Focas2ErrorCode.EW_SOCKET },
            config, "a", NullLogger.Instance, () => 0.1);
        var high = new Focas2ConnectionManager(
            new FakeFocas2Api { AllocResult = (short)Focas2ErrorCode.EW_SOCKET },
            config, "b", NullLogger.Instance, () => 0.9);

        low.EnsureConnected();
        high.EnsureConnected();

        // De-correlation: two sources with different jitter must not reconnect
        // at the same instant (this is what breaks the reconnect storm).
        low.LastBackoffMs.Should().NotBe(high.LastBackoffMs);
    }

    [Fact]
    public void EnsureConnected_AppliesConfiguredDataTimeout()
    {
        var api = new FakeFocas2Api
        {
            StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 0 },
        };
        var mgr = new Focas2ConnectionManager(
            api, CreateConfig(dataTimeoutSeconds: 30), "test", NullLogger.Instance);

        mgr.EnsureConnected().Should().BeTrue();

        api.SetDataTimeoutCallCount.Should().Be(1);
        api.LastDataTimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void EnsureConnected_DataTimeoutZero_SkipsSetDataTimeout()
    {
        var api = new FakeFocas2Api
        {
            StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 0 },
        };
        var mgr = new Focas2ConnectionManager(
            api, CreateConfig(dataTimeoutSeconds: 0), "test", NullLogger.Instance);

        mgr.EnsureConnected().Should().BeTrue();

        api.SetDataTimeoutCallCount.Should().Be(0);
    }

    [Fact]
    public void EnsureConnected_SetDataTimeoutFails_StillConnects()
    {
        var api = new FakeFocas2Api
        {
            StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 0 },
            SetDataTimeoutResult = (short)Focas2ErrorCode.EW_HANDLE,
        };
        var mgr = new Focas2ConnectionManager(
            api, CreateConfig(dataTimeoutSeconds: 30), "test", NullLogger.Instance);

        // A controller that does not honour cnc_setdtimeout must still connect.
        mgr.EnsureConnected().Should().BeTrue();
        mgr.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void EnsureConnected_SetDataTimeoutThrows_StillConnects()
    {
        var api = new FakeFocas2Api
        {
            StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 0 },
            // Simulate a P/Invoke failure (e.g. native entry point missing).
            SetDataTimeoutException = new System.EntryPointNotFoundException("cnc_setdtimeout"),
        };
        var mgr = new Focas2ConnectionManager(
            api, CreateConfig(dataTimeoutSeconds: 30), "test", NullLogger.Instance);

        // A thrown timeout call must be swallowed — the connect is still valid.
        mgr.EnsureConnected().Should().BeTrue();
        mgr.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void EnsureConnected_Success_ReturnsTrue()
    {
        var api = new FakeFocas2Api
        {
            StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 0 },
        };
        var mgr = new Focas2ConnectionManager(api, CreateConfig(), "test", NullLogger.Instance);

        var result = mgr.EnsureConnected();

        result.Should().BeTrue();
        mgr.IsConnected.Should().BeTrue();
        mgr.Handle.Should().BeGreaterThan(0);
        api.AllocCallCount.Should().Be(1);
    }

    [Fact]
    public void EnsureConnected_AllocFails_ReturnsFalse()
    {
        var api = new FakeFocas2Api
        {
            AllocResult = (short)Focas2ErrorCode.EW_SOCKET,
        };
        var mgr = new Focas2ConnectionManager(api, CreateConfig(), "test", NullLogger.Instance);

        var result = mgr.EnsureConnected();

        result.Should().BeFalse();
        mgr.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void EnsureConnected_StatinfoFails_FreesHandleAndRetries()
    {
        var api = new FakeFocas2Api
        {
            StatusInfoResult = (short)Focas2ErrorCode.EW_HANDLE,
        };
        var config = CreateConfig(maxRetries: 2);
        var mgr = new Focas2ConnectionManager(api, config, "test", NullLogger.Instance);

        var result = mgr.EnsureConnected();

        result.Should().BeFalse();
        mgr.IsConnected.Should().BeFalse();
        // Should have allocated twice (2 retries), freed the handle each time
        api.AllocCallCount.Should().Be(2);
        api.FreeCallCount.Should().Be(2);
    }

    [Fact]
    public void EnsureConnected_CachesStatInfo()
    {
        var expectedStatInfo = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 0 };
        var api = new FakeFocas2Api { StatusInfo = expectedStatInfo };
        var mgr = new Focas2ConnectionManager(api, CreateConfig(), "test", NullLogger.Instance);

        mgr.EnsureConnected();

        mgr.CachedStatInfo.Should().NotBeNull();
        mgr.CachedStatInfo!.Value.Run.Should().Be(3);
        mgr.CachedStatInfo!.Value.Aut.Should().Be(1);
    }

    [Fact]
    public void ConsumeCachedStatInfo_ReturnsAndClears()
    {
        var api = new FakeFocas2Api
        {
            StatusInfo = new OdbStatusInfo { Run = 3 },
        };
        var mgr = new Focas2ConnectionManager(api, CreateConfig(), "test", NullLogger.Instance);
        mgr.EnsureConnected();

        var first = mgr.ConsumeCachedStatInfo();

        first.Should().NotBeNull();
        first!.Value.Run.Should().Be(3);

        var second = mgr.ConsumeCachedStatInfo();

        second.Should().BeNull();
    }

    [Fact]
    public void Disconnect_FreesHandle()
    {
        var api = new FakeFocas2Api
        {
            StatusInfo = new OdbStatusInfo { Run = 3 },
        };
        var mgr = new Focas2ConnectionManager(api, CreateConfig(), "test", NullLogger.Instance);
        mgr.EnsureConnected();
        var handle = mgr.Handle;

        mgr.Disconnect();

        mgr.IsConnected.Should().BeFalse();
        api.FreeCallCount.Should().Be(1);
        api.LastFreedHandle.Should().Be(handle);
    }

    [Fact]
    public void Disconnect_WhenNotConnected_DoesNothing()
    {
        var api = new FakeFocas2Api();
        var mgr = new Focas2ConnectionManager(api, CreateConfig(), "test", NullLogger.Instance);

        mgr.Disconnect();

        api.FreeCallCount.Should().Be(0);
    }

    [Fact]
    public void ExponentialBackoff_SkipsRetryDuringCooldown()
    {
        var api = new FakeFocas2Api
        {
            AllocResult = (short)Focas2ErrorCode.EW_SOCKET,
        };
        // Use a large backoff so the second call is definitely within cooldown
        var config = CreateConfig(initialBackoffMs: 60_000);
        var mgr = new Focas2ConnectionManager(api, config, "test", NullLogger.Instance);

        // First attempt fails and sets backoff
        var first = mgr.EnsureConnected();
        first.Should().BeFalse();
        mgr.ConsecutiveFailures.Should().Be(1);

        // Second attempt should be skipped (within backoff window)
        var second = mgr.EnsureConnected();
        second.Should().BeFalse();
        // Should NOT have tried again (AllocCallCount should still be 1 from first attempt)
        api.AllocCallCount.Should().Be(1);
    }

    [Fact]
    public void EnsureSystemInfo_ReadsAndCaches()
    {
        var api = new FakeFocas2Api
        {
            StatusInfo = new OdbStatusInfo { Run = 3 },
            SystemInfo = new OdbSystemInfo
            {
                CncType = "M",
                Series = "0iF",
                Version = "33.2",
                Axes = "3",
                MaxAxis = "8",
            },
            AxisCount = 3,
        };
        var mgr = new Focas2ConnectionManager(api, CreateConfig(), "test", NullLogger.Instance);
        mgr.EnsureConnected();

        mgr.EnsureSystemInfo();

        mgr.SystemInfo.Should().NotBeNull();
        mgr.SystemInfo!.CncType.Should().Be("M");
        mgr.SystemInfo.Series.Should().Be("0iF");
        mgr.SystemInfo.AxisCount.Should().Be(3);
        mgr.SystemInfo.AxisNames.Should().HaveCount(3);

        // Calling again should not re-read (already cached)
        mgr.EnsureSystemInfo();
        // SystemInfo is still the same object reference
        mgr.SystemInfo.CncType.Should().Be("M");
    }

    [Fact]
    public void HandleFatalError_DisconnectsAndIncrementsBackoff()
    {
        var api = new FakeFocas2Api
        {
            StatusInfo = new OdbStatusInfo { Run = 3 },
        };
        var mgr = new Focas2ConnectionManager(api, CreateConfig(), "test", NullLogger.Instance);
        mgr.EnsureConnected();
        mgr.IsConnected.Should().BeTrue();

        mgr.HandleFatalError();

        mgr.IsConnected.Should().BeFalse();
        mgr.ConsecutiveFailures.Should().Be(1);
        api.FreeCallCount.Should().Be(1);
    }
}
