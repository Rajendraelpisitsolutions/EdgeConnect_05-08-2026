// ============================================================================
// Tests: BrotherHttpConnectionManager — consecutive-failure counter +
//        "ever connected" flag + Q4 fault-threshold semantics.
// ============================================================================

using System;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

public sealed class BrotherHttpConnectionManagerTests
{
    [Fact]
    public void Constructor_ZeroOrNegativeThreshold_Throws()
    {
        Action act = () => _ = new BrotherHttpConnectionManager(0, "id", NullLogger.Instance);
        act.Should().Throw<ArgumentException>().WithParameterName("faultThreshold");
    }

    [Fact]
    public void InitialState_NotConnected_NoFailures()
    {
        var cm = new BrotherHttpConnectionManager(3, "id", NullLogger.Instance);

        cm.EverConnected.Should().BeFalse();
        cm.ConsecutiveFailures.Should().Be(0);
        cm.ShouldFault.Should().BeFalse();
    }

    [Fact]
    public void RecordHealthSuccess_FlipsEverConnected_ClearsFailures()
    {
        var cm = new BrotherHttpConnectionManager(3, "id", NullLogger.Instance);
        cm.RecordHealthFailure();
        cm.RecordHealthFailure();

        cm.RecordHealthSuccess();

        cm.EverConnected.Should().BeTrue();
        cm.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void RecordHealthFailure_BelowThreshold_DoesNotFault()
    {
        var cm = new BrotherHttpConnectionManager(3, "id", NullLogger.Instance);

        cm.RecordHealthFailure();
        cm.RecordHealthFailure();

        cm.ShouldFault.Should().BeFalse();
    }

    [Fact]
    public void RecordHealthFailure_AtThreshold_FaultsImmediately()
    {
        var cm = new BrotherHttpConnectionManager(3, "id", NullLogger.Instance);

        cm.RecordHealthFailure();
        cm.RecordHealthFailure();
        cm.RecordHealthFailure();   // 3rd consecutive

        cm.ShouldFault.Should().BeTrue();
        cm.ConsecutiveFailures.Should().Be(3);
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var cm = new BrotherHttpConnectionManager(3, "id", NullLogger.Instance);
        cm.RecordHealthSuccess();
        cm.RecordHealthFailure();
        cm.RecordHealthFailure();

        cm.Reset();

        cm.EverConnected.Should().BeFalse();
        cm.ConsecutiveFailures.Should().Be(0);
    }
}
