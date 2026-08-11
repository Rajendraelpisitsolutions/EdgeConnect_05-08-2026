// ============================================================================
// File: Routing/RetryStateMachineTests.cs
// Purpose: Pin tests for RetryStateMachine — attempt counter, success
//          resets, CanRetry gating.
// Milestone: C3 Commit 2 (phase 3).
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class RetryStateMachineTests
{
    private static DeliveryPolicyConfig Policy(int maxRetries = 3)
        => new()
        {
            MaxRetries = maxRetries,
            InitialBackoffMs = 10,
            MaxBackoffMs = 10_000,
            BackoffMultiplier = 2.0,
            JitterPercent = 0,
        };

    [Fact]
    public void NewMachine_HasZeroAttempts()
    {
        var m = new RetryStateMachine(Policy(), () => 0.5);

        m.Attempt.Should().Be(0);
        m.CanRetry.Should().BeTrue();
    }

    [Fact]
    public void RecordFailure_IncrementsAttemptAndReturnsBackoff()
    {
        var m = new RetryStateMachine(Policy(), () => 0.5);

        var b1 = m.RecordFailureAndComputeBackoff();
        var b2 = m.RecordFailureAndComputeBackoff();

        m.Attempt.Should().Be(2);
        b1.TotalMilliseconds.Should().Be(10);
        b2.TotalMilliseconds.Should().Be(20);
    }

    [Fact]
    public void Retry_Success_ResetsCounter()
    {
        var m = new RetryStateMachine(Policy(), () => 0.5);
        m.RecordFailureAndComputeBackoff();
        m.RecordFailureAndComputeBackoff();

        m.RecordSuccess();

        m.Attempt.Should().Be(0);
        m.CanRetry.Should().BeTrue();
    }

    [Fact]
    public void CanRetry_FalseAfterReachingMaxRetries()
    {
        var m = new RetryStateMachine(Policy(maxRetries: 2), () => 0.5);
        m.RecordFailureAndComputeBackoff();
        m.RecordFailureAndComputeBackoff();

        m.CanRetry.Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsAttempt()
    {
        var m = new RetryStateMachine(Policy(), () => 0.5);
        m.RecordFailureAndComputeBackoff();

        m.Reset();

        m.Attempt.Should().Be(0);
    }
}
