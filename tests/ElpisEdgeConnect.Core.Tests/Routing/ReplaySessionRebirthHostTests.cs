// ============================================================================
// File: Routing/ReplaySessionRebirthHostTests.cs
// Covers: K1.3 slice 4 — the coalescing, epoch-gated ReplaySessionRebirthHost in
//         isolation. Proves the reverse (sink → Core) rebirth handshake: a request for
//         the current session+epoch is accepted (and wakes the driver); duplicates
//         coalesce; a superseded session, an already-passed epoch, and a not-yet-minted
//         epoch are all ignored deterministically; and Promote advances the gate and
//         drops any obsolete pending request.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.md §R5.4;
//            …-v3.1-amendment.md §A2; …-v3.2-amendment.md §C3.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class ReplaySessionRebirthHostTests
{
    private static readonly ReplaySessionId Session = ReplaySessionId.Create(7);
    private static readonly ReplayEpochId Epoch0 = ReplayEpochId.Create(0);

    private static RebirthRequest Request(ReplaySessionId session, ReplayEpochId epoch) =>
        RebirthRequest.Create(session, epoch, RebirthReason.SchemaChange);

    [Fact]
    public async Task Accepts_Request_For_Current_Session_And_Epoch_And_Wakes()
    {
        using var host = new ReplaySessionRebirthHost(Session, Epoch0);

        await host.RequestRebirthAsync(Request(Session, Epoch0), CancellationToken.None);

        // The wake latch was released by the accept — a driver waiting on it returns immediately
        // (assert the wait completes well within a timeout; TryTakePending is the authoritative state).
        var wake = host.WaitForRebirthAsync(CancellationToken.None);
        var completed = await Task.WhenAny(wake, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(wake);

        host.TryTakePending(out var taken).Should().BeTrue();
        taken.SessionId.Should().Be(Session);
        taken.Epoch.Should().Be(Epoch0);
    }

    [Fact]
    public async Task Coalesces_Duplicate_Requests_For_The_Same_Epoch()
    {
        using var host = new ReplaySessionRebirthHost(Session, Epoch0);

        await host.RequestRebirthAsync(Request(Session, Epoch0), CancellationToken.None);
        await host.RequestRebirthAsync(Request(Session, Epoch0), CancellationToken.None);
        await host.RequestRebirthAsync(Request(Session, Epoch0), CancellationToken.None);

        host.TryTakePending(out _).Should().BeTrue();  // exactly ONE pending survives
        host.TryTakePending(out _).Should().BeFalse(); // …the duplicates coalesced away
    }

    [Fact]
    public async Task Ignores_A_Superseded_Session()
    {
        using var host = new ReplaySessionRebirthHost(Session, Epoch0);

        await host.RequestRebirthAsync(Request(ReplaySessionId.Create(99), Epoch0), CancellationToken.None);

        host.TryTakePending(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Ignores_An_Already_Passed_Lower_Epoch()
    {
        using var host = new ReplaySessionRebirthHost(Session, ReplayEpochId.Create(3));

        await host.RequestRebirthAsync(Request(Session, ReplayEpochId.Create(2)), CancellationToken.None);

        host.TryTakePending(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Ignores_A_Not_Yet_Minted_Higher_Epoch()
    {
        using var host = new ReplaySessionRebirthHost(Session, Epoch0);

        // Core owns epoch advancement; a request above the authoritative epoch cannot be honoured.
        await host.RequestRebirthAsync(Request(Session, ReplayEpochId.Create(5)), CancellationToken.None);

        host.TryTakePending(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Promote_Advances_The_Gate_And_Drops_An_Obsolete_Pending_Request()
    {
        using var host = new ReplaySessionRebirthHost(Session, Epoch0);
        await host.RequestRebirthAsync(Request(Session, Epoch0), CancellationToken.None);

        var epoch1 = ReplayEpochId.Create(1);
        host.Promote(Session, epoch1);

        host.CurrentEpoch.Should().Be(epoch1);
        host.TryTakePending(out _).Should().BeFalse(); // the epoch-0 request is obsolete post-rebirth

        // After promotion the gate accepts the NEW epoch and ignores the OLD one.
        await host.RequestRebirthAsync(Request(Session, Epoch0), CancellationToken.None);
        host.TryTakePending(out _).Should().BeFalse();

        await host.RequestRebirthAsync(Request(Session, epoch1), CancellationToken.None);
        host.TryTakePending(out _).Should().BeTrue();
    }

    [Fact]
    public async Task Disposed_Host_Ignores_Requests()
    {
        var host = new ReplaySessionRebirthHost(Session, Epoch0);
        host.Dispose();

        await host.RequestRebirthAsync(Request(Session, Epoch0), CancellationToken.None);

        host.TryTakePending(out _).Should().BeFalse();
    }
}
