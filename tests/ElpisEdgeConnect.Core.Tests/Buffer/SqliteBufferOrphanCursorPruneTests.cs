// ============================================================================
// File: Buffer/SqliteBufferOrphanCursorPruneTests.cs
// Purpose: Pin the D15 orphaned-cursor retention fix — IMessageBuffer.
//          ReconcileSinksAsync.
//
// The defect these tests exist for
// --------------------------------
// A route buffer file is keyed by RouteId. When a route's sink is deleted and
// the route is recreated with the same RouteId pointing at a NEW sink, the old
// buffer file is reused — dead cursor and all. Nothing ever removed that
// cursor: SqliteRouteStore loaded EVERY row of the cursors table back into the
// tracker at open, and DeregisterSinkAsync had no production caller. Retention
// then computes
//
//     minCursor = _cursors.Min(defaultIfEmpty: _tail);
//     if (minCursor <= _tail) return;   // orphan sits at/below tail -> never reclaims
//
// so ONE orphan pins tail_sequence permanently, the pin is RESURRECTED on every
// open (a restart does not clear it), the points table grows without bound, and
// CurrentDepth — measured against the pinned tail — manufactures a phantom
// backlog on a route that is delivering perfectly.
//
// Observed on a live gateway:
//     cursors  ModMqtt     next_unread=14316  frozen 29 min   <- ORPHAN
//              ModbusMqtt  next_unread=17479  updated 4s ago  <- live
//     meta     tail_sequence = 14316
//     points   3163 rows ( = 17479 - 14316 exactly )          <- 100% phantom
//
// Every test below fails against the pre-fix behaviour (a register-only pass,
// which is exactly what IMessageBuffer.ReconcileSinksAsync's DEFAULT interface
// body still does, and exactly what RouteWorker's old register loop did).
//
// Note on disk usage: the prune + reclaim makes growth BOUNDED. It does not
// truncate the file — SQLite's DELETE frees pages for reuse inside the database
// rather than returning them to the OS, so the .db will not shrink without an
// explicit VACUUM. These tests assert row/cursor/tail state, not file size.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2aTestFixtures;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferOrphanCursorPruneTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    /// <summary>
    /// A reclaim interval long enough that the BACKGROUND loop cannot be the
    /// thing that advances the tail. Anything these tests observe must be the
    /// work reconciliation did synchronously, in its own critical section.
    /// </summary>
    private static readonly TimeSpan NoBackgroundReclaim = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Read the DURABLE cursor sink ids straight out of the file. This is the
    /// only view that distinguishes a real fix from a cosmetic one: dropping the
    /// sink from the in-memory tracker looks identical from the API surface, but
    /// leaves the row on disk to be resurrected at the next open.
    /// </summary>
    private static List<string> DurableCursorSinkIds(string filePath)
    {
        var ids = new List<string>();
        using var conn = new SqliteConnection($"Data Source={filePath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sink_id FROM cursors ORDER BY sink_id;";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            ids.Add(rdr.GetString(0));
        }

        return ids;
    }

    /// <summary>Count the rows still physically held in the points table.</summary>
    private static long DurablePointCount(string filePath)
    {
        using var conn = new SqliteConnection($"Data Source={filePath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM points;";
        return (long)cmd.ExecuteScalar()!;
    }

    // ------------------------------------------------------------------------
    // 1. The live-gateway repro.
    // ------------------------------------------------------------------------

    /// <summary>
    /// RED without the fix: a register-only pass leaves 'ModMqtt' in the cursor
    /// set, so <c>Min</c> stays at 0, <c>minCursor &lt;= _tail</c> short-circuits
    /// reclaim, and the tail never leaves 0. The Tail / CurrentDepth / point-count
    /// assertions all fail (tail 0 not 200, depth 200 not 0, 200 rows not 0), and
    /// <c>Pruned</c> is empty rather than naming the orphan.
    /// </summary>
    [Fact]
    public async Task Reconcile_PrunesOrphanedCursor_AndTailAdvancesPastThePin()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "route-modbus", path, SmallSqlitePolicy(maxDepth: 4096, reclaimInterval: NoBackgroundReclaim));

            // The sink that will later be deleted from the route, and the one that replaces it.
            await buf.RegisterSinkAsync("ModMqtt", Ct);
            await buf.RegisterSinkAsync("ModbusMqtt", Ct);

            await buf.EnqueueAsync(Batch(0, 200), Ct);
            await buf.AckAsync("ModbusMqtt", 199, Ct); // live sink fully caught up: cursor 200

            // Pre-condition: the orphan pins the tail and manufactures the phantom backlog.
            buf.Tail.Should().Be(0);
            (await buf.GetStatsAsync()).CurrentDepth.Should().Be(200);

            var result = await buf.ReconcileSinksAsync(new[] { "ModbusMqtt" }, Ct);

            result.Pruned.Should().ContainSingle();
            result.Pruned[0].SinkId.Should().Be("ModMqtt");
            result.Pruned[0].PinnedSequence.Should().Be(0);
            result.Pruned[0].UndeliveredPoints.Should().Be(200);
            result.Registered.Should().BeEmpty();

            // The pin is gone, so the tail advances and the phantom backlog evaporates.
            buf.Tail.Should().Be(200);
            (await buf.GetStatsAsync()).CurrentDepth.Should().Be(0);
            DurablePointCount(path).Should().Be(0);
            DurableCursorSinkIds(path).Should().Equal("ModbusMqtt");
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ------------------------------------------------------------------------
    // 2. Durability — the one that separates a real fix from a cosmetic one.
    // ------------------------------------------------------------------------

    /// <summary>
    /// RED without the fix: with no DELETE at all the row survives trivially. It
    /// is also RED for a *cosmetic* fix that only calls
    /// <c>SinkCursorTracker.Deregister</c> — the tail would still advance in-process,
    /// but the row stays on disk and <c>LoadCursors</c> resurrects it on the next
    /// open (clamped to the tail, so it silently re-pins from there). The
    /// discriminating assertion is the DURABLE cursor set after reopen.
    /// </summary>
    [Fact]
    public async Task Reconcile_PruneSurvivesReopen_AndIsNotResurrectedFromTheCursorsTable()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync(
                "route-a", path, SmallSqlitePolicy(maxDepth: 4096, reclaimInterval: NoBackgroundReclaim)))
            {
                await buf.RegisterSinkAsync("dead", Ct);
                await buf.RegisterSinkAsync("live", Ct);

                await buf.EnqueueAsync(Batch(0, 100), Ct);
                await buf.AckAsync("live", 49, Ct); // live cursor = 50; 50 points genuinely pending

                var result = await buf.ReconcileSinksAsync(new[] { "live" }, Ct);
                result.Pruned.Should().ContainSingle().Which.SinkId.Should().Be("dead");

                // Reclaim ran in the SAME critical section: tail is already at the live cursor.
                buf.Tail.Should().Be(50);
            }

            // The DELETE was committed, not merely mirrored in memory.
            DurableCursorSinkIds(path).Should().Equal("live");

            await using (var reopened = await SqliteBuffer.OpenAsync(
                "route-a", path, SmallSqlitePolicy(maxDepth: 4096, reclaimInterval: NoBackgroundReclaim)))
            {
                // Nothing to resurrect: the open-time cursor load sees only the live sink.
                DurableCursorSinkIds(path).Should().Equal("live");
                reopened.Head.Should().Be(100);
                reopened.Tail.Should().Be(50);
                (await reopened.GetStatsAsync()).CurrentDepth.Should().Be(50);

                // ...and a second reconcile has nothing left to do.
                var again = await reopened.ReconcileSinksAsync(new[] { "live" }, Ct);
                again.IsNoOp.Should().BeTrue();
            }
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ------------------------------------------------------------------------
    // 3. The store-and-forward guarantee: lagging != orphaned.
    // ------------------------------------------------------------------------

    /// <summary>
    /// RED without the fix on the <c>ghost</c> prune assertions (a register-only
    /// pass prunes nothing, so <c>Pruned</c> is empty and the durable cursor set
    /// still contains three ids).
    /// <para>
    /// This test also pins the opposite direction, which is the more dangerous
    /// failure: an over-eager fix that prunes anything "stale-looking" would drop
    /// <c>slow</c> too, advance the tail to 20 and silently destroy 20 points the
    /// operator is entitled to. So it asserts the tail is STILL pinned at 0, the
    /// depth is STILL 20, and <c>slow</c> still replays from its own sequence in
    /// strict order.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Reconcile_LaggingConfiguredSink_StillPinsTail_AndStillReplaysInOrder()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "route-a", path, SmallSqlitePolicy(maxDepth: 4096, reclaimInterval: NoBackgroundReclaim));

            await buf.RegisterSinkAsync("fast", Ct);
            await buf.RegisterSinkAsync("slow", Ct);   // configured, but far behind
            await buf.RegisterSinkAsync("ghost", Ct);  // no longer configured

            await buf.EnqueueAsync(Batch(0, 20), Ct);
            await buf.AckAsync("fast", 19, Ct); // fast cursor = 20; slow and ghost both at 0

            var result = await buf.ReconcileSinksAsync(new[] { "fast", "slow" }, Ct);

            // Only the unconfigured id is pruned.
            result.Pruned.Should().ContainSingle().Which.SinkId.Should().Be("ghost");
            DurableCursorSinkIds(path).Should().Equal("fast", "slow");

            // The lagging CONFIGURED sink still pins everything.
            buf.Tail.Should().Be(0);
            (await buf.GetStatsAsync()).CurrentDepth.Should().Be(20);
            DurablePointCount(path).Should().Be(20);

            // ...and still replays from its own cursor, in strict sequence order.
            var batch = await buf.DequeueBatchAsync("slow", 100, Ct);
            batch.FirstSequence.Should().Be(0);
            batch.LastSequence.Should().Be(19);
            batch.Points.Should().HaveCount(20);
            for (var i = 0; i < batch.Points.Count; i++)
            {
                batch.Points[i].SequenceNumber.Should().Be(i);
            }
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ------------------------------------------------------------------------
    // 4. Nothing stale: register the newcomer, prune nothing, disturb nothing.
    // ------------------------------------------------------------------------

    /// <summary>
    /// RED without the fix: the default (register-only) body returns
    /// <see cref="SinkReconciliationResult.Empty"/>, so
    /// <c>Registered</c> is empty rather than reporting the one cursor it actually
    /// created — reconciliation would be unable to tell an operator what it did.
    /// The second-pass <c>IsNoOp</c> assertion fails for the same reason.
    /// </summary>
    [Fact]
    public async Task Reconcile_WithNothingStale_RegistersOnlyTheNewSink_AndPrunesNothing()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "route-a", path, SmallSqlitePolicy(maxDepth: 4096, reclaimInterval: NoBackgroundReclaim));

            await buf.RegisterSinkAsync("a", Ct);
            await buf.EnqueueAsync(Batch(0, 10), Ct);

            var result = await buf.ReconcileSinksAsync(new[] { "a", "b" }, Ct);

            result.Pruned.Should().BeEmpty();
            result.Registered.Should().Equal("b");
            DurableCursorSinkIds(path).Should().Equal("a", "b");

            // Nothing was reclaimed, and the newcomer starts at the tail so it
            // replays the buffered backlog (D12).
            buf.Tail.Should().Be(0);
            DurablePointCount(path).Should().Be(10);

            var replayed = await buf.DequeueBatchAsync("b", 100, Ct);
            replayed.FirstSequence.Should().Be(0);
            replayed.Points.Should().HaveCount(10);

            // A second identical pass is a true no-op.
            var again = await buf.ReconcileSinksAsync(new[] { "a", "b" }, Ct);
            again.IsNoOp.Should().BeTrue();
            DurableCursorSinkIds(path).Should().Equal("a", "b");
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ------------------------------------------------------------------------
    // 5. InMemoryBuffer parity — a route re-pointed within one process.
    // ------------------------------------------------------------------------

    /// <summary>
    /// RED without the fix: <see cref="InMemoryBuffer"/> would inherit the
    /// register-only default, so 'dead' keeps its cursor,
    /// <c>ReleaseEvictableLocked</c> short-circuits on
    /// <c>minCursor &lt;= _tail</c>, and depth stays at 20 with two registered
    /// sinks.
    /// </summary>
    [Fact]
    public async Task InMemoryBuffer_Reconcile_PrunesOrphan_AndReleasesTheStorageItPinned()
    {
        await using var buf = new InMemoryBuffer("route-a", SmallInMemoryPolicy(maxDepth: 64));

        await buf.RegisterSinkAsync("dead", Ct);
        await buf.RegisterSinkAsync("live", Ct);

        await buf.EnqueueAsync(Batch(0, 20), Ct);
        await buf.AckAsync("live", 19, Ct);

        (await buf.GetStatsAsync()).CurrentDepth.Should().Be(20); // pinned by 'dead'

        var result = await buf.ReconcileSinksAsync(new[] { "live" }, Ct);

        result.Pruned.Should().ContainSingle();
        result.Pruned[0].SinkId.Should().Be("dead");
        result.Pruned[0].PinnedSequence.Should().Be(0);
        result.Pruned[0].UndeliveredPoints.Should().Be(20);

        var stats = await buf.GetStatsAsync();
        stats.CurrentDepth.Should().Be(0);
        stats.RegisteredSinks.Should().Be(1);
    }

    // ------------------------------------------------------------------------
    // 6. The designated replay sink is force-added and never pruned.
    // ------------------------------------------------------------------------

    /// <summary>
    /// RED without the force-add: 'sparkplug' is absent from the caller's active
    /// set, so a naive "prune everything not in activeSinkIds" would delete the
    /// authoritative replay cursor and strip generation fencing — the same thing
    /// <see cref="SqliteRouteStore.DeregisterSinkAsync"/> already refuses to do.
    /// (It is trivially RED without the fix too, since the method does not exist.)
    /// </summary>
    [Fact]
    public async Task Reconcile_NeverPrunesTheDesignatedReplaySink_EvenWhenAbsentFromTheActiveSet()
    {
        var path = NewFilePath();
        try
        {
            // Drain the store so replay activation is permitted.
            await using (var seed = await SqliteRouteStore.OpenAsync(
                "route-a", path, SmallSqlitePolicy(maxDepth: 4096, reclaimInterval: NoBackgroundReclaim)))
            {
                await seed.RegisterSinkAsync("s", Ct);
                await seed.EnqueueAsync(Batch(0, 3), Ct);
                await seed.AckAsync("s", 2, Ct);
            }

            await using var store = await SqliteRouteStore.OpenAsync(
                "route-a", path, SmallSqlitePolicy(maxDepth: 4096, reclaimInterval: NoBackgroundReclaim));

            await store.ActivateReplayStateTrackingAsync("route-a", "sparkplug", Ct);
            DurableCursorSinkIds(path).Should().Contain("sparkplug");

            // The caller does NOT list the replay sink; the store must force-add it.
            var result = await store.ReconcileSinksAsync(new[] { "s" }, Ct);

            result.Pruned.Should().BeEmpty();
            DurableCursorSinkIds(path).Should().Equal("s", "sparkplug");
        }
        finally
        {
            TryDelete(path);
        }
    }
}
