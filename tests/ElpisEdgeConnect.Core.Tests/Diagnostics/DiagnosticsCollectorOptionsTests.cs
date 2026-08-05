// ============================================================================
// File: Diagnostics/DiagnosticsCollectorOptionsTests.cs
// Purpose: Pin the Validate() behavior, especially the paramName attribution
//          fix for C4 minor finding #1.A. Before the fix, every out-of-range
//          retention reported paramName = "RouteEventRetention" regardless
//          of which one was actually invalid.
// Reference: docs/PHASE2_ENTRY.md carry-forward C4 finding 1.A
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class DiagnosticsCollectorOptionsTests
{
    [Fact]
    public void Validate_RouteEventRetentionZero_ThrowsWithRouteParamName()
    {
        var opts = new DiagnosticsCollectorOptions { RouteEventRetention = 0 };
        var act = () => opts.Validate();
        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be(nameof(DiagnosticsCollectorOptions.RouteEventRetention));
    }

    [Fact]
    public void Validate_SinkEventRetentionZero_ThrowsWithSinkParamName()
    {
        var opts = new DiagnosticsCollectorOptions { SinkEventRetention = 0 };
        var act = () => opts.Validate();
        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be(nameof(DiagnosticsCollectorOptions.SinkEventRetention));
    }

    [Fact]
    public void Validate_BackpressureEventRetentionZero_ThrowsWithBackpressureParamName()
    {
        var opts = new DiagnosticsCollectorOptions { BackpressureEventRetention = 0 };
        var act = () => opts.Validate();
        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be(nameof(DiagnosticsCollectorOptions.BackpressureEventRetention));
    }

    [Fact]
    public void Validate_AllPositive_ReturnsClampedInstance()
    {
        var opts = new DiagnosticsCollectorOptions
        {
            RouteEventRetention = DiagnosticsConstants.MaxEventRetention + 100,
            SinkEventRetention = 10,
            BackpressureEventRetention = 20,
        };

        var validated = opts.Validate();

        validated.RouteEventRetention.Should().Be(DiagnosticsConstants.MaxEventRetention);
        validated.SinkEventRetention.Should().Be(10);
        validated.BackpressureEventRetention.Should().Be(20);
    }
}
