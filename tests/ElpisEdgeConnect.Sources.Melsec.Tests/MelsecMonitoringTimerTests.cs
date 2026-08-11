// ============================================================================
// Tests: MelsecMonitoringTimer — ADR-0033 Rule 6 ceil-up encoding (never
//        shortens a supplied timeout) + client/device timeout coherence.
// ============================================================================

using System;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Melsec.Tests;

public class MelsecMonitoringTimerTests
{
    [Theory]
    [InlineData(0, 0, 0, false)]        // 0 = wait indefinitely
    [InlineData(250, 1, 250, false)]    // exact unit boundary
    [InlineData(1000, 4, 1000, false)]  // exact multiple
    [InlineData(1100, 5, 1250, true)]   // ceil UP (never 1000) — the key case
    [InlineData(1, 1, 250, true)]       // smallest non-zero ceils to one unit
    public void Encode_ceils_up_and_never_shortens(int ms, int units, int effectiveMs, bool rounded)
    {
        var encoding = MelsecMonitoringTimer.Encode(ms);

        encoding.Units.Should().Be((ushort)units);
        encoding.EffectiveMs.Should().Be(effectiveMs);
        encoding.Rounded.Should().Be(rounded);
        encoding.EffectiveMs.Should().BeGreaterThanOrEqualTo(ms, "ceil-up must never shorten the supplied timeout");
    }

    [Fact]
    public void Encode_rejects_negative()
    {
        var act = () => MelsecMonitoringTimer.Encode(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Encode_rejects_value_above_max_units()
    {
        var tooLarge = (MelsecMonitoringTimer.MaxUnits * MelsecMonitoringTimer.UnitMs) + 1;
        var act = () => MelsecMonitoringTimer.Encode(tooLarge);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TryValidateCoherence_accepts_socket_timeout_at_or_above_monitoring_timer()
    {
        MelsecMonitoringTimer.TryValidateCoherence(monitoringTimerMs: 4000, requestTimeoutMs: 5000, out var error)
            .Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateCoherence_rejects_socket_timeout_below_monitoring_timer()
    {
        MelsecMonitoringTimer.TryValidateCoherence(monitoringTimerMs: 4000, requestTimeoutMs: 3000, out var error)
            .Should().BeFalse();
        error.Should().Contain("shorter than the encoded monitoring timer");
    }

    [Fact]
    public void TryValidateCoherence_infinite_monitoring_timer_is_always_coherent()
    {
        // 0 = device waits indefinitely; the client socket timeout is the only bound.
        MelsecMonitoringTimer.TryValidateCoherence(monitoringTimerMs: 0, requestTimeoutMs: 100, out var error)
            .Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateCoherence_uses_ceiled_effective_ms()
    {
        // 1100 ms ceils to 1250 ms; a 1200 ms socket timeout is therefore incoherent.
        MelsecMonitoringTimer.TryValidateCoherence(monitoringTimerMs: 1100, requestTimeoutMs: 1200, out _)
            .Should().BeFalse();
    }
}
