// ============================================================================
// File: Routing/RouteLifecycleManagerTests.cs
// Purpose: Phase 5 pin tests for the centralized route lifecycle manager.
//          Covers: full legal transition table, illegal-transition rejection,
//          idempotent no-op behavior, degraded/draining/recovered sink
//          notifications, Blocked-is-startup-only, and Failed-only-to-Stopping.
// Milestone: C3 Commit 2 (phase 5).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class RouteLifecycleManagerTests
{
    private static (RouteLifecycleManager lm, RecordingRoutingDiagnostics diag) MakeAt(RouteState initial)
    {
        var diag = new RecordingRoutingDiagnostics();
        var lm = new RouteLifecycleManager("route-1", diag, initial);
        return (lm, diag);
    }

    /// <summary>
    /// The full legal transition table from blueprint §19.9. This data is
    /// duplicated from RouteStateTransitionValidator on purpose — if anyone
    /// edits the validator, this test must be updated alongside it. That is
    /// the whole point of the pin.
    /// </summary>
    public static IEnumerable<object[]> AllLegalTransitions()
    {
        var legal = new (RouteState From, RouteState To)[]
        {
            (RouteState.Configured, RouteState.Starting),
            (RouteState.Configured, RouteState.Stopping),
            (RouteState.Configured, RouteState.Failed),
            (RouteState.Configured, RouteState.Blocked),
            (RouteState.Starting, RouteState.Running),
            (RouteState.Starting, RouteState.Failed),
            (RouteState.Starting, RouteState.Blocked),
            (RouteState.Starting, RouteState.Stopping),
            (RouteState.Running, RouteState.Degraded),
            (RouteState.Running, RouteState.Stopping),
            (RouteState.Running, RouteState.Failed),
            (RouteState.Degraded, RouteState.Draining),
            (RouteState.Degraded, RouteState.Running),
            (RouteState.Degraded, RouteState.Stopping),
            (RouteState.Degraded, RouteState.Failed),
            (RouteState.Draining, RouteState.Running),
            (RouteState.Draining, RouteState.Degraded),
            (RouteState.Draining, RouteState.Stopping),
            (RouteState.Draining, RouteState.Failed),
            (RouteState.Stopping, RouteState.Stopped),
            (RouteState.Stopping, RouteState.Failed),
            (RouteState.Stopped, RouteState.Configured),
            (RouteState.Failed, RouteState.Stopping),
            (RouteState.Blocked, RouteState.Stopping),
        };
        foreach (var (f, t) in legal)
        {
            yield return new object[] { f, t };
        }
    }

    [Theory]
    [MemberData(nameof(AllLegalTransitions))]
    public void RouteLifecycle_AllValidTransitions(RouteState from, RouteState to)
    {
        var (lm, diag) = MakeAt(from);

        var changed = lm.TryTransitionTo(to);

        changed.Should().BeTrue();
        lm.State.Should().Be(to);
        diag.Events.Should().ContainSingle(e => e == $"route:route-1:{from}->{to}");
    }

    [Theory]
    [InlineData(RouteState.Configured, RouteState.Running)]   // must go through Starting
    [InlineData(RouteState.Running, RouteState.Starting)]     // no going backward
    [InlineData(RouteState.Running, RouteState.Blocked)]      // Blocked is startup-only
    [InlineData(RouteState.Stopped, RouteState.Running)]      // Stopped only → Configured
    [InlineData(RouteState.Failed, RouteState.Running)]       // Failed only → Stopping
    [InlineData(RouteState.Failed, RouteState.Configured)]
    [InlineData(RouteState.Blocked, RouteState.Running)]      // Blocked only → Stopping
    [InlineData(RouteState.Draining, RouteState.Starting)]
    public void RouteLifecycle_InvalidTransitionsRejected(RouteState from, RouteState to)
    {
        var (lm, diag) = MakeAt(from);

        var act = () => lm.TryTransitionTo(to);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{CoreErrors.RouteInvalidLifecycleTransition}*");
        lm.State.Should().Be(from);
        diag.Events.Should().BeEmpty();
    }

    [Fact]
    public void IdempotentTransition_DoesNotReemit()
    {
        var (lm, diag) = MakeAt(RouteState.Running);

        var first = lm.TryTransitionTo(RouteState.Running);

        first.Should().BeFalse();
        lm.State.Should().Be(RouteState.Running);
        diag.Events.Should().BeEmpty();
    }

    [Fact]
    public void Blocked_IsStartupOnly()
    {
        // Legal from Configured and Starting.
        var (a, _) = MakeAt(RouteState.Configured);
        a.TryTransitionTo(RouteState.Blocked).Should().BeTrue();

        var (b, _) = MakeAt(RouteState.Starting);
        b.TryTransitionTo(RouteState.Blocked).Should().BeTrue();

        // Illegal from every other state.
        foreach (var origin in new[]
        {
            RouteState.Running, RouteState.Degraded, RouteState.Draining,
            RouteState.Stopping, RouteState.Stopped, RouteState.Failed, RouteState.Blocked,
        })
        {
            var (lm, _) = MakeAt(origin);
            var act = () => lm.TryTransitionTo(RouteState.Blocked);
            if (origin == RouteState.Blocked)
            {
                // Identity → idempotent no-op, returns false, no throw.
                act().Should().BeFalse();
            }
            else
            {
                act.Should().Throw<InvalidOperationException>(
                    $"Blocked must be reachable only from Configured/Starting, not from {origin}");
            }
        }
    }

    [Fact]
    public void Failed_IsTerminalUntilUnregister()
    {
        var (lm, _) = MakeAt(RouteState.Failed);

        // Only Stopping is legal from Failed.
        foreach (var target in Enum.GetValues<RouteState>())
        {
            if (target == RouteState.Failed)
            {
                continue; // identity
            }
            if (target == RouteState.Stopping)
            {
                continue; // legal — covered by AllValidTransitions
            }

            var (clean, _) = MakeAt(RouteState.Failed);
            var act = () => clean.TryTransitionTo(target);
            act.Should().Throw<InvalidOperationException>(
                $"Failed must not be able to transition to {target}");
        }
    }

    [Fact]
    public void NotifySinkDegraded_RunningToDegraded()
    {
        var (lm, diag) = MakeAt(RouteState.Running);

        lm.NotifySinkDegraded("sink-a");

        lm.State.Should().Be(RouteState.Degraded);
        diag.Events.Should().ContainSingle(e => e == "route:route-1:Running->Degraded");
    }

    [Fact]
    public void NotifySinkDegraded_IsNoOpWhenAlreadyDegraded()
    {
        var (lm, diag) = MakeAt(RouteState.Running);

        lm.NotifySinkDegraded("sink-a");
        lm.NotifySinkDegraded("sink-b");

        lm.State.Should().Be(RouteState.Degraded);
        // Only one route-level transition event.
        diag.Events.Count(e => e.StartsWith("route:", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void DegradedToDrainingToRunning_EmitsInOrder()
    {
        var (lm, diag) = MakeAt(RouteState.Running);

        lm.NotifySinkDegraded("sink-a");
        lm.NotifySinkDraining("sink-a");
        lm.NotifySinkRecovered("sink-a");

        lm.State.Should().Be(RouteState.Running);

        var routeEvents = diag.Events
            .Where(e => e.StartsWith("route:", StringComparison.Ordinal))
            .ToArray();
        routeEvents.Should().Equal(
            "route:route-1:Running->Degraded",
            "route:route-1:Degraded->Draining",
            "route:route-1:Draining->Running");
    }

    [Fact]
    public void MultipleSinks_DegradedStaysUntilAllRecover()
    {
        var (lm, _) = MakeAt(RouteState.Running);

        lm.NotifySinkDegraded("sink-a");
        lm.NotifySinkDegraded("sink-b");
        lm.State.Should().Be(RouteState.Degraded);

        lm.NotifySinkDraining("sink-a");
        lm.NotifySinkRecovered("sink-a");
        // sink-b still degraded → route must not return to Running.
        lm.State.Should().Be(RouteState.Degraded);

        lm.NotifySinkDraining("sink-b");
        lm.NotifySinkRecovered("sink-b");
        lm.State.Should().Be(RouteState.Running);
    }

    [Fact]
    public void NotifySinkDegraded_DrainingToDegraded_OnSinkReFailure()
    {
        // Pin for review fix 1.B: a sink that fails AGAIN while the route is
        // mid-drain must transition the route back to Degraded. The validator
        // allows Draining→Degraded; this test pins that the lifecycle manager
        // actually takes that path.
        var (lm, diag) = MakeAt(RouteState.Running);

        lm.NotifySinkDegraded("sink-a");                  // Running → Degraded
        lm.NotifySinkDraining("sink-a");                  // Degraded → Draining
        lm.State.Should().Be(RouteState.Draining);

        // Sink-a fails again mid-drain (e.g. network blip).
        lm.NotifySinkDegraded("sink-a");                  // Draining → Degraded
        lm.State.Should().Be(RouteState.Degraded);

        var routeEvents = diag.Events
            .Where(e => e.StartsWith("route:", StringComparison.Ordinal))
            .ToArray();
        routeEvents.Should().Equal(
            "route:route-1:Running->Degraded",
            "route:route-1:Degraded->Draining",
            "route:route-1:Draining->Degraded");
    }

    [Fact]
    public void StoppingToStopped_OnShutdown()
    {
        var (lm, diag) = MakeAt(RouteState.Running);

        lm.TryTransitionTo(RouteState.Stopping).Should().BeTrue();
        lm.TryTransitionTo(RouteState.Stopped).Should().BeTrue();

        diag.Events.Should().Equal(
            "route:route-1:Running->Stopping",
            "route:route-1:Stopping->Stopped");
    }
}
