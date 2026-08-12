// ============================================================================
// File: Routing/RouteStateTransitionValidatorTests.cs
// Covers: every legal transition from blueprint §19.9, every illegal
//         transition rejected, identity transitions rejected.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.9, PHASE1_EXECUTION_PLAN.md C3
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class RouteStateTransitionValidatorTests
{
    /// <summary>
    /// The exact set of legal transitions from blueprint §19.9. Pinned as
    /// inline data so the table here MUST match the table in
    /// <see cref="RouteStateTransitionValidator"/> — any drift fails the
    /// test, which forces blueprint and code to stay in sync.
    /// </summary>
    public static IEnumerable<object[]> LegalTransitions =>
        new (RouteState From, RouteState To)[]
        {
            // Configured →
            (RouteState.Configured, RouteState.Starting),
            (RouteState.Configured, RouteState.Stopping),
            (RouteState.Configured, RouteState.Failed),
            (RouteState.Configured, RouteState.Blocked),
            // Starting →
            (RouteState.Starting, RouteState.Running),
            (RouteState.Starting, RouteState.Failed),
            (RouteState.Starting, RouteState.Blocked),
            (RouteState.Starting, RouteState.Stopping),
            // Running →
            (RouteState.Running, RouteState.Degraded),
            (RouteState.Running, RouteState.Stopping),
            (RouteState.Running, RouteState.Failed),
            // Degraded →
            (RouteState.Degraded, RouteState.Draining),
            (RouteState.Degraded, RouteState.Running),
            (RouteState.Degraded, RouteState.Stopping),
            (RouteState.Degraded, RouteState.Failed),
            // Draining →
            (RouteState.Draining, RouteState.Running),
            (RouteState.Draining, RouteState.Degraded),
            (RouteState.Draining, RouteState.Stopping),
            (RouteState.Draining, RouteState.Failed),
            // Stopping →
            (RouteState.Stopping, RouteState.Stopped),
            (RouteState.Stopping, RouteState.Failed),
            // Stopped →
            (RouteState.Stopped, RouteState.Configured),
            // Failed →
            (RouteState.Failed, RouteState.Stopping),
            // Blocked →
            (RouteState.Blocked, RouteState.Stopping),
        }.Select(t => new object[] { t.From, t.To });

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public void IsAllowed_ReturnsTrue_ForLegalTransitions(RouteState from, RouteState to)
    {
        RouteStateTransitionValidator.IsAllowed(from, to).Should().BeTrue(
            $"the transition {from} → {to} is part of the blueprint §19.9 lifecycle.");
    }

    [Fact]
    public void IsAllowed_ReturnsFalse_ForIdentityTransitions()
    {
        foreach (var state in Enum.GetValues<RouteState>())
        {
            RouteStateTransitionValidator.IsAllowed(state, state).Should().BeFalse(
                $"identity transition {state} → {state} is not allowed; the lifecycle " +
                "manager must short-circuit before calling the validator.");
        }
    }

    [Fact]
    public void IsAllowed_ReturnsFalse_ForIllegalTransitions()
    {
        // Build the legal set, then assert every other (from, to) pair is illegal.
        var legalPairs = new HashSet<(RouteState, RouteState)>(
            LegalTransitions.Select(arr => ((RouteState)arr[0], (RouteState)arr[1])));

        foreach (var from in Enum.GetValues<RouteState>())
        {
            foreach (var to in Enum.GetValues<RouteState>())
            {
                if (from == to)
                {
                    continue; // covered by IdentityTransitions test
                }
                var allowed = RouteStateTransitionValidator.IsAllowed(from, to);
                var inLegalSet = legalPairs.Contains((from, to));
                allowed.Should().Be(inLegalSet,
                    $"transition {from} → {to} is {(inLegalSet ? "legal" : "illegal")} per blueprint §19.9.");
            }
        }
    }

    [Theory]
    [InlineData(RouteState.Stopped, RouteState.Running)]
    [InlineData(RouteState.Stopped, RouteState.Failed)]
    [InlineData(RouteState.Running, RouteState.Configured)]
    [InlineData(RouteState.Running, RouteState.Starting)]
    [InlineData(RouteState.Failed, RouteState.Running)]
    [InlineData(RouteState.Blocked, RouteState.Running)]
    public void IsAllowed_KnownIllegalSamples_AreRejected(RouteState from, RouteState to)
    {
        RouteStateTransitionValidator.IsAllowed(from, to).Should().BeFalse(
            $"{from} → {to} is explicitly forbidden by blueprint §19.9.");
    }

    [Fact]
    public void AllowedFrom_ReturnsExpectedSet_ForRunning()
    {
        // Pin the Running state's allowed-out set explicitly.
        var allowed = RouteStateTransitionValidator.AllowedFrom(RouteState.Running);
        allowed.Should().BeEquivalentTo(new[]
        {
            RouteState.Degraded,
            RouteState.Stopping,
            RouteState.Failed,
        });
    }

    [Fact]
    public void AllowedFrom_TerminalStates_HaveLimitedExits()
    {
        RouteStateTransitionValidator.AllowedFrom(RouteState.Failed)
            .Should().BeEquivalentTo(new[] { RouteState.Stopping });
        RouteStateTransitionValidator.AllowedFrom(RouteState.Blocked)
            .Should().BeEquivalentTo(new[] { RouteState.Stopping });
        RouteStateTransitionValidator.AllowedFrom(RouteState.Stopped)
            .Should().BeEquivalentTo(new[] { RouteState.Configured });
    }
}
