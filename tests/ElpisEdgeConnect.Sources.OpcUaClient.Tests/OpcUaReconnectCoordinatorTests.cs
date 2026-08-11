// ============================================================================
// Tests: OpcUaReconnectCoordinatorTests — pin the PR 6a reconnect
//        coordinator's state-machine + counter discipline.
//
//        Invariants pinned (per PR 6a plan + amendments, user lock
//        2026-05-29):
//          1. Healthy keep-alive does nothing
//          2. Bad keep-alive triggers BeginReconnectAsync + raises
//             StateChanged(EnteredReconnect=true)
//          3. Concurrent bad keep-alives are de-duped via single-flight
//             guard (currentlyReconnecting)
//          4. Transfer outcome → counter increments, LastSuccessfulReconnectUtc
//             populated, LastReconnectMode = Transfer, StateChanged(false)
//          5. Recreate outcome → counter increments, LastReconnectMode =
//             Recreate, keep-alive re-wired on new session
//          6. Failed outcome → counter increments, LastReconnectMode = Failed,
//             LastSuccessfulReconnectUtc UNCHANGED
//          7. Disposal unsubscribes ReconnectCompleted + disposes wrapper
//          8. Single-flight clears after completion — next bad keep-alive
//             can re-trigger
//
//        The substituted IOpcUaReconnectHandlerWrapper lets us verify the
//        coordinator's reactions WITHOUT standing up a real OPC stack.
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3, §1.3.5, §2.5
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Opc.Ua;
using Opc.Ua.Client;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaReconnectCoordinatorTests
{
    private static OpcUaReconnectCoordinator NewCoordinator(out IOpcUaReconnectHandlerWrapper wrapper)
    {
        wrapper = Substitute.For<IOpcUaReconnectHandlerWrapper>();
        return new OpcUaReconnectCoordinator(wrapper, NullLogger.Instance);
    }

    private static KeepAliveEventArgs HealthyKeepAlive() =>
        new(ServiceResult.Good, ServerState.Running, DateTime.UtcNow);

    private static KeepAliveEventArgs BadKeepAlive() =>
        new(new ServiceResult(StatusCodes.BadCommunicationError), ServerState.Unknown, DateTime.UtcNow);

    // ─── Healthy keep-alive ───────────────────────────────────────────

    [Fact]
    public void HealthyKeepAlive_DoesNotTriggerReconnect()
    {
        var coordinator = NewCoordinator(out var wrapper);
        var session = Substitute.For<ISession>();
        coordinator.Attach(session);

        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, HealthyKeepAlive());

        wrapper.DidNotReceive().BeginReconnectAsync(Arg.Any<ISession>(), Arg.Any<CancellationToken>());
        coordinator.GetCounters().CurrentlyReconnecting.Should().BeFalse();
    }

    // ─── Bad keep-alive triggers reconnect ────────────────────────────

    [Fact]
    public void BadKeepAlive_TriggersReconnectAndRaisesEnteredReconnect()
    {
        var coordinator = NewCoordinator(out var wrapper);
        var session = Substitute.For<ISession>();
        coordinator.Attach(session);

        CoordinatorStateChange? captured = null;
        coordinator.StateChanged += change => captured = change;

        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, BadKeepAlive());

        wrapper.Received(1).BeginReconnectAsync(session, Arg.Any<CancellationToken>());
        captured.Should().NotBeNull();
        captured!.EnteredReconnect.Should().BeTrue();
        captured.Result.Should().BeNull();
        coordinator.GetCounters().CurrentlyReconnecting.Should().BeTrue();
    }

    [Fact]
    public void BadKeepAlive_WhileAlreadyReconnecting_IsDeduped()
    {
        var coordinator = NewCoordinator(out var wrapper);
        var session = Substitute.For<ISession>();
        coordinator.Attach(session);

        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, BadKeepAlive());
        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, BadKeepAlive());
        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, BadKeepAlive());

        // Single-flight — only one BeginReconnectAsync, even with 3 keep-alives.
        wrapper.Received(1).BeginReconnectAsync(session, Arg.Any<CancellationToken>());
    }

    // ─── Reconnect outcomes ───────────────────────────────────────────

    [Fact]
    public void TransferOutcome_IncrementsCounter_PopulatesLastSuccess_SetsLastMode()
    {
        var coordinator = NewCoordinator(out var wrapper);
        var session = Substitute.For<ISession>();
        coordinator.Attach(session);

        var newSession = Substitute.For<ISession>();
        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, BadKeepAlive());

        var before = DateTime.UtcNow.AddSeconds(-1);
        wrapper.ReconnectCompleted += Raise.Event<Action<ReconnectResult>>(new ReconnectResult
        {
            Mode = ReconnectMode.Transfer,
            NewSession = newSession,
        });

        var counters = coordinator.GetCounters();
        counters.ReconnectsViaTransfer.Should().Be(1);
        counters.ReconnectsViaRecreate.Should().Be(0);
        counters.ReconnectsFailed.Should().Be(0);
        counters.CurrentlyReconnecting.Should().BeFalse();
        counters.LastSuccessfulReconnectUtc.Should().NotBeNull();
        counters.LastSuccessfulReconnectUtc!.Value.Should().BeOnOrAfter(before);
        counters.LastReconnectMode.Should().Be(ReconnectMode.Transfer);
    }

    [Fact]
    public void RecreateOutcome_IncrementsRecreateCounter_AndRewiresKeepAliveOnNewSession()
    {
        var coordinator = NewCoordinator(out var wrapper);
        var oldSession = Substitute.For<ISession>();
        coordinator.Attach(oldSession);

        var newSession = Substitute.For<ISession>();
        oldSession.KeepAlive += Raise.Event<KeepAliveEventHandler>(oldSession, BadKeepAlive());

        wrapper.ReconnectCompleted += Raise.Event<Action<ReconnectResult>>(new ReconnectResult
        {
            Mode = ReconnectMode.Recreate,
            NewSession = newSession,
        });

        coordinator.GetCounters().ReconnectsViaRecreate.Should().Be(1);
        coordinator.GetCounters().LastReconnectMode.Should().Be(ReconnectMode.Recreate);

        // Recreate path — a bad keep-alive on the NEW session should now
        // trigger a fresh reconnect attempt (coordinator re-wired itself).
        newSession.KeepAlive += Raise.Event<KeepAliveEventHandler>(newSession, BadKeepAlive());
        wrapper.Received(1).BeginReconnectAsync(newSession, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FailedOutcome_IncrementsFailedCounter_DoesNotUpdateLastSuccess()
    {
        var coordinator = NewCoordinator(out var wrapper);
        var session = Substitute.For<ISession>();
        coordinator.Attach(session);

        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, BadKeepAlive());

        wrapper.ReconnectCompleted += Raise.Event<Action<ReconnectResult>>(new ReconnectResult
        {
            Mode = ReconnectMode.Failed,
            Error = new InvalidOperationException("retry budget exhausted"),
        });

        var counters = coordinator.GetCounters();
        counters.ReconnectsFailed.Should().Be(1);
        counters.ReconnectsViaTransfer.Should().Be(0);
        counters.ReconnectsViaRecreate.Should().Be(0);
        counters.CurrentlyReconnecting.Should().BeFalse();
        counters.LastSuccessfulReconnectUtc.Should().BeNull(
            "LastSuccessfulReconnectUtc tracks SUCCESS only, not failures (amendment #1).");
        counters.LastReconnectMode.Should().Be(ReconnectMode.Failed);
    }

    // ─── Single-flight clears + StateChanged on completion ────────────

    [Fact]
    public void Completion_ClearsSingleFlight_AndEmitsLeavingStateChange()
    {
        var coordinator = NewCoordinator(out var wrapper);
        var session = Substitute.For<ISession>();
        coordinator.Attach(session);

        var changes = new System.Collections.Generic.List<CoordinatorStateChange>();
        coordinator.StateChanged += changes.Add;

        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, BadKeepAlive());
        var newSession = Substitute.For<ISession>();
        wrapper.ReconnectCompleted += Raise.Event<Action<ReconnectResult>>(new ReconnectResult
        {
            Mode = ReconnectMode.Transfer,
            NewSession = newSession,
        });

        // Two state changes: entering + leaving.
        changes.Should().HaveCount(2);
        changes[0].EnteredReconnect.Should().BeTrue();
        changes[1].EnteredReconnect.Should().BeFalse();
        changes[1].Result.Should().NotBeNull();
        changes[1].Result!.Mode.Should().Be(ReconnectMode.Transfer);

        coordinator.GetCounters().CurrentlyReconnecting.Should().BeFalse();

        // A subsequent bad keep-alive on the new session SHOULD trigger
        // another reconnect attempt — single-flight cleared.
        newSession.KeepAlive += Raise.Event<KeepAliveEventHandler>(newSession, BadKeepAlive());
        wrapper.Received(1).BeginReconnectAsync(newSession, Arg.Any<CancellationToken>());
    }

    // ─── Detach + Dispose ─────────────────────────────────────────────

    [Fact]
    public void Detach_UnsubscribesFromKeepAlive_NoMoreReconnectsTriggered()
    {
        var coordinator = NewCoordinator(out var wrapper);
        var session = Substitute.For<ISession>();
        coordinator.Attach(session);

        coordinator.Detach();
        session.KeepAlive += Raise.Event<KeepAliveEventHandler>(session, BadKeepAlive());

        wrapper.DidNotReceive().BeginReconnectAsync(Arg.Any<ISession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_DisposesWrapper()
    {
        var coordinator = NewCoordinator(out var wrapper);

        await coordinator.DisposeAsync();

        await wrapper.Received(1).DisposeAsync();
    }

    // ─── Initial counter snapshot ─────────────────────────────────────

    [Fact]
    public void GetCounters_BeforeAnyEvent_ReturnsZerosAndUnknownMode()
    {
        var coordinator = NewCoordinator(out _);

        var counters = coordinator.GetCounters();

        counters.ReconnectsViaTransfer.Should().Be(0);
        counters.ReconnectsViaRecreate.Should().Be(0);
        counters.ReconnectsFailed.Should().Be(0);
        counters.CurrentlyReconnecting.Should().BeFalse();
        counters.LastSuccessfulReconnectUtc.Should().BeNull();
        counters.LastReconnectMode.Should().Be(ReconnectMode.Unknown);
    }
}
