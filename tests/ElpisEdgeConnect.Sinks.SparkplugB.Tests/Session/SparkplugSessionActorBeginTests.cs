// ============================================================================
// File: Session/SparkplugSessionActorBeginTests.cs
// Purpose: Locks K3 slice-4 initial Begin against a deterministic FAKE transport
//          (no broker — the team avoids the in-process MQTTnet server; the concrete
//          transport's real-wire behavior is a K6 broker-interop concern). Covers:
//          the CONNECT -> exact-NCMD SUBSCRIBE -> NBIRTH ordering; the per-attempt
//          NDEATH Will (topic + bdSeq payload); durable bdSeq before CONNECT; alias
//          resolution against the store; the actor-owned long generation token;
//          atomic promotion on NBIRTH success; and "failed CONNECT/SUBSCRIBE/NBIRTH
//          promotes nothing, retires the attempt, faults"; plus the not-wired /
//          not-Running gates.
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
using Microsoft.Data.Sqlite;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;

public sealed class SparkplugSessionActorBeginTests : IDisposable
{
    private const string Group = "PlantA";
    private const string Node = "gw-1";
    private static readonly DateTimeOffset Clock = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "k3-begin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); } }
        catch { /* best effort */ }
    }

    // ==== Happy path ====

    [Fact]
    public async Task Begin_EmptyRoute_OrdersConnectSubscribeNbirth_AndPromotes()
    {
        var (actor, fake) = await RunningActor();

        await actor.BeginReplaySessionAsync(Start(sessionId: 7, epoch: 3), CancellationToken.None);

        fake.Calls.Should().Equal(
            "connect",
            $"subscribe:spBv1.0/{Group}/NCMD/{Node}",
            $"publish:spBv1.0/{Group}/NBIRTH/{Node}");

        actor.State.Should().Be(AdapterState.Running);
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Replaying);
        actor.HasSession.Should().BeTrue();
        actor.CurrentSessionId.Should().Be(ReplaySessionId.Create(7));
        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(3));
        actor.CurrentRouteId.Should().Be("route-1");
        actor.CurrentHost.Should().NotBeNull();
        actor.CurrentBdSeq.Value.Should().Be(0); // first reservation
        actor.CurrentGeneration.Should().Be(1); // actor-owned long, first attempt
        actor.LastIssuedGeneration.Should().Be(1);
    }

    [Fact]
    public async Task Begin_RegistersNDeathWill_WithReservedBdSeq()
    {
        var (actor, fake) = await RunningActor();

        await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);

        fake.ConnectRequest!.WillTopic.Should().Be($"spBv1.0/{Group}/NDEATH/{Node}");
        fake.ConnectRequest.WillPayload.ToArray()
            .Should().Equal(SparkplugPayloadEncoder.EncodeNDeath(SparkplugBirthDeathSequence.Create(0)));
        fake.Generation.Should().Be(1); // actor supplied the generation
    }

    [Fact]
    public async Task Begin_ReservesBdSeqBeforeConnect_AndPersistsIt()
    {
        var store = NewStore();
        var (actor, _) = await RunningActor(store);

        await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);

        // The reservation committed to the store (a fresh reserve continues from 1).
        store.ReserveNextBdSeq(StoreId()).Value.Should().Be(1);
    }

    [Fact]
    public async Task Begin_PopulatedRoute_ResolvesAliases_AndPromotesManifest()
    {
        var store = NewStore();
        var (actor, _) = await RunningActor(store);

        await actor.BeginReplaySessionAsync(StartPopulated(), CancellationToken.None);

        actor.CurrentManifest!.Metrics.Should().HaveCount(2);
        actor.CurrentManifest.AliasMap.Values.Should().OnlyHaveUniqueItems().And.NotContain(0UL);
        actor.CurrentBaseline!.BaselineMetrics.Should().HaveCount(2);
    }

    // ==== Failed step promotes nothing ====

    [Theory]
    [InlineData("connect")]
    [InlineData("subscribe")]
    [InlineData("nbirth")]
    public async Task Begin_FailedStep_PromotesNothing_RetiresAttempt_Faults(string failAt)
    {
        var (actor, fake) = await RunningActor();
        switch (failAt)
        {
            case "connect": fake.FailConnect = _ => throw new InvalidOperationException("connect"); break;
            case "subscribe": fake.FailSubscribe = _ => throw new InvalidOperationException("subscribe"); break;
            case "nbirth": fake.PublishReturnsFalse = true; break;
        }

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
            .Should().ThrowAsync<Exception>();

        actor.HasSession.Should().BeFalse();
        actor.CurrentManifest.Should().BeNull();
        actor.State.Should().Be(AdapterState.Failed);
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
        fake.Calls.Should().Contain("dispose"); // the failed attempt's transport was retired
    }

    [Fact]
    public async Task Begin_FailedNbirth_LeavesStoreBdSeqReservedButUnused()
    {
        var store = NewStore();
        var (actor, fake) = await RunningActor(store);
        fake.PublishReturnsFalse = true;

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None)).Should().ThrowAsync<Exception>();

        // bdSeq 0 was reserved (committed before CONNECT) and is skipped, never reused.
        store.ReserveNextBdSeq(StoreId()).Value.Should().Be(1);
    }

    // ==== Preflight failure precedes every durable side effect (review B1) ====

    [Fact]
    public async Task Begin_AliasResolutionFails_ConsumesNoBdSeq_NoGeneration_NoTransport()
    {
        // Preflight (plan + alias resolve) runs BEFORE bdSeq reservation, the generation issue,
        // and transport creation. An alias-store failure must therefore leave all three untouched.
        var store = new ThrowingAliasStore();
        var transportCreated = 0;
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => { transportCreated++; return new FakeTransport(); }, () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.IdentityStoreUnavailable);

        store.ReserveCalls.Should().Be(0);           // no durable bdSeq consumed
        actor.LastIssuedGeneration.Should().Be(0);   // no generation issued
        transportCreated.Should().Be(0);             // no client created
        actor.HasSession.Should().BeFalse();
        actor.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task Begin_PreEpochSnapshot_FailsBeforeAliasBdSeqGenerationOrTransport()
    {
        // A pre-Unix-epoch acquisition timestamp is a VALID Core LatestValueSnapshot input that the
        // shared Sparkplug mapper rejects during planning — the reachable planning failure. It must
        // precede alias resolution, bdSeq reservation, the generation issue, and transport creation.
        var store = new RecordingStore();
        var transportCreated = 0;
        var host = new FakeReplaySessionHost();
        var actor = new SparkplugSessionActor(
            "spb-1", store, () => { transportCreated++; return new FakeTransport(); }, () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);

        await actor.Invoking(a => a.BeginReplaySessionAsync(StartPreEpoch(host), CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.EncodeTimestampPreEpoch);

        store.AliasCalls.Should().Be(0);             // planning rejected before alias resolution
        store.ReserveCalls.Should().Be(0);           // ... and before bdSeq reservation
        actor.LastIssuedGeneration.Should().Be(0);   // no generation issued
        transportCreated.Should().Be(0);             // no client created
        actor.HasSession.Should().BeFalse();
        actor.State.Should().Be(AdapterState.Failed);
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
        host.RebirthRequests.Should().Be(0);
    }

    // ==== Atomic disconnect/promotion handoff (review r2 R2) ====

    [Fact]
    public async Task Begin_DisconnectRacesPromotion_PromotesNothing_FaultsSuspect()
    {
        var (actor, fake) = await RunningActor();
        var host = new FakeReplaySessionHost();

        // Deterministically inject the drop in the window immediately BEFORE the promotion CAS:
        // NBIRTH already succeeded locally; the disconnect must still prevent promotion.
        actor.PrePromotionBarrier = () => fake.RaiseDisconnected(fake.Generation!.Value);

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(host: host), CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.SessionSuspectDuringBegin);

        actor.HasSession.Should().BeFalse();      // nothing promoted onto a dead transport
        actor.State.Should().Be(AdapterState.Failed);
        host.RebirthRequests.Should().Be(0);      // no authoritative birth existed
        fake.Calls.Should().Contain("dispose");   // the suspect attempt was aborted
    }

    [Fact]
    public async Task Begin_PostPromotionDisconnect_MarksSessionSuspect_NotCleanReplaying()
    {
        var (actor, fake) = await RunningActor();
        await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);
        actor.CurrentSessionSuspect.Should().BeFalse(); // clean at promotion

        // A genuine drop AFTER promotion must be captured, not lost: the promoted authority is
        // flagged suspect for the operational path (slice 6), never left as clean Replaying.
        await fake.RaiseDisconnected(actor.CurrentGeneration);

        actor.HasSession.Should().BeTrue();             // the authority remains (slice 6 recovers)
        actor.CurrentSessionSuspect.Should().BeTrue();  // ... but is now suspect
        actor.ProtocolState.Should().Be(SparkplugProtocolState.Replaying);
    }

    // ==== Readiness / wiring gates ====

    [Fact]
    public async Task Begin_NotWiredWithStore_FailsClosed()
    {
        var actor = new SparkplugSessionActor("spb-1"); // lifecycle-only, no store
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.SessionNotReady);
    }

    [Fact]
    public async Task Begin_BeforeRunning_FailsClosed()
    {
        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => new FakeTransport(), () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None); // not started

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Begin_HealthAfterBirth_IsDegradedWithSession()
    {
        var (actor, _) = await RunningActor();
        await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);

        var health = await actor.CheckHealthAsync(CancellationToken.None);
        health.Level.Should().Be(HealthLevel.Degraded); // replaying = active session
        health.Metrics.Should().ContainKey("hasSession").WhoseValue.Should().Be(true);
    }

    // ==== NBIRTH content (byte-parity with an independently-built K2 payload) ====

    [Fact]
    public async Task Begin_EmptyRoute_PublishesExpectedNbirthBytes()
    {
        var (actor, fake) = await RunningActor();
        await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);

        var expected = SparkplugPayloadEncoder.EncodeNBirth(
            SparkplugSequenceNumber.Create(0), SparkplugBirthDeathSequence.Create(0), bdSeqAlias: 1UL, Clock,
            actor.CurrentManifest!.Metrics, actor.CurrentManifest.AliasMap);
        var nbirth = fake.Published.Single(p => p.Topic.Contains("NBIRTH")).Payload;
        nbirth.Should().Equal(expected); // seq=0, bdSeq=0, Node Control/Rebirth=false, control metrics — all via K2
    }

    [Fact]
    public async Task Begin_PopulatedRoute_PublishesExpectedNbirthBytes()
    {
        var (actor, fake) = await RunningActor();
        await actor.BeginReplaySessionAsync(StartPopulated(), CancellationToken.None);

        var expected = SparkplugPayloadEncoder.EncodeNBirth(
            SparkplugSequenceNumber.Create(0), SparkplugBirthDeathSequence.Create(0), bdSeqAlias: 3UL, Clock,
            actor.CurrentManifest!.Metrics, actor.CurrentManifest.AliasMap); // bdSeqAlias = max(1,2)+1
        var nbirth = fake.Published.Single(p => p.Topic.Contains("NBIRTH")).Payload;
        nbirth.Should().Equal(expected);
    }

    // ==== Duplicate Begin ====

    [Fact]
    public async Task Begin_SecondBegin_FailsClosed_WithNoSideEffects()
    {
        var (actor, fake) = await RunningActor();
        await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);
        var generationAfterFirst = actor.LastIssuedGeneration;
        var callsAfterFirst = fake.Calls.Count;

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.SessionAlreadyActive);

        actor.LastIssuedGeneration.Should().Be(generationAfterFirst); // no new generation issued
        fake.Calls.Count.Should().Be(callsAfterFirst);                // no new store/network call
    }

    // ==== Generation is an attempt token (consumed even on failure) ====

    [Fact]
    public async Task Begin_FailedAttempt_ConsumesGeneration_ThenRestartUsesNext()
    {
        var store = NewStore();
        var fake1 = new FakeTransport { PublishReturnsFalse = true };
        var fake2 = new FakeTransport();
        var factoryCall = 0;
        var actor = new SparkplugSessionActor("spb-1", store, () => (factoryCall++ == 0 ? (ISparkplugMqttTransport)fake1 : fake2), () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None)).Should().ThrowAsync<Exception>();
        actor.LastIssuedGeneration.Should().Be(1); // failed attempt consumed generation 1
        actor.HasSession.Should().BeFalse();

        // Recover (Stop -> Start) and Begin again — the next attempt uses generation 2.
        await actor.StopAsync(CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);

        actor.LastIssuedGeneration.Should().Be(2);
        actor.CurrentGeneration.Should().Be(2);
        fake2.Generation.Should().Be(2);
    }

    // ==== Cancellation at each step ====

    [Theory]
    [InlineData("connect")]
    [InlineData("subscribe")]
    [InlineData("nbirth")]
    public async Task Begin_CancellationAtStep_FaultsAndPromotesNothing(string step)
    {
        var (actor, fake) = await RunningActor();
        using var cts = new CancellationTokenSource();
        Func<CancellationToken, Task> cancel = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        switch (step)
        {
            case "connect": fake.FailConnect = cancel; break;
            case "subscribe": fake.FailSubscribe = cancel; break;
            case "nbirth": fake.FailPublish = cancel; break;
        }

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        actor.HasSession.Should().BeFalse();
        actor.State.Should().Be(AdapterState.Failed);
        fake.Calls.Should().Contain("dispose");
    }

    // ==== Pre-authoritative disconnect ====

    [Fact]
    public async Task Begin_PreAuthoritativeDisconnect_FaultsAndRequestsNoRebirth()
    {
        var (actor, fake) = await RunningActor();
        var host = new FakeReplaySessionHost();
        fake.DisconnectDuringSubscribe = true;

        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(host: host), CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.SessionSuspectDuringBegin);

        actor.HasSession.Should().BeFalse();
        host.RebirthRequests.Should().Be(0); // no authoritative birth exists — never request a Core rebirth
        fake.Calls.Should().Contain("dispose");
    }

    // ==== Helpers ====

    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake)> RunningActor(
        SqliteSparkplugIdentityStateStore? store = null)
    {
        var fake = new FakeTransport();
        var actor = new SparkplugSessionActor("spb-1", store ?? NewStore(), () => fake, () => Clock);
        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
        await actor.StartAsync(CancellationToken.None);
        return (actor, fake);
    }

    private SqliteSparkplugIdentityStateStore NewStore() =>
        new(Path.Combine(_dir, "sparkplug", "identity-state.db"));

    private static SparkplugStoreIdentity StoreId() =>
        SparkplugStoreIdentity.Create(
            ValidConfig().ResolveBrokerEndpoint(), SparkplugEdgeNodeIdentity.Create(Group, Node));

    private static SparkplugSinkConfiguration ValidConfig() => new()
    {
        InstanceId = "spb-1",
        ProtocolName = SparkplugBProtocol.ProtocolName,
        BrokerHost = "localhost",
        GroupId = Group,
        EdgeNodeId = Node,
    };

    private static ReplaySessionStart Start(long sessionId = 1, long epoch = 0, FakeReplaySessionHost? host = null) =>
        ReplaySessionStart.Create(
            ReplaySessionId.Create(sessionId), ReplayEpochId.Create(epoch), "route-1",
            ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(0))),
            host ?? new FakeReplaySessionHost());

    private static ReplaySessionStart StartPopulated()
    {
        var snapshot = new LatestValueSnapshot(RouteSchemaGeneration.Create(0), new Dictionary<CanonicalMetricKey, LatestMetricValue>
        {
            [Key("srcA")] = Lmv("srcA"),
            [Key("srcB")] = Lmv("srcB"),
        });
        return ReplaySessionStart.Create(
            ReplaySessionId.Create(1), ReplayEpochId.Create(0), "route-1",
            ReplaySessionStartState.Create(ReplayBoundary.Create(0, 5), snapshot), new FakeReplaySessionHost());
    }

    private static ReplaySessionStart StartPreEpoch(FakeReplaySessionHost host)
    {
        var preEpoch = new DateTimeOffset(1969, 12, 31, 0, 0, 0, TimeSpan.Zero); // before the Unix epoch
        var value = LatestMetricValue.Create(
            Key("srcA"), CanonicalValueType.Integer, 1, isNull: false, preEpoch, DataQuality.Good, routeBufferSequence: 1);
        var snapshot = new LatestValueSnapshot(RouteSchemaGeneration.Create(0),
            new Dictionary<CanonicalMetricKey, LatestMetricValue> { [Key("srcA")] = value });
        return ReplaySessionStart.Create(
            ReplaySessionId.Create(1), ReplayEpochId.Create(0), "route-1",
            ReplaySessionStartState.Create(ReplayBoundary.Create(0, 2), snapshot), host); // cutoff strictly above seq 1
    }

    private static CanonicalMetricKey Key(string source) => CanonicalMetricKey.Create(source, "dev", "temp");

    private static LatestMetricValue Lmv(string source) =>
        LatestMetricValue.Create(Key(source), CanonicalValueType.Integer, 1, isNull: false, Clock, DataQuality.Good, routeBufferSequence: 1);

    // A store that fails alias resolution and records whether the durable bdSeq reservation was
    // ever reached — proving preflight failure precedes any durable side effect (review B1).
    private sealed class ThrowingAliasStore : ISparkplugIdentityStateStore
    {
        public int ReserveCalls { get; private set; }

        public SparkplugBirthDeathSequence ReserveNextBdSeq(SparkplugStoreIdentity identity)
        {
            ReserveCalls++;
            return SparkplugBirthDeathSequence.Create(0);
        }

        public IReadOnlyDictionary<SparkplugAliasKey, ulong> ResolveAliases(
            SparkplugStoreIdentity identity, IReadOnlyCollection<SparkplugAliasKey> manifest) =>
            throw new AdapterException(new AdapterError
            {
                Code = SparkplugErrors.IdentityStoreUnavailable,
                Category = ErrorCategory.Internal,
                Message = "injected alias-store failure.",
                Retryable = false,
            });

        public void Dispose()
        {
        }
    }

    // A store that never fails but records whether alias resolution / bdSeq reservation were reached,
    // so a test can prove a PLANNING failure precedes both (review r2 R1).
    private sealed class RecordingStore : ISparkplugIdentityStateStore
    {
        public int AliasCalls { get; private set; }
        public int ReserveCalls { get; private set; }

        public SparkplugBirthDeathSequence ReserveNextBdSeq(SparkplugStoreIdentity identity)
        {
            ReserveCalls++;
            return SparkplugBirthDeathSequence.Create(0);
        }

        public IReadOnlyDictionary<SparkplugAliasKey, ulong> ResolveAliases(
            SparkplugStoreIdentity identity, IReadOnlyCollection<SparkplugAliasKey> manifest)
        {
            AliasCalls++;
            return new Dictionary<SparkplugAliasKey, ulong>();
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeReplaySessionHost : IReplaySessionHost
    {
        public int RebirthRequests { get; private set; }

        public ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken)
        {
            RebirthRequests++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTransport : ISparkplugMqttTransport
    {
        public List<string> Calls { get; } = new();
        public List<(string Topic, byte[] Payload)> Published { get; } = new();
        public SparkplugMqttConnectRequest? ConnectRequest { get; private set; }
        public long? Generation { get; private set; }
        public bool IsConnected { get; private set; }
        public Func<CancellationToken, Task>? FailConnect { get; set; }
        public Func<CancellationToken, Task>? FailSubscribe { get; set; }
        public bool PublishReturnsFalse { get; set; }
        public Func<CancellationToken, Task>? FailPublish { get; set; }
        public bool DisconnectDuringSubscribe { get; set; }

        public event Func<long, Task>? Disconnected;
        public event Func<long, ReadOnlyMemory<byte>, Task>? NodeCommandReceived;

        /// <summary>Raise the actor-facing disconnect callback for a given generation (test-driven drop).</summary>
        public Task RaiseDisconnected(long generation) => Disconnected?.Invoke(generation) ?? Task.CompletedTask;

        /// <summary>Raise an inbound NCMD for a given generation (test-driven).</summary>
        public Task RaiseNodeCommand(long generation, byte[] payload) =>
            NodeCommandReceived?.Invoke(generation, payload) ?? Task.CompletedTask;

        public async Task ConnectAsync(SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken)
        {
            Calls.Add("connect");
            ConnectRequest = request;
            Generation = connectionGeneration;
            if (FailConnect is not null) { await FailConnect(cancellationToken); }
            IsConnected = true;
        }

        public async Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken)
        {
            Calls.Add($"subscribe:{topicFilter}");
            if (DisconnectDuringSubscribe && Disconnected is not null)
            {
                await Disconnected(Generation!.Value); // pre-authoritative drop during Begin
            }

            if (FailSubscribe is not null) { await FailSubscribe(cancellationToken); }
        }

        public async Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Calls.Add($"publish:{topic}");
            Published.Add((topic, payload.ToArray()));
            if (FailPublish is not null) { await FailPublish(cancellationToken); }
            return !PublishReturnsFalse;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Calls.Add("disconnect");
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Calls.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }
}
