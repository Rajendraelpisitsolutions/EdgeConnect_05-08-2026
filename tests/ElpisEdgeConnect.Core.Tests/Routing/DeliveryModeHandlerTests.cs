// ============================================================================
// File: Routing/DeliveryModeHandlerTests.cs
// Purpose: Pin the small, explicit mapping from (DeliveryMode, retry state)
//          to PublishOutcome. Also pins that ExactlyOnce is absent from the
//          enum (compile-time guarantee per blueprint §19.7).
// Milestone: C3 Commit 2 (phase 3).
// ============================================================================

using System;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class DeliveryModeHandlerTests
{
    private static RetryStateMachine Machine(int maxRetries, int currentAttempt)
    {
        var m = new RetryStateMachine(
            new DeliveryPolicyConfig
            {
                MaxRetries = maxRetries,
                InitialBackoffMs = 10,
                MaxBackoffMs = 1000,
                BackoffMultiplier = 2.0,
                JitterPercent = 0,
            },
            () => 0.5);
        for (var i = 0; i < currentAttempt; i++)
        {
            m.RecordFailureAndComputeBackoff();
        }
        return m;
    }

    [Fact]
    public void AtMostOnce_Failure_Drops()
    {
        var outcome = DeliveryModeHandler.HandleFailure(
            DeliveryMode.AtMostOnce, Machine(maxRetries: 5, currentAttempt: 0));

        outcome.Should().Be(PublishOutcome.DroppedAndAdvanced);
    }

    [Fact]
    public void AtLeastOnce_Failure_Retries_WhenBudgetRemaining()
    {
        var outcome = DeliveryModeHandler.HandleFailure(
            DeliveryMode.AtLeastOnce, Machine(maxRetries: 5, currentAttempt: 2));

        outcome.Should().Be(PublishOutcome.RetryScheduled);
    }

    [Fact]
    public void AtLeastOnce_Failure_ExhaustedWhenBudgetGone()
    {
        var outcome = DeliveryModeHandler.HandleFailure(
            DeliveryMode.AtLeastOnce, Machine(maxRetries: 2, currentAttempt: 2));

        outcome.Should().Be(PublishOutcome.RetriesExhausted);
    }

    [Fact]
    public void ExactlyOnce_Rejected_AtEnumLevel()
    {
        // Structural pin: ExactlyOnce is NOT a member of the enum per
        // blueprint §19.7. If someone adds it, this test fails and forces
        // the blueprint revision conversation.
        var members = Enum.GetNames<DeliveryMode>();
        members.Should().NotContain("ExactlyOnce");
        members.Should().BeEquivalentTo(new[] { "AtMostOnce", "AtLeastOnce" });
    }

    [Fact]
    public void ExactlyOnce_Rejected_AtRuntime()
    {
        // If the type system is ever bypassed (e.g., casting an int), the
        // handler throws immediately rather than silently defaulting.
        var act = () => DeliveryModeHandler.HandleFailure(
            (DeliveryMode)99, Machine(maxRetries: 1, currentAttempt: 0));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
