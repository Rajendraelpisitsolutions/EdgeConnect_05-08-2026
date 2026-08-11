// ============================================================================
// File: Routing/RetryBackoffTests.cs
// Purpose: Pure pin tests for RetryBackoff.Compute — doubling, capping, and
//          jitter range. No Random — jitter is fed as a fixed sample so the
//          envelope can be asserted precisely.
// Milestone: C3 Commit 2 (phase 3).
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class RetryBackoffTests
{
    private static DeliveryPolicyConfig Policy(
        int initialMs = 100,
        int maxMs = 30_000,
        double multiplier = 2.0,
        int jitterPercent = 0)
        => new()
        {
            InitialBackoffMs = initialMs,
            MaxBackoffMs = maxMs,
            BackoffMultiplier = multiplier,
            JitterPercent = jitterPercent,
        };

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 200)]
    [InlineData(3, 400)]
    [InlineData(4, 800)]
    [InlineData(5, 1600)]
    public void Retry_BackoffDoubles_UpToCap(int attempt, int expectedMs)
    {
        // jitterSample 0.5 → zero jitter, exact envelope.
        var delay = RetryBackoff.Compute(attempt, Policy(), 0.5);

        delay.TotalMilliseconds.Should().Be(expectedMs);
    }

    [Fact]
    public void Retry_Backoff_CapsAtMax()
    {
        var policy = Policy(initialMs: 100, maxMs: 1000, multiplier: 2.0);

        // attempt 20 would be 100 * 2^19 = ~52 million ms without the cap.
        var delay = RetryBackoff.Compute(20, policy, 0.5);

        delay.TotalMilliseconds.Should().Be(1000);
    }

    [Theory]
    [InlineData(0.0, 90.0)]    // -1 * 10% => 90%
    [InlineData(0.5, 100.0)]   // zero jitter
    [InlineData(1.0, 110.0)]   // +1 * 10% => 110%
    public void Retry_Jitter_StaysWithinAllowedRange(double sample, double expectedMs)
    {
        var policy = Policy(jitterPercent: 10);

        var delay = RetryBackoff.Compute(1, policy, sample);

        delay.TotalMilliseconds.Should().BeApproximately(expectedMs, 0.0001);
    }

    [Fact]
    public void Retry_Jitter_RejectsOutOfRangeSample()
    {
        var policy = Policy();

        var act = () => RetryBackoff.Compute(1, policy, 1.5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
