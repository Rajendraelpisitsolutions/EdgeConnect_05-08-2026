// ============================================================================
// File: Adapters/AdapterStateTransitionsTests.cs
// Covers: A2 "state machine transitions" — which AdapterState transitions are
//         legal. Exhaustive coverage of the transition table.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Adapters;

public sealed class AdapterStateTransitionsTests
{
    [Theory]
    // Created → Initializing, Blocked, Failed
    [InlineData(AdapterState.Created, AdapterState.Initializing, true)]
    [InlineData(AdapterState.Created, AdapterState.Blocked, true)]
    [InlineData(AdapterState.Created, AdapterState.Failed, true)]
    [InlineData(AdapterState.Created, AdapterState.Running, false)]
    [InlineData(AdapterState.Created, AdapterState.Stopped, false)]

    // Initializing → Initialized, Failed
    [InlineData(AdapterState.Initializing, AdapterState.Initialized, true)]
    [InlineData(AdapterState.Initializing, AdapterState.Failed, true)]
    [InlineData(AdapterState.Initializing, AdapterState.Running, false)]

    // Initialized → Starting, Stopped, Failed
    [InlineData(AdapterState.Initialized, AdapterState.Starting, true)]
    [InlineData(AdapterState.Initialized, AdapterState.Stopped, true)]
    [InlineData(AdapterState.Initialized, AdapterState.Failed, true)]
    [InlineData(AdapterState.Initialized, AdapterState.Running, false)]

    // Starting → Running, Failed
    [InlineData(AdapterState.Starting, AdapterState.Running, true)]
    [InlineData(AdapterState.Starting, AdapterState.Failed, true)]
    [InlineData(AdapterState.Starting, AdapterState.Stopped, false)]

    // Running → Degraded, Stopping, Failed
    [InlineData(AdapterState.Running, AdapterState.Degraded, true)]
    [InlineData(AdapterState.Running, AdapterState.Stopping, true)]
    [InlineData(AdapterState.Running, AdapterState.Failed, true)]
    [InlineData(AdapterState.Running, AdapterState.Stopped, false)]
    [InlineData(AdapterState.Running, AdapterState.Initialized, false)]

    // Degraded → Running, Stopping, Failed
    [InlineData(AdapterState.Degraded, AdapterState.Running, true)]
    [InlineData(AdapterState.Degraded, AdapterState.Stopping, true)]
    [InlineData(AdapterState.Degraded, AdapterState.Failed, true)]
    [InlineData(AdapterState.Degraded, AdapterState.Stopped, false)]

    // Stopping → Stopped, Failed
    [InlineData(AdapterState.Stopping, AdapterState.Stopped, true)]
    [InlineData(AdapterState.Stopping, AdapterState.Failed, true)]
    [InlineData(AdapterState.Stopping, AdapterState.Running, false)]

    // Stopped → Starting, Initializing
    [InlineData(AdapterState.Stopped, AdapterState.Starting, true)]
    [InlineData(AdapterState.Stopped, AdapterState.Initializing, true)]
    [InlineData(AdapterState.Stopped, AdapterState.Running, false)]

    // Failed → Initializing (recovery), Stopped
    [InlineData(AdapterState.Failed, AdapterState.Initializing, true)]
    [InlineData(AdapterState.Failed, AdapterState.Stopped, true)]
    [InlineData(AdapterState.Failed, AdapterState.Running, false)]

    // Blocked → Initializing (license reinstated), Stopped
    [InlineData(AdapterState.Blocked, AdapterState.Initializing, true)]
    [InlineData(AdapterState.Blocked, AdapterState.Stopped, true)]
    [InlineData(AdapterState.Blocked, AdapterState.Running, false)]
    public void IsAllowed_ReturnsExpected(AdapterState from, AdapterState to, bool expected)
    {
        AdapterStateTransitions.IsAllowed(from, to).Should().Be(expected);
    }

    [Fact]
    public void GetAllowedTransitions_Created_ContainsExpectedTargets()
    {
        var allowed = AdapterStateTransitions.GetAllowedTransitions(AdapterState.Created);

        allowed.Should().BeEquivalentTo(new[]
        {
            AdapterState.Initializing,
            AdapterState.Blocked,
            AdapterState.Failed,
        });
    }

    [Fact]
    public void GetAllowedTransitions_Running_ContainsExpectedTargets()
    {
        var allowed = AdapterStateTransitions.GetAllowedTransitions(AdapterState.Running);

        allowed.Should().BeEquivalentTo(new[]
        {
            AdapterState.Degraded,
            AdapterState.Stopping,
            AdapterState.Failed,
        });
    }

    [Fact]
    public void IsAllowed_SelfTransitionIsRejected()
    {
        foreach (AdapterState state in System.Enum.GetValues<AdapterState>())
        {
            AdapterStateTransitions.IsAllowed(state, state)
                .Should().BeFalse($"self-transition for {state} should not be allowed");
        }
    }
}
