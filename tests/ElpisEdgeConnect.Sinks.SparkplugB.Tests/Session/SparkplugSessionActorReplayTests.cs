// ============================================================================
// File: Session/SparkplugSessionActorReplayTests.cs
// Purpose: Locks K3 slice-5 Replay/CatchUp/Live DATA + catch-up cutover against a
//          deterministic fake transport (no broker). Covers: session/epoch/phase
//          gating (fail-closed on mismatch); the is_historical flag per phase
//          (Replay/CatchUp = true, Live + final update = false, byte-parity vs an
//          independently-built K2 NDATA); the seq commit point (advanced ONLY after a
//          successful local publish — never on empty batch, first-observed, send
//          failure, suspect, or a fail-closed throw); first-observed → SchemaChange
//          rebirth-before-retry (no seq, no publish); material mutation fail-closed;
//          the catch-up final-update matrix (dirty ∪ changed, the 1→2→1 case, missing-
//          announced fail-closed, first-observed rebirth) and the cutover-suspect
//          composition (final-update send failure latches suspect, requests rebirth,
//          does NOT enter Live).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Configuration;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
using ElpisEdgeConnect.Sinks.SparkplugB.Session;
using ElpisEdgeConnect.Sinks.SparkplugB.Store;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using Org.Eclipse.Tahu.Protobuf;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;

public sealed class SparkplugSessionActorReplayTests : IDisposable
{
    private const string Group = "PlantA";
    private const string Node = "gw-1";
    private static readonly DateTimeOffset Clock = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "k3-replay-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); } }
        catch { /* best effort */ }
    }

    // ==== Happy-path DATA: phase → is_historical, seq commit, full accept ====

    [Fact]
    public async Task Publish_Replay_IsHistorical_AdvancesSeq_FullAccept()
    {
        var (actor, fake) = await BornActor();

        var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AcceptedCount.Should().Be(1);
        actor.NextSeq.Should().Be(2); // seq 1 consumed by this NDATA
        actor.DiagnosticsSnapshot.LastDataPublishAt.Should().Be(Clock); // slice 7: liveness timestamp set on success

        var expected = SparkplugPayloadEncoder.EncodeNData(
            SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
            actor.CurrentManifest!.AliasMap, isHistorical: true);
        NData(fake).Should().Equal(expected); // seq=1, is_historical=true, exact alias/value — all via K2
    }

    [Fact]
    public async Task Publish_Live_IsNotHistorical()
    {
        var (actor, fake) = await BornActor();
        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None); // enter Live via cutover
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Live);
        fake.Published.Clear();

        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Live, first: 10, last: 10), CancellationToken.None);

        var expected = SparkplugPayloadEncoder.EncodeNData(
            SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
            actor.CurrentManifest!.AliasMap, isHistorical: false);
        NData(fake).Should().Equal(expected);
    }

    [Fact]
    public async Task Publish_CatchUp_IsHistorical()
    {
        var (actor, fake) = await BornActor();

        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.CatchUp, first: 5, last: 5), CancellationToken.None);

        var expected = SparkplugPayloadEncoder.EncodeNData(
            SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
            actor.CurrentManifest!.AliasMap, isHistorical: true);
        NData(fake).Should().Equal(expected);
    }

    [Fact]
    public async Task Publish_EmptyBatch_AcceptsZero_ConsumesNoSeq_PublishesNothing()
    {
        var (actor, fake) = await BornActor();

        var result = await actor.PublishAsync(Array.Empty<CanonicalDataPoint>(), Ctx(ReplayPhase.Replay), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AcceptedCount.Should().Be(0);
        actor.NextSeq.Should().Be(1);            // no seq consumed
        fake.Published.Should().BeEmpty();
    }

    // ==== DATA send failure: suspect + rebirth, zero accept, no seq ====

    [Fact]
    public async Task Publish_SendFails_LatchesSuspect_RequestsRebirth_ZeroAccept_NoSeq()
    {
        var (actor, fake, host) = await BornActorWithHost();
        fake.PublishReturnsFalse = true;

        var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.AcceptedCount.Should().Be(0);
        result.Error!.Code.Should().Be(SparkplugErrors.PublishRebirthRequested);
        result.Error.Retryable.Should().BeTrue();
        actor.NextSeq.Should().Be(1);            // send failure consumes no seq
        actor.CurrentSessionSuspect.Should().BeTrue();
        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
        actor.DiagnosticsSnapshot.PublishFailures.Should().Be(1); // slice 7: the DATA send failure is counted
    }

    [Fact]
    public async Task Diagnostics_InTransportDataCancellation_IncrementsPublishFailures()
    {
        var (actor, fake, _) = await BornActorWithHost();
        using var cts = new CancellationTokenSource();
        fake.FailPublish = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };

        await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        actor.DiagnosticsSnapshot.PublishFailures.Should().Be(1); // in-transport DATA cancel = uncertain send (B4)
    }

    [Fact]
    public async Task Diagnostics_PreSendDataCancellation_CountsNoPublishFailure()
    {
        var (actor, _, _) = await BornActorWithHost();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        actor.DiagnosticsSnapshot.PublishFailures.Should().Be(0); // aborted at the gate, never entered the transport
    }

    [Fact]
    public async Task Publish_WhenRebirthPendingFromNodeCommand_AcceptsNothing_NoSeq_NoPublish()
    {
        var (actor, fake, host) = await BornActorWithHost();
        // A host NCMD makes a rebirth pending (transport still healthy) — the control latch blocks DATA.
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
        fake.Published.Clear();

        var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.AcceptedCount.Should().Be(0);
        actor.NextSeq.Should().Be(1);            // no seq consumed once a rebirth is pending
        fake.Published.Should().BeEmpty();       // no new DATA send starts
        host.Requests.Should().ContainSingle();  // still coalesced to the one pending request
        actor.CurrentSessionSuspect.Should().BeFalse(); // a healthy pending rebirth is NOT a transport failure (r2 R2.1)
        result.Error!.Category.Should().Be(Core.Errors.ErrorCategory.Configuration);
    }

    [Fact]
    public async Task Cutover_WhenRebirthPendingFromNodeCommand_NoLive_NoSuspect()
    {
        var (actor, fake, host) = await BornActorWithHost();
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // healthy pending
        fake.Published.Clear();

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);

        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live); // deferred to the rebirth
        actor.CurrentSessionSuspect.Should().BeFalse();                  // ... but transport stays healthy (r2 R2.1)
        fake.Published.Should().BeEmpty();
        host.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Cutover_NodeCommandRacesLiveCommit_NoLive_StaysHealthy()
    {
        var (actor, fake, host) = await BornActorWithHost();
        // A host NCMD lands in the window right before the atomic Live commit (r3 R3.2).
        actor.PreLiveCommitBarrier = () => fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);

        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live); // Live requires an idle episode
        actor.CurrentSessionSuspect.Should().BeFalse();                  // ... but the transport stays healthy
        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.HostCommand);
    }

    [Fact]
    public async Task Publish_FirstObservedThenNodeCommand_RequestReasonStaysSchemaChange()
    {
        var (actor, fake, host) = await BornActorWithHost();
        // First-observed opens a SchemaChange episode (queued); a coalescing NCMD must NOT overwrite its
        // accepted cause — first cause wins (r3 R3.3).
        await actor.PublishAsync(new[] { Point("srcNEW", 5) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());

        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
    }

    [Fact]
    public async Task Publish_FirstObservedTwice_StaysHealthyPending_NotSuspect()
    {
        var (actor, _, host) = await BornActorWithHost();

        // First-observed makes a healthy SchemaChange rebirth pending; a repeated DATA batch must not
        // upgrade it to transport recovery (r2 R2.1).
        (await actor.PublishAsync(new[] { Point("srcNEW", 5) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
            .Success.Should().BeFalse();
        (await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
            .Success.Should().BeFalse();

        actor.CurrentSessionSuspect.Should().BeFalse();
        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
    }

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

    [Fact]
    public async Task Publish_WhenAlreadySuspect_AcceptsNothing_RequestsRebirth_PublishesNothing()
    {
        var (actor, fake, host) = await BornActorWithHost();
        await fake.RaiseDisconnected(actor.CurrentGeneration); // a post-promotion drop → suspect
        actor.CurrentSessionSuspect.Should().BeTrue();

        var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.AcceptedCount.Should().Be(0);
        fake.Published.Should().BeEmpty();       // a suspect authority accepts no DATA (carry-forward #1)
        actor.NextSeq.Should().Be(1);
        host.Requests.Should().ContainSingle();
    }

    // ==== First-observed: SchemaChange rebirth, no seq, no publish (healthy transport) ====

    [Fact]
    public async Task Publish_FirstObservedMetric_RequestsSchemaChangeRebirth_NoSeq_NoPublish_NotSuspect()
    {
        var (actor, fake, host) = await BornActorWithHost();

        var result = await actor.PublishAsync(new[] { Point("srcNEW", 5) }, Ctx(ReplayPhase.Replay), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.AcceptedCount.Should().Be(0);
        actor.NextSeq.Should().Be(1);            // no seq on an unknown metric
        fake.Published.Should().BeEmpty();       // nothing published
        actor.CurrentSessionSuspect.Should().BeFalse(); // transport is healthy — not suspect
        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
        result.Error!.Category.Should().Be(Core.Errors.ErrorCategory.Configuration); // schema growth, not a network error
    }

    // ==== Material mutation: fail closed ====

    [Fact]
    public async Task Publish_MaterialMutation_FailsClosed_Faults()
    {
        var (actor, _) = await BornActor();

        // srcA was announced as Integer; the same key arriving as a Double is a material schema mutation.
        await actor.Invoking(a => a.PublishAsync(
                new[] { Point("srcA", 2.5d, CanonicalValueType.Double) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.MaterialSchemaMutation);

        actor.State.Should().Be(AdapterState.Failed);
        actor.NextSeq.Should().Be(1); // fail-closed throw consumes no seq
    }

    // ==== Session / epoch / no-session gating: fail closed ====

    [Fact]
    public async Task Publish_StaleSession_FailsClosed_Faults()
    {
        var (actor, _) = await BornActor();

        await actor.Invoking(a => a.PublishAsync(
                new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay, session: 999), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.PublishSessionMismatch);

        actor.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task Publish_StaleEpoch_FailsClosed_Faults()
    {
        var (actor, _) = await BornActor();

        await actor.Invoking(a => a.PublishAsync(
                new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay, epoch: 7), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.PublishEpochMismatch);

        actor.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task Publish_NoActiveSession_FailsClosed()
    {
        // Running but Begin never ran — a context publish is a lifecycle-invariant violation.
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => new FakeTransport(), () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);

        await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.PublishNoSession);
    }

    // ==== Cancellation / transport-exception boundary (review r1 B3) ====

    [Fact]
    public async Task Publish_PreCancelledToken_CleanCancellation_NotSuspect()
    {
        var (actor, fake) = await BornActor();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // cancelled BEFORE the transport is entered

        await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        actor.State.Should().Be(AdapterState.Running);
        actor.CurrentSessionSuspect.Should().BeFalse(); // never entered the send — the authority stays clean
        fake.Published.Should().BeEmpty();
        actor.NextSeq.Should().Be(1);
    }

    [Fact]
    public async Task Publish_CancellationAfterTransportEntry_MarksSuspect_NoSeq_NotFaulted()
    {
        var (actor, fake) = await BornActor();
        using var cts = new CancellationTokenSource();
        fake.FailPublish = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };

        await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        actor.State.Should().Be(AdapterState.Running);       // cancellation is not a coarse fault
        actor.CurrentSessionSuspect.Should().BeTrue();       // ... but an in-transport cancel is uncertain → suspect
        actor.NextSeq.Should().Be(1);                        // no seq consumed
    }

    [Fact]
    public async Task Publish_TransportThrows_ZeroAccept_Suspect_RequestsRebirth_NoSeq_NotFaulted()
    {
        var (actor, fake, host) = await BornActorWithHost();
        fake.FailPublish = _ => throw new InvalidOperationException("socket boom");

        var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.AcceptedCount.Should().Be(0);
        actor.State.Should().Be(AdapterState.Running);       // normalized to a rebirth, NOT a terminal fault
        actor.CurrentSessionSuspect.Should().BeTrue();
        actor.NextSeq.Should().Be(1);
        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
    }

    // ==== seq wrap (modulo 256) — frozen acceptance matrix ====

    [Fact]
    public async Task Publish_SeqWrapsThrough255To0_WithWireEvidence()
    {
        var (actor, fake) = await BornActor();
        var aliasMap = actor.CurrentManifest!.AliasMap;

        for (var i = 1; i <= 254; i++) // consume seq 1..254
        {
            (await actor.PublishAsync(new[] { Point("srcA", i) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
                .Success.Should().BeTrue();
        }

        actor.NextSeq.Should().Be(255);
        fake.Published.Clear();
        await actor.PublishAsync(new[] { Point("srcA", 1) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // uses seq 255
        NData(fake).Should().Equal(SparkplugPayloadEncoder.EncodeNData(
            SparkplugSequenceNumber.Create(255), Clock, new[] { Sample("srcA", 1) }, aliasMap, isHistorical: true));
        actor.NextSeq.Should().Be(0); // wrapped

        fake.Published.Clear();
        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // uses seq 0
        NData(fake).Should().Equal(SparkplugPayloadEncoder.EncodeNData(
            SparkplugSequenceNumber.Create(0), Clock, new[] { Sample("srcA", 2) }, aliasMap, isHistorical: true));
        actor.NextSeq.Should().Be(1);
    }

    // ==== Exhaustive classification precedence: material mutation wins (review r1 B2) ====

    [Theory]
    [InlineData(true)]  // [first-observed, material-mutation]
    [InlineData(false)] // [material-mutation, first-observed]
    public async Task Publish_MixedFirstObservedAndMaterialMutation_MaterialWins(bool firstObservedFirst)
    {
        var (actor, fake, host) = await BornActorWithHost();
        var material = Point("srcA", 2.5d, CanonicalValueType.Double); // srcA announced Integer → material mutation
        var firstObserved = Point("srcNEW", 5);                        // not in manifest → first-observed
        var batch = firstObservedFirst ? new[] { firstObserved, material } : new[] { material, firstObserved };

        await actor.Invoking(a => a.PublishAsync(batch, Ctx(ReplayPhase.Replay), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.MaterialSchemaMutation);

        actor.State.Should().Be(AdapterState.Failed);
        fake.Published.Should().BeEmpty();      // no publish regardless of order
        actor.NextSeq.Should().Be(1);           // no seq
        host.Requests.Should().BeEmpty();        // no rebirth escaped before the hard violation
    }

    [Theory]
    [InlineData(true)]  // [first-observed, known-invalid]
    [InlineData(false)] // [known-invalid, first-observed]
    public async Task Publish_FirstObservedAndMalformedKnownPoint_FailsClosed_NoRebirth(bool firstObservedFirst)
    {
        var (actor, fake, host) = await BornActorWithHost();
        var firstObserved = Point("srcNEW", 5);                                     // valid, not in manifest
        var malformed = Point("srcA", 2, CanonicalValueType.Integer, NonUtcTimestamp); // announced, but non-UTC timestamp
        var batch = firstObservedFirst ? new[] { firstObserved, malformed } : new[] { malformed, firstObserved };

        await actor.Invoking(a => a.PublishAsync(batch, Ctx(ReplayPhase.Replay), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.EncodeTimestampNotUtc);

        actor.State.Should().Be(AdapterState.Failed); // full wire preflight rejects the malformed point BEFORE the rebirth decision
        fake.Published.Should().BeEmpty();
        actor.NextSeq.Should().Be(1);
        host.Requests.Should().BeEmpty();             // no SchemaChange rebirth concealed the malformed DATA
    }

    [Fact]
    public async Task Publish_FirstObservedPointItself_WrongClrValue_FailsClosed_NoRebirth()
    {
        var (actor, fake, host) = await BornActorWithHost();
        // srcNEW is first-observed AND carries a string under a declared Integer type — the wire preflight
        // must reject it rather than emit a SchemaChange rebirth for a malformed metric.
        await actor.Invoking(a => a.PublishAsync(
                new[] { Point("srcNEW", "not-an-int", CanonicalValueType.Integer) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.EncodeValueTypeMismatch);

        actor.State.Should().Be(AdapterState.Failed);
        fake.Published.Should().BeEmpty();
        actor.NextSeq.Should().Be(1);
        host.Requests.Should().BeEmpty();
    }

    // ==== Catch-up cutover: final-update matrix ====

    [Fact]
    public async Task Cutover_DirtyMetricReturnsToBirthValue_StillEmitsFinalUpdate_EntersLive()
    {
        var (actor, fake) = await BornActor();
        // 1 (birth) -> 2 (replay, dirty) -> 1 (cutover): stays dirty, final non-historical 1 emitted.
        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
        fake.Published.Clear();

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);

        var expected = SparkplugPayloadEncoder.EncodeNData(
            SparkplugSequenceNumber.Create(2), Clock, new[] { Sample("srcA", 1) },
            actor.CurrentManifest!.AliasMap, isHistorical: false);
        NData(fake).Should().Equal(expected); // only the dirty metric, non-historical, seq=2
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Live);
        actor.NextSeq.Should().Be(3);
    }

    [Fact]
    public async Task Cutover_NoChangeSinceBirth_EmitsNothing_ConsumesNoSeq_EntersLive()
    {
        var (actor, fake) = await BornActor();

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);

        fake.Published.Should().BeEmpty();       // nothing changed → no final update
        actor.NextSeq.Should().Be(1);            // no seq consumed
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Live);
    }

    [Fact]
    public async Task Cutover_MissingAnnouncedMetric_FailsClosed_Faults()
    {
        var (actor, _) = await BornActor();

        await actor.Invoking(a => a.CompleteCatchUpAsync(Cutover(("srcA", 1)), CancellationToken.None)) // srcB missing
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.ManifestInvariantViolation);

        actor.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task Cutover_FirstObservedMetric_RequestsSchemaChangeRebirth_DoesNotEnterLive()
    {
        var (actor, fake, host) = await BornActorWithHost();

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1), ("srcNEW", 9)), CancellationToken.None);

        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
        fake.Published.Should().BeEmpty();       // no final update emitted
        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
    }

    // ==== Cutover-suspect composition (the §4.4 special rule) ====

    [Fact]
    public async Task Cutover_FinalUpdateSendFails_LatchesSuspect_RequestsRebirth_DoesNotEnterLive()
    {
        var (actor, fake, host) = await BornActorWithHost();
        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // dirty srcA
        fake.Published.Clear();
        fake.PublishReturnsFalse = true; // the final-update send will fail

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 5), ("srcB", 1)), CancellationToken.None);

        actor.CurrentSessionSuspect.Should().BeTrue();
        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live); // final update not claimed; Core rebirths first
    }

    [Fact]
    public async Task Cutover_WhenAlreadySuspect_RequestsRebirth_DoesNotEnterLive()
    {
        var (actor, fake, host) = await BornActorWithHost();
        await fake.RaiseDisconnected(actor.CurrentGeneration); // suspect before cutover

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);

        host.Requests.Should().ContainSingle();
        fake.Published.Should().BeEmpty();
        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
    }

    [Fact]
    public async Task Cutover_StaleEpoch_FailsClosed_Faults()
    {
        var (actor, _) = await BornActor();

        await actor.Invoking(a => a.CompleteCatchUpAsync(
                ReplaySessionCutover.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(9),
                    ReplaySessionCutoverState.Create(6, SnapshotOf(("srcA", 1), ("srcB", 1)))), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.PublishEpochMismatch);

        actor.State.Should().Be(AdapterState.Failed);
    }

    // ==== Cutover static-schema preflight (review r1 B1) ====

    [Fact]
    public async Task Cutover_MaterialMutation_FailsClosed_NoPublish_NoSeq_NoRebirth()
    {
        var (actor, fake, host) = await BornActorWithHost();

        // srcA announced Integer; the cutover snapshot presents it as Double → material mutation.
        await actor.Invoking(a => a.CompleteCatchUpAsync(
                CutoverTyped(("srcA", 2.5d, CanonicalValueType.Double), ("srcB", 1, CanonicalValueType.Integer)),
                CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.MaterialSchemaMutation);

        actor.State.Should().Be(AdapterState.Failed);
        fake.Published.Should().BeEmpty();  // no final update
        actor.NextSeq.Should().Be(1);       // no seq
        host.Requests.Should().BeEmpty();   // material mutation wins — no rebirth escapes
    }

    [Theory]
    [InlineData(true)]  // material metric enumerated first
    [InlineData(false)] // material metric enumerated last (after the first-observed)
    public async Task Cutover_MixedFirstObservedAndMaterialMutation_MaterialWins(bool materialFirst)
    {
        var (actor, fake, host) = await BornActorWithHost();
        var material = ("srcA", (object)2.5d, CanonicalValueType.Double);     // announced Integer → material
        var srcB = ("srcB", (object)1, CanonicalValueType.Integer);          // unchanged
        var firstObserved = ("srcNEW", (object)9, CanonicalValueType.Integer); // not in manifest
        var metrics = materialFirst
            ? new[] { material, srcB, firstObserved }
            : new[] { firstObserved, srcB, material };

        await actor.Invoking(a => a.CompleteCatchUpAsync(CutoverTyped(metrics), CancellationToken.None))
            .Should().ThrowAsync<Core.Errors.AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.MaterialSchemaMutation);

        actor.State.Should().Be(AdapterState.Failed);
        host.Requests.Should().BeEmpty();
        fake.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Cutover_FinalUpdateTransportThrows_Suspect_RequestsRebirth_NotLive_NotFaulted()
    {
        var (actor, fake, host) = await BornActorWithHost();
        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // dirty srcA
        fake.FailPublish = _ => throw new InvalidOperationException("socket boom");

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 5), ("srcB", 1)), CancellationToken.None);

        actor.State.Should().Be(AdapterState.Running); // not faulted
        actor.CurrentSessionSuspect.Should().BeTrue();
        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
    }

    [Fact]
    public async Task Cutover_FinalUpdateCancellationAfterTransportEntry_MarksSuspect_NoSeq_NotLive_NotFaulted()
    {
        var (actor, fake) = await BornActor();
        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // dirty srcA, seq→2
        using var cts = new CancellationTokenSource();
        fake.FailPublish = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };

        await actor.Invoking(a => a.CompleteCatchUpAsync(Cutover(("srcA", 5), ("srcB", 1)), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        actor.State.Should().Be(AdapterState.Running);  // in-transport cancellation is not a coarse fault
        actor.CurrentSessionSuspect.Should().BeTrue();  // ... but the final-update send is uncertain → suspect
        actor.NextSeq.Should().Be(2);                   // the final-update send consumed no seq
        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
    }

    // ==== Cutover→Live vs. the asynchronous suspect latch (review r1 B4) ====

    [Fact]
    public async Task Cutover_NoChange_DisconnectWinsBeforeLiveCommit_Suspect_NotLive()
    {
        var (actor, fake, host) = await BornActorWithHost();
        // A disconnect lands in the window immediately BEFORE the Live compare-exchange.
        actor.PreLiveCommitBarrier = () => fake.RaiseDisconnected(actor.CurrentGeneration);

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);

        actor.CurrentSessionSuspect.Should().BeTrue();
        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live); // suspect won the race — no Live on a dead authority
        host.Requests.Should().ContainSingle();                          // rebirth requested instead
    }

    [Fact]
    public async Task Cutover_SuccessfulFinalUpdate_DisconnectWinsBeforeLiveCommit_Suspect_NotLive()
    {
        var (actor, fake, host) = await BornActorWithHost();
        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // dirty srcA
        actor.PreLiveCommitBarrier = () => fake.RaiseDisconnected(actor.CurrentGeneration);

        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);

        actor.CurrentSessionSuspect.Should().BeTrue();
        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
        host.Requests.Should().ContainSingle();
    }

    // ==== Helpers ====

    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake)> BornActor()
    {
        var fake = new FakeTransport();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(StartPopulated(new CapturingHost()), CancellationToken.None);
        fake.Published.Clear(); // drop the birth NBIRTH — tests assert on slice-5 NDATA only
        return (actor, fake);
    }

    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake, CapturingHost Host)> BornActorWithHost()
    {
        var fake = new FakeTransport();
        var host = new CapturingHost();
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(StartPopulated(host), CancellationToken.None);
        fake.Published.Clear(); // drop the birth NBIRTH — tests assert on slice-5 NDATA only
        return (actor, fake, host);
    }

    private SqliteSparkplugIdentityStateStore NewStore() =>
        new(Path.Combine(_dir, "sparkplug", "identity-state.db"));

    private static SparkplugSinkConfiguration ValidConfig() => new()
    {
        InstanceId = "spb-1",
        ProtocolName = SparkplugBProtocol.ProtocolName,
        BrokerHost = "localhost",
        GroupId = Group,
        EdgeNodeId = Node,
    };

    // Birth with srcA and srcB (both Integer=1), boundary H=5.
    private static ReplaySessionStart StartPopulated(CapturingHost host)
    {
        var snapshot = SnapshotOf(("srcA", 1), ("srcB", 1));
        return ReplaySessionStart.Create(
            ReplaySessionId.Create(1), ReplayEpochId.Create(0), "route-1",
            ReplaySessionStartState.Create(ReplayBoundary.Create(0, 5), snapshot), host);
    }

    private static LatestValueSnapshot SnapshotOf(params (string Source, int Value)[] metrics)
    {
        var dict = metrics.ToDictionary(m => Key(m.Source), m => LatestMetricValue.Create(
            Key(m.Source), CanonicalValueType.Integer, m.Value, isNull: false, Clock, DataQuality.Good, routeBufferSequence: 1));
        return new LatestValueSnapshot(RouteSchemaGeneration.Create(0), dict);
    }

    private static ReplaySessionCutover Cutover(params (string Source, int Value)[] metrics) =>
        ReplaySessionCutover.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(0),
            ReplaySessionCutoverState.Create(5, SnapshotOf(metrics)));

    private static ReplaySessionCutover CutoverTyped(params (string Source, object Value, CanonicalValueType Type)[] metrics)
    {
        var dict = metrics.ToDictionary(m => Key(m.Source), m => LatestMetricValue.Create(
            Key(m.Source), m.Type, m.Value, isNull: false, Clock, DataQuality.Good, routeBufferSequence: 1));
        return ReplaySessionCutover.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(0),
            ReplaySessionCutoverState.Create(5, new LatestValueSnapshot(RouteSchemaGeneration.Create(0), dict)));
    }

    private static CanonicalMetricKey Key(string source) => CanonicalMetricKey.Create(source, "dev", "temp");

    private static PublishContext Ctx(
        ReplayPhase phase, long session = 1, long epoch = 0, long first = 0, long last = 0) =>
        PublishContext.Create("route-1", ReplaySessionId.Create(session), ReplayEpochId.Create(epoch), phase,
            replayCutoffExclusive: 5, catchUpCutoffExclusive: 10, first, last);

    private static CanonicalDataPoint Point(
        string source, object? value, CanonicalValueType type = CanonicalValueType.Integer, DateTime? deviceTimestamp = null) => new()
    {
        GatewayId = "gw",
        SourceInstanceId = source,
        ProtocolName = "test",
        DeviceId = "dev",
        TagName = "temp",
        TagPath = "temp",
        Value = value,
        ValueType = type,
        Quality = DataQuality.Good,
        DeviceTimestamp = deviceTimestamp ?? Clock.UtcDateTime,
        GatewayTimestamp = Clock.UtcDateTime,
    };

    // A DeviceTimestamp with Kind=Unspecified — the shared mapper must reject it (ENCODE_TIMESTAMP_NOT_UTC).
    private static readonly DateTime NonUtcTimestamp =
        DateTime.SpecifyKind(new DateTime(2021, 1, 1, 0, 0, 0), DateTimeKind.Unspecified);

    private static SparkplugMetricSample Sample(string source, object? value, CanonicalValueType type = CanonicalValueType.Integer) => new()
    {
        Key = SparkplugAliasKey.FromCanonical(Key(source)),
        ValueType = type,
        Value = value,
        IsNull = value is null,
        AcquisitionTimestamp = SparkplugAcquisitionTimestamp.RequireUtc(Clock.UtcDateTime),
        Quality = DataQuality.Good,
    };

    private static byte[] NData(FakeTransport fake) =>
        fake.Published.Single(p => p.Topic.Contains("NDATA")).Payload;

    private sealed class CapturingHost : IReplaySessionHost
    {
        public List<RebirthRequest> Requests { get; } = new();

        public ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTransport : ISparkplugMqttTransport
    {
        public List<(string Topic, byte[] Payload)> Published { get; } = new();
        public long? Generation { get; private set; }
        public bool IsConnected { get; private set; }
        public bool PublishReturnsFalse { get; set; }
        public Func<CancellationToken, Task>? FailPublish { get; set; }

        public event Func<long, Task>? Disconnected;
        public event Func<long, ReadOnlyMemory<byte>, Task>? NodeCommandReceived;

        public Task RaiseDisconnected(long generation) => Disconnected?.Invoke(generation) ?? Task.CompletedTask;

        public Task RaiseNodeCommand(long generation, byte[] payload) =>
            NodeCommandReceived?.Invoke(generation, payload) ?? Task.CompletedTask;

        public Task ConnectAsync(SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken)
        {
            Generation = connectionGeneration;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            if (FailPublish is not null) { await FailPublish(cancellationToken); }
            Published.Add((topic, payload.ToArray()));
            return !PublishReturnsFalse;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; return Task.CompletedTask; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
