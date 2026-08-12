// ============================================================================
// File: Adapters/ReplayContractsTests.cs
// Covers: K1.1 (corrective pass round 3) — everything from rounds 1-2 PLUS:
//         (1) a typed ReplayEpochId threads through start/rebirth/cutover/publish/
//         rebirth-request, and the sink's epoch-gating rule holds (a pre-rebirth
//         epoch's publish/cutover is rejected; a failed NBIRTH does not promote the
//         candidate epoch; a stale queued rebirth request is coalesced); and (2) the
//         snapshot datatype contract matches ADR-0035 — ByteArray is supported via an
//         immutable copy, CanonicalValueType.Null is rejected, undefined value-type /
//         quality fail closed, and mutable static-property values are immutable-copied
//         or rejected. Contracts only.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Adapters;

public sealed class ReplayContractsTests
{
    private static readonly DateTimeOffset Ts = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RouteSchemaGeneration Gen(long g) => RouteSchemaGeneration.Create(g);

    private static ReplayEpochId Epoch(long e) => ReplayEpochId.Create(e);

    private static LatestValueSnapshot SnapshotOf(long generation, params (string tag, double value, long seq)[] metrics)
    {
        var dict = new Dictionary<CanonicalMetricKey, LatestMetricValue>();
        foreach (var (tag, value, seq) in metrics)
        {
            var key = CanonicalMetricKey.Create("src-1", "dev-1", tag);
            dict[key] = LatestMetricValue.Create(key, CanonicalValueType.Double, value, false, Ts, DataQuality.Good, seq);
        }

        return new LatestValueSnapshot(Gen(generation), dict);
    }

    // ---- ReplayBoundary ----
    [Fact]
    public void ReplayBoundary_HasBacklog_Reflects_Cutoff()
    {
        ReplayBoundary.Create(0, 5).HasBacklog.Should().BeTrue();
        ReplayBoundary.Create(5, 5).HasBacklog.Should().BeFalse();
        default(ReplayBoundary).HasBacklog.Should().BeFalse();
    }

    [Fact]
    public void ReplayBoundary_Rejects_Cursor_Past_Cutoff_And_Negatives()
    {
        ((Action)(() => ReplayBoundary.Create(6, 5))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => ReplayBoundary.Create(-1, 5))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => ReplayBoundary.Create(0, -1))).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- Typed ids reject negatives; default is zero ----
    [Fact]
    public void Typed_Ids_Reject_Negatives_And_Default_Is_Zero()
    {
        ((Action)(() => ReplaySessionId.Create(-1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => RouteSchemaGeneration.Create(-1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => ReplayEpochId.Create(-1))).Should().Throw<ArgumentOutOfRangeException>();
        default(ReplaySessionId).Value.Should().Be(0);
        default(RouteSchemaGeneration).Value.Should().Be(0);
        default(ReplayEpochId).Value.Should().Be(0);
        ReplayEpochId.Create(5).Should().Be(ReplayEpochId.Create(5));
    }

    // ---- ADR-0035 datatype map: value/type agreement, ByteArray supported, Array/Object + Null rejected ----
    [Fact]
    public void LatestMetricValue_Enforces_Value_Type_Agreement_ByteArray_Supported_Null_And_Complex_Rejected()
    {
        var key = CanonicalMetricKey.Create("s", "d", "t");

        // Type mismatch: a string is not a Double.
        ((Action)(() => LatestMetricValue.Create(key, CanonicalValueType.Double, "not a double", false, Ts, DataQuality.Good, 0)))
            .Should().Throw<ArgumentException>();

        // Null datatype is rejected outright (even when IsNull=true).
        ((Action)(() => LatestMetricValue.Create(key, CanonicalValueType.Null, null, true, Ts, DataQuality.Good, 0)))
            .Should().Throw<ArgumentException>();

        // Array/Object have no scalar equivalent → rejected.
        ((Action)(() => LatestMetricValue.Create(key, CanonicalValueType.Object, new object(), false, Ts, DataQuality.Good, 0)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => LatestMetricValue.Create(key, CanonicalValueType.Array, new[] { 1, 2 }, false, Ts, DataQuality.Good, 0)))
            .Should().Throw<ArgumentException>();

        // ByteArray IS supported (ADR-0035 locks ByteArray → Bytes).
        LatestMetricValue.Create(key, CanonicalValueType.ByteArray, new byte[] { 1, 2 }, false, Ts, DataQuality.Good, 0)
            .Value.Should().BeOfType<ImmutableArray<byte>>();

        // Null/value invariant + non-negative sequence.
        ((Action)(() => LatestMetricValue.Create(key, CanonicalValueType.Double, null, false, Ts, DataQuality.Good, 0)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => LatestMetricValue.Create(key, CanonicalValueType.Double, 1.0, false, Ts, DataQuality.Good, -1)))
            .Should().Throw<ArgumentOutOfRangeException>();

        // A known-null metric keeps its real declared datatype (Double) with IsNull=true.
        LatestMetricValue.Create(key, CanonicalValueType.Double, null, true, Ts, DataQuality.Bad, 0).ValueType
            .Should().Be(CanonicalValueType.Double);

        // Each supported scalar arm accepts its exact CLR type.
        LatestMetricValue.Create(key, CanonicalValueType.Integer, 5, false, Ts, DataQuality.Good, 0).Value.Should().Be(5);
        LatestMetricValue.Create(key, CanonicalValueType.Boolean, true, false, Ts, DataQuality.Good, 0).Value.Should().Be(true);
    }

    // ---- Fail closed on undefined value-type / quality enums ----
    [Fact]
    public void LatestMetricValue_Fails_Closed_On_Undefined_Enums()
    {
        var key = CanonicalMetricKey.Create("s", "d", "t");
        ((Action)(() => LatestMetricValue.Create(key, (CanonicalValueType)99, null, true, Ts, DataQuality.Good, 0)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => LatestMetricValue.Create(key, CanonicalValueType.Double, 1.0, false, Ts, (DataQuality)99, 0)))
            .Should().Throw<ArgumentException>();
    }

    // ---- ByteArray survives source mutation through the immutable representation ----
    [Fact]
    public void ByteArray_Value_Is_Deep_Copied_And_Immutable()
    {
        var key = CanonicalMetricKey.Create("s", "d", "blob");
        var bytes = new byte[] { 1, 2, 3 };
        var stored = LatestMetricValue.Create(key, CanonicalValueType.ByteArray, bytes, false, Ts, DataQuality.Good, 0);

        bytes[0] = 99; // mutate the source after construction

        var immutable = (ImmutableArray<byte>)stored.Value!;
        immutable[0].Should().Be(1); // unaffected — the snapshot retained a copy
        immutable.Should().Equal(1, 2, 3);
    }

    // ---- Static-property mutation cannot change a snapshot; mutable byte[] is immutable-copied; graphs rejected ----
    [Fact]
    public void StaticProperties_Are_ReadOnly_ImmutableCopied_And_Reject_Object_Graphs()
    {
        var key = CanonicalMetricKey.Create("src-1", "dev-1", "Spindle/Speed");
        var blob = new byte[] { 5, 6 };
        var staticProps = new Dictionary<string, object?> { ["EngUnits"] = "rpm", ["blob"] = blob };
        var value = LatestMetricValue.Create(
            key, CanonicalValueType.Double, 100.0, false, Ts, DataQuality.Bad,
            routeBufferSequence: 42, qualityReason: "sensor-timeout", unit: "rpm", staticProperties: staticProps);

        var input = new Dictionary<CanonicalMetricKey, LatestMetricValue> { [key] = value };
        var snap = new LatestValueSnapshot(Gen(7), input);

        input.Clear();
        staticProps["EngUnits"] = "MUTATED";
        blob[0] = 88;

        var stored = snap.TryGet(key)!;
        snap.Generation.Value.Should().Be(7);
        stored.QualityReason.Should().Be("sensor-timeout");
        stored.StaticProperties!["EngUnits"].Should().Be("rpm");
        ((ImmutableArray<byte>)stored.StaticProperties!["blob"]!)[0].Should().Be(5); // deep-copied

        // The exposed map cannot be downcast to a mutable Dictionary.
        (stored.StaticProperties is Dictionary<string, object?>).Should().BeFalse();
        var downcast = stored.StaticProperties as IDictionary<string, object?>;
        ((Action)(() => downcast!["EngUnits"] = "hacked")).Should().Throw<NotSupportedException>();

        // A nested dictionary / arbitrary object graph static value is rejected.
        var badProps = new Dictionary<string, object?> { ["nested"] = new Dictionary<string, object?>() };
        ((Action)(() => LatestMetricValue.Create(key, CanonicalValueType.Double, 1.0, false, Ts, DataQuality.Good, 0, staticProperties: badProps)))
            .Should().Throw<ArgumentException>();
    }

    // ---- Snapshot key/value consistency + generation-aware empty ----
    [Fact]
    public void Snapshot_Rejects_Key_That_Does_Not_Match_Its_Value_Metric()
    {
        var realKey = CanonicalMetricKey.Create("s", "d", "real");
        var otherKey = CanonicalMetricKey.Create("s", "d", "other");
        var value = LatestMetricValue.Create(realKey, CanonicalValueType.Double, 1.0, false, Ts, DataQuality.Good, 0);

        var bad = new Dictionary<CanonicalMetricKey, LatestMetricValue> { [otherKey] = value };
        ((Action)(() => new LatestValueSnapshot(Gen(1), bad))).Should().Throw<ArgumentException>();

        var byDefault = new Dictionary<CanonicalMetricKey, LatestMetricValue> { [default] = value };
        ((Action)(() => new LatestValueSnapshot(Gen(1), byDefault))).Should().Throw<ArgumentException>();
    }

    // ---- Round-4: a default(CanonicalMetricKey) is rejected by value AND snapshot ----
    [Fact]
    public void Default_Metric_Key_Is_Rejected_By_Value_And_Snapshot()
    {
        default(CanonicalMetricKey).IsValid.Should().BeFalse();

        // LatestMetricValue.Create rejects a defaulted identity outright.
        ((Action)(() => LatestMetricValue.Create(default, CanonicalValueType.Double, 1.0, false, Ts, DataQuality.Good, 0)))
            .Should().Throw<ArgumentException>();

        // The snapshot rejects a default key independently of the key==value.Metric check.
        var realKey = CanonicalMetricKey.Create("s", "d", "t");
        var value = LatestMetricValue.Create(realKey, CanonicalValueType.Double, 1.0, false, Ts, DataQuality.Good, 0);
        var byDefault = new Dictionary<CanonicalMetricKey, LatestMetricValue> { [default] = value };
        ((Action)(() => new LatestValueSnapshot(Gen(1), byDefault))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Empty_Snapshot_Is_Generation_Aware()
    {
        var empty = LatestValueSnapshot.CreateEmpty(Gen(7));
        empty.Count.Should().Be(0);
        empty.Generation.Value.Should().Be(7);
        empty.MaxRouteBufferSequence.Should().BeNull();
    }

    [Fact]
    public void CanonicalMetricKey_Rejects_Empty_Components_And_Compares_By_Value()
    {
        ((Action)(() => CanonicalMetricKey.Create("", "d", "t"))).Should().Throw<ArgumentException>();
        CanonicalMetricKey.Create("s", "d", "t").Should().Be(CanonicalMetricKey.Create("s", "d", "t"));
        CanonicalMetricKey.Create("s", "d", "t").Should().NotBe(CanonicalMetricKey.Create("s", "d", "u"));
    }

    // ---- Composite state captured coherently AND carried intact through the lifecycle ----
    [Fact]
    public async Task Composite_State_Provider_Pairs_Boundary_And_Snapshot_And_Lifecycle_Carries_It_Intact()
    {
        var provider = new FakeReplaySessionStateProvider();

        var startState = await provider.CaptureBirthStateAsync("route-1", "sink-1", default);
        startState.Boundary.CutoffExclusive.Should().Be(3);
        startState.Snapshot.MaxRouteBufferSequence.Should().Be(2);

        var cutoverState = await provider.CaptureCutoverAsync("route-1", default);
        cutoverState.CutoffExclusive.Should().Be(5);

        var start = ReplaySessionStart.Create(ReplaySessionId.Create(1), Epoch(1), "route-1", startState, new FakeReplaySessionHost());
        start.State.Should().BeSameAs(startState);
        var cutover = ReplaySessionCutover.Create(ReplaySessionId.Create(1), Epoch(1), cutoverState);
        cutover.State.Should().BeSameAs(cutoverState);
    }

    [Fact]
    public void State_Factories_Reject_An_Incoherent_Boundary_Snapshot_Pair()
    {
        var snap = SnapshotOf(1, ("A", 1.0, 5));
        ((Action)(() => ReplaySessionStartState.Create(ReplayBoundary.Create(0, 5), snap))).Should().Throw<ArgumentException>();
        ((Action)(() => ReplaySessionCutoverState.Create(5, snap))).Should().Throw<ArgumentException>();
    }

    // ---- Two-watermark PublishContext ----
    [Fact]
    public void PublishContext_Enforces_Two_Watermark_Phase_Ranges()
    {
        var s = ReplaySessionId.Create(1);
        var e = Epoch(1);

        PublishContext.Create("r", s, e, ReplayPhase.Replay, 10, null, 0, 9).Phase.Should().Be(ReplayPhase.Replay);
        PublishContext.Create("r", s, e, ReplayPhase.CatchUp, 10, 20, 10, 19).Phase.Should().Be(ReplayPhase.CatchUp);
        PublishContext.Create("r", s, e, ReplayPhase.Live, 10, 20, 20, 25).Phase.Should().Be(ReplayPhase.Live);

        ((Action)(() => PublishContext.Create("r", s, e, ReplayPhase.Replay, 10, null, 5, 10))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => PublishContext.Create("r", s, e, ReplayPhase.CatchUp, 10, null, 10, 15))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => PublishContext.Create("r", s, e, ReplayPhase.CatchUp, 10, 20, 10, 20))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => PublishContext.Create("r", s, e, ReplayPhase.CatchUp, 10, 20, 9, 15))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => PublishContext.Create("r", s, e, ReplayPhase.Live, 10, null, 20, 25))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => PublishContext.Create("r", s, e, ReplayPhase.Live, 10, 20, 19, 25))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => PublishContext.Create("r", s, e, ReplayPhase.Live, 10, 5, 10, 12))).Should().Throw<ArgumentOutOfRangeException>();
        PublishContext.Create("r", s, e, ReplayPhase.Live, 0, 0, 0, 0).CatchUpCutoffExclusive.Should().Be(0);
    }

    // ---- Non-bypassable factories (no public constructors) ----
    [Theory]
    [InlineData(typeof(PublishContext))]
    [InlineData(typeof(ReplaySessionStart))]
    [InlineData(typeof(ReplaySessionRebirth))]
    [InlineData(typeof(ReplaySessionCutover))]
    [InlineData(typeof(ReplaySessionEnd))]
    [InlineData(typeof(RebirthRequest))]
    [InlineData(typeof(ReplaySessionStartState))]
    [InlineData(typeof(ReplaySessionCutoverState))]
    public void Contract_Type_Has_No_Public_Constructor(Type type)
    {
        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty(
            $"{type.Name} must only be constructible through its validating factory");
    }

    [Fact]
    public async Task Begin_Before_Start_Is_A_Contract_Violation()
    {
        var sink = new FakeReplayAwareSink();
        var state = ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), LatestValueSnapshot.CreateEmpty(Gen(0)));
        var start = ReplaySessionStart.Create(ReplaySessionId.Create(1), Epoch(1), "r", state, new FakeReplaySessionHost());

        var act = async () => await sink.BeginReplaySessionAsync(start, default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- Full lifecycle: session identity + epoch thread through; Start before birth, Stop after death ----
    [Fact]
    public async Task Lifecycle_Threads_Session_And_Epoch_Orders_Start_Before_Birth_And_Stop_After_Death()
    {
        var sink = new FakeReplayAwareSink();
        (sink is ISinkAdapter).Should().BeTrue();

        var host = new FakeReplaySessionHost();
        var session = ReplaySessionId.Create(101);
        var empty = LatestValueSnapshot.CreateEmpty(Gen(0));
        var startState = ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), empty);

        await sink.StartAsync(default);
        await sink.BeginReplaySessionAsync(ReplaySessionStart.Create(session, Epoch(1), "route-1", startState, host), default);
        await sink.PublishAsync(Array.Empty<CanonicalDataPoint>(), PublishContext.Create("route-1", session, Epoch(1), ReplayPhase.Live, 0, 0, 0, 0), default);
        await sink.RebirthAsync(ReplaySessionRebirth.Create(session, Epoch(2), startState), default);
        await sink.CompleteCatchUpAsync(ReplaySessionCutover.Create(session, Epoch(2), ReplaySessionCutoverState.Create(0, empty)), default);
        await sink.EndSessionAsync(ReplaySessionEnd.Create(session, "route-1", ReplaySessionEndReason.Stop), default);
        await sink.StopAsync(default);

        sink.Log.Should().Equal("start", "begin", "publish:Live", "rebirth", "catchup", "end", "stop");
        sink.ObservedSessions.Should().OnlyContain(id => id == session);

        host.AuthoritativeEpoch = Epoch(2); // Core's current epoch after the rebirth
        await host.RequestRebirthAsync(RebirthRequest.Create(session, Epoch(2), RebirthReason.HostCommand, "Node Control/Rebirth"), default);
        host.LastAccepted!.SessionId.Should().Be(session);
        host.LastAccepted!.Reason.Should().Be(RebirthReason.HostCommand);
    }

    // ==== Epoch-gating (round-3 item 1) ====

    [Fact]
    public async Task Rebirth_Establishes_A_New_Epoch_While_Retaining_The_Session_Id()
    {
        var sink = new FakeReplayAwareSink();
        var session = ReplaySessionId.Create(7);
        var state = ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), LatestValueSnapshot.CreateEmpty(Gen(0)));

        await sink.StartAsync(default);
        await sink.BeginReplaySessionAsync(ReplaySessionStart.Create(session, Epoch(1), "r", state, new FakeReplaySessionHost()), default);
        sink.CurrentEpoch.Should().Be(Epoch(1));

        await sink.RebirthAsync(ReplaySessionRebirth.Create(session, Epoch(2), state), default);
        sink.CurrentEpoch.Should().Be(Epoch(2));
        sink.ObservedSessions.Should().OnlyContain(id => id == session); // session id unchanged
    }

    [Fact]
    public async Task Stale_PreRebirth_Publish_And_Cutover_Are_Rejected()
    {
        var sink = new FakeReplayAwareSink();
        var session = ReplaySessionId.Create(7);
        var empty = LatestValueSnapshot.CreateEmpty(Gen(0));
        var state = ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), empty);

        await sink.StartAsync(default);
        await sink.BeginReplaySessionAsync(ReplaySessionStart.Create(session, Epoch(1), "r", state, new FakeReplaySessionHost()), default);
        await sink.RebirthAsync(ReplaySessionRebirth.Create(session, Epoch(2), state), default); // epoch is now 2

        // A delayed epoch-1 publish is rejected.
        var stalePublish = async () => await sink.PublishAsync(
            Array.Empty<CanonicalDataPoint>(),
            PublishContext.Create("r", session, Epoch(1), ReplayPhase.Live, 0, 0, 0, 0),
            default);
        await stalePublish.Should().ThrowAsync<InvalidOperationException>();

        // A delayed epoch-1 cutover is rejected.
        var staleCutover = async () => await sink.CompleteCatchUpAsync(
            ReplaySessionCutover.Create(session, Epoch(1), ReplaySessionCutoverState.Create(0, empty)),
            default);
        await staleCutover.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Failed_NBIRTH_Does_Not_Promote_The_Candidate_Epoch()
    {
        var sink = new FakeReplayAwareSink();
        var session = ReplaySessionId.Create(7);
        var empty = LatestValueSnapshot.CreateEmpty(Gen(0));
        var state = ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), empty);

        await sink.StartAsync(default);
        await sink.BeginReplaySessionAsync(ReplaySessionStart.Create(session, Epoch(1), "r", state, new FakeReplaySessionHost()), default);

        // A rebirth to epoch 2 FAILS mid-birth.
        sink.FailNextBirth = true;
        var failing = async () => await sink.RebirthAsync(ReplaySessionRebirth.Create(session, Epoch(2), state), default);
        await failing.Should().ThrowAsync<InvalidOperationException>();

        // The authoritative epoch is still 1 — an epoch-1 publish is still honored.
        sink.CurrentEpoch.Should().Be(Epoch(1));
        await sink.PublishAsync(Array.Empty<CanonicalDataPoint>(), PublishContext.Create("r", session, Epoch(1), ReplayPhase.Live, 0, 0, 0, 0), default);
        sink.Log.Should().Contain("publish:Live");
    }

    [Fact]
    public async Task Stale_Queued_Rebirth_Request_Is_Coalesced_By_The_Host()
    {
        var host = new FakeReplaySessionHost { AuthoritativeEpoch = Epoch(2) };
        var session = ReplaySessionId.Create(7);

        // A request tagged with the superseded epoch 1 is ignored.
        await host.RequestRebirthAsync(RebirthRequest.Create(session, Epoch(1), RebirthReason.SchemaChange), default);
        host.AcceptedCount.Should().Be(0);

        // A request tagged with the current epoch 2 is accepted.
        await host.RequestRebirthAsync(RebirthRequest.Create(session, Epoch(2), RebirthReason.HostCommand), default);
        host.AcceptedCount.Should().Be(1);
    }

    private sealed class FakeReplaySessionStateProvider : IReplaySessionStateProvider
    {
        public ValueTask<ReplaySessionStartState> CaptureBirthStateAsync(string routeId, string sinkId, CancellationToken ct)
        {
            var snap = SnapshotOf(1, ("A", 10.0, 2));
            return ValueTask.FromResult(ReplaySessionStartState.Create(ReplayBoundary.Create(0, 3), snap));
        }

        public ValueTask<ReplaySessionCutoverState> CaptureCutoverAsync(string routeId, CancellationToken ct)
        {
            var snap = SnapshotOf(1, ("A", 11.0, 4));
            return ValueTask.FromResult(ReplaySessionCutoverState.Create(5, snap));
        }
    }

    private sealed class FakeReplaySessionHost : IReplaySessionHost
    {
        public ReplayEpochId AuthoritativeEpoch { get; set; }
        public int AcceptedCount { get; private set; }
        public RebirthRequest? LastAccepted { get; private set; }

        public ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken)
        {
            // Core coalesces a request whose epoch is no longer current.
            if (request.Epoch == AuthoritativeEpoch)
            {
                AcceptedCount++;
                LastAccepted = request;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeReplayAwareSink : IReplayAwareSinkAdapter
    {
        private bool _started;
        private bool _deathEmitted;

        public List<string> Log { get; } = new();
        public List<ReplaySessionId> ObservedSessions { get; } = new();
        public ReplayEpochId? CurrentEpoch { get; private set; }
        public bool FailNextBirth { get; set; }

        public Task BeginReplaySessionAsync(ReplaySessionStart start, CancellationToken cancellationToken)
        {
            if (!_started)
            {
                throw new InvalidOperationException("StartAsync must run before BeginReplaySessionAsync.");
            }

            PromoteEpochOrFail(start.Epoch);
            ObservedSessions.Add(start.SessionId);
            Log.Add("begin");
            return Task.CompletedTask;
        }

        public Task RebirthAsync(ReplaySessionRebirth rebirth, CancellationToken cancellationToken)
        {
            PromoteEpochOrFail(rebirth.Epoch);
            ObservedSessions.Add(rebirth.SessionId);
            Log.Add("rebirth");
            return Task.CompletedTask;
        }

        private void PromoteEpochOrFail(ReplayEpochId candidate)
        {
            if (FailNextBirth)
            {
                FailNextBirth = false;
                throw new InvalidOperationException("NBIRTH failed; candidate epoch must not be promoted.");
            }

            CurrentEpoch = candidate; // only a SUCCESSFUL birth promotes the authoritative epoch
        }

        private void RequireCurrentEpoch(ReplayEpochId epoch)
        {
            if (CurrentEpoch is null || epoch != CurrentEpoch.Value)
            {
                throw new InvalidOperationException($"Input for {epoch} does not match the current birth epoch {CurrentEpoch}.");
            }
        }

        public Task<PublishResult> PublishAsync(
            IReadOnlyList<CanonicalDataPoint> points, PublishContext context, CancellationToken cancellationToken)
        {
            RequireCurrentEpoch(context.Epoch);
            ObservedSessions.Add(context.SessionId);
            Log.Add($"publish:{context.Phase}");
            return Task.FromResult(PublishResult.Successful(points.Count, TimeSpan.Zero));
        }

        public Task CompleteCatchUpAsync(ReplaySessionCutover cutover, CancellationToken cancellationToken)
        {
            RequireCurrentEpoch(cutover.Epoch);
            ObservedSessions.Add(cutover.SessionId);
            Log.Add("catchup");
            return Task.CompletedTask;
        }

        public Task EndSessionAsync(ReplaySessionEnd sessionEnd, CancellationToken cancellationToken)
        {
            ObservedSessions.Add(sessionEnd.SessionId);
            _deathEmitted = true;
            Log.Add("end");
            return Task.CompletedTask;
        }

        // --- base ISinkAdapter ---
        public string InstanceId => "fake-replay-sink";
        public string ProtocolName => "fake";
        public SinkCapabilities Capabilities => SinkCapabilities.Push;
        public AdapterState State => AdapterState.Running;
        public Task InitializeAsync(SinkConfiguration config, CancellationToken ct) => Task.CompletedTask;

        public Task StartAsync(CancellationToken ct)
        {
            _started = true;
            Log.Add("start");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            if (!_deathEmitted)
            {
                Log.Add("death-on-stop");
            }

            Log.Add("stop");
            return Task.CompletedTask;
        }

        public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<PublishResult> PublishAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct) =>
            throw new NotSupportedException("replay-aware overload only; base PublishAsync must never be called on the replay path");
        public Task UpdateCurrentValuesAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct) => Task.CompletedTask;
        public Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
