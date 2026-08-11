// ============================================================================
// File: Session/SparkplugSessionActorRebirthTests.cs
// Purpose: Locks K3 slice-6 (pass 1) operational rebirth against a deterministic fake
//          transport (no broker): the two RebirthAsync branches (healthy in-place vs
//          transport-suspect new-CONNECT), same-session/increasing-epoch gating, the
//          async idle-disconnect -> coalesced Core rebirth request, the NCMD ->
//          HostCommand rebirth path, stale-generation suppression, and cross-source
//          coalescing. The bounded recovery budget + graceful End land in pass 2.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Configuration;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
using ElpisEdgeConnect.Sinks.SparkplugB.Session;
using ElpisEdgeConnect.Sinks.SparkplugB.Store;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using Org.Eclipse.Tahu.Protobuf;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;

public sealed class SparkplugSessionActorRebirthTests : IDisposable
{
    private const string Group = "PlantA";
    private const string Node = "gw-1";
    private static readonly DateTimeOffset Clock = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "k3-rebirth-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); } }
        catch { /* best effort */ }
    }

    // ==== Healthy in-place rebirth ====

    [Fact]
    public async Task Rebirth_HealthyTransport_ReusesConnection_RetainsBdSeq_AdvancesEpoch()
    {
        var (actor, fake, _) = await Born();
        var nbirthsBefore = NBirths(fake).Count;

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1)); // epoch advanced
        actor.CurrentBdSeq.Value.Should().Be(0);                 // bdSeq RETAINED (healthy)
        actor.CurrentGeneration.Should().Be(1);                  // same connection/generation
        actor.HasSession.Should().BeTrue();
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Replaying);
        actor.NextSeq.Should().Be(1);                            // re-birth NBIRTH consumed seq 0
        NBirths(fake).Count.Should().Be(nbirthsBefore + 1);      // re-emitted on the SAME connection (no new connect)
    }

    [Fact]
    public async Task Rebirth_HealthyTransport_ReEmitsNBirthSeq0_WithRetainedBdSeq()
    {
        var (actor, fake, _) = await Born();
        fake.Published.Clear();

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        // The re-birth NBIRTH carries seq=0 and the retained bdSeq=0 (byte-parity via K2).
        var expected = SparkplugPayloadEncoder.EncodeNBirth(
            SparkplugSequenceNumber.Create(0), SparkplugBirthDeathSequence.Create(0), bdSeqAlias: 1UL, Clock,
            actor.CurrentManifest!.Metrics, actor.CurrentManifest.AliasMap);
        NBirths(fake).Single().Should().Equal(expected);
    }

    [Fact]
    public async Task Rebirth_HealthyNBirthFails_IsFatal_Faults()
    {
        var (actor, fake, _) = await Born();
        fake.PublishReturnsFalse = true; // the re-birth NBIRTH send fails

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.BirthPublishFailed);

        actor.State.Should().Be(AdapterState.Failed); // healthy-transport rebirth NBIRTH failure is immediately fatal
    }

    // ==== Transport-suspect rebirth (new CONNECT + new bdSeq) ====

    [Fact]
    public async Task Rebirth_TransportSuspect_NewConnect_NewBdSeq_NewGeneration_RetiresOldClient()
    {
        var store = NewStore();
        var fake1 = new FakeTransport();
        var fake2 = new FakeTransport();
        var call = 0;
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => call++ == 0 ? (ISparkplugMqttTransport)fake1 : fake2, () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);

        await fake1.RaiseDisconnected(actor.CurrentGeneration); // drop → suspect (+ one coalesced rebirth request)
        actor.CurrentSessionSuspect.Should().BeTrue();

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        fake1.Disposed.Should().BeTrue();                 // old client abandoned (broker publishes its Will)
        fake2.Connected.Should().BeTrue();                // fresh CONNECT on the replacement client
        NBirths(fake2).Should().ContainSingle();          // fresh NBIRTH
        actor.CurrentBdSeq.Value.Should().Be(1);          // NEW bdSeq reserved for the new CONNECT
        actor.CurrentGeneration.Should().Be(2);           // new connection generation
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1));
        actor.CurrentSessionSuspect.Should().BeFalse();   // fresh handoff — no longer suspect
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Replaying);
    }

    // ==== Rebirth gating ====

    [Fact]
    public async Task Rebirth_WrongSession_FailsClosed()
    {
        var (actor, _, _) = await Born();

        await actor.Invoking(a => a.RebirthAsync(
                ReplaySessionRebirth.Create(ReplaySessionId.Create(999), ReplayEpochId.Create(1), StateOf(1)), CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.PublishSessionMismatch);

        actor.State.Should().Be(AdapterState.Failed);
    }

    [Theory]
    [InlineData(0)] // equal to the current epoch
    [InlineData(-1)] // below (encoded as 0 here; equal case covers non-increasing)
    public async Task Rebirth_NonIncreasingEpoch_FailsClosed(int epochDelta)
    {
        var (actor, _, _) = await Born(); // current epoch 0
        var epoch = Math.Max(0, epochDelta);

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch), CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.PublishEpochMismatch);

        actor.State.Should().Be(AdapterState.Failed);
    }

    // ==== Async idle disconnect -> coalesced Core rebirth ====

    [Fact]
    public async Task Disconnect_PostPromotion_RequestsOneCoalescedRebirth_Other()
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseDisconnected(actor.CurrentGeneration);
        await fake.RaiseDisconnected(actor.CurrentGeneration); // a repeat drop must coalesce

        actor.CurrentSessionSuspect.Should().BeTrue();
        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
    }

    [Fact]
    public async Task Disconnect_StaleGeneration_Ignored()
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseDisconnected(actor.CurrentGeneration + 99); // a retired client's delayed callback

        actor.CurrentSessionSuspect.Should().BeFalse(); // stale generation gate — no effect
        host.Requests.Should().BeEmpty();
    }

    // ==== NCMD -> HostCommand rebirth ====

    [Fact]
    public async Task NodeCommand_RebirthTrue_RequestsHostCommandRebirth_NoSuspect()
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());

        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.HostCommand);
        actor.CurrentSessionSuspect.Should().BeFalse(); // a host command does not mark the transport suspect
    }

    [Fact]
    public async Task NodeCommand_NotRebirth_NoRequest()
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseNodeCommand(actor.CurrentGeneration, NonRebirthCommand());

        host.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NodeCommand_StaleGeneration_Ignored()
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseNodeCommand(actor.CurrentGeneration + 99, RebirthCommand());

        host.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Disconnect_ThenNodeCommand_CoalesceToOneRequest()
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseDisconnected(actor.CurrentGeneration);          // requests (Other)
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // coalesced away

        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
    }

    // ==== B4: establishment-promotion drain (disconnect between promotion CAS and publication) ====

    [Fact]
    public async Task Establish_DisconnectAfterPromotionBeforePublish_DrainsExactlyOneRebirth()
    {
        var fake = new FakeTransport();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        // A disconnect lands after the promotion CAS but before _activeSession is published.
        actor.PostPromotionBarrier = () => fake.RaiseDisconnected(fake.Generation!.Value);

        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);

        actor.HasSession.Should().BeTrue();
        actor.CurrentSessionSuspect.Should().BeTrue();
        // No DATA arrival required — establishment drained exactly one Other rebirth request.
        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
    }

    // ==== B1: episode is per-rebirth, resettable, DATA-visible, failure-safe ====

    [Fact]
    public async Task Rebirth_Healthy_ThenSecondNodeCommand_StartsNewEpisode_QueuesSecondRequest()
    {
        var (actor, fake, host) = await Born();
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // NCMD #1 -> request 1
        host.Requests.Should().ContainSingle();

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy rebirth resets the episode

        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // NCMD #2 -> a NEW episode
        host.Requests.Should().HaveCount(2);
        host.Requests[1].Reason.Should().Be(RebirthReason.HostCommand);
        host.Requests[1].Epoch.Value.Should().Be(1); // against the newly authoritative epoch
    }

    [Fact]
    public async Task NodeCommand_Repeated_BeforeRebirth_CoalesceToOneRequest()
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());

        host.Requests.Should().ContainSingle(); // coalesced to one pending request
    }

    [Fact]
    public async Task HostRequestFailure_ReleasesClaim_AllowsLaterRebirthRequest()
    {
        var (actor, fake, host) = await Born();
        host.ThrowOnRequestCount = 1; // the first RequestRebirthAsync throws before acceptance

        await actor.Invoking(a => fake.RaiseNodeCommand(a.CurrentGeneration, RebirthCommand()))
            .Should().ThrowAsync<InvalidOperationException>();
        host.Requests.Should().BeEmpty(); // not accepted

        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // a later NCMD can requeue
        host.Requests.Should().ContainSingle(); // the connection is not permanently stuck
    }

    // ==== B2: atomic healthy-rebirth completion vs. a racing disconnect ====

    [Fact]
    public async Task Rebirth_DisconnectBeforeHealthyCompletion_PivotsToSuspect_NewConnect()
    {
        var (actor, fake1, fake2, host) = TwoFakeActor();
        await Begin(actor, host);
        // A disconnect lands after the re-birth NBIRTH but before the completion CAS.
        actor.PreRebirthCommitBarrier = () => fake1.RaiseDisconnected(1);

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        fake1.Disposed.Should().BeTrue();       // pivoted: old client abandoned
        fake2.Connected.Should().BeTrue();      // new CONNECT
        actor.CurrentBdSeq.Value.Should().Be(1); // new bdSeq
        actor.CurrentGeneration.Should().Be(2);  // new generation
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1));
        actor.CurrentSessionSuspect.Should().BeFalse();
    }

    [Fact]
    public async Task Rebirth_DisconnectDuringHealthyNBirth_PivotsToSuspect()
    {
        var (actor, fake1, fake2, host) = TwoFakeActor();
        await Begin(actor, host);
        fake1.OnPublishOnce = () => fake1.RaiseDisconnected(1); // drop DURING the re-birth NBIRTH publish

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        fake2.Connected.Should().BeTrue();       // pivoted to a new CONNECT
        actor.CurrentGeneration.Should().Be(2);
        actor.CurrentBdSeq.Value.Should().Be(1);
    }

    [Fact]
    public async Task Rebirth_NodeCommandThenDisconnect_UsesNewConnectionAndBdSeq()
    {
        var (actor, fake1, fake2, host) = TwoFakeActor();
        await Begin(actor, host);
        await fake1.RaiseNodeCommand(1, RebirthCommand()); // host command (healthy pending)
        await fake1.RaiseDisconnected(1);                  // then a transport loss -> suspect wins

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        fake2.Connected.Should().BeTrue();       // transport-suspect branch (new CONNECT), not healthy
        actor.CurrentGeneration.Should().Be(2);
        actor.CurrentBdSeq.Value.Should().Be(1);
    }

    [Fact]
    public async Task Rebirth_DisconnectAfterHealthyPromotion_RequestsAgainstNewEpoch()
    {
        var (actor, fake, host) = await Born();
        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy → epoch 1

        await fake.RaiseDisconnected(actor.CurrentGeneration);

        actor.CurrentSessionSuspect.Should().BeTrue();
        host.Requests.Should().ContainSingle().Which.Epoch.Value.Should().Be(1); // against the new epoch
    }

    // ==== B3: candidate-only suspect rebirth preserves the previous authority on failure ====

    [Theory]
    [InlineData("connect")]
    [InlineData("subscribe")]
    [InlineData("nbirth")]
    public async Task Rebirth_SuspectReplacementFails_PreservesPreviousAuthority(string failAt)
    {
        var (actor, fake1, fake2, host) = TwoFakeActor();
        await Begin(actor, host);
        await fake1.RaiseDisconnected(1); // suspect → the rebirth will take the new-CONNECT branch
        var prevManifestCount = actor.CurrentManifest!.Metrics.Length;
        switch (failAt)
        {
            case "connect": fake2.FailConnect = true; break;
            case "subscribe": fake2.FailSubscribe = true; break;
            case "nbirth": fake2.PublishReturnsFalse = true; break;
        }

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None))
            .Should().ThrowAsync<Exception>();

        actor.State.Should().Be(AdapterState.Failed);
        // The PREVIOUS authority is preserved (never erased by the failed candidate) — B3.
        actor.CurrentSessionId.Should().Be(ReplaySessionId.Create(1));
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(0)); // still the pre-rebirth epoch
        actor.CurrentBdSeq.Value.Should().Be(0);                 // previous bdSeq
        actor.CurrentManifest!.Metrics.Length.Should().Be(prevManifestCount);
    }

    // ==== R2.1: a healthy pending rebirth is not a transport failure ====

    [Fact]
    public async Task NodeCommand_PendingRebirth_StaysHealthy_RebirthReusesConnection()
    {
        var (actor, fake, _) = await Born();
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
        actor.CurrentSessionSuspect.Should().BeFalse(); // pending, but transport healthy

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        actor.CurrentGeneration.Should().Be(1);  // reused connection (healthy branch, not new-CONNECT)
        actor.CurrentBdSeq.Value.Should().Be(0);  // retained bdSeq
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1));
        fake.Disposed.Should().BeFalse();
    }

    // ==== R2.2: race-safe episode completion (a command during completion is not erased) ====

    [Fact]
    public async Task Rebirth_SecondNodeCommandDuringEpisodeCompletion_QueuesSecondRequest_NewEpoch()
    {
        var (actor, fake, host) = await Born();
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // NCMD #1 -> request 1 (epoch 0)
        // A second NCMD lands in the commit window (after the rebirth wins, before the new authority is finished).
        actor.PostRebirthCommitBarrier = () => fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        host.Requests.Should().HaveCount(2);          // the second command is NOT erased by the reset
        host.Requests[1].Reason.Should().Be(RebirthReason.HostCommand);
        host.Requests[1].Epoch.Value.Should().Be(1);  // queued against the newly authoritative epoch
    }

    [Fact]
    public async Task Rebirth_DisconnectDuringCommit_InstallsNewEpochSuspect_QueuesWake()
    {
        var (actor, fake, host) = await Born();
        // A disconnect lands in the commit window (after the rebirth wins, before the new authority is finished).
        actor.PostRebirthCommitBarrier = () => fake.RaiseDisconnected(actor.CurrentGeneration);

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1)); // the new authority IS installed
        actor.CurrentSessionSuspect.Should().BeTrue();           // ... but suspect (the drop was not lost)
        // The idle route is woken by a queued request against the NEW epoch (no DATA needed) — r3 R3.1.
        host.Requests.Should().ContainSingle().Which.Epoch.Value.Should().Be(1);
    }

    // ==== R2.3: the drained reason is preserved (and transport-suspect takes precedence) ====

    [Fact]
    public async Task Establish_NodeCommandBeforePublish_DrainsAsHostCommand()
    {
        var fake = new FakeTransport();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        actor.PostPromotionBarrier = () => fake.RaiseNodeCommand(fake.Generation!.Value, RebirthCommand());

        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);

        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.HostCommand);
        actor.CurrentSessionSuspect.Should().BeFalse(); // an NCMD-only episode is not suspect
    }

    [Fact]
    public async Task Establish_NodeCommandThenDisconnectBeforePublish_DrainsOnce_TransportSuspectWins()
    {
        var fake = new FakeTransport();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        actor.PostPromotionBarrier = async () =>
        {
            await fake.RaiseNodeCommand(fake.Generation!.Value, RebirthCommand()); // host command...
            await fake.RaiseDisconnected(fake.Generation!.Value);                  // ...then a transport loss
        };

        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);

        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other); // suspect precedence
        actor.CurrentSessionSuspect.Should().BeTrue();
    }

    // ==== R2.4: healthy-NBIRTH cancellation cleanup ====

    [Fact]
    public async Task Rebirth_HealthyNBirthPreCancelled_DoesNotSend_NotSuspect()
    {
        var (actor, fake, _) = await Born();
        var nbirthsBefore = NBirths(fake).Count;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        actor.CurrentSessionSuspect.Should().BeFalse();     // never entered the transport
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(0)); // prior epoch retained
        NBirths(fake).Count.Should().Be(nbirthsBefore);     // no re-birth NBIRTH sent
    }

    [Fact]
    public async Task Rebirth_HealthyNBirthInTransportCancellation_MarksSuspect_RetainsPriorEpoch_NotStuckRebirthing()
    {
        var (actor, fake, _) = await Born();
        using var cts = new CancellationTokenSource();
        fake.OnPublishOnce = () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; };

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        actor.State.Should().Be(AdapterState.Running);       // cancellation is not a coarse fault
        actor.CurrentSessionSuspect.Should().BeTrue();       // uncertain in-transport cancel -> suspect
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(0)); // candidate epoch NOT promoted
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Suspect); // not stranded in Rebirthing
    }

    // ==== Pass 2: bounded transport-recovery loop ====

    [Fact]
    public async Task Rebirth_TransportSuspect_RecoversWithinBudget_NoFault_DistinctBdSeqPerAttempt()
    {
        var fake0 = new FakeTransport();                       // initial birth (bdSeq 0)
        var failing = new FakeTransport { FailConnect = true }; // recovery attempt 1 fails (bdSeq 1)
        var good = new FakeTransport();                         // recovery attempt 2 succeeds (bdSeq 2)
        var fakes = new Queue<ISparkplugMqttTransport>(new ISparkplugMqttTransport[] { fake0, failing, good });
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration); // suspect

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        actor.State.Should().Be(AdapterState.Running);   // recovered within budget — no route fault
        actor.HasSession.Should().BeTrue();
        good.Connected.Should().BeTrue();
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1));
        actor.CurrentBdSeq.Value.Should().Be(2);         // the failed attempt consumed its own bdSeq (1), never reused
    }

    [Fact]
    public async Task Rebirth_TransportSuspect_ExhaustsBudget_Faults_PreservesPreviousAuthority()
    {
        var store = NewStore();
        var fake0 = new FakeTransport();
        var host = new CapturingHost();
        // Every recovery attempt fails to connect (budget 3).
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => fake0.Connected ? new FakeTransport { FailConnect = true } : fake0, () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None))
            .Should().ThrowAsync<Exception>();

        actor.State.Should().Be(AdapterState.Failed);            // terminal after the budget is exhausted
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(0)); // the previous authority is preserved
    }

    [Fact]
    public async Task Rebirth_Recovery_AbortedByStopDuringBackoff()
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport { FailConnect = true };
        var fakes = new Queue<ISparkplugMqttTransport>(
            new ISparkplugMqttTransport[] { fake0, failing, new FakeTransport(), new FakeTransport() });
        var host = new CapturingHost();
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        Func<TimeSpan, CancellationToken, Task> delay = async (_, ct) => { entered.TrySetResult(); await release.Task; };
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, delay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // attempt 1 fails → gate-released backoff
        await entered.Task;                                  // recovery is in the released-gate backoff
        await actor.StopAsync(CancellationToken.None);       // takes the gate, invalidates the recovery token
        release.SetResult();                                 // recovery reacquires → token invalid → aborts

        await actor.Invoking(_ => rebirth).Should().ThrowAsync<OperationCanceledException>();
        actor.State.Should().Be(AdapterState.Stopped);
    }

    // ==== Pass 2: graceful EndSession + idempotence ====

    [Fact]
    public async Task EndSession_PublishesNDeathThenCleanDisconnect_Once_RetiresSession()
    {
        var (actor, fake, _) = await Born();

        await actor.EndSessionAsync(End(), CancellationToken.None);

        NDeaths(fake).Should().ContainSingle();  // exactly one explicit NDEATH
        fake.DisconnectCalled.Should().BeTrue();  // a clean DISCONNECT (broker discards the Will → no second death)
        fake.Disposed.Should().BeTrue();
        actor.HasSession.Should().BeFalse();
    }

    [Fact]
    public async Task EndSession_Twice_SecondIsNoOp_NoSecondDeath()
    {
        var (actor, fake, _) = await Born();
        await actor.EndSessionAsync(End(), CancellationToken.None);
        var deaths = NDeaths(fake).Count;

        await actor.EndSessionAsync(End(), CancellationToken.None); // no active session — idempotent no-op

        NDeaths(fake).Count.Should().Be(deaths); // no second death
    }

    [Fact]
    public async Task Stop_AfterEndSession_NoSecondDeath()
    {
        var (actor, fake, _) = await Born();
        await actor.EndSessionAsync(End(), CancellationToken.None);
        var deaths = NDeaths(fake).Count;

        await actor.StopAsync(CancellationToken.None);

        actor.State.Should().Be(AdapterState.Stopped);
        NDeaths(fake).Count.Should().Be(deaths); // Stop after End retires nothing — no second death
    }

    // ==== Pass 2 r1: retryable-transport vs fatal-preparation classification (B1) ====

    [Fact]
    public async Task Rebirth_Recovery_FatalPreparationFailure_FailsOnce_NoBackoff()
    {
        var recording = new List<TimeSpan>();
        var (actor, fake, _) = await BornRecording(recording);
        await fake.RaiseDisconnected(actor.CurrentGeneration); // suspect

        // A pre-epoch rebirth snapshot fails deterministically in PrepareBirth — before the retry loop.
        await actor.Invoking(a => a.RebirthAsync(RebirthPreEpoch(epoch: 1), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.EncodeTimestampPreEpoch);

        actor.State.Should().Be(AdapterState.Failed);
        recording.Should().BeEmpty(); // no backoff for a deterministic preparation failure
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("subscribe")]
    [InlineData("nbirth")]
    public async Task Rebirth_Recovery_TransportFailure_RetriesWithinBudget(string failAt)
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport();
        switch (failAt)
        {
            case "connect": failing.FailConnect = true; break;
            case "subscribe": failing.FailSubscribe = true; break;
            case "nbirth": failing.PublishReturnsFalse = true; break;
        }

        var good = new FakeTransport();
        var fakes = new Queue<ISparkplugMqttTransport>(new ISparkplugMqttTransport[] { fake0, failing, good });
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, InstantDelay);
        await Begin(actor, host); // begin dequeues fake0
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        actor.State.Should().Be(AdapterState.Running); // a transport failure of any establishment step is retryable
        actor.HasSession.Should().BeTrue();
        good.Connected.Should().BeTrue();
    }

    // ==== Pass 2 r1: recovery evidence (delay sequence, single attempt, distinct generation) ====

    [Fact]
    public async Task Rebirth_Recovery_DelaySequence_IsCappedExponential_NoDelayAfterLastAttempt()
    {
        var recording = new List<TimeSpan>();
        var fake0 = new FakeTransport();
        var host = new CapturingHost();
        // Every recovery attempt fails (budget 3, initial 1000ms, ×2, cap 30000ms).
        var actor = new SparkplugSessionActor(
            "spb-1", NewStore(), () => fake0.Connected ? new FakeTransport { FailConnect = true } : fake0, () => Clock,
            Recording(recording));
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None)).Should().ThrowAsync<Exception>();

        // 3 attempts → exactly 2 backoffs (none after the last failed attempt): 1000ms, 2000ms.
        recording.Select(d => d.TotalMilliseconds).Should().Equal(1000d, 2000d);
    }

    [Fact]
    public async Task Rebirth_Recovery_MaxAttemptsOne_FailsWithNoBackoff()
    {
        var recording = new List<TimeSpan>();
        var fake0 = new FakeTransport();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor(
            "spb-1", NewStore(), () => fake0.Connected ? new FakeTransport { FailConnect = true } : fake0, () => Clock,
            Recording(recording));
        await actor.InitializeAsync(ValidConfig() with { TransportRecoveryMaxAttempts = 1 }, CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None)).Should().ThrowAsync<Exception>();

        actor.State.Should().Be(AdapterState.Failed);
        recording.Should().BeEmpty(); // a budget of 1 never backs off
    }

    [Fact]
    public async Task Rebirth_Recovery_DistinctGenerationAndBdSeqPerAttempt()
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport { FailConnect = true }; // attempt 1: generation 2, bdSeq 1
        var good = new FakeTransport();                          // attempt 2: generation 3, bdSeq 2
        var fakes = new Queue<ISparkplugMqttTransport>(new ISparkplugMqttTransport[] { fake0, failing, good });
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);

        actor.CurrentGeneration.Should().Be(3); // begin(1) + failed(2) + success(3): distinct per attempt
        actor.CurrentBdSeq.Value.Should().Be(2);
    }

    // ==== Pass 2 r1: single recovery + safe Dispose (B2) ====

    [Fact]
    public async Task Rebirth_Recovery_DisposeDuringBackoff_AbortsCleanly_NoObjectDisposed()
    {
        var (actor, fake0, gatedDelay, entered, release) = await BornInBackoff();

        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
        await entered.Task;                            // recovery parked in the released-gate backoff
        await actor.DisposeAsync();                    // invalidates the token; does NOT dispose the gate
        release.SetResult();                           // recovery reacquires → token invalid → aborts

        (await actor.Invoking(_ => rebirth).Should().ThrowAsync<OperationCanceledException>())
            .Which.Should().NotBeOfType<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_Concurrent_RetiresTransportOnce()
    {
        var (actor, fake, _) = await Born();

        await Task.WhenAll(actor.DisposeAsync().AsTask(), actor.DisposeAsync().AsTask());

        fake.DisposeCount.Should().Be(1); // atomic idempotence — retired exactly once
    }

    [Fact]
    public async Task Rebirth_Recovery_CancellationDuringBackoff_PreventsNextAttempt()
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport { FailConnect = true };
        var good = new FakeTransport();
        var fakes = new Queue<ISparkplugMqttTransport>(new ISparkplugMqttTransport[] { fake0, failing, good });
        var host = new CapturingHost();
        using var cts = new CancellationTokenSource();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock,
            (_, ct) => { cts.Cancel(); return Task.FromCanceled(ct); }); // cancel during the first backoff
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        good.Connected.Should().BeFalse();                       // the second attempt never ran
        actor.State.Should().Be(AdapterState.Running);           // cancellation is not a coarse fault
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Suspect); // normalized, not a stale Connecting/Birthing (r2 R2.2)
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(0)); // the previous authority is retained
        actor.CurrentSessionSuspect.Should().BeTrue();
    }

    [Fact]
    public async Task Rebirth_SecondRebirthDuringBackoff_NonFatalReject_RecoveryASucceeds()
    {
        var (actor, _, _, entered, release) = await BornInBackoff();
        var rebirthA = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
        await entered.Task; // recovery A parked in the released-gate backoff

        // A second Rebirth (even a later epoch) is rejected NONFATALLY and does not replace A's token.
        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 2), CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();
        actor.State.Should().NotBe(AdapterState.Failed); // the rejection did not fault the actor

        release.SetResult(); // recovery A resumes → attempt 2 succeeds
        await rebirthA;

        actor.State.Should().Be(AdapterState.Running);
        actor.HasSession.Should().BeTrue();
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1)); // A's epoch is authoritative, not B's
    }

    // ==== Pass 2 r2: terminal, non-resurrectable disposal (R2.3) ====

    [Theory]
    [InlineData("initialize")]
    [InlineData("start")]
    [InlineData("begin")]
    [InlineData("rebirth")]
    [InlineData("publish")]
    [InlineData("cutover")]
    public async Task LifecycleCall_AfterDispose_FailsClosed_NoStateMutation(string method)
    {
        var (actor, fake, host) = await Born();
        await actor.DisposeAsync();
        var nbirthsBefore = NBirths(fake).Count;

        Func<Task> act = method switch
        {
            "initialize" => () => actor.InitializeAsync(ValidConfig(), CancellationToken.None),
            "start" => () => actor.StartAsync(CancellationToken.None),
            "begin" => () => actor.BeginReplaySessionAsync(Start(host), CancellationToken.None),
            "rebirth" => () => actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None),
            "publish" => () => actor.PublishAsync(Array.Empty<CanonicalDataPoint>(), Ctx(), CancellationToken.None),
            "cutover" => () => actor.CompleteCatchUpAsync(Cutover(), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

        await act.Should().ThrowAsync<ObjectDisposedException>();
        actor.State.Should().Be(AdapterState.Stopped);                   // terminal state stands
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
        NBirths(fake).Count.Should().Be(nbirthsBefore);                  // no new transport / birth
    }

    [Fact]
    public async Task LifecycleCall_QueuedBehindDisposal_FailsClosed_NoResurrection()
    {
        var factoryCalls = 0;
        var fake = new FakeTransport();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor(
            "spb-1", NewStore(), () => { factoryCalls++; return fake; }, () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None); // factoryCalls == 1
        var callsAfterBirth = factoryCalls;

        var block = new TaskCompletionSource();
        fake.DisposeGate = block.Task;                       // hold the transport retirement (disposal keeps the gate)
        var dispose = actor.DisposeAsync().AsTask();          // wins ownership (task installed), then blocks in retirement
        var begin = actor.BeginReplaySessionAsync(Start(host, session: 2), CancellationToken.None); // queued on the gate
        var init = actor.InitializeAsync(ValidConfig(), CancellationToken.None);                    // queued on the gate
        block.SetResult();                                   // release retirement → disposal completes
        await dispose;

        await FluentActions.Awaiting(() => begin).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => init).Should().ThrowAsync<ObjectDisposedException>();
        factoryCalls.Should().Be(callsAfterBirth); // the queued Begin created NO new transport (no resurrection)
    }

    [Fact]
    public async Task Dispose_ConcurrentCaller_DoesNotCompleteBeforeRetirementReleased()
    {
        var (actor, fake, _) = await Born();
        var block = new TaskCompletionSource();
        fake.DisposeGate = block.Task; // A's retirement blocks here

        var a = actor.DisposeAsync().AsTask();
        var b = actor.DisposeAsync().AsTask(); // awaits A's shared task

        b.IsCompleted.Should().BeFalse();   // B must not complete before A's retirement is released
        block.SetResult();
        await Task.WhenAll(a, b);
        fake.DisposeCount.Should().Be(1);   // retired exactly once
    }

    [Fact]
    public async Task EndSession_LosingToDisposal_EmitsNoDeathOrDisconnect()
    {
        var (actor, fake, _) = await Born();
        await actor.DisposeAsync(); // disposal wins first
        fake.Events.Clear();

        await actor.EndSessionAsync(End(), CancellationToken.None); // loses to disposal → no-op

        NDeaths(fake).Should().BeEmpty();          // no explicit NDEATH
        fake.Events.Should().NotContain("disconnect"); // no clean DISCONNECT
    }

    // ==== Pass 2 r3: focused recovery/end evidence completions ====

    [Fact]
    public async Task Rebirth_Recovery_StoreFailureDuringPrepare_FailsOnce_NoBackoff_NoNewTransport()
    {
        var recording = new List<TimeSpan>();
        var factoryCalls = 0;
        var fake0 = new FakeTransport();
        var store = new ScriptableStore(NewStore());
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => { factoryCalls++; return fake0.Connected ? new FakeTransport() : fake0; },
            () => Clock, Recording(recording));
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        var callsAfterBirth = factoryCalls;
        await fake0.RaiseDisconnected(actor.CurrentGeneration);
        store.ThrowOnResolve = true; // the identity store is now unavailable

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.IdentityStoreUnavailable);

        recording.Should().BeEmpty();              // a store failure in PrepareBirth never backs off
        factoryCalls.Should().Be(callsAfterBirth); // and never opens a new transport
        actor.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task Rebirth_Recovery_GenerationExhausted_FailsOnce_NoBackoff_NoBdSeqReserved()
    {
        var recording = new List<TimeSpan>();
        var fake0 = new FakeTransport();
        var store = new ScriptableStore(NewStore());
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => fake0.Connected ? new FakeTransport() : fake0, () => Clock, Recording(recording));
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);
        SeedGeneration(actor, long.MaxValue); // the connection-generation counter is exhausted
        var reservesAfterBirth = store.ReserveCalls;

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.GenerationOverflow);

        recording.Should().BeEmpty();                       // overflow is fatal — no backoff
        store.ReserveCalls.Should().Be(reservesAfterBirth); // the overflow check precedes bdSeq reservation
        actor.DiagnosticsSnapshot.TransportRecoveryAttempts.Should().Be(0); // fatal preflight = NO attempt (r2 R2.2)
        actor.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task Rebirth_Recovery_AbortedByEndDuringBackoff_EndsCleanly_ReadyNoSession()
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport { FailConnect = true };
        var fakes = new Queue<ISparkplugMqttTransport>(
            new ISparkplugMqttTransport[] { fake0, failing, new FakeTransport() });
        var host = new CapturingHost();
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        Func<TimeSpan, CancellationToken, Task> delay = async (_, ct) => { entered.TrySetResult(); await release.Task; };
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, delay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // attempt 1 fails → gate-released backoff
        await entered.Task;                                     // recovery parked in the released-gate backoff
        await actor.EndSessionAsync(End(), CancellationToken.None); // End takes the gate, invalidates the token
        release.SetResult();                                   // recovery reacquires → token invalid → aborts

        await actor.Invoking(_ => rebirth).Should().ThrowAsync<OperationCanceledException>();
        actor.State.Should().Be(AdapterState.Running);              // ready-no-session (coarse Running)
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
        actor.HasSession.Should().BeFalse();
    }

    // ==== Pass 2 r4: disposal supersedes an in-flight recovery (marker-vs-token interval) ====

    [Fact]
    public async Task Dispose_DuringRecoveryBackoff_SupersedesRecovery_NoNewAttempt()
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport { FailConnect = true };  // attempt 1 fails retryably → backoff
        var queue = new Queue<FakeTransport>(new[] { fake0, failing });
        var store = new ScriptableStore(NewStore());
        var host = new CapturingHost();
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        Func<TimeSpan, CancellationToken, Task> delay = async (_, ct) => { entered.TrySetResult(); await release.Task; };
        var factoryCalls = 0;
        var actor = new SparkplugSessionActor(
            "spb-1", store,
            () => { factoryCalls++; return queue.Count > 0 ? queue.Dequeue() : new FakeTransport(); },
            () => Clock, delay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None); // fake0 (factory call 1)
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // attempt 1 = failing (call 2) → backoff
        await entered.Task;                                    // recovery parked in gate-released backoff
        var callsAtBackoff = factoryCalls;                     // 2
        var genAtBackoff = actor.LastIssuedGeneration;         // one generation per attempt so far
        var reservesAtBackoff = store.ReserveCalls;            // one bdSeq per attempt so far
        var attemptsAtBackoff = actor.DiagnosticsSnapshot.TransportRecoveryAttempts; // one admitted attempt so far

        // Drive the exact ordering: disposal wins ownership, its retirement is held on a transport-dispose
        // barrier, THEN the recovery backoff is released — recovery must abort, never begin another attempt.
        var retirementBarrier = new TaskCompletionSource();
        fake0.DisposeGate = retirementBarrier.Task;            // disposal's retirement of the active transport blocks here
        var dispose = actor.DisposeAsync().AsTask();           // nulls the token + installs the marker, then blocks in retirement
        release.SetResult();                                   // recovery delay completes → it reacquires the gate (disposal holds it)
        retirementBarrier.SetResult();                         // retirement finishes → gate freed → recovery observes disposal and aborts
        await dispose;

        await FluentActions.Awaiting(() => rebirth).Should().ThrowAsync<OperationCanceledException>();
        factoryCalls.Should().Be(callsAtBackoff);              // no next transport created
        actor.LastIssuedGeneration.Should().Be(genAtBackoff);  // no next generation issued
        store.ReserveCalls.Should().Be(reservesAtBackoff);     // no additional bdSeq reserved
        actor.DiagnosticsSnapshot.TransportRecoveryAttempts.Should().Be(attemptsAtBackoff); // rejected admission = no attempt (B4)
        actor.State.Should().Be(AdapterState.Stopped);         // disposal terminal
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
        actor.HasSession.Should().BeFalse();                   // no candidate authority promoted
    }

    // ==== Pass 2 r5: disposal wins during the INITIAL suspect-transport retirement (pre-attempt window) ====

    [Fact]
    public async Task Dispose_DuringInitialSuspectRetirement_SupersedesRecovery_BeforePrepareOrAttempt()
    {
        var fake0 = new FakeTransport();
        var store = new ScriptableStore(NewStore());
        var host = new CapturingHost();
        var factoryCalls = 0;
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => { factoryCalls++; return fake0.Connected ? new FakeTransport() : fake0; },
            () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None); // fake0 (factory call 1)
        var callsAfterBirth = factoryCalls;                    // 1
        var genAfterBirth = actor.LastIssuedGeneration;        // 1
        var reservesAfterBirth = store.ReserveCalls;           // 1
        var resolvesAfterBirth = store.ResolveCalls;           // 1 (Begin's single PrepareBirth)

        // Block the recovery INSIDE the initial `previous.Transport.DisposeAsync()` — before any bdSeq,
        // generation or connection attempt, and (pre-r5) before the token was even assigned.
        var retirementBarrier = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        fake0.DisposeGate = retirementBarrier.Task;
        fake0.DisposeEntered = entered;
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // enters SuspectRebirthAsync
        await entered.Task;                                    // recovery parked in the initial suspect retirement (holding the gate)

        var dispose = actor.DisposeAsync().AsTask();           // nulls the token + installs the marker, then blocks on the gate
        retirementBarrier.SetResult();                         // release the retirement → recovery resumes, sees disposal, aborts, frees the gate
        await dispose;                                         // disposal then acquires the gate and retires to terminal

        await FluentActions.Awaiting(() => rebirth).Should().ThrowAsync<OperationCanceledException>();
        factoryCalls.Should().Be(callsAfterBirth);             // no new transport created
        actor.LastIssuedGeneration.Should().Be(genAfterBirth); // no new generation issued
        store.ReserveCalls.Should().Be(reservesAfterBirth);    // no bdSeq reserved
        store.ResolveCalls.Should().Be(resolvesAfterBirth);    // recovery aborted BEFORE PrepareBirth (no alias resolve)
        actor.State.Should().Be(AdapterState.Stopped);         // disposal terminal
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
        actor.HasSession.Should().BeFalse();                   // no candidate authority promoted
    }

    // ==== Pass 2 r5.1: the in-attempt guard aborts before the first durable allocation (bdSeq/generation/transport) ====

    [Fact]
    public async Task Begin_DisposalWinsDuringBirthPrep_FailsClosed_BeforeBdSeqOrTransport()
    {
        var fake = new FakeTransport();
        var store = new ScriptableStore(NewStore());
        var host = new CapturingHost();
        var factoryCalls = 0;
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => { factoryCalls++; return fake; }, () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);

        // Park Begin INSIDE PrepareBirth's alias resolution — after the outer ThrowIfDisposed, before the
        // in-attempt guard and the first durable allocation. Begin holds the gate throughout.
        var resolveEntered = new TaskCompletionSource();
        var resolveBarrier = new TaskCompletionSource();
        store.ResolveEntered = resolveEntered;
        store.ResolveGate = resolveBarrier.Task;
        var begin = Task.Run(() => actor.BeginReplaySessionAsync(Start(host), CancellationToken.None));
        await resolveEntered.Task;                     // Begin parked in birth prep, holding the gate

        var dispose = actor.DisposeAsync().AsTask();   // wins ownership (installs the marker), then blocks on the gate
        resolveBarrier.SetResult();                    // release birth prep → AttemptConnectionAsync's in-attempt guard sees disposal
        await dispose;

        await FluentActions.Awaiting(() => begin).Should().ThrowAsync<ObjectDisposedException>();
        store.ReserveCalls.Should().Be(0);             // guard tripped BEFORE ReserveNextBdSeq — no durable bdSeq
        factoryCalls.Should().Be(0);                   // no transport created
        actor.LastIssuedGeneration.Should().Be(0);     // no generation issued
        actor.State.Should().Be(AdapterState.Stopped); // disposal terminal
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
        actor.HasSession.Should().BeFalse();           // nothing promoted
    }

    [Fact]
    public async Task Dispose_LeavesCoherentTerminalStoppedState()
    {
        var (actor, _, _) = await Born();

        await actor.DisposeAsync();

        actor.State.Should().Be(AdapterState.Stopped);
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
        actor.HasSession.Should().BeFalse();
        (await actor.CheckHealthAsync(CancellationToken.None)).State.Should().Be(AdapterState.Stopped);
    }

    // ==== Pass 2 r2: focused evidence ====

    [Fact]
    public async Task Rebirth_Recovery_BackoffReachesAndRepeatsMaxDelayCap()
    {
        var recording = new List<TimeSpan>();
        var fake0 = new FakeTransport();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor(
            "spb-1", NewStore(), () => fake0.Connected ? new FakeTransport { FailConnect = true } : fake0, () => Clock,
            Recording(recording));
        // initial 100ms, ×2, cap 150ms, budget 4 → delays: 100, 150 (200 capped), 150 (400 capped).
        await actor.InitializeAsync(
            ValidConfig() with { TransportRecoveryMaxAttempts = 4, TransportRecoveryInitialDelayMs = 100, TransportRecoveryMaxDelayMs = 150 },
            CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None)).Should().ThrowAsync<Exception>();

        recording.Select(d => d.TotalMilliseconds).Should().Equal(100d, 150d, 150d); // cap reached and repeated
    }

    [Fact]
    public async Task EndSession_NDeathCancellationAfterTransportEntry_NoCleanDisconnect()
    {
        var (actor, fake, _) = await Born();
        using var cts = new CancellationTokenSource();
        fake.OnPublishOnce = () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; };

        await actor.EndSessionAsync(End(), cts.Token);

        fake.DisconnectCalled.Should().BeFalse(); // uncertain NDEATH → abort-dispose, broker publishes the Will
        fake.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task EndSession_ThenNewSession_StaleEndForOldSession_LeavesNewSessionIntact()
    {
        var store = NewStore();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => new FakeTransport(), () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host, session: 1), CancellationToken.None); // session 1
        await actor.EndSessionAsync(End(session: 1), CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host, session: 2), CancellationToken.None); // session 2

        await actor.EndSessionAsync(End(session: 1), CancellationToken.None); // a stale End for session 1

        actor.HasSession.Should().BeTrue();                       // session 2 is untouched
        actor.CurrentSessionId.Should().Be(ReplaySessionId.Create(2));
    }

    [Fact]
    public async Task Rebirth_Recovery_DelayedCallbackFromFailedClient_CannotAffectReplacement()
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport { FailConnect = true }; // recovery attempt 1
        var good = new FakeTransport();                          // recovery attempt 2
        var fakes = new Queue<ISparkplugMqttTransport>(new ISparkplugMqttTransport[] { fake0, failing, good });
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);
        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // recovers on good (generation 3)
        var requestsAfterRecovery = host.Requests.Count; // the initial disconnect legitimately queued one

        // A delayed callback from the FAILED recovery client (generation 2) must not affect the live session.
        await failing.RaiseDisconnected(2);
        await failing.RaiseNodeCommand(2, RebirthCommand());

        actor.CurrentSessionSuspect.Should().BeFalse();          // the live authority is untouched
        host.Requests.Count.Should().Be(requestsAfterRecovery);  // no new request from the retired client
    }

    // ==== Pass 2 r1: NDEATH-success-gated clean DISCONNECT (B3) ====

    [Fact]
    public async Task EndSession_NDeathReturnsFalse_NoCleanDisconnect_AbortDisposes()
    {
        var (actor, fake, _) = await Born();
        fake.PublishReturnsFalse = true; // the NDEATH publish is unconfirmed

        await actor.EndSessionAsync(End(), CancellationToken.None);

        fake.DisconnectCalled.Should().BeFalse(); // no clean DISCONNECT → the broker publishes the Will
        fake.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task EndSession_NDeathThrows_NoCleanDisconnect_AbortDisposes()
    {
        var (actor, fake, _) = await Born();
        fake.ThrowOnPublish = true; // the NDEATH publish is uncertain

        await actor.EndSessionAsync(End(), CancellationToken.None);

        fake.DisconnectCalled.Should().BeFalse();
        fake.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task EndSession_Success_OrderIsNDeathThenDisconnectThenDispose_BytesMatchBdSeq()
    {
        var (actor, fake, _) = await Born();
        fake.Events.Clear(); // drop the birth NBIRTH — assert only the End sequence

        await actor.EndSessionAsync(End(), CancellationToken.None);

        fake.Events.Should().Equal("publish:NDEATH", "disconnect", "dispose"); // exact order
        NDeaths(fake).Single().Should().Equal(SparkplugPayloadEncoder.EncodeNDeath(SparkplugBirthDeathSequence.Create(0)));
    }

    // ==== Pass 2 r1: authoritative End + ready-no-session (B4) ====

    [Theory]
    [InlineData("session")]
    [InlineData("route")]
    public async Task EndSession_StaleIdentity_DoesNotEndActiveSession(string mismatch)
    {
        var (actor, fake, _) = await Born();
        var stale = mismatch == "session"
            ? ReplaySessionEnd.Create(ReplaySessionId.Create(999), "route-1", ReplaySessionEndReason.Stop)
            : ReplaySessionEnd.Create(ReplaySessionId.Create(1), "route-OTHER", ReplaySessionEndReason.Stop);

        await actor.EndSessionAsync(stale, CancellationToken.None);

        actor.HasSession.Should().BeTrue();     // the current authority is untouched
        NDeaths(fake).Should().BeEmpty();
    }

    [Fact]
    public async Task EndSession_Success_ReadyNoSession_HealthyAndRebeginnable()
    {
        var (actor, _, _) = await Born();

        await actor.EndSessionAsync(End(), CancellationToken.None);

        actor.State.Should().Be(AdapterState.Running);                 // ready-no-session
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
        actor.HasSession.Should().BeFalse();
        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Healthy);

        // A fresh Begin is possible after End (reuses the same store; a new session births).
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
        actor.HasSession.Should().BeTrue();
    }

    // ==== Slice 7: health / diagnostics / counters / redaction (plan v3 §8, §11) ====

    [Fact]
    public async Task Health_ReadyNoSession_IsHealthy()
    {
        var fake = new FakeTransport();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);

        var health = await actor.CheckHealthAsync(CancellationToken.None);

        health.Level.Should().Be(HealthLevel.Healthy);
        health.State.Should().Be(AdapterState.Running);
        health.Metrics!["hasSession"].Should().Be(false);
        health.Metrics["protocolState"].Should().Be(SparkplugProtocolState.Stopped.ToString());
    }

    [Fact]
    public async Task Health_Live_IsHealthy()
    {
        var (actor, _, _) = await Born();
        await actor.CompleteCatchUpAsync(Cutover(), CancellationToken.None); // cutover → Live

        var health = await actor.CheckHealthAsync(CancellationToken.None);

        health.Level.Should().Be(HealthLevel.Healthy);
        health.Metrics!["protocolState"].Should().Be(SparkplugProtocolState.Live.ToString());
        health.Metrics["hasSession"].Should().Be(true);
    }

    [Fact]
    public async Task Health_ReplayingBeforeCutover_IsDegraded()
    {
        var (actor, _, _) = await Born(); // promoted, Replaying — an active transitional session

        var health = await actor.CheckHealthAsync(CancellationToken.None);

        health.Level.Should().Be(HealthLevel.Degraded); // active session not yet Live
        health.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task Health_AfterFatalBirthFailure_IsUnhealthy_WithSanitizedError()
    {
        var (actor, fake, _) = await Born();
        fake.PublishReturnsFalse = true; // the healthy-rebirth NBIRTH will fail locally (fatal)

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None)).Should().ThrowAsync<Exception>();

        var health = await actor.CheckHealthAsync(CancellationToken.None);
        health.Level.Should().Be(HealthLevel.Unhealthy);
        health.State.Should().Be(AdapterState.Failed);
        health.Metrics!["birthFailures"].Should().Be(1L);
        health.LastError.Should().NotBeNull();
        health.LastError!.Message.Should().BeEmpty();                 // sanitized — no message leaks
        health.LastError.Code.Should().Be(SparkplugErrors.BirthPublishFailed);
        health.Metrics["lastErrorCode"].Should().Be(SparkplugErrors.BirthPublishFailed);
    }

    [Fact]
    public async Task Diagnostics_SessionFields_CoherentWhenBorn()
    {
        var (actor, _, _) = await Born();

        var diag = actor.DiagnosticsSnapshot;

        diag.HasSession.Should().BeTrue();
        diag.SessionId.Should().Be(1);
        diag.Epoch.Should().Be(0);
        diag.RouteId.Should().Be("route-1");
        diag.ConnectionGeneration.Should().Be(actor.CurrentGeneration);
        diag.BdSeq.Should().Be(actor.CurrentBdSeq.Value);
        diag.NextSeq.Should().Be(1); // NBIRTH consumed seq 0
    }

    [Fact]
    public async Task Diagnostics_Version_IsStrictlyMonotonicAcrossTransitions()
    {
        var fake = new FakeTransport();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, InstantDelay);
        var v0 = actor.DiagnosticsSnapshot.Version;
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        var v1 = actor.DiagnosticsSnapshot.Version;
        await actor.StartAsync(CancellationToken.None);
        var v2 = actor.DiagnosticsSnapshot.Version;
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
        var v3 = actor.DiagnosticsSnapshot.Version;

        v1.Should().BeGreaterThan(v0);
        v2.Should().BeGreaterThan(v1);
        v3.Should().BeGreaterThan(v2);
    }

    [Fact]
    public async Task Diagnostics_LastTransition_ReflectsPrecedingStateChange()
    {
        var (actor, _, _) = await Born();

        var diag = actor.DiagnosticsSnapshot;

        diag.LastStateChangeAt.Should().Be(Clock);              // injected clock, deterministic
        diag.PreviousProtocolState.Should().NotBe(diag.ProtocolState); // an actual transition preceded this state
        diag.LastTransitionReasonCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Diagnostics_StaleCallbacks_FromReplacedClient_Counted_LiveUnaffected()
    {
        // Birth client A, replace it with client B via a suspect recovery, then deliver A's DELAYED disconnect
        // and NCMD carrying A's OWN REAL generation (the concrete transport echoes it). Both must count as
        // stale by handoff identity, and the live session B must be untouched (slice-7 review B2).
        var fake0 = new FakeTransport();
        var fake1 = new FakeTransport();
        var call = 0;
        var actor = new SparkplugSessionActor(
            "spb-1", NewStore(), () => call++ == 0 ? (ISparkplugMqttTransport)fake0 : fake1, () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None); // A = fake0
        var aGeneration = actor.CurrentGeneration;
        await fake0.RaiseDisconnected(aGeneration);                             // legit suspect (A authoritative)
        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);    // recover → B = fake1 authoritative
        var epochAfter = actor.CurrentEpoch;

        await fake0.RaiseDisconnected(aGeneration);                             // A's delayed disconnect, real gen
        await fake0.RaiseNodeCommand(aGeneration, RebirthCommand());            // A's delayed NCMD, real gen

        var diag = actor.DiagnosticsSnapshot;
        diag.StaleDisconnectCallbacks.Should().Be(1);   // ONLY the post-replacement one (the first was authoritative)
        diag.StaleNodeCommandCallbacks.Should().Be(1);
        actor.CurrentEpoch.Should().Be(epochAfter);     // B is unaffected
        actor.CurrentSessionSuspect.Should().BeFalse();  // the live session B was not marked suspect
        (await actor.CheckHealthAsync(CancellationToken.None)).Metrics!["staleDisconnectCallbacks"].Should().Be(1L);
    }

    [Fact]
    public async Task Diagnostics_RebirthRequest_QueuedThenCoalesced()
    {
        var (actor, fake, _) = await Born();

        await fake.RaiseDisconnected(actor.CurrentGeneration);                  // queues one Core request
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // coalesces into the open episode

        var diag = actor.DiagnosticsSnapshot;
        diag.RebirthRequestsQueued.Should().Be(1);
        diag.RebirthRequestsCoalesced.Should().Be(1);
        diag.LastRebirthRequestAt.Should().Be(Clock);
    }

    [Fact]
    public async Task Diagnostics_HealthyRebirth_IncrementsCounter_AndBirthTimestamp()
    {
        var (actor, _, _) = await Born();

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy in-place rebirth

        var diag = actor.DiagnosticsSnapshot;
        diag.HealthyRebirths.Should().Be(1);
        diag.LastSuccessfulBirthAt.Should().Be(Clock);
        diag.Epoch.Should().Be(1);
    }

    [Fact]
    public async Task Diagnostics_TransportRecovery_CountsStartsAttemptsSuccesses()
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport { FailConnect = true }; // attempt 1 fails (retryable)
        var good = new FakeTransport();
        var fakes = new Queue<ISparkplugMqttTransport>(new ISparkplugMqttTransport[] { fake0, failing, good });
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // recovers on attempt 2

        var diag = actor.DiagnosticsSnapshot;
        diag.TransportRecoveryStarts.Should().Be(1);
        diag.TransportRecoveryAttempts.Should().Be(2);   // one failed + one good, lifetime
        diag.TransportRecoverySuccesses.Should().Be(1);
        diag.TransportRecoveryExhaustions.Should().Be(0);
        diag.CurrentRecoveryAttempt.Should().Be(0);      // no episode running after success
    }

    [Fact]
    public async Task Diagnostics_TransportRecovery_Exhaustion_CountsExhaustionAndFaults()
    {
        var recording = new List<TimeSpan>();
        var fake0 = new FakeTransport();
        var actor = new SparkplugSessionActor(
            "spb-1", NewStore(), () => fake0.Connected ? new FakeTransport { FailConnect = true } : fake0, () => Clock,
            Recording(recording));
        await actor.InitializeAsync(ValidConfig() with { TransportRecoveryMaxAttempts = 2 }, CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None)).Should().ThrowAsync<Exception>();

        var diag = actor.DiagnosticsSnapshot;
        diag.TransportRecoveryExhaustions.Should().Be(1);
        diag.CurrentRecoveryAttempt.Should().Be(0);            // reset after the episode ends
        diag.State.Should().Be(AdapterState.Failed);
        diag.LastRecoveryFailureCode.Should().NotBeNullOrEmpty();
        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Unhealthy);
    }

    [Fact]
    public async Task Diagnostics_CurrentRecoveryAttempt_TracksOrdinalDuringBackoff()
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport { FailConnect = true };
        var fakes = new Queue<ISparkplugMqttTransport>(
            new ISparkplugMqttTransport[] { fake0, failing, new FakeTransport() });
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        Func<TimeSpan, CancellationToken, Task> delay = async (_, __) => { entered.TrySetResult(); await release.Task; };
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, delay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
        await entered.Task; // parked in backoff after attempt 1 failed

        var during = actor.DiagnosticsSnapshot;
        during.CurrentRecoveryAttempt.Should().Be(1);
        during.ProtocolState.Should().Be(SparkplugProtocolState.RecoveringTransport);
        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Degraded);

        release.SetResult();
        await rebirth; // recovers on attempt 2
        actor.DiagnosticsSnapshot.CurrentRecoveryAttempt.Should().Be(0);
    }

    [Fact]
    public async Task Diagnostics_HealthSnapshot_NeverExposesCredentialsOrEndpoint()
    {
        const string secret = "sup3r-s3cret-pw";
        var fake = new FakeTransport();
        var config = ValidConfig() with { BrokerHost = "broker.internal.example", Username = "operator", Password = secret };
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, InstantDelay);
        await actor.InitializeAsync(config, CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);

        var health = await actor.CheckHealthAsync(CancellationToken.None);

        var rendered = string.Join("|", health.Metrics!.Select(kv => $"{kv.Key}={kv.Value}"))
            + "|" + (health.LastError?.Message ?? "") + "|" + (health.Detail ?? "");
        rendered.Should().NotContain(secret);
        rendered.Should().NotContain("operator");
        rendered.Should().NotContain("broker.internal.example");
    }

    // ==== Slice 7 r1: NCMD classification, coalescing, failure diagnostics ====

    [Fact]
    public async Task NodeCommand_RebirthWithUnknownExtras_RequestsOnce_DiagnosesExtras()
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthWithUnknownExtrasCommand());

        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.HostCommand); // rebirth once
        actor.DiagnosticsSnapshot.LastNodeCommandDiagnosticCode.Should().Be("rebirth+unknown-extras"); // extras diagnosed
        actor.DiagnosticsSnapshot.NodeCommandsIgnored.Should().Be(0); // an actionable rebirth is not "ignored"
    }

    [Theory]
    [InlineData("false", "ignored:false")]
    [InlineData("null", "ignored:null")]
    [InlineData("wrong-type", "ignored:wrong-type")]
    [InlineData("missing", "ignored:missing")]
    public async Task NodeCommand_IgnoredKind_TallyAndDiagnostic_NoRequest(string kind, string code)
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseNodeCommand(actor.CurrentGeneration, IgnoredNodeCommand(kind));

        host.Requests.Should().BeEmpty();                                     // never a side effect
        actor.DiagnosticsSnapshot.NodeCommandsIgnored.Should().Be(1);         // tallied
        actor.DiagnosticsSnapshot.LastNodeCommandDiagnosticCode.Should().Be(code); // distinguishable + redacted
    }

    [Fact]
    public async Task Diagnostics_RepeatedCoalescingNodeCommands_DoNotInflateBeyondFolds()
    {
        var (actor, fake, host) = await Born();

        await fake.RaiseDisconnected(actor.CurrentGeneration);                  // opens the episode + queues one
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // folds (coalesced #1)
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // folds (coalesced #2)

        var diag = actor.DiagnosticsSnapshot;
        diag.RebirthRequestsQueued.Should().Be(1);      // exactly one Core request for the episode
        diag.RebirthRequestsCoalesced.Should().Be(2);   // only the two genuine new signals that folded
    }

    [Fact]
    public async Task Diagnostics_UntypedFailure_RecordsSanitizedFallbackErrorCodeAndTime()
    {
        var fake = new FakeTransport();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        // NOT started → Begin hits RequireReadyForSession, an UNTYPED InvalidOperationException.

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        var diag = actor.DiagnosticsSnapshot;
        diag.LastErrorCode.Should().Be(SparkplugErrors.ActorFailure); // sanitized fallback (no exception message/type)
        diag.LastErrorAt.Should().Be(Clock);
        diag.LastError!.Message.Should().BeEmpty();
    }

    [Fact]
    public async Task Diagnostics_InTransportNBirthCancellation_IncrementsBirthFailures()
    {
        var (actor, fake, _) = await Born();
        using var cts = new CancellationTokenSource();
        fake.OnPublishOnce = () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; };

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), cts.Token)).Should().ThrowAsync<OperationCanceledException>();

        actor.DiagnosticsSnapshot.BirthFailures.Should().Be(1); // in-transport NBIRTH cancel = uncertain send (B4)
    }

    [Fact]
    public async Task Diagnostics_PreSendNBirthCancellation_CountsNoBirthFailure()
    {
        var (actor, _, _) = await Born();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), cts.Token)).Should().ThrowAsync<OperationCanceledException>();

        actor.DiagnosticsSnapshot.BirthFailures.Should().Be(0); // aborted at the gate, never entered the transport
    }

    // ==== Slice 7 r2: live operational-control overlay (health reflects async suspect/pending) ====

    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake, CapturingHost Host)> BornLive()
    {
        var born = await Born();
        await born.Actor.CompleteCatchUpAsync(Cutover(), CancellationToken.None); // Replaying → Live
        return born;
    }

    [Fact]
    public async Task Health_LiveThenAsyncDisconnect_IsDegraded_SuspectAndPending()
    {
        var (actor, fake, _) = await BornLive();
        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Healthy); // baseline

        await fake.RaiseDisconnected(actor.CurrentGeneration); // async transport drop while Live

        var health = await actor.CheckHealthAsync(CancellationToken.None);
        health.Level.Should().Be(HealthLevel.Degraded);                    // waiting-for-rebirth is NOT healthy
        health.Metrics!["suspectTransport"].Should().Be(true);
        health.Metrics["pendingRebirth"].Should().Be(true);
        health.Metrics["pendingRebirthReason"].Should().Be(RebirthReason.Other.ToString());
    }

    [Fact]
    public async Task Health_LiveThenValidNodeCommand_IsDegraded_PendingNotSuspect()
    {
        var (actor, fake, _) = await BornLive();

        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // host command, transport healthy

        var health = await actor.CheckHealthAsync(CancellationToken.None);
        health.Level.Should().Be(HealthLevel.Degraded);                    // the control latch blocks DATA
        health.Metrics!["pendingRebirth"].Should().Be(true);
        health.Metrics["pendingRebirthReason"].Should().Be(RebirthReason.HostCommand.ToString());
        health.Metrics["suspectTransport"].Should().Be(false);             // a host command does not mark suspect
    }

    [Fact]
    public async Task Health_RepeatedBlockedNodeCommands_DoNotChangeControlStateOrHealth()
    {
        var (actor, fake, _) = await BornLive();
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
        var afterFirst = actor.DiagnosticsSnapshot;

        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());

        var health = await actor.CheckHealthAsync(CancellationToken.None);
        health.Level.Should().Be(HealthLevel.Degraded);
        actor.DiagnosticsSnapshot.PendingRebirth.Should().BeTrue();
        actor.DiagnosticsSnapshot.RebirthRequestsQueued.Should().Be(afterFirst.RebirthRequestsQueued); // no new request
    }

    [Fact]
    public async Task Health_AfterHealthyRebirth_ClearsPendingAndSuspect()
    {
        var (actor, fake, _) = await BornLive();
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // pending
        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Degraded);

        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy rebirth fulfils the episode

        var diag = actor.DiagnosticsSnapshot;
        diag.PendingRebirth.Should().BeFalse();
        diag.SuspectTransport.Should().BeFalse();
        diag.Epoch.Should().Be(1);
    }

    [Fact]
    public async Task Health_DisposalWinsWhileRetirementBlocked_TerminalDisposed_NotHealthy()
    {
        var (actor, fake, _) = await BornLive();
        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Healthy); // baseline Live

        var block = new TaskCompletionSource();
        fake.DisposeGate = block.Task;                 // hold the transport retirement
        var dispose = actor.DisposeAsync().AsTask();   // wins ownership (marker), blocks in retirement

        var health = await actor.CheckHealthAsync(CancellationToken.None);
        health.Metrics!["terminalDisposed"].Should().Be(true);   // disposal has won, even before retirement completes
        health.Level.Should().NotBe(HealthLevel.Healthy);        // must NOT keep reporting healthy Live

        block.SetResult();
        await dispose;
        (await actor.CheckHealthAsync(CancellationToken.None)).State.Should().Be(AdapterState.Stopped);
    }

    // ==== Slice 7 r2: recovery-attempt counted only at the real attempt boundary ====

    [Theory]
    [InlineData("connect", SparkplugErrors.TransportConnectFailed)]
    [InlineData("subscribe", SparkplugErrors.TransportSubscribeFailed)]
    [InlineData("nbirth", SparkplugErrors.BirthPublishFailed)]
    public async Task Diagnostics_EstablishmentFailureDuringBackoff_ShowsFailureCode_CountsOneAttempt(string failAt, string expectedCode)
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport();
        switch (failAt)
        {
            case "connect": failing.FailConnect = true; break;
            case "subscribe": failing.FailSubscribe = true; break;
            case "nbirth": failing.PublishReturnsFalse = true; break;
        }

        var fakes = new Queue<ISparkplugMqttTransport>(
            new ISparkplugMqttTransport[] { fake0, failing, new FakeTransport() });
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        Func<TimeSpan, CancellationToken, Task> delay = async (_, __) => { entered.TrySetResult(); await release.Task; };
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, delay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
        await entered.Task; // attempt 1 (CONNECT/SUBSCRIBE/NBIRTH) failed → parked in backoff

        var diag = actor.DiagnosticsSnapshot;
        diag.TransportRecoveryAttempts.Should().Be(1);                       // exactly one admitted attempt
        diag.LastRecoveryFailureCode.Should().Be(expectedCode);              // the causing code, visible DURING backoff
        diag.CurrentRecoveryAttempt.Should().Be(1);

        release.SetResult();
        await rebirth; // recovers on attempt 2
        actor.DiagnosticsSnapshot.TransportRecoveryAttempts.Should().Be(2);
    }

    [Fact]
    public async Task Diagnostics_FactoryFailureDuringRecovery_CountsNoAttempt_OrdinalZero()
    {
        var recording = new List<TimeSpan>();
        var fake0 = new FakeTransport();
        var factoryCalls = 0;
        var actor = new SparkplugSessionActor(
            "spb-1", NewStore(),
            () => { factoryCalls++; return factoryCalls == 1 ? fake0 : throw new InvalidOperationException("factory boom"); },
            () => Clock, Recording(recording));
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None)).Should().ThrowAsync<Exception>();

        var diag = actor.DiagnosticsSnapshot;
        diag.TransportRecoveryAttempts.Should().Be(0); // the client was never created → no complete attempt (r3 R3.2)
        diag.CurrentRecoveryAttempt.Should().Be(0);
        recording.Should().BeEmpty();                  // a non-retryable factory failure does not back off
    }

    [Fact]
    public async Task Diagnostics_CutoverRedrainWhilePending_DoesNotInflateCoalesced()
    {
        var (actor, fake, _) = await Born();
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // opens episode: queued 1, coalesced 0
        var coalescedBefore = actor.DiagnosticsSnapshot.RebirthRequestsCoalesced;

        await actor.CompleteCatchUpAsync(Cutover(), CancellationToken.None); // pending → re-drain (no new signal, no Live)

        actor.DiagnosticsSnapshot.RebirthRequestsCoalesced.Should().Be(coalescedBefore); // a re-drain is NOT a coalesce
    }

    // ==== Slice 7 r3: atomic authority-bound handoff overlay ====

    [Fact]
    public async Task Diagnostics_HostCommandThenDisconnect_ControlTripleIsCoherent()
    {
        var (actor, fake, _) = await BornLive();
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // pending HostCommand, NOT suspect
        await fake.RaiseDisconnected(actor.CurrentGeneration);                  // now suspect → reason resolves to Other

        var diag = actor.DiagnosticsSnapshot;
        // The atomic read yields a combination that actually existed — suspect implies reason Other. The torn
        // combination suspect=false with reason=Other (a race between separate reads) can never appear.
        diag.SuspectTransport.Should().BeTrue();
        diag.PendingRebirth.Should().BeTrue();
        diag.PendingRebirthReason.Should().Be(RebirthReason.Other.ToString());
        (diag is { SuspectTransport: false, PendingRebirthReason: "Other" }).Should().BeFalse();
    }

    [Fact]
    public async Task Diagnostics_HealthyEpochPromotion_DoesNotLeakNewEpochControlOntoOldRoot()
    {
        var (actor, fake, _) = await BornLive();
        var atBarrier = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        // Fire INSIDE the authority-swap window: _activeSession is epoch 1, _semantic is still epoch 0.
        actor.PostAuthorityPublishBarrier = async () => { atBarrier.TrySetResult(); await release.Task; };

        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy: epoch 0→1, SAME generation
        await atBarrier.Task;

        // A control event now opens a FRESH episode on the (epoch-1) handoff. With generation-only binding this
        // would leak onto the epoch-0 semantic root; full-authority (session+epoch+generation) binding rejects it.
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());

        var duringPromotion = actor.DiagnosticsSnapshot;
        duringPromotion.Epoch.Should().Be(0);                 // the semantic root is still the old epoch
        duringPromotion.PendingRebirth.Should().BeFalse();    // epoch-1 control is NOT attached to the epoch-0 root

        release.SetResult();
        await rebirth;
        var afterPublish = actor.DiagnosticsSnapshot;
        afterPublish.Epoch.Should().Be(1);                    // new authority published
        afterPublish.PendingRebirth.Should().BeTrue();        // and its live control overlay is now coherent + visible
    }

    [Fact]
    public async Task Diagnostics_StoreReserveFailureDuringRecovery_CountsNoAttempt()
    {
        var recording = new List<TimeSpan>();
        var fake0 = new FakeTransport();
        var store = new ScriptableStore(NewStore());
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => fake0.Connected ? new FakeTransport() : fake0, () => Clock, Recording(recording));
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);
        store.ThrowOnReserve = true; // bdSeq reservation fails (fatal preparation, before the attempt boundary)

        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>();

        var diag = actor.DiagnosticsSnapshot;
        diag.TransportRecoveryAttempts.Should().Be(0); // reservation failure is not a "complete attempt" (r2 R2.2)
        recording.Should().BeEmpty();                  // fatal preparation, no backoff
    }

    // ==== Helpers ====

    private static Func<TimeSpan, CancellationToken, Task> Recording(List<TimeSpan> sink) =>
        (d, ct) => { sink.Add(d); return ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask; };

    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake, CapturingHost Host)> BornRecording(List<TimeSpan> sink)
    {
        var fake = new FakeTransport();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, Recording(sink));
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        return (actor, fake, host);
    }

    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake0, Func<TimeSpan, CancellationToken, Task> Delay, TaskCompletionSource Entered, TaskCompletionSource Release)> BornInBackoff()
    {
        var fake0 = new FakeTransport();
        var failing = new FakeTransport { FailConnect = true };
        var fakes = new Queue<ISparkplugMqttTransport>(
            new ISparkplugMqttTransport[] { fake0, failing, new FakeTransport(), new FakeTransport() });
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        Func<TimeSpan, CancellationToken, Task> delay = async (_, __) => { entered.TrySetResult(); await release.Task; };
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, delay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host: new CapturingHost()), CancellationToken.None);
        await fake0.RaiseDisconnected(actor.CurrentGeneration);
        return (actor, fake0, delay, entered, release);
    }

    private static ReplaySessionRebirth RebirthPreEpoch(long epoch)
    {
        var preEpoch = new DateTimeOffset(1969, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var key = CanonicalMetricKey.Create("srcA", "dev", "temp");
        var value = LatestMetricValue.Create(
            key, CanonicalValueType.Integer, 1, isNull: false, preEpoch, DataQuality.Good, routeBufferSequence: 1);
        var snapshot = new LatestValueSnapshot(RouteSchemaGeneration.Create(0),
            new Dictionary<CanonicalMetricKey, LatestMetricValue> { [key] = value });
        return ReplaySessionRebirth.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(epoch),
            ReplaySessionStartState.Create(ReplayBoundary.Create(0, 2), snapshot));
    }

    private static ReplaySessionEnd End(long session = 1) =>
        ReplaySessionEnd.Create(ReplaySessionId.Create(session), "route-1", ReplaySessionEndReason.Stop);

    private static ReplaySessionCutover Cutover() =>
        ReplaySessionCutover.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(0),
            ReplaySessionCutoverState.Create(0, new LatestValueSnapshot(
                RouteSchemaGeneration.Create(0), new Dictionary<CanonicalMetricKey, LatestMetricValue>())));

    private static PublishContext Ctx() =>
        PublishContext.Create("route-1", ReplaySessionId.Create(1), ReplayEpochId.Create(0), ReplayPhase.Replay, 5, 10, 0, 0);

    private static List<byte[]> NDeaths(FakeTransport fake) =>
        fake.Published.Where(p => p.Topic.Contains("NDEATH")).Select(p => p.Payload).ToList();

    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake, CapturingHost Host)> Born()
    {
        var fake = new FakeTransport();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, InstantDelay);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
        return (actor, fake, host);
    }

    // A backoff seam that completes instantly (deterministic, no wall-clock) but honors cancellation.
    private static Task InstantDelay(TimeSpan _, CancellationToken ct) =>
        ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;

    private (SparkplugSessionActor Actor, FakeTransport Fake1, FakeTransport Fake2, CapturingHost Host) TwoFakeActor()
    {
        var fake1 = new FakeTransport();
        var fake2 = new FakeTransport();
        var call = 0;
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor(
            "spb-1", NewStore(), () => call++ == 0 ? (ISparkplugMqttTransport)fake1 : fake2, () => Clock, InstantDelay);
        return (actor, fake1, fake2, host);
    }

    private static async Task Begin(SparkplugSessionActor actor, CapturingHost host)
    {
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
    }

    private SqliteSparkplugIdentityStateStore NewStore() =>
        new(Path.Combine(_dir, "sparkplug", "identity-state.db"));

    // Seed the private connection-generation counter to drive the deterministic overflow branch.
    // Test-only reflection is used deliberately so the exhaustion path can be proven without adding
    // a production mutation seam (a symmetric internal setter would work too — surfaced in the bundle).
    private static void SeedGeneration(SparkplugSessionActor actor, long value) =>
        typeof(SparkplugSessionActor)
            .GetField("_lastIssuedConnectionGeneration",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(actor, value);

    // A thin scriptable decorator over the real durable store: counts bdSeq reservations and can
    // force a fail-closed IdentityStoreUnavailable on alias resolution (the PrepareBirth path).
    private sealed class ScriptableStore : ISparkplugIdentityStateStore
    {
        private readonly ISparkplugIdentityStateStore _inner;
        public ScriptableStore(ISparkplugIdentityStateStore inner) => _inner = inner;

        public bool ThrowOnResolve { get; set; }
        public bool ThrowOnReserve { get; set; }
        public int ReserveCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public Task? ResolveGate { get; set; }                 // if set, ResolveAliases blocks (synchronously) until it completes
        public TaskCompletionSource? ResolveEntered { get; set; } // signalled when a gated ResolveAliases starts blocking

        public SparkplugBirthDeathSequence ReserveNextBdSeq(SparkplugStoreIdentity identity)
        {
            ReserveCalls++;
            if (ThrowOnReserve)
            {
                throw new Core.Errors.AdapterException(new Core.Errors.AdapterError
                {
                    Code = SparkplugErrors.IdentityStoreUnavailable,
                    Category = Core.Errors.ErrorCategory.Internal,
                    Message = "bdSeq reservation failed (test)",
                    Retryable = false,
                });
            }

            return _inner.ReserveNextBdSeq(identity);
        }

        public IReadOnlyDictionary<SparkplugAliasKey, ulong> ResolveAliases(
            SparkplugStoreIdentity identity, IReadOnlyCollection<SparkplugAliasKey> manifest)
        {
            ResolveCalls++;
            if (ResolveGate is { } gate) { ResolveEntered?.TrySetResult(); gate.GetAwaiter().GetResult(); }
            if (ThrowOnResolve)
            {
                throw new Core.Errors.AdapterException(new Core.Errors.AdapterError
                {
                    Code = SparkplugErrors.IdentityStoreUnavailable,
                    Category = Core.Errors.ErrorCategory.Internal,
                    Message = "identity store unavailable (test)",
                    Retryable = false,
                });
            }

            return _inner.ResolveAliases(identity, manifest);
        }

        public void Dispose() => _inner.Dispose();
    }

    private static SparkplugSinkConfiguration ValidConfig() => new()
    {
        InstanceId = "spb-1",
        ProtocolName = SparkplugBProtocol.ProtocolName,
        BrokerHost = "localhost",
        GroupId = Group,
        EdgeNodeId = Node,
    };

    private static ReplaySessionStart Start(CapturingHost host, long session = 1) =>
        ReplaySessionStart.Create(
            ReplaySessionId.Create(session), ReplayEpochId.Create(0), "route-1",
            ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(0))),
            host);

    private static ReplaySessionRebirth Rebirth(long epoch) =>
        ReplaySessionRebirth.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(epoch), StateOf(epoch));

    // An empty coherent state (boundary cutoff 0, empty snapshot).
    private static ReplaySessionStartState StateOf(long _) =>
        ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(0)));

    private static byte[] RebirthCommand()
    {
        var payload = new Payload();
        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = SparkplugPayloadEncoder.NodeControlRebirthMetricName,
            BooleanValue = true,
        });
        return payload.ToByteArray();
    }

    private static byte[] RebirthWithUnknownExtrasCommand()
    {
        var payload = new Payload();
        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = SparkplugPayloadEncoder.NodeControlRebirthMetricName,
            BooleanValue = true,
        });
        payload.Metrics.Add(new Payload.Types.Metric { Name = "Some/Other", IntValue = 7 });
        return payload.ToByteArray();
    }

    private static byte[] IgnoredNodeCommand(string kind)
    {
        var payload = new Payload();
        var name = SparkplugPayloadEncoder.NodeControlRebirthMetricName;
        payload.Metrics.Add(kind switch
        {
            "false" => new Payload.Types.Metric { Name = name, BooleanValue = false },
            "null" => new Payload.Types.Metric { Name = name, BooleanValue = true, IsNull = true },
            "wrong-type" => new Payload.Types.Metric { Name = name, IntValue = 1 },
            _ => new Payload.Types.Metric { Name = "Some/Other", IntValue = 1 }, // missing/unknown-only
        });
        return payload.ToByteArray();
    }

    private static byte[] NonRebirthCommand()
    {
        var payload = new Payload();
        payload.Metrics.Add(new Payload.Types.Metric { Name = "Some/Other", IntValue = 1 });
        return payload.ToByteArray();
    }

    private static List<byte[]> NBirths(FakeTransport fake) =>
        fake.Published.Where(p => p.Topic.Contains("NBIRTH")).Select(p => p.Payload).ToList();

    private sealed class CapturingHost : IReplaySessionHost
    {
        public List<RebirthRequest> Requests { get; } = new();
        public int ThrowOnRequestCount { get; set; } // first N requests throw before acceptance

        public ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken)
        {
            if (ThrowOnRequestCount > 0)
            {
                ThrowOnRequestCount--;
                throw new InvalidOperationException("host rebirth request rejected");
            }

            Requests.Add(request);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTransport : ISparkplugMqttTransport
    {
        public List<(string Topic, byte[] Payload)> Published { get; } = new();
        public List<string> Events { get; } = new(); // ordered: "publish:NDEATH", "disconnect", "dispose"
        public long? Generation { get; private set; }
        public bool IsConnected { get; private set; }
        public bool Connected { get; private set; }
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }
        public bool PublishReturnsFalse { get; set; }
        public bool ThrowOnPublish { get; set; }
        public bool FailConnect { get; set; }
        public bool FailSubscribe { get; set; }

        public event Func<long, Task>? Disconnected;
        public event Func<long, ReadOnlyMemory<byte>, Task>? NodeCommandReceived;

        public Task RaiseDisconnected(long generation) => Disconnected?.Invoke(generation) ?? Task.CompletedTask;

        public Task RaiseNodeCommand(long generation, byte[] payload) =>
            NodeCommandReceived?.Invoke(generation, payload) ?? Task.CompletedTask;

        public Task ConnectAsync(SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken)
        {
            Generation = connectionGeneration;
            // Mirror the concrete transport: a CONNECT failure surfaces as a typed, RETRYABLE transport error.
            if (FailConnect) { throw Transport(SparkplugErrors.TransportConnectFailed, "connect failed"); }
            IsConnected = true;
            Connected = true;
            return Task.CompletedTask;
        }

        public Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken)
        {
            if (FailSubscribe) { throw Transport(SparkplugErrors.TransportSubscribeFailed, "subscribe failed"); }
            return Task.CompletedTask;
        }

        private static AdapterException Transport(string code, string message) =>
            new(new AdapterError { Code = code, Category = ErrorCategory.Network, Message = message, Retryable = false });

        public Func<Task>? OnPublishOnce { get; set; } // fires once at the start of the next publish

        public async Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            if (OnPublishOnce is { } hook) { OnPublishOnce = null; await hook(); }
            if (ThrowOnPublish) { throw Transport(SparkplugErrors.BirthPublishFailed, "publish threw"); }
            Published.Add((topic, payload.ToArray()));
            Events.Add("publish:" + (topic.Contains("NDEATH") ? "NDEATH" : topic.Contains("NBIRTH") ? "NBIRTH" : "NDATA"));
            return !PublishReturnsFalse;
        }

        public bool DisconnectCalled { get; private set; }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCalled = true; IsConnected = false; Events.Add("disconnect"); return Task.CompletedTask;
        }

        public Task? DisposeGate { get; set; }                 // if set, the transport retirement blocks until it completes
        public TaskCompletionSource? DisposeEntered { get; set; } // signalled when a gated retirement starts blocking

        public async ValueTask DisposeAsync()
        {
            if (DisposeGate is { } gate) { DisposeEntered?.TrySetResult(); await gate; }
            Disposed = true; DisposeCount++; IsConnected = false; Events.Add("dispose");
        }
    }
}
