// ============================================================================
// File: Buffer/SqliteRouteStore.cs
// Purpose: The per-route SQLite database OWNER. Backs the durable IMessageBuffer
//          and is the sole owner of the file's connections, reclaim loop, disposal,
//          and the exclusive ownership lock. K1.2b+ extends it (in the same
//          transaction domain) with the latest-value manifest, schema generation,
//          and the replay-state capture providers. The public SqliteBuffer type is
//          now a thin façade over this owner (see SqliteBuffer.cs) — the façade opens
//          no connections and starts no reclaim loop of its own.
// Reference: docs/sessions/2026-07-14-sparkplug-b-k1.2-route-store-plan-v3.md (O5,
//            §4b lifetime lock); v3.1 §3 (single disposal authority).
//
// (Renamed from SqliteBuffer in K1.2a; the buffer logic below is unchanged.)
// Original purpose: Durable IMessageBuffer implementation backed by SQLite.
//          Implements docs/core/buffer-contract.md without modification
//          and the C2b design in docs/core/buffer-durability.md.
//
// LOCKED design points (do not change without revising buffer-durability.md):
//   - WAL + synchronous=FULL by default (D1)
//   - Single writer connection + single read connection (D2)
//   - Per-route file at {dataPath}/buffer/{routeId}.db (D3)
//   - Schema version 1, persisted in meta table (D4)
//   - MaxBatchSize 10_000, splits larger producer batches (D5)
//   - No auto-deregister of lagging sinks (D6)
//   - Reclaim is periodic only; ack handlers signal but never DELETE (D7)
//   - Dispose is cleanup, not correctness (D8)
//   - quick_check on every open, throw on failure, no auto-quarantine (D9)
//   - BUFFER_SCHEMA_MISMATCH error code (D10)
//   - Checkpoint then close in dispose (D11)
//   - **Register starts at _tail to match C2a contract (D12 — REVERSED)**
//   - In-memory SinkCursorTracker mirrors the cursors table (D13)
//   - No mid-batch cancellation (D14)
//   - ReconcileSinksAsync is the ONE production entry point that prunes a
//     cursor whose sink is no longer configured (D15). Register + prune commit
//     in a SINGLE transaction and the tail is recomputed inside the SAME
//     critical section, so the tail is never derived from a half-updated
//     cursor set. The in-memory tracker is mirrored only AFTER the commit.
//
// Reclaim invariant (buffer-contract.md §4.4):
//   For every active sink s, after any committed mutation,
//   s.next_unread refers to an existing held sequence or exactly _head.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using Microsoft.Data.Sqlite;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// SQLite-backed durable <see cref="IMessageBuffer"/> implementation.
/// Satisfies the contract in <c>docs/core/buffer-contract.md</c> without
/// modification. Designed to the C2b durability spec in
/// <c>docs/core/buffer-durability.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Construct via <see cref="OpenAsync"/>, never via <c>new</c>: the open
/// path runs an integrity check, schema migration, and recovery walk that
/// must succeed before the buffer is usable.
/// </para>
/// <para>
/// All writer operations (Enqueue, Ack, Register, Deregister, Reclaim) are
/// serialized through a single <see cref="SemaphoreSlim"/>. Reads use a
/// separate read-only connection that does not take the writer mutex —
/// WAL gives readers a consistent snapshot.
/// </para>
/// <para>
/// The reclaim loop runs on a dedicated background task at
/// <see cref="BufferPolicy.ReclaimInterval"/>. Ack handlers may
/// <em>signal</em> the reclaim loop via a <see cref="TaskCompletionSource"/>
/// flip — but they never enter a DELETE path or await a reclaim
/// transaction. Ack latency is bounded by one row's UPDATE + COMMIT.
/// </para>
/// </remarks>
internal sealed class SqliteRouteStore : IMessageBuffer, IReplayBoundaryProvider, IReplaySessionStateProvider
{
    private readonly string _filePath;
    private readonly BufferPolicy _policy;
    private readonly SqliteConnection _writer;
    private readonly SqliteConnection _reader;

    // Exclusive per-route-database ownership lock (held for the store's lifetime).
    // Prevents a second SqliteRouteStore from owning the same file with a competing
    // connection set / reclaim loop. Released in DisposeAsync. K1.2a.
    private readonly FileStream _ownershipLock;
    private readonly SemaphoreSlim _writerMutex = new(1, 1);
    private readonly SinkCursorTracker _cursors = new();
    private readonly Func<DateTime> _utcNow;

    // Optional sink for points that fail to serialize. Invoked OUTSIDE the
    // writer mutex (after the batch commits) so a slow observer can never
    // stall the write path. Null when no observer was supplied.
    private readonly Action<QuarantinedPoint>? _onQuarantine;

    // K1.2d: optional, immutable, constructor-injected capture test hooks. Null in
    // production. Lets a test deterministically interleave against the capture critical
    // section and observe which logical capture query ran. Hook exceptions escape
    // unchanged (never translated to a Buffer error). See SqliteRouteStoreTestHooks.
    private readonly SqliteRouteStoreTestHooks? _testHooks;

    // Reclaim loop coordination.
    private readonly CancellationTokenSource _reclaimCts = new();
    private Task? _reclaimLoopTask;
    // D10: captures the last unexpected exception caught by the reclaim
    // loop's defensive catch. Null in the steady state. Surfaced via
    // LastReclaimError for diagnostics / test assertions.
    private volatile Exception? _lastReclaimError;
    private TaskCompletionSource<bool>? _reclaimSignal;

    // Block-mode producer waiter (re-created on each space release).
    private TaskCompletionSource<bool>? _spaceWaiter;

    // In-process state mirrored from SQLite.
    private long _head;                     // next sequence to assign
    private long _tail;                     // oldest sequence still held
    private long _totalEnqueued;
    private long _totalDrained;
    private long _totalDropped;             // backward-compat sum
    private long _droppedByCapacity;
    private long _droppedByRetention;
    private long _sinksFastForwarded;
    private long _totalQuarantined;         // points skipped due to serialization failure

    private bool _disposed;

    // K1.2b: whether replay-state tracking is enabled for this store. Loaded from
    // meta.replay_state_tracking on open; flipped true (one-way) by activation. Guarded
    // by _writerMutex on write; read on the enqueue path under the same mutex.
    private bool _replayTrackingEnabled;

    // K1.2b: the current route-schema generation. Loaded from meta on open; advanced only
    // by AdvanceGenerationAsync (compare-and-set) under _writerMutex.
    private long _currentGeneration;

    private SqliteRouteStore(
        string bufferId,
        string filePath,
        BufferPolicy policy,
        SqliteConnection writer,
        SqliteConnection reader,
        FileStream ownershipLock,
        Func<DateTime>? utcNow,
        Action<QuarantinedPoint>? onQuarantine = null,
        SqliteRouteStoreTestHooks? testHooks = null)
    {
        BufferId = bufferId;
        _filePath = filePath;
        _policy = policy;
        _writer = writer;
        _reader = reader;
        _ownershipLock = ownershipLock;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _onQuarantine = onQuarantine;
        _testHooks = testHooks;
    }

    /// <inheritdoc/>
    public string BufferId { get; }

    /// <summary>The on-disk database file path.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Test seam: in-process head sequence. Visible to the test assembly via
    /// <c>InternalsVisibleTo</c> so the reclaim-invariant tests can compare
    /// in-memory state against the on-disk cursors table.
    /// </summary>
    internal long Head => Volatile.Read(ref _head);

    /// <summary>Test seam: in-process tail sequence. See <see cref="Head"/>.</summary>
    internal long Tail => Volatile.Read(ref _tail);

    /// <summary>Whether replay-state tracking is enabled for this store (K1.2b). One-way once set.</summary>
    internal bool IsReplayStateTrackingEnabled => Volatile.Read(ref _replayTrackingEnabled);

    /// <summary>The current route-schema generation (K1.2b). Advanced only via compare-and-set.</summary>
    internal long CurrentSchemaGeneration => Interlocked.Read(ref _currentGeneration);

    /// <summary>
    /// Test / diagnostics seam: the last unexpected exception caught by the
    /// reclaim loop's D10 defensive catch, or <c>null</c> if the loop has
    /// not caught an unexpected exception. BufferException and SqliteException
    /// are NOT surfaced here — they have dedicated catch blocks and are
    /// considered expected transient noise. Any non-null value here is a
    /// signal that something went wrong and the defensive catch saved the
    /// pipeline from a silent stall.
    /// </summary>
    internal Exception? LastReclaimError => _lastReclaimError;

    // ------------------------------------------------------------------------
    // Open / recovery
    // ------------------------------------------------------------------------

    /// <summary>
    /// Open (or create) a durable buffer at <paramref name="filePath"/>,
    /// run startup recovery, and return a ready-to-use buffer instance.
    /// </summary>
    /// <param name="bufferId">Logical buffer id (typically the route id).</param>
    /// <param name="filePath">Absolute path to the SQLite database file.</param>
    /// <param name="policy">Runtime buffer policy.</param>
    /// <param name="utcNow">Optional clock for tests.</param>
    /// <param name="onQuarantine">
    /// Optional callback invoked once per point the buffer could not serialize
    /// and therefore skipped ("quarantine-and-continue"). Invoked OUTSIDE the
    /// writer mutex, after the surviving batch commits. Must be non-blocking;
    /// a throwing callback is swallowed. Null disables reporting (the point is
    /// still skipped and counted in <see cref="BufferStats.Quarantined"/>).
    /// </param>
    /// <param name="testHooks">
    /// Optional, immutable K1.2d capture test hooks (null in production). See
    /// <see cref="SqliteRouteStoreTestHooks"/>.
    /// </param>
    public static async Task<SqliteRouteStore> OpenAsync(
        string bufferId,
        string filePath,
        BufferPolicy policy,
        Func<DateTime>? utcNow = null,
        Action<QuarantinedPoint>? onQuarantine = null,
        SqliteRouteStoreTestHooks? testHooks = null)
    {
        ArgumentNullException.ThrowIfNull(bufferId);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.MaxDepth <= 0)
        {
            throw new ArgumentException("MaxDepth must be strictly positive for SqliteBuffer.", nameof(policy));
        }
        if (policy.MaxBatchSize <= 0)
        {
            throw new ArgumentException("MaxBatchSize must be strictly positive.", nameof(policy));
        }

        // Canonicalize so two spellings of the same path map to one ownership lock and
        // one DataSource (otherwise two relative spellings could each "own" the file).
        filePath = Path.GetFullPath(filePath);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
        var readerConnStr = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();

        // The ENTIRE open sequence runs under one ownership-safe scope. The exclusive
        // lock is acquired FIRST — before opening either SQLite connection or performing
        // any PRAGMA / schema / recovery mutation — so a second owner is rejected before
        // it can touch the database. Any failure after acquisition synchronously releases
        // the lock and all opened resources; we never depend on GC/finalization.
        FileStream? ownershipLock = null;
        SqliteConnection? writer = null;
        SqliteConnection? reader = null;
        SqliteRouteStore? store = null;
        var completed = false;
        try
        {
            ownershipLock = AcquireOwnershipLock(filePath);

            writer = new SqliteConnection(connStr);
            try
            {
                await writer.OpenAsync().ConfigureAwait(false);

                // 1. PRAGMAs (apply BEFORE quick_check so we have busy_timeout etc.)
                ApplyPragmas(writer, SqliteBufferSchema.WriterConnectionPragmas);

                // 2. quick_check
                EnsureIntegrity(writer);

                // 3. Classify the existing schema WITHOUT DDL, then migrate/validate:
                //    fresh/v1 create-and-stamp transactionally; v2 validate (no auto-create);
                //    malformed/future reject without touching the database.
                MigrateSchema(writer);
            }
            catch (SqliteException ex)
            {
                // PRAGMAs / DDL on a garbled file surface as raw SqliteExceptions
                // (e.g. SQLITE_NOTADB). Translate to typed BufferException so the
                // host receives the same shape as a quick_check failure. The outer
                // finally releases the writer and the ownership lock.
                throw TranslateSqliteException(ex, "OpenAsync");
            }

            // 5/6. Recover head and tail.
            var (head, tail) = ReadHeadTail(writer);

            // Bug 2 (P0) follow-up — fully-drained-then-restarted recovery.
            // ReadHeadTail derives head from MAX(sequence)+1 in the points
            // table. After a clean drain + reclaim, that table is empty so
            // head and tail both collapse to 0. But the cursors table still
            // records the highest acked position (e.g. 2264 after the sink
            // delivered 2264 points). On the next OpenAsync, the cursor row
            // looks like it lives above head — LoadCursors would throw
            // BufferCursorInconsistent, bricking restart on every
            // shutdown-after-drain.
            //
            // Fix: ONLY when the points table is genuinely empty (head == 0
            // because there are no rows to read MAX from), snap head and
            // tail forward to the highest persisted cursor. That preserves
            // monotonic sequence numbers across restarts (downstream
            // consumers see strictly-increasing sequence ids for the
            // lifetime of the buffer file, not a wraparound at every clean
            // shutdown).
            //
            // Critical: gate on head == 0 (empty points). When points exist
            // and a cursor still claims to be above the max sequence, that's
            // genuine corruption (the sink couldn't have ack'd past data
            // that's still in the table) and LoadCursors must still throw.
            // Always read+validate the persisted floor (malformed/negative fails closed,
            // even when points exist), but only recover the head forward when the points
            // table is empty (see the head==0 rationale above).
            var persistedFloor = ReadPersistedSequenceFloor(writer);
            if (head == 0)
            {
                var highWaterMark = Math.Max(PeekMaxCursor(writer), persistedFloor);
                if (highWaterMark > 0)
                {
                    head = highWaterMark;
                    tail = highWaterMark;
                }
            }

            // Strict replay-state load: an enabled store must have complete, consistent
            // metadata (route id, replay sink id + cursor, generation, next_sequence == head);
            // an unknown flag or any missing enabled-state metadata fails closed. Nothing is
            // seeded or defaulted for an enabled store.
            var replayState = LoadReplayState(writer, bufferId, head);

            // Migration seed only for a DISABLED store (never reconstruct an enabled store's
            // authoritative next_sequence).
            if (!replayState.Enabled)
            {
                SeedNextSequenceIfAbsent(writer, head);
            }

            // 7. Load cursors and clamp/validate.
            var clampEvents = LoadCursors(writer, head, tail, out var loadedCursors);

            // Open the read connection.
            reader = new SqliteConnection(readerConnStr);
            await reader.OpenAsync().ConfigureAwait(false);
            ApplyPragmas(reader, SqliteBufferSchema.ReaderConnectionPragmas);

            store = new SqliteRouteStore(bufferId, filePath, policy, writer, reader, ownershipLock, utcNow, onQuarantine, testHooks)
            {
                _head = head,
                _tail = tail,
                _sinksFastForwarded = clampEvents,
                _replayTrackingEnabled = replayState.Enabled,
                _currentGeneration = replayState.Generation,
            };

            // Ownership of the connections and the lock has transferred to the store;
            // from here, cleanup on failure goes through store.DisposeAsync().
            writer = null;
            reader = null;
            ownershipLock = null;

            foreach (var (sinkId, cursor) in loadedCursors)
            {
                store._cursors.Register(sinkId, cursor);
            }

            // 8. Run a reclaim pass synchronously to catch slack from a previous run.
            store.ReclaimOnceLocked();

            // 10. Start the reclaim loop.
            store._reclaimLoopTask = Task.Run(store.ReclaimLoopAsync);

            completed = true;
            return store;
        }
        finally
        {
            if (!completed)
            {
                if (store is not null)
                {
                    // The store owns the connections + lock now; disposal releases all.
                    try { await store.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                else
                {
                    try { reader?.Dispose(); } catch { }
                    try { writer?.Dispose(); } catch { }
                    try { ownershipLock?.Dispose(); } catch { }
                }
            }
        }
    }

    private static void ApplyPragmas(SqliteConnection conn, IReadOnlyList<string> pragmas)
    {
        foreach (var p in pragmas)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = p;
            cmd.ExecuteNonQuery();
        }
    }

    private static void EnsureIntegrity(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check;";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt,
                    "SQLite quick_check returned no rows.");
            }
            var result = reader.GetString(0);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt,
                    $"SQLite quick_check failed: {result}");
            }
        }
        catch (SqliteException ex)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"SQLite quick_check raised an exception: {ex.Message}");
        }
    }

    private enum SchemaClassification
    {
        Fresh,
        V1,
        V2,
    }

    /// <summary>Expected shape of a column, validated against <c>PRAGMA table_info</c>.</summary>
    /// <param name="Name">Column name.</param>
    /// <param name="DeclaredType">Expected declared type (normalized, case-insensitive).</param>
    /// <param name="NotNull">Whether a non-PK column must be NOT NULL (PK columns supply this implicitly).</param>
    /// <param name="PrimaryKeyOrdinal">1-based PK position, or 0 for a non-key column.</param>
    private sealed record ExpectedColumn(string Name, string DeclaredType, bool NotNull, int PrimaryKeyOrdinal);

    // Full expected shape of each core table — column type + NOT NULL + primary-key
    // membership/order — so a damaged schema (name present but wrong type/PK) fails closed.
    private static readonly ExpectedColumn[] PointsSchema =
    {
        new("sequence", "INTEGER", NotNull: false, PrimaryKeyOrdinal: 1),
        new("payload", "BLOB", NotNull: true, PrimaryKeyOrdinal: 0),
        new("enqueued_at", "INTEGER", NotNull: true, PrimaryKeyOrdinal: 0),
        new("expires_at", "INTEGER", NotNull: false, PrimaryKeyOrdinal: 0),
    };

    private static readonly ExpectedColumn[] CursorsSchema =
    {
        new("sink_id", "TEXT", NotNull: false, PrimaryKeyOrdinal: 1),
        new("next_unread", "INTEGER", NotNull: true, PrimaryKeyOrdinal: 0),
        new("updated_at", "INTEGER", NotNull: true, PrimaryKeyOrdinal: 0),
    };

    private static readonly ExpectedColumn[] MetaSchema =
    {
        new("key", "TEXT", NotNull: false, PrimaryKeyOrdinal: 1),
        new("value", "TEXT", NotNull: true, PrimaryKeyOrdinal: 0),
    };

    private static readonly ExpectedColumn[] LatestValueSchema =
    {
        new("source_instance_id", "TEXT", NotNull: false, PrimaryKeyOrdinal: 1),
        new("device_id", "TEXT", NotNull: false, PrimaryKeyOrdinal: 2),
        new("tag_path", "TEXT", NotNull: false, PrimaryKeyOrdinal: 3),
        new("value_type", "INTEGER", NotNull: true, PrimaryKeyOrdinal: 0),
        new("route_buffer_sequence", "INTEGER", NotNull: true, PrimaryKeyOrdinal: 0),
        new("schema_generation", "INTEGER", NotNull: true, PrimaryKeyOrdinal: 0),
        new("envelope", "BLOB", NotNull: true, PrimaryKeyOrdinal: 0),
        new("updated_at", "INTEGER", NotNull: true, PrimaryKeyOrdinal: 0),
    };

    /// <summary>
    /// Migrate/validate the schema. Classifies the EXISTING state without any DDL, then:
    /// FRESH creates the complete v2 schema and stamps it; V1 validates the v1 core tables
    /// first, then creates ONLY the v2 additions (never recreates v1 tables) and stamps v2;
    /// V2 validates all core tables + required columns (never auto-creates a missing one —
    /// that would mask data loss). Malformed/future/damaged files are rejected without any
    /// DDL. All mutations are one transaction.
    /// </summary>
    private static void MigrateSchema(SqliteConnection conn)
    {
        switch (ClassifySchema(conn)) // may throw (fail closed) BEFORE any DDL
        {
            case SchemaClassification.V2:
                ValidateV2Schema(conn);
                return;

            case SchemaClassification.V1:
                // A v1 file MUST already contain complete, well-formed v1 core tables — a
                // damaged v1 (missing/mis-typed/mis-keyed points/cursors/meta) is
                // corruption, not a migration candidate. Validate before any DDL.
                RequireTableSchema(conn, "meta", MetaSchema);
                RequireTableSchema(conn, "points", PointsSchema);
                RequireTableSchema(conn, "cursors", CursorsSchema);
                ApplyDdlAndStamp(conn, SqliteBufferSchema.V2AdditionStatements); // only v2 additions
                return;

            case SchemaClassification.Fresh:
                ApplyDdlAndStamp(conn, SqliteBufferSchema.CreateStatements); // full schema
                return;
        }
    }

    /// <summary>Run the given DDL and stamp the current schema version, atomically.</summary>
    private static void ApplyDdlAndStamp(SqliteConnection conn, IReadOnlyList<string> ddlStatements)
    {
        using var tx = conn.BeginTransaction(deferred: false);
        try
        {
            foreach (var ddl in ddlStatements)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = ddl;
                cmd.ExecuteNonQuery();
            }

            WriteMeta(conn, SqliteBufferSchema.SchemaVersionKey,
                SqliteBufferSchema.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture), tx);
            tx.Commit();
        }
        catch (SqliteException ex)
        {
            tx.Rollback();
            throw TranslateSqliteException(ex, "MigrateSchema");
        }
    }

    /// <summary>Classify the existing schema without changing it. Throws (fail closed) for malformed/future/ambiguous files.</summary>
    private static SchemaClassification ClassifySchema(SqliteConnection conn)
    {
        if (!TableExists(conn, "meta"))
        {
            // FRESH only when there are NO application tables at all. Any existing
            // application table (latest_value, an orphaned points, an unrelated user
            // table) without meta is not a clean fresh route store → fail closed.
            var appTables = ListApplicationTables(conn);
            if (appTables.Count > 0)
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt,
                    $"File is not a clean route store: it has application tables ({string.Join(", ", appTables)}) but no meta table.");
            }

            return SchemaClassification.Fresh;
        }

        var version = ReadMetaValue(conn, SqliteBufferSchema.SchemaVersionKey);
        if (version is null)
        {
            throw new BufferException(CoreErrors.BufferCorrupt, "Route store meta has no schema_version.");
        }

        if (!int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < 1)
        {
            throw new BufferException(CoreErrors.BufferCorrupt, $"Route store schema version is malformed: '{version}'.");
        }

        if (v > SqliteBufferSchema.CurrentSchemaVersion)
        {
            throw new BufferException(
                CoreErrors.BufferSchemaMismatch,
                $"Route store schema version {v} is newer than this binary supports ({SqliteBufferSchema.CurrentSchemaVersion}).");
        }

        return v == SqliteBufferSchema.SchemaVersionV1 ? SchemaClassification.V1 : SchemaClassification.V2;
    }

    /// <summary>Validate a v2 store has every required core table with the expected column shape. No DDL; fail closed on a gap.</summary>
    private static void ValidateV2Schema(SqliteConnection conn)
    {
        RequireTableSchema(conn, "points", PointsSchema);
        RequireTableSchema(conn, "cursors", CursorsSchema);
        RequireTableSchema(conn, "meta", MetaSchema);
        RequireTableSchema(conn, "latest_value", LatestValueSchema);
    }

    /// <summary>
    /// Require a table to exist with the expected column shape — declared type, NOT NULL
    /// (for non-PK columns), and primary-key membership/order — validated against
    /// <c>PRAGMA table_info</c>. Fails closed on any mismatch, without any DDL. Extra
    /// (unexpected) columns are tolerated for forward compatibility.
    /// </summary>
    private static void RequireTableSchema(SqliteConnection conn, string table, ExpectedColumn[] expected)
    {
        if (!TableExists(conn, table))
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Route store is missing required table '{table}' — refusing to recreate it (would mask data loss).");
        }

        var actual = ReadTableInfo(conn, table);
        foreach (var col in expected)
        {
            if (!actual.TryGetValue(col.Name, out var info))
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt, $"Route store table '{table}' is missing required column '{col.Name}'.");
            }

            if (!TypeMatches(info.DeclaredType, col.DeclaredType))
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt,
                    $"Route store column '{table}.{col.Name}' has type '{info.DeclaredType}', expected '{col.DeclaredType}'.");
            }

            if (info.PrimaryKeyOrdinal != col.PrimaryKeyOrdinal)
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt,
                    $"Route store column '{table}.{col.Name}' has primary-key ordinal {info.PrimaryKeyOrdinal}, expected {col.PrimaryKeyOrdinal}.");
            }

            // A PK column supplies non-null/uniqueness implicitly; PRAGMA may report
            // notnull=0 for it. Only enforce NOT NULL on non-PK columns.
            if (col.PrimaryKeyOrdinal == 0 && col.NotNull && !info.NotNull)
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt, $"Route store column '{table}.{col.Name}' must be NOT NULL.");
            }
        }
    }

    /// <summary>True when two declared types match after normalization (trim + case-insensitive).</summary>
    private static bool TypeMatches(string actual, string expected) =>
        string.Equals(actual.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>List application (non-SQLite-internal) table names.</summary>
    private static List<string> ListApplicationTables(SqliteConnection conn)
    {
        var tables = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            tables.Add(rdr.GetString(0));
        }

        return tables;
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $t;";
        cmd.Parameters.AddWithValue("$t", table);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>Read column shapes (declared type, NOT NULL, PK ordinal) from PRAGMA table_info.</summary>
    private static Dictionary<string, (string DeclaredType, bool NotNull, int PrimaryKeyOrdinal)> ReadTableInfo(
        SqliteConnection conn, string table)
    {
        var columns = new Dictionary<string, (string, bool, int)>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        // Table name is an internal constant, not user input — safe to interpolate.
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            // Columns: 0=cid, 1=name, 2=type, 3=notnull, 4=dflt_value, 5=pk.
            var name = rdr.GetString(1);
            var type = rdr.IsDBNull(2) ? string.Empty : rdr.GetString(2);
            var notNull = rdr.GetInt64(3) != 0;
            var pk = (int)rdr.GetInt64(5);
            columns[name] = (type, notNull, pk);
        }

        return columns;
    }

    /// <summary>Upsert a single meta key/value.</summary>
    private static void WriteMeta(SqliteConnection conn, string key, string value, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText =
            "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Seed <see cref="SqliteBufferSchema.NextSequenceKey"/> to the recovered head when absent (K1.2b migration seed).</summary>
    private static void SeedNextSequenceIfAbsent(SqliteConnection conn, long head)
    {
        using var read = conn.CreateCommand();
        read.CommandText = "SELECT value FROM meta WHERE key = $k;";
        read.Parameters.AddWithValue("$k", SqliteBufferSchema.NextSequenceKey);
        if (read.ExecuteScalar() is string)
        {
            return; // already present — leave it (authoritative once tracking is enabled)
        }

        WriteMeta(conn, SqliteBufferSchema.NextSequenceKey, head.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Read a single meta value, or null when the key is absent. When
    /// <paramref name="tx"/> is supplied the read is issued on that transaction (used by
    /// the K1.2d capture path so every read shares one coherent snapshot).
    /// </summary>
    private static string? ReadMetaValue(SqliteConnection conn, string key, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Strictly load and validate the replay-state metadata as one state machine.
    /// <list type="bullet">
    /// <item>flag absent or <c>"disabled"</c> → disabled (generation absent → 0, malformed → corrupt).</item>
    /// <item>flag exactly <c>"enabled"</c> → require route_id == <paramref name="bufferId"/>, a
    /// non-empty replay_sink_id whose cursor exists, a non-negative generation, and a
    /// non-negative next_sequence that EQUALS <paramref name="recoveredHead"/>, with the
    /// replay cursor in <c>[0, next_sequence]</c>. Nothing is seeded/defaulted.</item>
    /// <item>any other flag value → fail closed.</item>
    /// </list>
    /// </summary>
    private static (bool Enabled, long Generation) LoadReplayState(
        SqliteConnection conn, string bufferId, long recoveredHead)
    {
        var flag = ReadMetaValue(conn, SqliteBufferSchema.ReplayStateTrackingKey);
        if (flag is null || string.Equals(flag, SqliteBufferSchema.ReplayTrackingDisabled, StringComparison.Ordinal))
        {
            return (false, ReadCurrentGeneration(conn));
        }

        if (!string.Equals(flag, SqliteBufferSchema.ReplayTrackingEnabled, StringComparison.Ordinal))
        {
            throw new BufferException(CoreErrors.BufferCorrupt, $"Unknown replay_state_tracking value '{flag}'.");
        }

        var routeId = ReadMetaValue(conn, SqliteBufferSchema.RouteIdKey)
            ?? throw new BufferException(CoreErrors.BufferCorrupt, "Enabled route store is missing route_id.");
        if (!string.Equals(routeId, bufferId, StringComparison.Ordinal))
        {
            throw new BufferException(
                CoreErrors.RouteStoreRouteMismatch,
                $"Enabled route store persisted route_id '{routeId}' does not match the opened id '{bufferId}'.");
        }

        var replaySinkId = ReadMetaValue(conn, SqliteBufferSchema.ReplaySinkIdKey);
        if (string.IsNullOrEmpty(replaySinkId))
        {
            throw new BufferException(CoreErrors.BufferCorrupt, "Enabled route store is missing replay_sink_id.");
        }

        var genRaw = ReadMetaValue(conn, SqliteBufferSchema.SchemaGenerationKey)
            ?? throw new BufferException(CoreErrors.BufferCorrupt, "Enabled route store is missing current_schema_generation.");
        if (!long.TryParse(genRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation) || generation < 0)
        {
            throw new BufferException(CoreErrors.BufferCorrupt, $"Enabled route store has a malformed generation '{genRaw}'.");
        }

        var nextRaw = ReadMetaValue(conn, SqliteBufferSchema.NextSequenceKey)
            ?? throw new BufferException(CoreErrors.BufferCorrupt, "Enabled route store is missing next_sequence.");
        if (!long.TryParse(nextRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nextSequence) || nextSequence < 0)
        {
            throw new BufferException(CoreErrors.BufferCorrupt, $"Enabled route store has a malformed next_sequence '{nextRaw}'.");
        }

        if (nextSequence != recoveredHead)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Enabled route store next_sequence {nextSequence} is inconsistent with the recovered head {recoveredHead}.");
        }

        var cursor = ReadCursorValue(conn, replaySinkId);
        if (cursor is null)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Enabled route store is missing the replay-sink cursor '{replaySinkId}'.");
        }

        if (cursor < 0 || cursor > nextSequence)
        {
            throw new BufferException(
                CoreErrors.BufferCursorInconsistent,
                $"Replay-sink cursor {cursor} is outside [0, {nextSequence}].");
        }

        return (true, generation);
    }

    /// <summary>
    /// Read the persisted current route-schema generation. Absent → 0 (never activated).
    /// A malformed or negative value fails closed (<see cref="CoreErrors.BufferCorrupt"/>).
    /// </summary>
    private static long ReadCurrentGeneration(SqliteConnection conn) =>
        ParseGeneration(ReadMetaValue(conn, SqliteBufferSchema.SchemaGenerationKey));

    /// <summary>
    /// K1.2d capture overload: read the current generation inside the capture transaction
    /// (required <paramref name="tx"/>, so a capture read can never fall outside the pinned
    /// snapshot). Same fail-closed semantics as the connection-only overload.
    /// </summary>
    private static long ReadCurrentGeneration(SqliteConnection conn, SqliteTransaction tx) =>
        ParseGeneration(ReadMetaValue(conn, SqliteBufferSchema.SchemaGenerationKey, tx));

    /// <summary>Parse a persisted generation meta value: absent → 0; malformed/negative fails closed.</summary>
    private static long ParseGeneration(string? raw)
    {
        if (raw is null)
        {
            return 0;
        }

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation) || generation < 0)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Route store meta key '{SqliteBufferSchema.SchemaGenerationKey}' has a malformed value '{raw}'.");
        }

        return generation;
    }

    /// <summary>
    /// Read the persisted append-head floor from meta: the max of
    /// <see cref="SqliteBufferSchema.TailSequenceKey"/> (which equals the head high-water
    /// once the buffer has fully drained) and
    /// <see cref="SqliteBufferSchema.NextSequenceKey"/> (the authoritative head when
    /// replay-state tracking is enabled). Absent keys contribute 0. A malformed or
    /// negative value fails closed — it is never treated as a valid floor. (K1.2b: this
    /// is the v1→v2 head-recovery seed so the head cannot regress after a full reclaim.)
    /// </summary>
    private static long ReadPersistedSequenceFloor(SqliteConnection conn)
    {
        long floor = 0;
        foreach (var key in new[] { SqliteBufferSchema.TailSequenceKey, SqliteBufferSchema.NextSequenceKey })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
            cmd.Parameters.AddWithValue("$k", key);
            if (cmd.ExecuteScalar() is not string raw)
            {
                continue;
            }

            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt,
                    $"Route store meta key '{key}' has a malformed sequence value '{raw}'.");
            }

            if (value > floor)
            {
                floor = value;
            }
        }

        return floor;
    }

    private static (long Head, long Tail) ReadHeadTail(SqliteConnection conn)
    {
        long max;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(MAX(sequence), -1) FROM points;";
            max = (long)cmd.ExecuteScalar()!;
        }
        var head = max + 1;

        long tail;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(MIN(sequence), $h) FROM points;";
            cmd.Parameters.AddWithValue("$h", head);
            tail = (long)cmd.ExecuteScalar()!;
        }
        return (head, tail);
    }

    /// <summary>
    /// Read the maximum <c>next_unread</c> across all persisted cursors.
    /// Returns 0 when the cursors table is empty (fresh DB / never had
    /// a sink registered). Used by <see cref="OpenAsync"/> to recover
    /// head/tail forward across a clean-shutdown-after-drain restart,
    /// where the points table is empty but cursors record activity.
    /// </summary>
    private static long PeekMaxCursor(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(next_unread), 0) FROM cursors;";
        return (long)cmd.ExecuteScalar()!;
    }

    private static long LoadCursors(
        SqliteConnection conn,
        long head,
        long tail,
        out List<(string SinkId, long Cursor)> loaded)
    {
        loaded = new List<(string, long)>();
        long clampEvents = 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sink_id, next_unread FROM cursors;";
        using var rdr = cmd.ExecuteReader();
        var clamps = new List<(string SinkId, long NewValue)>();
        while (rdr.Read())
        {
            var sinkId = rdr.GetString(0);
            var cursor = rdr.GetInt64(1);

            if (cursor > head)
            {
                throw new BufferException(
                    CoreErrors.BufferCursorInconsistent,
                    $"Sink '{sinkId}' has cursor {cursor} which is above head {head}. Manual repair required.");
            }

            if (cursor < tail)
            {
                // Defensive clamp — the rows the cursor pointed at were
                // already reclaimed. The sink loses them; this is the
                // documented DropOldest semantics from the previous run.
                clamps.Add((sinkId, tail));
                loaded.Add((sinkId, tail));
                clampEvents++;
            }
            else
            {
                loaded.Add((sinkId, cursor));
            }
        }
        rdr.Close();

        // Persist any clamps so future opens see consistent state.
        if (clamps.Count > 0)
        {
            using var tx = conn.BeginTransaction(deferred: false);
            foreach (var (sinkId, newValue) in clamps)
            {
                using var update = conn.CreateCommand();
                update.Transaction = tx;
                update.CommandText = "UPDATE cursors SET next_unread = $v, updated_at = $t WHERE sink_id = $s;";
                update.Parameters.AddWithValue("$v", newValue);
                update.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                update.Parameters.AddWithValue("$s", sinkId);
                update.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // SinksFastForwarded is an EVENT counter. The whole open-time clamp
        // pass counts as one event regardless of how many cursors were bumped.
        return clamps.Count > 0 ? 1 : 0;
    }

    // ------------------------------------------------------------------------
    // Enqueue
    // ------------------------------------------------------------------------

    /// <inheritdoc/>
    public ValueTask EnqueueAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return ValueTask.CompletedTask;
        }
        return new ValueTask(EnqueueCoreAsync(points, cancellationToken));
    }

    private async Task EnqueueCoreAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken cancellationToken)
    {
        // Split into chunks of MaxBatchSize transactions.
        var maxBatch = _policy.MaxBatchSize;
        for (int offset = 0; offset < points.Count; offset += maxBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var len = Math.Min(maxBatch, points.Count - offset);
            await EnqueueChunkAsync(points, offset, len, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnqueueChunkAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        // Points that could not be serialized are collected here under the
        // writer mutex and reported AFTER the mutex is released, so the
        // (non-blocking, but external) quarantine observer can never run while
        // the write lock is held.
        List<QuarantinedPoint>? quarantined = null;

        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_replayTrackingEnabled)
            {
                // Once replay-state tracking is on, appends must go through the
                // generation-aware path so the manifest and next_sequence stay coherent.
                throw new BufferException(
                    CoreErrors.RouteStoreLegacyAppendOnEnabledStore,
                    $"Legacy EnqueueAsync is not permitted on route store '{BufferId}' with replay-state tracking enabled.");
            }

            // Fast path: if the entire chunk fits in current free space,
            // commit it as a single transaction (one fsync for the whole
            // chunk). This is the dominant case for steady-state production
            // and turns the synchronous=FULL fsync from per-point to
            // per-batch — semantics-preserving, since no eviction or
            // blocking is required when there's room.
            var available = (long)_policy.MaxDepth - (_head - _tail);
            if (count <= available)
            {
                WriteBatchLocked(points, offset, count, ref quarantined);
            }
            else
            {
                // Slow path: per-point loop. Each point may need to wait for
                // space (Block) or get evicted/dropped (DropOldest/DropNewest).
                // The fsync cost here is amortized across however many points
                // can be written between eviction events. Correctness is the
                // priority; performance is documented in phase1-baseline.md.
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Serialize FIRST so an un-serializable point is quarantined
                    // without triggering an unnecessary eviction or a blocking
                    // wait on its behalf.
                    var payload = TrySerializeOrQuarantine(points[offset + i], ref quarantined);
                    if (payload is null)
                    {
                        continue;
                    }

                    if (!HasSpaceLocked())
                    {
                        if (_policy.DropPolicy == DropPolicy.DropNewest)
                        {
                            _totalDropped++;
                            _droppedByCapacity++;
                            continue;
                        }

                        if (_policy.DropPolicy == DropPolicy.Block)
                        {
                            // Release the writer mutex while waiting; re-acquire after.
                            await WaitForSpaceAsync(cancellationToken).ConfigureAwait(false);
                            if (!HasSpaceLocked())
                            {
                                // Spurious wake or another producer claimed the slot.
                                i--;
                                continue;
                            }
                        }
                        else // DropOldest
                        {
                            EvictOldestLocked();
                        }
                    }

                    WritePreSerializedSingleLocked(payload);
                }
            }
        }
        finally
        {
            _writerMutex.Release();
        }

        // Quarantine-and-continue: report every skipped point AFTER releasing
        // the write lock. Diagnostics must never break or stall the data path,
        // so a throwing observer is swallowed.
        if (quarantined is not null && _onQuarantine is not null)
        {
            foreach (var q in quarantined)
            {
                try { _onQuarantine(q); }
                catch { /* diagnostics never break the write path */ }
            }
        }
    }

    /// <summary>
    /// Serialize one point, or quarantine it. A point whose value cannot be
    /// represented on the wire (e.g. an unsupported metadata value type, or a
    /// non-UTC DateTime) must NEVER fail the whole enqueue and strand the
    /// route — the serialization exception is NOT a SqliteException, so left
    /// uncaught it would propagate out of the buffer and kill the route's
    /// intake pump. Instead we skip the point, count it, and add it to the
    /// <paramref name="quarantined"/> list for post-commit reporting.
    /// Caller MUST hold the writer mutex (the counter mutation requires it).
    /// </summary>
    private byte[]? TrySerializeOrQuarantine(
        CanonicalDataPoint point,
        ref List<QuarantinedPoint>? quarantined)
    {
        try
        {
            return BinaryWriterFormat.Instance.Serialize(point);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _totalQuarantined++;
            (quarantined ??= new List<QuarantinedPoint>()).Add(new QuarantinedPoint
            {
                TagName = point.TagName,
                Reason = $"{ex.GetType().Name}: {ex.Message}",
            });
            return null;
        }
    }

    /// <summary>
    /// Insert <paramref name="count"/> points starting at
    /// <paramref name="offset"/> in a single BEGIN IMMEDIATE...COMMIT.
    /// Caller must hold the writer mutex AND must have verified that the
    /// entire chunk fits in current free space (no eviction will be
    /// required). One fsync for the whole chunk under
    /// <c>synchronous=FULL</c>. Points that fail to serialize are skipped
    /// (quarantined) and the surviving points keep contiguous sequence
    /// numbers so per-sink cursors observe no gaps.
    /// </summary>
    private void WriteBatchLocked(
        IReadOnlyList<CanonicalDataPoint> points,
        int offset,
        int count,
        ref List<QuarantinedPoint>? quarantined)
    {
        var enqueuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long? expiresAt = _policy.MaxAge is { } age
            ? enqueuedAt + (long)age.TotalMilliseconds
            : null;

        var written = 0;
        using var tx = _writer.BeginTransaction(deferred: false);
        try
        {
            // Reuse one prepared command for the whole batch.
            using var cmd = _writer.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO points (sequence, payload, enqueued_at, expires_at) VALUES ($s, $p, $e, $x);";
            var pSeq = cmd.CreateParameter();
            pSeq.ParameterName = "$s";
            cmd.Parameters.Add(pSeq);
            var pPayload = cmd.CreateParameter();
            pPayload.ParameterName = "$p";
            cmd.Parameters.Add(pPayload);
            var pEnq = cmd.CreateParameter();
            pEnq.ParameterName = "$e";
            pEnq.Value = enqueuedAt;
            cmd.Parameters.Add(pEnq);
            var pExp = cmd.CreateParameter();
            pExp.ParameterName = "$x";
            pExp.Value = expiresAt as object ?? DBNull.Value;
            cmd.Parameters.Add(pExp);

            // SqliteCommand only re-prepares if CommandText changes; the
            // parameter values are pushed for each iteration.
            cmd.Prepare();
            for (int i = 0; i < count; i++)
            {
                var payload = TrySerializeOrQuarantine(points[offset + i], ref quarantined);
                if (payload is null)
                {
                    // Skipped — do NOT consume a sequence, so survivors stay
                    // contiguous.
                    continue;
                }
                pSeq.Value = _head + written;
                pPayload.Value = payload;
                cmd.ExecuteNonQuery();
                written++;
            }
            tx.Commit();
        }
        catch (SqliteException ex)
        {
            tx.Rollback();
            throw TranslateSqliteException(ex, "EnqueueAsync (batch)");
        }

        _head += written;
        _totalEnqueued += written;
    }

    private bool HasSpaceLocked() => (_head - _tail) < _policy.MaxDepth;

    /// <summary>
    /// Insert one point inside its own transaction. Called inside the writer
    /// mutex. The transaction-per-point cost is amortized by the producer
    /// batching multiple points per call to <see cref="EnqueueAsync"/> and
    /// the producer (route engine) preferring large batches.
    ///
    /// Trade-off note: keeping a single per-point transaction simplifies the
    /// failure path under DropOldest/Block where individual points may be
    /// rejected or evicted differently. A future optimization can promote
    /// to a multi-row transaction when no eviction is in play, but that is
    /// strictly a perf concern, not a correctness concern.
    /// </summary>
    private void WritePreSerializedSingleLocked(byte[] payload)
    {
        var seq = _head;
        var enqueuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long? expiresAt = _policy.MaxAge is { } age
            ? enqueuedAt + (long)age.TotalMilliseconds
            : null;

        using var tx = _writer.BeginTransaction(deferred: false);
        try
        {
            using var cmd = _writer.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO points (sequence, payload, enqueued_at, expires_at) VALUES ($s, $p, $e, $x);";
            cmd.Parameters.AddWithValue("$s", seq);
            cmd.Parameters.AddWithValue("$p", payload);
            cmd.Parameters.AddWithValue("$e", enqueuedAt);
            cmd.Parameters.Add(new SqliteParameter("$x", expiresAt as object ?? DBNull.Value));
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch (SqliteException ex)
        {
            tx.Rollback();
            throw TranslateSqliteException(ex, "EnqueueAsync");
        }

        _head++;
        _totalEnqueued++;
    }

    private async Task WaitForSpaceAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> waiter;
        // _writerMutex is held by the caller. Capture/create the TCS here,
        // then release the mutex while we wait.
        _spaceWaiter ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiter = _spaceWaiter;
        _writerMutex.Release();
        try
        {
            using var reg = cancellationToken.Register(static state =>
            {
                ((TaskCompletionSource<bool>)state!).TrySetCanceled();
            }, waiter);
            await waiter.Task.ConfigureAwait(false);
        }
        finally
        {
            await _writerMutex.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void EvictOldestLocked()
    {
        // Drop the row at _tail. Fast-forward any cursor that pointed at it.
        using var tx = _writer.BeginTransaction(deferred: false);
        try
        {
            using (var del = _writer.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM points WHERE sequence = $s;";
                del.Parameters.AddWithValue("$s", _tail);
                del.ExecuteNonQuery();
            }
            // Bump any cursor at or below the doomed sequence.
            using (var upd = _writer.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText =
                    "UPDATE cursors SET next_unread = $newTail, updated_at = $t WHERE next_unread <= $oldTail;";
                upd.Parameters.AddWithValue("$newTail", _tail + 1);
                upd.Parameters.AddWithValue("$oldTail", _tail);
                upd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                upd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (SqliteException ex)
        {
            tx.Rollback();
            throw TranslateSqliteException(ex, "EvictOldest");
        }

        // Use long.MaxValue as the empty sentinel: when no sinks are
        // registered there is nothing to fast-forward, so the comparison
        // below must evaluate to false. Caught by C2b close-out R2
        // (independent review mutation 20).
        var minBefore = _cursors.Min(defaultIfEmpty: long.MaxValue);
        _cursors.FastForwardBelow(_tail + 1);
        _tail++;
        _totalDropped++;
        _droppedByCapacity++;
        if (minBefore <= _tail - 1)
        {
            _sinksFastForwarded++;
        }
    }

    // ------------------------------------------------------------------------
    // Dequeue
    // ------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask<BufferBatch> DequeueBatchAsync(
        string sinkId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        if (maxCount <= 0)
        {
            return BufferBatch.Empty;
        }
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        // Serialize the entire dequeue under the store's single mutex — the SAME
        // lock GetStatsAsync and every writer take. The read connection
        // (_reader) is NOT thread-safe: Microsoft.Data.Sqlite's SqliteConnection
        // keeps an internal command list, and concurrent CreateCommand/dispose
        // from the sink-loop thread (this method) and the intake-pump thread
        // (GetStatsAsync) corrupts that list, throwing ArgumentOutOfRange /
        // IndexOutOfRange / NullReferenceException out of
        // SqliteConnection.RemoveCommand. That escapes the worker and faults the
        // route to Failed (red) on any transient sink slowdown that widens the
        // overlap window (e.g. an MQTT publish timeout). Taking the mutex also
        // makes the _head/_tail/_cursors reads below consistent with concurrent
        // writers, which were previously read lock-free.
        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (!_cursors.IsRegistered(sinkId))
            {
                throw new InvalidOperationException($"Sink '{sinkId}' is not registered.");
            }

            var cursor = _cursors.Get(sinkId);
            if (cursor < _tail)
            {
                cursor = _tail;
            }
            if (cursor >= _head)
            {
                return BufferBatch.Empty;
            }

            // Read on the read connection. WAL gives a consistent snapshot.
            // DisposeAsync flips _disposed under this same mutex before it
            // disposes _reader, so holding the mutex here closes the old
            // dispose race; the catch below stays as defense-in-depth.
            var list = new List<CanonicalDataPoint>(Math.Min(maxCount, 64));
            long firstSeq = cursor;
            long lastSeq = cursor - 1;

            try
            {
                using (var cmd = _reader.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT sequence, payload FROM points WHERE sequence >= $c ORDER BY sequence LIMIT $n;";
                    cmd.Parameters.AddWithValue("$c", cursor);
                    cmd.Parameters.AddWithValue("$n", maxCount);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var seq = rdr.GetInt64(0);
                        var bytes = (byte[])rdr.GetValue(1);
                        var point = BinaryWriterFormat.Instance.Deserialize(bytes);
                        list.Add(point);
                        if (list.Count == 1)
                        {
                            firstSeq = seq;
                        }
                        lastSeq = seq;
                    }
                }
            }
            catch (Exception ex) when (_disposed && (ex is ObjectDisposedException or NullReferenceException))
            {
                // The reader was disposed between ThrowIfDisposed() and here.
                // Normalize to ObjectDisposedException so callers see a
                // meaningful exception type, not an internal SQLite NRE.
                // Preserve the PUBLIC type identity (the façade) in the disposed exception,
                // not this internal owner type. K1.2a behavior-neutrality.
                throw new ObjectDisposedException(typeof(SqliteBuffer).FullName, ex);
            }

            if (list.Count == 0)
            {
                return BufferBatch.Empty;
            }

            return new BufferBatch
            {
                Points = list,
                FirstSequence = firstSeq,
                LastSequence = lastSeq,
            };
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    // ------------------------------------------------------------------------
    // Ack
    // ------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask AckAsync(
        string sinkId,
        long upToSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<bool>? reclaimSignal = null;
        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_cursors.IsRegistered(sinkId))
            {
                throw new InvalidOperationException($"Sink '{sinkId}' is not registered.");
            }

            var newCursor = upToSequence + 1;
            using var tx = _writer.BeginTransaction(deferred: false);
            try
            {
                using var cmd = _writer.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    "UPDATE cursors SET next_unread = $v, updated_at = $t WHERE sink_id = $s AND next_unread < $v;";
                cmd.Parameters.AddWithValue("$v", newCursor);
                cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                cmd.Parameters.AddWithValue("$s", sinkId);
                var rows = cmd.ExecuteNonQuery();
                tx.Commit();
                if (rows > 0)
                {
                    _cursors.TryAdvance(sinkId, newCursor);
                    // Signal the reclaim loop. We do NOT enter a DELETE path
                    // here — that is the SLO from buffer-durability.md §D7.
                    reclaimSignal = Interlocked.Exchange(ref _reclaimSignal, null);
                }
            }
            catch (SqliteException ex)
            {
                tx.Rollback();
                throw TranslateSqliteException(ex, "AckAsync");
            }
        }
        finally
        {
            _writerMutex.Release();
        }

        reclaimSignal?.TrySetResult(true);
    }

    // ------------------------------------------------------------------------
    // Register / Deregister
    // ------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        cancellationToken.ThrowIfCancellationRequested();

        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_cursors.IsRegistered(sinkId))
            {
                // Idempotent — preserve existing cursor.
                return;
            }

            // Per buffer-contract.md §4.2 and the D12 reversal: new sinks
            // start at the OLDEST currently held sequence so they replay
            // the buffered backlog.
            var initial = _tail;

            using var tx = _writer.BeginTransaction(deferred: false);
            try
            {
                using var cmd = _writer.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO cursors (sink_id, next_unread, updated_at) VALUES ($s, $v, $t) " +
                    "ON CONFLICT(sink_id) DO NOTHING;";
                cmd.Parameters.AddWithValue("$s", sinkId);
                cmd.Parameters.AddWithValue("$v", initial);
                cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            catch (SqliteException ex)
            {
                tx.Rollback();
                throw TranslateSqliteException(ex, "RegisterSinkAsync");
            }

            _cursors.Register(sinkId, initial);
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<bool>? reclaimSignal = null;
        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_cursors.IsRegistered(sinkId))
            {
                return;
            }

            // Protect the designated replay sink on an enabled store — deregistering it
            // would strip generation fencing of its authoritative cursor. Replacement
            // semantics are locked with K1.3.
            if (_replayTrackingEnabled)
            {
                var replaySinkId = ReadMetaValue(_writer, SqliteBufferSchema.ReplaySinkIdKey);
                if (string.Equals(replaySinkId, sinkId, StringComparison.Ordinal))
                {
                    throw new BufferException(
                        CoreErrors.RouteStoreReplaySinkProtected,
                        $"Cannot deregister the designated replay sink '{sinkId}' on an enabled route store.");
                }
            }

            using var tx = _writer.BeginTransaction(deferred: false);
            try
            {
                using var cmd = _writer.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM cursors WHERE sink_id = $s;";
                cmd.Parameters.AddWithValue("$s", sinkId);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            catch (SqliteException ex)
            {
                tx.Rollback();
                throw TranslateSqliteException(ex, "DeregisterSinkAsync");
            }

            _cursors.Deregister(sinkId);
            // Removing a sink may release data only it was pinning. Signal
            // the reclaim loop; do not run DELETE inline.
            reclaimSignal = Interlocked.Exchange(ref _reclaimSignal, null);
        }
        finally
        {
            _writerMutex.Release();
        }

        reclaimSignal?.TrySetResult(true);
    }

    /// <summary>
    /// Read every persisted (sink_id, next_unread) pair from the WRITER
    /// connection. The writer is used deliberately: the read connection is a
    /// separate WAL snapshot and could be stale relative to a just-committed
    /// register/ack, which would make reconciliation prune a live sink.
    /// </summary>
    private static List<(string SinkId, long Cursor)> ReadAllCursors(SqliteConnection conn)
    {
        var cursors = new List<(string, long)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sink_id, next_unread FROM cursors;";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            cursors.Add((rdr.GetString(0), rdr.GetInt64(1)));
        }

        return cursors;
    }

    /// <inheritdoc/>
    public async ValueTask<SinkReconciliationResult> ReconcileSinksAsync(
        IReadOnlyCollection<string> activeSinkIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activeSinkIds);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<bool>? reclaimSignal = null;
        TaskCompletionSource<bool>? spaceWaiter = null;
        SinkReconciliationResult result;

        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var active = new HashSet<string>(activeSinkIds, StringComparer.Ordinal);

            // The designated replay sink is FORCE-ADDED to the active set. Its cursor is
            // authoritative for generation fencing (see DeregisterSinkAsync's identical
            // protection) so it must survive reconciliation no matter what the caller
            // passed. This is a hard invariant, not a courtesy.
            if (_replayTrackingEnabled)
            {
                var replaySinkId = ReadMetaValue(_writer, SqliteBufferSchema.ReplaySinkIdKey);
                if (!string.IsNullOrEmpty(replaySinkId))
                {
                    active.Add(replaySinkId);
                }
            }

            // Authoritative view = the durable cursors table. The tracker is only a mirror,
            // but union it in so a tracker entry that somehow has no row is dropped too.
            var known = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var (sinkId, cursor) in ReadAllCursors(_writer))
            {
                known[sinkId] = cursor;
            }
            foreach (var (sinkId, cursor) in _cursors.Snapshot())
            {
                if (!known.ContainsKey(sinkId))
                {
                    known[sinkId] = cursor;
                }
            }

            // New sinks start at the OLDEST currently held sequence (D12 reversal), matching
            // RegisterSinkAsync exactly, so a freshly configured sink replays the backlog.
            var initial = _tail;

            var toRegister = new List<string>();
            foreach (var sinkId in active)
            {
                if (!known.ContainsKey(sinkId))
                {
                    toRegister.Add(sinkId);
                }
            }

            var toPrune = new List<PrunedSinkCursor>();
            foreach (var (sinkId, cursor) in known)
            {
                if (active.Contains(sinkId))
                {
                    // CONFIGURED — never pruned, however far it lags. It keeps pinning the
                    // tail and keeps replaying in order from its own cursor.
                    continue;
                }

                // Clamp against the tail: sequences below the tail are already gone, so they
                // were never "undelivered" in any recoverable sense.
                var effective = Math.Max(cursor, _tail);
                toPrune.Add(new PrunedSinkCursor
                {
                    SinkId = sinkId,
                    PinnedSequence = cursor,
                    UndeliveredPoints = Math.Max(0, _head - effective),
                });
            }

            if (toRegister.Count == 0 && toPrune.Count == 0)
            {
                return SinkReconciliationResult.Empty;
            }

            // ONE transaction for the whole set change. A partial commit would let the
            // reclaim below compute a tail over a half-updated cursor set — the exact
            // failure mode this method exists to prevent.
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var tx = _writer.BeginTransaction(deferred: false))
            {
                try
                {
                    foreach (var sinkId in toRegister)
                    {
                        using var ins = _writer.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText =
                            "INSERT INTO cursors (sink_id, next_unread, updated_at) VALUES ($s, $v, $t) " +
                            "ON CONFLICT(sink_id) DO NOTHING;";
                        ins.Parameters.AddWithValue("$s", sinkId);
                        ins.Parameters.AddWithValue("$v", initial);
                        ins.Parameters.AddWithValue("$t", now);
                        ins.ExecuteNonQuery();
                    }

                    foreach (var pruned in toPrune)
                    {
                        using var del = _writer.CreateCommand();
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM cursors WHERE sink_id = $s;";
                        del.Parameters.AddWithValue("$s", pruned.SinkId);
                        del.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                catch (SqliteException ex)
                {
                    tx.Rollback();
                    // Tracker untouched — a rolled-back transaction must never leave the
                    // mirror claiming a row the table does not have (or vice versa).
                    throw TranslateSqliteException(ex, "ReconcileSinksAsync");
                }
            }

            // Mirror into the in-memory tracker ONLY after the commit succeeded.
            foreach (var sinkId in toRegister)
            {
                _cursors.Register(sinkId, initial);
            }
            foreach (var pruned in toPrune)
            {
                _cursors.Deregister(pruned.SinkId);
            }

            // Reclaim inside the SAME critical section, still holding _writerMutex. Deferring
            // to the background loop would leave a window in which the tail is stale, and
            // makes the caller's post-condition ("depth reflects the live sinks") untestable.
            if (toPrune.Count > 0)
            {
                ReclaimOnceLocked();
                reclaimSignal = Interlocked.Exchange(ref _reclaimSignal, null);
                spaceWaiter = Interlocked.Exchange(ref _spaceWaiter, null);
            }

            result = new SinkReconciliationResult
            {
                Registered = toRegister,
                Pruned = toPrune,
            };
        }
        finally
        {
            _writerMutex.Release();
        }

        reclaimSignal?.TrySetResult(true);
        spaceWaiter?.TrySetResult(true);
        return result;
    }

    // ------------------------------------------------------------------------
    // Replay-state activation (K1.2b)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Enable replay-state tracking for this route store. This is a <b>drained-store</b>
    /// transition: it is refused (typed
    /// <see cref="CoreErrors.RouteStoreReplayActivationBacklogPending"/>) unless the
    /// store holds no buffered points and every existing cursor sits at the head — so no
    /// pre-activation backlog can be replayed as DATA before a manifest exists. On
    /// success it registers <paramref name="replaySinkId"/> at the head, persists
    /// next_sequence / route_id / generation 0 / the enabled flag atomically, and begins
    /// authoritative next_sequence maintenance on the (generation-aware) append path.
    /// One-way: once enabled it stays enabled; a repeat call is an idempotent
    /// <see cref="ReplayTrackingActivationOutcome.AlreadyEnabled"/>.
    /// </summary>
    /// <param name="routeId">Must equal this store's <see cref="BufferId"/>.</param>
    /// <param name="replaySinkId">The replay sink to register at the activation head.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The activation result (Activated / AlreadyEnabled + the activation head).</returns>
    /// <exception cref="BufferException">
    /// <see cref="CoreErrors.RouteStoreRouteMismatch"/> when <paramref name="routeId"/> is
    /// not this store's id; <see cref="CoreErrors.RouteStoreReplayActivationBacklogPending"/>
    /// when the store is not fully drained.
    /// </exception>
    internal async ValueTask<ReplayTrackingActivationResult> ActivateReplayStateTrackingAsync(
        string routeId, string replaySinkId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeId);
        ArgumentException.ThrowIfNullOrEmpty(replaySinkId);
        cancellationToken.ThrowIfCancellationRequested();

        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (!string.Equals(routeId, BufferId, StringComparison.Ordinal))
            {
                throw new BufferException(
                    CoreErrors.RouteStoreRouteMismatch,
                    $"Activation route id '{routeId}' does not match the store's route id '{BufferId}'.");
            }

            var head = _head;

            // Idempotent: already enabled → no-op success (one-way disabled → enabled).
            // A different replay sink id is refused rather than silently accepted;
            // replacement-sink semantics are locked with K1.3.
            if (_replayTrackingEnabled)
            {
                var persistedSink = ReadMetaValue(_writer, SqliteBufferSchema.ReplaySinkIdKey);
                if (persistedSink is not null && !string.Equals(persistedSink, replaySinkId, StringComparison.Ordinal))
                {
                    throw new BufferException(
                        CoreErrors.RouteStoreReplaySinkMismatch,
                        $"Route '{BufferId}' is already activated with replay sink '{persistedSink}'; cannot re-activate with '{replaySinkId}'.");
                }

                return new ReplayTrackingActivationResult(ReplayTrackingActivationOutcome.AlreadyEnabled, head);
            }

            // Drained-store checks: no retained points AND every cursor caught up to head.
            if ((_head - _tail) > 0 || CountPoints(_writer) > 0)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreReplayActivationBacklogPending,
                    $"Cannot activate replay tracking on route '{BufferId}': the store still holds buffered points.");
            }

            if (CountCursorsBehind(_writer, head) > 0)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreReplayActivationBacklogPending,
                    $"Cannot activate replay tracking on route '{BufferId}': a sink cursor is behind the head ({head}).");
            }

            using var tx = _writer.BeginTransaction(deferred: false);
            try
            {
                // Register the replay sink at the activation head.
                using (var cur = _writer.CreateCommand())
                {
                    cur.Transaction = tx;
                    cur.CommandText =
                        "INSERT INTO cursors (sink_id, next_unread, updated_at) VALUES ($s, $v, $t) " +
                        "ON CONFLICT(sink_id) DO UPDATE SET next_unread = excluded.next_unread, updated_at = excluded.updated_at;";
                    cur.Parameters.AddWithValue("$s", replaySinkId);
                    cur.Parameters.AddWithValue("$v", head);
                    cur.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    cur.ExecuteNonQuery();
                }

                WriteMeta(_writer, SqliteBufferSchema.NextSequenceKey, head.ToString(CultureInfo.InvariantCulture), tx);
                WriteMeta(_writer, SqliteBufferSchema.RouteIdKey, routeId, tx);
                WriteMeta(_writer, SqliteBufferSchema.ReplaySinkIdKey, replaySinkId, tx);
                WriteMeta(_writer, SqliteBufferSchema.SchemaGenerationKey, "0", tx);
                WriteMeta(_writer, SqliteBufferSchema.ReplayStateTrackingKey, SqliteBufferSchema.ReplayTrackingEnabled, tx);

                tx.Commit();
            }
            catch (SqliteException ex)
            {
                tx.Rollback();
                throw TranslateSqliteException(ex, "ActivateReplayStateTrackingAsync");
            }

            _cursors.Register(replaySinkId, head); // idempotent in-memory (drained ⇒ existing == head)
            Interlocked.Exchange(ref _currentGeneration, 0);
            Volatile.Write(ref _replayTrackingEnabled, true);
            return new ReplayTrackingActivationResult(ReplayTrackingActivationOutcome.Activated, head);
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    /// <summary>Count buffered points (drained-store check).</summary>
    private static long CountPoints(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM points;";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Count cursors whose next_unread is not exactly at <paramref name="head"/> (drained-store check).</summary>
    private static long CountCursorsBehind(SqliteConnection conn, long head)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cursors WHERE next_unread <> $h;";
        cmd.Parameters.AddWithValue("$h", head);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Advance the route-schema generation via compare-and-set, fenced on a drained sink.
    /// Requires tracking enabled; <paramref name="next"/> == <paramref name="expectedCurrent"/> + 1
    /// (checked); <paramref name="expectedCurrent"/> == the persisted current generation; the
    /// named sink's cursor to exist and sit <b>exactly at</b> the append head (else
    /// <see cref="CoreErrors.RouteStoreGenerationBacklogPending"/>, so old-generation backlog
    /// cannot replay after the new birth). A cursor beyond the head is corruption. On success
    /// the new generation is committed; a failure leaves the current generation unchanged and
    /// the same proposed transition may be retried. Reopening preserves the committed generation.
    /// </summary>
    /// <param name="expectedCurrent">The generation the caller believes is current (CAS guard).</param>
    /// <param name="next">The proposed next generation; must equal <paramref name="expectedCurrent"/> + 1.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The fence is bound to the store's PERSISTED replay sink (read from meta), not a
    /// caller-supplied sink — so a caught-up ordinary sink cannot be used to advance the
    /// generation while the real replay sink still has old-generation backlog.
    /// </remarks>
    internal async ValueTask AdvanceGenerationAsync(
        long expectedCurrent, long next, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (!_replayTrackingEnabled)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreReplayTrackingNotEnabled,
                    $"Cannot advance generation on route '{BufferId}': replay-state tracking is not enabled.");
            }

            // Transition arithmetic: next must be exactly current+1, non-negative, no overflow.
            long computedNext;
            try
            {
                computedNext = checked(expectedCurrent + 1);
            }
            catch (OverflowException)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreGenerationInvalid,
                    $"Generation overflow advancing from {expectedCurrent}.");
            }

            if (expectedCurrent < 0 || next < 0 || next != computedNext)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreGenerationInvalid,
                    $"Invalid generation transition: next ({next}) must equal expectedCurrent+1 ({computedNext}).");
            }

            // Compare-and-set against the persisted current generation.
            var current = ReadCurrentGeneration(_writer);
            if (expectedCurrent != current)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreStaleGeneration,
                    $"Generation transition expected current {expectedCurrent} but the store is at {current}.");
            }

            // Fence against the PERSISTED replay sink's cursor (the authority).
            var replaySinkId = ReadMetaValue(_writer, SqliteBufferSchema.ReplaySinkIdKey);
            if (string.IsNullOrEmpty(replaySinkId))
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt, $"Enabled route store '{BufferId}' is missing replay_sink_id.");
            }

            var cursor = ReadCursorValue(_writer, replaySinkId);
            if (cursor is null)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreSinkCursorNotFound,
                    $"Replay sink '{replaySinkId}' has no cursor on route '{BufferId}'.");
            }

            var nextSequence = ReadNextSequence(_writer);
            if (cursor < 0 || cursor > nextSequence)
            {
                throw new BufferException(
                    CoreErrors.BufferCursorInconsistent,
                    $"Replay sink '{replaySinkId}' cursor {cursor} is out of range [0, {nextSequence}].");
            }

            if (cursor != nextSequence)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreGenerationBacklogPending,
                    $"Replay sink '{replaySinkId}' cursor {cursor} has not caught up to the head {nextSequence}; drain before advancing generation.");
            }

            using var tx = _writer.BeginTransaction(deferred: false);
            try
            {
                WriteMeta(_writer, SqliteBufferSchema.SchemaGenerationKey, next.ToString(CultureInfo.InvariantCulture), tx);
                tx.Commit();
            }
            catch (SqliteException ex)
            {
                tx.Rollback();
                throw TranslateSqliteException(ex, "AdvanceGenerationAsync");
            }

            Interlocked.Exchange(ref _currentGeneration, next);
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    /// <summary>Read a sink cursor's next_unread, or null when the sink has no cursor.</summary>
    private static long? ReadCursorValue(SqliteConnection conn, string sinkId) =>
        ReadCursorValueCore(conn, sinkId, null);

    /// <summary>
    /// K1.2d capture overload: read a sink cursor inside the capture transaction (required
    /// <paramref name="tx"/>, so a capture read can never fall outside the pinned snapshot).
    /// </summary>
    private static long? ReadCursorValue(SqliteConnection conn, SqliteTransaction tx, string sinkId) =>
        ReadCursorValueCore(conn, sinkId, tx);

    /// <summary>
    /// Read a sink cursor's next_unread (optionally on <paramref name="tx"/>), or null when the
    /// sink has no cursor. A present-but-non-integer storage class (a corrupt/tampered file) fails
    /// closed as <see cref="CoreErrors.BufferCorrupt"/> rather than leaking an
    /// <see cref="InvalidCastException"/>.
    /// </summary>
    private static long? ReadCursorValueCore(SqliteConnection conn, string sinkId, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText = "SELECT next_unread FROM cursors WHERE sink_id = $s;";
        cmd.Parameters.AddWithValue("$s", sinkId);
        var raw = cmd.ExecuteScalar();
        if (raw is null)
        {
            return null; // no cursor row for this sink
        }

        if (raw is not long cursor)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Sink '{sinkId}' cursor has storage class '{raw.GetType().Name}', expected integer.");
        }

        return cursor;
    }

    /// <summary>Read the authoritative next_sequence (present on an enabled store). Malformed/absent fails closed.</summary>
    private static long ReadNextSequence(SqliteConnection conn) =>
        ParseNextSequence(ReadMetaValue(conn, SqliteBufferSchema.NextSequenceKey));

    /// <summary>
    /// K1.2d capture overload: read the authoritative next_sequence inside the capture
    /// transaction (required <paramref name="tx"/>). It is read FIRST in the capture so the
    /// deferred snapshot is pinned before any other statement. Same fail-closed semantics.
    /// </summary>
    private static long ReadNextSequence(SqliteConnection conn, SqliteTransaction tx) =>
        ParseNextSequence(ReadMetaValue(conn, SqliteBufferSchema.NextSequenceKey, tx));

    /// <summary>Parse a persisted next_sequence meta value: absent/malformed/negative fails closed.</summary>
    private static long ParseNextSequence(string? raw)
    {
        if (raw is null)
        {
            throw new BufferException(CoreErrors.BufferCorrupt, "next_sequence is missing on a replay-enabled store.");
        }

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new BufferException(CoreErrors.BufferCorrupt, $"next_sequence has a malformed value '{raw}'.");
        }

        return value;
    }

    // ------------------------------------------------------------------------
    // Tracked append (enabled-store path — K1.2c)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Append a batch to a replay-enabled store, atomically maintaining the
    /// <c>latest_value</c> manifest and the authoritative <c>next_sequence</c>. In ONE
    /// writer transaction: the batch's points are inserted, each metric's manifest row is
    /// upserted (keeping the row with the greater <c>route_buffer_sequence</c>), and
    /// <c>next_sequence</c> is advanced to one past the last assigned sequence — so the
    /// manifest, the buffered rows, and the append head can never disagree after a crash.
    /// <para>
    /// This is the enabled-store append path; the legacy context-free
    /// <see cref="EnqueueAsync"/> stays rejected on an enabled store. Every point is
    /// serialized and turned into a manifest envelope BEFORE the transaction opens, so a
    /// point that cannot form a manifest row (an unsupported
    /// <see cref="Model.CanonicalValueType"/>, a value/type mismatch, or a serialization
    /// failure) fails the whole append closed
    /// (<see cref="CoreErrors.RouteStoreEnvelopeUnsupported"/>) with no partial DB
    /// mutation — a well-behaved (K1.3) route path never feeds one.
    /// </para>
    /// <para>
    /// The manifest value is derived from the point directly: its acquisition timestamp is
    /// the point's <see cref="Model.CanonicalDataPoint.DeviceTimestamp"/>, which MUST already
    /// be UTC (a non-UTC timestamp fails the append closed rather than being silently
    /// shifted — the buffer layer's locked no-silent-shift policy); static metric properties
    /// are NOT sourced here
    /// (per-point <c>Metadata</c> is per-reading enrichment, not stable manifest metadata),
    /// so they are left null until a static metric descriptor is wired in a later milestone.
    /// Capacity backpressure (MaxDepth eviction/blocking) is intentionally NOT applied on
    /// the tracked path — evicting unacked rows would break replay; the tracked store's
    /// retention is governed by the replay-sink cursor and MaxAge, not depth eviction.
    /// </para>
    /// </summary>
    /// <param name="points">The batch to append (the whole list is one transaction).</param>
    /// <param name="expectedGeneration">
    /// The route-schema generation the caller believes is current; must equal the persisted
    /// generation (else <see cref="CoreErrors.RouteStoreStaleGeneration"/>). Each manifest
    /// row is stamped with it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token (honored before the transaction only).</param>
    /// <returns>The contiguous sequence range assigned (Count 0 for an empty batch).</returns>
    /// <exception cref="BufferException">
    /// <see cref="CoreErrors.RouteStoreReplayTrackingNotEnabled"/> when tracking is off;
    /// <see cref="CoreErrors.RouteStoreStaleGeneration"/> on a generation mismatch;
    /// <see cref="CoreErrors.RouteStoreEnvelopeUnsupported"/> on an unrepresentable point.
    /// </exception>
    internal async ValueTask<AssignedSequenceRange> AppendAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();

        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (!_replayTrackingEnabled)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreReplayTrackingNotEnabled,
                    $"Cannot tracked-append on route '{BufferId}': replay-state tracking is not enabled.");
            }

            // Compare-and-set the generation against the persisted authority. This runs
            // BEFORE any preparation so a doomed batch (wrong store / stale generation) is
            // rejected without wasting serialization work. The read-then-write is a valid
            // atomic compare even though ReadCurrentGeneration is a separate statement from
            // the transaction below: the store holds the exclusive per-database lifetime
            // ownership lock (K1.2a — RouteStoreAlreadyOwned) so no second writer,
            // connection, or process can touch this file, and the writer mutex is held for
            // the whole method — the persisted generation cannot change between this compare
            // and the commit.
            var current = ReadCurrentGeneration(_writer);
            if (expectedGeneration != current)
            {
                throw new BufferException(
                    CoreErrors.RouteStoreStaleGeneration,
                    $"Tracked append expected generation {expectedGeneration} but the store is at {current}.");
            }

            if (points.Count == 0)
            {
                return new AssignedSequenceRange(_head, _head, 0);
            }

            // Prepare payloads + manifest envelopes AFTER the guards (fail fast on a doomed
            // batch) but BEFORE the transaction — preparation runs no SQL, and a point that
            // cannot form a manifest row fails closed here with no DB mutation.
            var prepared = new PreparedTrackedPoint[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                prepared[i] = PrepareTrackedPoint(points[i]);
            }

            var first = _head;
            var last = _head + prepared.Length - 1;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long? expiresAt = _policy.MaxAge is { } age ? nowMs + (long)age.TotalMilliseconds : null;

            using var tx = _writer.BeginTransaction(deferred: false);
            try
            {
                AppendPointsLocked(tx, prepared, first, nowMs, expiresAt);
                UpsertManifestLocked(tx, prepared, first, current, nowMs);
                WriteMeta(_writer, SqliteBufferSchema.NextSequenceKey,
                    (last + 1).ToString(CultureInfo.InvariantCulture), tx);
                tx.Commit();
            }
            catch (SqliteException ex)
            {
                tx.Rollback();
                throw TranslateSqliteException(ex, "AppendAsync");
            }

            _head += prepared.Length;
            _totalEnqueued += prepared.Length;
            return new AssignedSequenceRange(first, last, prepared.Length);
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    /// <summary>Insert the prepared batch into <c>points</c> with contiguous sequences from <paramref name="first"/>.</summary>
    private void AppendPointsLocked(
        SqliteTransaction tx, PreparedTrackedPoint[] prepared, long first, long enqueuedAt, long? expiresAt)
    {
        using var cmd = _writer.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO points (sequence, payload, enqueued_at, expires_at) VALUES ($s, $p, $e, $x);";
        var pSeq = cmd.CreateParameter(); pSeq.ParameterName = "$s"; cmd.Parameters.Add(pSeq);
        var pPayload = cmd.CreateParameter(); pPayload.ParameterName = "$p"; cmd.Parameters.Add(pPayload);
        var pEnq = cmd.CreateParameter(); pEnq.ParameterName = "$e"; pEnq.Value = enqueuedAt; cmd.Parameters.Add(pEnq);
        var pExp = cmd.CreateParameter(); pExp.ParameterName = "$x"; pExp.Value = expiresAt as object ?? DBNull.Value; cmd.Parameters.Add(pExp);
        cmd.Prepare();

        for (int i = 0; i < prepared.Length; i++)
        {
            pSeq.Value = first + i;
            pPayload.Value = prepared[i].Payload;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Upsert each metric's manifest row, keeping the one with the greater
    /// <c>route_buffer_sequence</c> (so intra-batch order and cross-call monotonicity both
    /// hold — an older sequence never overwrites a newer one).
    /// </summary>
    private void UpsertManifestLocked(
        SqliteTransaction tx, PreparedTrackedPoint[] prepared, long first, long generation, long updatedAt)
    {
        using var cmd = _writer.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO latest_value " +
            "(source_instance_id, device_id, tag_path, value_type, route_buffer_sequence, schema_generation, envelope, updated_at) " +
            "VALUES ($src, $dev, $tag, $vt, $seq, $gen, $env, $ts) " +
            "ON CONFLICT(source_instance_id, device_id, tag_path) DO UPDATE SET " +
            "value_type = excluded.value_type, route_buffer_sequence = excluded.route_buffer_sequence, " +
            "schema_generation = excluded.schema_generation, envelope = excluded.envelope, updated_at = excluded.updated_at " +
            "WHERE excluded.route_buffer_sequence > latest_value.route_buffer_sequence;";
        var pSrc = cmd.CreateParameter(); pSrc.ParameterName = "$src"; cmd.Parameters.Add(pSrc);
        var pDev = cmd.CreateParameter(); pDev.ParameterName = "$dev"; cmd.Parameters.Add(pDev);
        var pTag = cmd.CreateParameter(); pTag.ParameterName = "$tag"; cmd.Parameters.Add(pTag);
        var pVt = cmd.CreateParameter(); pVt.ParameterName = "$vt"; cmd.Parameters.Add(pVt);
        var pSeq = cmd.CreateParameter(); pSeq.ParameterName = "$seq"; cmd.Parameters.Add(pSeq);
        var pGen = cmd.CreateParameter(); pGen.ParameterName = "$gen"; pGen.Value = generation; cmd.Parameters.Add(pGen);
        var pEnv = cmd.CreateParameter(); pEnv.ParameterName = "$env"; cmd.Parameters.Add(pEnv);
        var pTs = cmd.CreateParameter(); pTs.ParameterName = "$ts"; pTs.Value = updatedAt; cmd.Parameters.Add(pTs);
        cmd.Prepare();

        for (int i = 0; i < prepared.Length; i++)
        {
            var p = prepared[i];
            pSrc.Value = p.SourceInstanceId;
            pDev.Value = p.DeviceId;
            pTag.Value = p.TagPath;
            pVt.Value = p.ValueType;
            pSeq.Value = first + i;
            pEnv.Value = p.Envelope;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Serialize a point for the buffer and pre-encode its manifest envelope, failing
    /// closed if the point cannot be represented either way (no partial mutation). The
    /// manifest value is built via <c>LatestMetricValue.Create</c> (the sole validator of
    /// the datatype/null/arm invariants). The envelope is sequence-independent — the real
    /// <c>route_buffer_sequence</c> is written to the column under the mutex — so a
    /// placeholder sequence of 0 is used here purely to satisfy the constructor.
    /// </summary>
    private static PreparedTrackedPoint PrepareTrackedPoint(CanonicalDataPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        byte[] payload;
        try
        {
            payload = BinaryWriterFormat.Instance.Serialize(point);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new BufferException(
                CoreErrors.RouteStoreEnvelopeUnsupported,
                $"Tracked append cannot serialize point '{point.TagName}': {ex.Message}");
        }

        var acquisitionUtc = RequireUtcTimestamp(point.DeviceTimestamp, point.TagName);

        Routing.LatestMetricValue manifestValue;
        try
        {
            var key = Routing.CanonicalMetricKey.Create(point.SourceInstanceId, point.DeviceId, point.TagPath);
            manifestValue = Routing.LatestMetricValue.Create(
                key,
                point.ValueType,
                point.Value,
                isNull: point.Value is null,
                timestamp: acquisitionUtc,
                quality: point.Quality,
                routeBufferSequence: 0, // placeholder — the column carries the real sequence
                qualityReason: point.QualityReason,
                unit: point.Unit,
                staticProperties: null);
        }
        catch (ArgumentException ex) // covers ArgumentOutOfRangeException (negative sequence etc.)
        {
            throw new BufferException(
                CoreErrors.RouteStoreEnvelopeUnsupported,
                $"Tracked append cannot build a manifest row for point '{point.TagName}': {ex.Message}");
        }

        var envelope = LatestValueEnvelopeV1.Encode(manifestValue);
        return new PreparedTrackedPoint(
            payload, point.SourceInstanceId, point.DeviceId, point.TagPath, (int)point.ValueType, envelope);
    }

    /// <summary>
    /// Convert a canonical acquisition <see cref="DateTime"/> to a UTC
    /// <see cref="DateTimeOffset"/> for the manifest, requiring it to already be UTC.
    /// <para>
    /// The canonical data-model contract states device/gateway timestamps are UTC, and the
    /// buffer layer's locked policy is to <b>fail loud on non-UTC and never silently shift</b>
    /// (see <c>BinaryWriterFormat.RequireUtcTicks</c> / <c>MessagePackFormat</c>, C2a R2).
    /// A <see cref="DateTimeKind.Local"/> or <see cref="DateTimeKind.Unspecified"/> value is
    /// therefore rejected rather than relabelled or converted: relabelling
    /// (<c>new DateTimeOffset(ticks, TimeSpan.Zero)</c>) would corrupt the represented instant
    /// for a Local value, and converting would violate the no-silent-shift rule and diverge
    /// from the payload serializer that already rejects the same point. Since the manifest is
    /// about to become Sparkplug birth-state authority (K1.2d), a wrong acquisition timestamp
    /// would become externally observable — so this fails closed.
    /// </para>
    /// </summary>
    private static DateTimeOffset RequireUtcTimestamp(DateTime timestamp, string tagName)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
        {
            throw new BufferException(
                CoreErrors.RouteStoreEnvelopeUnsupported,
                $"Tracked append point '{tagName}' has a non-UTC acquisition timestamp " +
                $"(DeviceTimestamp.Kind = {timestamp.Kind}); the canonical model requires UTC and the " +
                "buffer layer fails loud on non-UTC rather than silently shifting the instant.");
        }

        // Kind == Utc → offset zero, ticks preserved: the represented instant is exact.
        return new DateTimeOffset(timestamp);
    }

    /// <summary>A point prepared for the tracked-append transaction: its buffer payload and its manifest envelope + key columns.</summary>
    private readonly record struct PreparedTrackedPoint(
        byte[] Payload,
        string SourceInstanceId,
        string DeviceId,
        string TagPath,
        int ValueType,
        byte[] Envelope);

    // ------------------------------------------------------------------------
    // Replay-state raw capture (K1.2d — R12 step 1)
    // ------------------------------------------------------------------------
    //
    // Governing directive (LOCKED, v3 §R2): birth/cutover capture executes UNDER the
    // existing per-database ownership lock and writer mutex, on the single owning
    // connection set, inside ONE deferred read transaction whose FIRST statement pins a
    // coherent read snapshot — no secondary SQLite writer, no side-channel mutation path.
    // The decode of the captured rows into a LatestValueSnapshot happens OFF the lock in a
    // later step from the immutable RawCaptureState this produces.

    /// <summary>
    /// Capture the coherent raw replay state for <paramref name="sinkId"/> — the append
    /// cutoff, current generation, sink cursor, and current-generation manifest rows — in
    /// one deferred read transaction under the writer mutex. The rows are deep-copied so
    /// the result is safe to decode after the transaction ends. Fails closed
    /// (<see cref="CoreErrors.RouteStoreReplayTrackingNotEnabled"/>) when tracking is off,
    /// and translates any SQLite failure to the §7 error families; a malformed meta value
    /// surfaces as <see cref="CoreErrors.BufferCorrupt"/>.
    /// </summary>
    /// <param name="sinkId">The sink whose cursor to capture.</param>
    /// <param name="cancellationToken">Cancellation token (honored before entering the critical section).</param>
    /// <returns>The coherent raw capture.</returns>
    internal ValueTask<RawCaptureState> CaptureRawStateAsync(string sinkId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkId);
        return CaptureUnderLockAsync(tx => ReadRawState(tx, sinkId), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<ReplayBoundary> CaptureReplayBoundaryAsync(string sinkId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkId);
        return CaptureUnderLockAsync(tx => BuildBoundary(tx, sinkId), cancellationToken);
    }

    /// <summary>
    /// Build the sink's replay boundary from the boundary-only reads (cursor + cutoff) — no
    /// manifest scan. Delegates the cursor validation to <see cref="ToBoundary"/>.
    /// </summary>
    private ReplayBoundary BuildBoundary(SqliteTransaction tx, string sinkId)
    {
        // Tracking-enabled precedence is enforced once in CaptureUnderLockAsync before the tx.
        var (cursor, cutoff) = ReadBoundaryRaw(tx, sinkId);
        return ToBoundary(sinkId, cursor, cutoff);
    }

    /// <inheritdoc/>
    public async ValueTask<ReplaySessionStartState> CaptureBirthStateAsync(
        string routeId, string sinkId, CancellationToken cancellationToken)
    {
        RequireMatchingRoute(routeId);

        // Under the lock: coherent raw capture (cutoff + cursor + generation + manifest).
        var raw = await CaptureRawStateAsync(sinkId, cancellationToken).ConfigureAwait(false);

        // Off the lock: validate the cursor into the boundary and decode the manifest rows
        // into the birth snapshot. ReplaySessionStartState.Create re-verifies coherence
        // (every snapshot value strictly below the boundary cutoff), which the decoder
        // already enforces per row — so a corrupt row fails as a typed Buffer error first.
        var boundary = ToBoundary(sinkId, raw.Cursor, raw.CutoffExclusive);
        var snapshot = BuildSnapshotFromRawRows(raw.Manifest, raw.Generation, raw.CutoffExclusive, cancellationToken);
        return ReplaySessionStartState.Create(boundary, snapshot);
    }

    /// <inheritdoc/>
    public async ValueTask<ReplaySessionCutoverState> CaptureCutoverAsync(
        string routeId, CancellationToken cancellationToken)
    {
        RequireMatchingRoute(routeId);

        // Cutover has no sink — capture cutoff + generation + manifest (no cursor), then
        // decode off the lock.
        var raw = await CaptureUnderLockAsync(tx => ReadRawState(tx, null), cancellationToken).ConfigureAwait(false);

        var snapshot = BuildSnapshotFromRawRows(raw.Manifest, raw.Generation, raw.CutoffExclusive, cancellationToken);
        return ReplaySessionCutoverState.Create(raw.CutoffExclusive, snapshot);
    }

    /// <summary>Fail closed (<see cref="CoreErrors.RouteStoreRouteMismatch"/>) when the capture route id is not this store's id.</summary>
    private void RequireMatchingRoute(string routeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeId);
        if (!string.Equals(routeId, BufferId, StringComparison.Ordinal))
        {
            throw new BufferException(
                CoreErrors.RouteStoreRouteMismatch,
                $"Capture route id '{routeId}' does not match the store's route id '{BufferId}'.");
        }
    }

    /// <summary>
    /// Decode captured raw manifest rows into an immutable <see cref="LatestValueSnapshot"/>
    /// OFF the writer lock. Each row is structurally validated (<see cref="ValidateRawRow"/>)
    /// then decoded via <see cref="LatestValueEnvelopeV1.Decode"/> with the column cross-checks;
    /// a structural inconsistency (bad generation, undefined datatype, empty envelope, invalid
    /// identity, duplicate identity) fails closed as <see cref="CoreErrors.BufferCorrupt"/>, a
    /// value at/beyond the cutoff as <see cref="CoreErrors.BufferCursorInconsistent"/>, and an
    /// unrepresentable envelope as <see cref="CoreErrors.RouteStoreEnvelopeUnsupported"/> — no
    /// <see cref="ArgumentException"/>/<see cref="InvalidCastException"/> is leaked. Cancellation
    /// is checked every 256 rows; on cancel it throws with no partial snapshot. The
    /// <paramref name="rowDecodedForTest"/> callback (null in production) fires after each row is
    /// decoded, so a test can drive deterministic mid-loop cancellation without timing.
    /// </summary>
    internal static LatestValueSnapshot BuildSnapshotFromRawRows(
        IReadOnlyList<RawManifestRow> rows,
        long generation,
        long cutoff,
        CancellationToken cancellationToken,
        Action<int>? rowDecodedForTest = null)
    {
        var values = new Dictionary<CanonicalMetricKey, LatestMetricValue>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            if ((i & 0xFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = rows[i];
            ValidateRawRow(row, generation, cutoff);

            var key = BuildMetricKey(row);
            var value = LatestValueEnvelopeV1.Decode(
                row.Envelope, key, (CanonicalValueType)row.ValueType, row.RouteBufferSequence);

            // Defensive: the manifest PK (source, device, tag) makes a genuine collision
            // impossible today, but the guard stays (a normalized-key collision fixture is
            // deferred until canonical normalization exists — plan §R6).
            if (!values.TryAdd(key, value))
            {
                throw new BufferException(
                    CoreErrors.BufferCorrupt,
                    $"Duplicate manifest identity '{key}' at generation {generation}.");
            }

            rowDecodedForTest?.Invoke(i);
        }

        return new LatestValueSnapshot(Adapters.RouteSchemaGeneration.Create(generation), values);
    }

    /// <summary>Structurally validate one raw manifest row before decode (see <see cref="BuildSnapshotFromRawRows"/> for the error mapping).</summary>
    private static void ValidateRawRow(RawManifestRow row, long generation, long cutoff)
    {
        var identity = $"{row.SourceInstanceId}/{row.DeviceId}/{row.TagPath}";

        if (row.SchemaGeneration != generation)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Manifest row '{identity}' has schema_generation {row.SchemaGeneration}, expected {generation}.");
        }

        if (!Enum.IsDefined(typeof(CanonicalValueType), row.ValueType))
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Manifest row '{identity}' has an undefined value_type {row.ValueType}.");
        }

        if (row.Envelope is null || row.Envelope.Length == 0)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Manifest row '{identity}' has an empty envelope.");
        }

        if (row.RouteBufferSequence < 0)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Manifest row '{identity}' has a negative route_buffer_sequence {row.RouteBufferSequence}.");
        }

        if (row.RouteBufferSequence >= cutoff)
        {
            throw new BufferException(
                CoreErrors.BufferCursorInconsistent,
                $"Manifest row '{identity}' has route_buffer_sequence {row.RouteBufferSequence} at or beyond the capture cutoff {cutoff}.");
        }
    }

    /// <summary>Build the metric key from a raw row, translating an invalid identity to <see cref="CoreErrors.BufferCorrupt"/>.</summary>
    private static CanonicalMetricKey BuildMetricKey(RawManifestRow row)
    {
        try
        {
            return CanonicalMetricKey.Create(row.SourceInstanceId, row.DeviceId, row.TagPath);
        }
        catch (ArgumentException ex)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Manifest row has an invalid metric identity ('{row.SourceInstanceId}'/'{row.DeviceId}'/'{row.TagPath}'): {ex.Message}");
        }
    }

    /// <summary>
    /// Run <paramref name="read"/> inside one deferred read transaction on the writer
    /// connection, under the writer mutex. Fires the critical-section test hook after the
    /// mutex is acquired and before the transaction opens. Uses <c>using var tx</c> disposal
    /// for cleanup — there is nothing to undo on a read, so no explicit rollback is issued
    /// (which would risk masking the original failure). A SQLite failure is translated to a
    /// typed <see cref="BufferException"/> (§7); any other exception (a fail-closed
    /// <see cref="BufferException"/> from the reads, or a test-hook exception) propagates
    /// unchanged.
    /// </summary>
    private async ValueTask<T> CaptureUnderLockAsync<T>(
        Func<SqliteTransaction, T> read, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Fast path: preserve the established SqliteBuffer disposed identity BEFORE waiting on
        // the mutex — DisposeAsync disposes _writerMutex, so a post-dispose WaitAsync would
        // otherwise surface the semaphore's ObjectDisposedException identity instead.
        ThrowIfDisposed();
        try
        {
            await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // Dispose raced the WaitAsync (mutex disposed after the fast-path check). _disposed is
            // set under the mutex BEFORE the mutex is disposed, so it is already true here — re-emit
            // the SqliteBuffer identity. The trailing throw is unreachable but satisfies the compiler.
            ThrowIfDisposed();
            throw;
        }

        try
        {
            ThrowIfDisposed();

            // Capability precedence: a disabled store fails with RouteStoreReplayTrackingNotEnabled
            // BEFORE any transaction is opened, so a transaction-open failure can never mask it.
            RequireTrackingEnabled();

            // Test seam: fired synchronously AFTER the mutex + guards and BEFORE opening the
            // transaction. A throwing hook escapes unchanged (it runs outside the SqliteException
            // translation below and is never converted to a Buffer error).
            _testHooks?.CaptureEnteredCriticalSection?.Invoke();

            // One deferred read transaction: the FIRST read statement pins a coherent snapshot for
            // the whole capture (proven on Microsoft.Data.Sqlite 8.0.10). 'deferred' provides the
            // snapshot; K1.2d issues only SELECTs. BeginTransaction is INSIDE the translation scope
            // so a transaction-open (or disposal-time) SQLite failure is translated too; no explicit
            // Rollback() — a read has nothing to undo and `using` disposal releases the transaction.
            try
            {
                using var tx = _writer.BeginTransaction(deferred: true);
                var result = read(tx);
                tx.Commit();
                return result;
            }
            catch (SqliteException ex)
            {
                throw TranslateSqliteException(ex, "CaptureReplayState");
            }
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    /// <summary>
    /// The boundary read group under the capture transaction: the append cutoff
    /// (<c>next_sequence</c>, read FIRST so the deferred snapshot is pinned) and — when
    /// <paramref name="sinkId"/> is non-null — the sink cursor. Emits
    /// <see cref="CaptureQueryKind.Boundary"/> — never
    /// <see cref="CaptureQueryKind.ManifestScan"/> — so the boundary-only path is provably
    /// manifest-scan-free. A null <paramref name="sinkId"/> (the cutover capture, which has no
    /// sink) skips the cursor read and returns a null cursor.
    /// </summary>
    private (long? Cursor, long CutoffExclusive) ReadBoundaryRaw(SqliteTransaction tx, string? sinkId)
    {
        _testHooks?.QueryExecuting?.Invoke(CaptureQueryKind.Boundary);
        var cutoff = ReadNextSequence(_writer, tx); // FIRST statement — pins the snapshot
        var cursor = sinkId is null ? (long?)null : ReadCursorValue(_writer, tx, sinkId);
        return (cursor, cutoff);
    }

    /// <summary>
    /// The full raw capture under the transaction: boundary (cutoff + optional cursor),
    /// current generation, then the current-generation manifest scan. Requires tracking
    /// enabled. A null <paramref name="sinkId"/> captures the cutover state (no cursor).
    /// </summary>
    private RawCaptureState ReadRawState(SqliteTransaction tx, string? sinkId)
    {
        // Tracking-enabled precedence is enforced once in CaptureUnderLockAsync before the tx.
        var (cursor, cutoff) = ReadBoundaryRaw(tx, sinkId);
        var generation = ReadCurrentGeneration(_writer, tx);
        _testHooks?.QueryExecuting?.Invoke(CaptureQueryKind.ManifestScan);
        var manifest = ReadCurrentGenerationManifest(_writer, tx, generation);
        return new RawCaptureState(cutoff, generation, cursor, manifest);
    }

    /// <summary>
    /// Turn a captured (cursor, cutoff) pair into a validated <see cref="ReplayBoundary"/>.
    /// A missing cursor is <see cref="CoreErrors.RouteStoreSinkCursorNotFound"/>; an
    /// out-of-range cursor is <see cref="CoreErrors.BufferCursorInconsistent"/>. The range
    /// check runs BEFORE <see cref="ReplayBoundary.Create"/> so a corrupt cursor surfaces as a
    /// typed Buffer error, never a leaked <see cref="ArgumentException"/>.
    /// </summary>
    private ReplayBoundary ToBoundary(string sinkId, long? cursor, long cutoff)
    {
        if (cursor is null)
        {
            throw new BufferException(
                CoreErrors.RouteStoreSinkCursorNotFound,
                $"Sink '{sinkId}' has no cursor on route '{BufferId}'; cannot capture a replay boundary.");
        }

        if (cursor < 0 || cursor > cutoff)
        {
            throw new BufferException(
                CoreErrors.BufferCursorInconsistent,
                $"Sink '{sinkId}' cursor {cursor} is outside the boundary range [0, {cutoff}] on route '{BufferId}'.");
        }

        return ReplayBoundary.Create(cursor.Value, cutoff);
    }

    /// <summary>Fail closed when replay-state tracking is not enabled (capture is only valid on an enabled store).</summary>
    private void RequireTrackingEnabled()
    {
        if (!_replayTrackingEnabled)
        {
            throw new BufferException(
                CoreErrors.RouteStoreReplayTrackingNotEnabled,
                $"Cannot capture replay state on route '{BufferId}': replay-state tracking is not enabled.");
        }
    }

    /// <summary>
    /// K1.2d capture read: the current-generation <c>latest_value</c> rows, deep-copied into
    /// <see cref="RawManifestRow"/> (the envelope is an owned <c>byte[]</c>, never an alias to
    /// a live DB buffer) so they can be decoded off the lock. Rows stamped with a stale
    /// generation (a metric removed in a later generation leaves a permanent stale-gen row)
    /// are excluded. Required <paramref name="tx"/> keeps the scan inside the pinned snapshot.
    /// </summary>
    private static List<RawManifestRow> ReadCurrentGenerationManifest(
        SqliteConnection conn, SqliteTransaction tx, long generation)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT source_instance_id, device_id, tag_path, value_type, route_buffer_sequence, schema_generation, envelope " +
            "FROM latest_value WHERE schema_generation = $g;";
        cmd.Parameters.AddWithValue("$g", generation);

        var rows = new List<RawManifestRow>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            rows.Add(ReadManifestRow(rdr));
        }

        return rows;
    }

    /// <summary>
    /// Materialize one manifest row with EXACT storage-class + range checks. SQLite column
    /// affinity does not guarantee the stored runtime type, so a corrupt/tampered file could
    /// hold a different storage class (e.g. TEXT in an INTEGER column). Every mismatch fails
    /// closed as <see cref="CoreErrors.BufferCorrupt"/> rather than leaking an
    /// <see cref="InvalidCastException"/>/<see cref="InvalidOperationException"/>, and
    /// <c>value_type</c> narrows to <see cref="int"/> with a CHECKED cast so a corrupt 64-bit
    /// value cannot wrap into an apparently-valid discriminator (a fail-open).
    /// </summary>
    private static RawManifestRow ReadManifestRow(SqliteDataReader rdr)
    {
        var src = ReadRequiredText(rdr, 0, "source_instance_id");
        var dev = ReadRequiredText(rdr, 1, "device_id");
        var tag = ReadRequiredText(rdr, 2, "tag_path");
        var identity = $"{src}/{dev}/{tag}";
        var valueType = ReadInt32Column(rdr, 3, "value_type", identity);
        var sequence = ReadInt64Column(rdr, 4, "route_buffer_sequence", identity);
        var manifestGeneration = ReadInt64Column(rdr, 5, "schema_generation", identity);
        var envelope = ReadRequiredBlob(rdr, 6, "envelope", identity);
        return new RawManifestRow(src, dev, tag, valueType, sequence, manifestGeneration, envelope);
    }

    /// <summary>Read a required TEXT column, failing closed on NULL or a non-text storage class.</summary>
    private static string ReadRequiredText(SqliteDataReader rdr, int ordinal, string column)
    {
        var value = rdr.GetValue(ordinal);
        if (value is not string text)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Manifest column '{column}' has storage class '{value.GetType().Name}', expected text.");
        }

        return text;
    }

    /// <summary>Read a required INTEGER column, failing closed on NULL or a non-integer storage class.</summary>
    private static long ReadInt64Column(SqliteDataReader rdr, int ordinal, string column, string identity)
    {
        var value = rdr.GetValue(ordinal);
        if (value is not long number)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Manifest row '{identity}' column '{column}' has storage class '{value.GetType().Name}', expected integer.");
        }

        return number;
    }

    /// <summary>Read an INTEGER column and narrow to <see cref="int"/> with a checked cast (out-of-range fails closed).</summary>
    private static int ReadInt32Column(SqliteDataReader rdr, int ordinal, string column, string identity)
    {
        var number = ReadInt64Column(rdr, ordinal, column, identity);
        try
        {
            return checked((int)number);
        }
        catch (OverflowException ex)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Manifest row '{identity}' column '{column}' value {number} is out of Int32 range.",
                ex);
        }
    }

    /// <summary>Read a required BLOB column as an owned <c>byte[]</c>, failing closed on NULL or a non-blob storage class.</summary>
    private static byte[] ReadRequiredBlob(SqliteDataReader rdr, int ordinal, string column, string identity)
    {
        var value = rdr.GetValue(ordinal);
        if (value is not byte[] bytes)
        {
            throw new BufferException(
                CoreErrors.BufferCorrupt,
                $"Manifest row '{identity}' column '{column}' has storage class '{value.GetType().Name}', expected blob.");
        }

        return bytes; // GetValue returns a fresh owned array for a BLOB — not an alias to a live DB buffer
    }

    // ------------------------------------------------------------------------
    // Reclaim loop
    // ------------------------------------------------------------------------

    private async Task ReclaimLoopAsync()
    {
        var ct = _reclaimCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Either the periodic delay or an ack/dereg signal wakes us.
                //
                // RACE FIX (D10): capture a strong local reference to the
                // signal. The previous code read `_reclaimSignal.Task` on a
                // separate line after the `??=` assignment, which allowed
                // this interleaving:
                //
                //   1. Reclaim loop: `_reclaimSignal ??= new TCS()` assigns
                //      a fresh TCS to the field.
                //   2. AckAsync (another thread): calls
                //      `Interlocked.Exchange(ref _reclaimSignal, null)` and
                //      completes the returned TCS.
                //   3. Reclaim loop: reads `_reclaimSignal.Task` → NRE,
                //      because the field is now null.
                //
                // The NRE used to escape the inner BufferException/
                // SqliteException catches and kill the whole reclaim loop
                // task silently. After that, _tail never advanced and the
                // buffer gradually stalled the entire pipeline (see
                // docs/phase1-exit/leak-harness-kickoff-summary.md §"4-hour
                // kickoff run — FAIL" for the observed behavior).
                //
                // Capturing `signal` via the return value of `??=` gives us
                // a local strong reference that Interlocked.Exchange cannot
                // invalidate. The outer catch below is also broadened so no
                // future unexpected exception can kill the loop silently.
                var signal = _reclaimSignal ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var signalTask = signal.Task;
                var delayTask = Task.Delay(_policy.ReclaimInterval, ct);
                await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await ReclaimOnceAsync(ct).ConfigureAwait(false);
                    if (_policy.MaxAge is { })
                    {
                        await RetentionSweepAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (BufferException)
                {
                    // Reclaim is best-effort. A transient SQLite error here
                    // (BUSY etc.) is logged via stats but does not crash the
                    // loop. Next tick will retry.
                }
                catch (SqliteException)
                {
                    // Same.
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // D10 DEFENSIVE CATCH: reclaim is load-bearing for _tail
                    // advancement and therefore for steady-state enqueue
                    // throughput. It MUST NEVER die silently on an unexpected
                    // exception. Skip this tick and continue; the next signal
                    // or delay will retry. The exception is captured in
                    // _lastReclaimError so operators can surface it via
                    // diagnostics if needed.
                    _lastReclaimError = ex;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal dispose path.
        }
    }

    private async Task ReclaimOnceAsync(CancellationToken cancellationToken)
    {
        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }
            ReclaimOnceLocked();
        }
        finally
        {
            _writerMutex.Release();
        }
        // Wake any blocked producers.
        var spaceWaiter = Interlocked.Exchange(ref _spaceWaiter, null);
        spaceWaiter?.TrySetResult(true);
    }

    private void ReclaimOnceLocked()
    {
        // When no sinks are registered, do NOT reclaim. Data must be
        // preserved for late-arriving sinks that need to replay the
        // backlog (see D12 reversal pin test
        // Register_NewSink_StartsAtTail_AndReplaysBacklog). The previous
        // code used defaultIfEmpty: _head which advanced _tail to _head
        // and deleted everything — a correctness bug exposed on slow
        // hardware where the reclaim loop fires between enqueue and
        // the first RegisterSinkAsync.
        var minCursor = _cursors.Min(defaultIfEmpty: _tail);
        if (minCursor <= _tail)
        {
            return;
        }
        var newTail = Math.Min(_head, minCursor);

        using var tx = _writer.BeginTransaction(deferred: false);
        try
        {
            using (var del = _writer.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM points WHERE sequence < $t;";
                del.Parameters.AddWithValue("$t", newTail);
                var deleted = del.ExecuteNonQuery();
                _totalDrained += deleted;
            }
            using (var upd = _writer.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE meta SET value = $v WHERE key = $k;";
                upd.Parameters.AddWithValue("$v", newTail.ToString(CultureInfo.InvariantCulture));
                upd.Parameters.AddWithValue("$k", SqliteBufferSchema.TailSequenceKey);
                upd.ExecuteNonQuery();
            }
            // The TailSequenceKey row may not exist yet on first run; ensure it.
            using (var upsert = _writer.CreateCommand())
            {
                upsert.Transaction = tx;
                upsert.CommandText =
                    "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO NOTHING;";
                upsert.Parameters.AddWithValue("$k", SqliteBufferSchema.TailSequenceKey);
                upsert.Parameters.AddWithValue("$v", newTail.ToString(CultureInfo.InvariantCulture));
                upsert.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (SqliteException ex)
        {
            tx.Rollback();
            throw TranslateSqliteException(ex, "ReclaimOnce");
        }

        _tail = newTail;
    }

    private async Task RetentionSweepAsync(CancellationToken cancellationToken)
    {
        if (_policy.MaxAge is not { } age)
        {
            return;
        }
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)age.TotalMilliseconds;

        await _writerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            // Find the lowest sequence that is NOT expired. Anything below
            // it can be retention-deleted, regardless of cursor position.
            long? newCursorFloor = null;
            using (var cmd = _writer.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT MIN(sequence) FROM points WHERE expires_at IS NULL OR expires_at >= $c;";
                cmd.Parameters.AddWithValue("$c", cutoff);
                var result = cmd.ExecuteScalar();
                if (result is not null && result is not DBNull)
                {
                    newCursorFloor = (long)result;
                }
            }

            // newCursorFloor == null means EVERY row is expired. We delete
            // everything; the floor becomes _head (sinks are caught up).
            var floor = newCursorFloor ?? _head;

            // Did anyone need fast-forwarding? (For the SinksFastForwarded counter.)
            var minBefore = _cursors.Min(defaultIfEmpty: floor);

            using var tx = _writer.BeginTransaction(deferred: false);
            try
            {
                // Fast-forward lagging cursors first (so the reclaim invariant
                // holds inside the same transaction).
                using (var bumpCursors = _writer.CreateCommand())
                {
                    bumpCursors.Transaction = tx;
                    bumpCursors.CommandText =
                        "UPDATE cursors SET next_unread = $f, updated_at = $t WHERE next_unread < $f;";
                    bumpCursors.Parameters.AddWithValue("$f", floor);
                    bumpCursors.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    bumpCursors.ExecuteNonQuery();
                }
                int deleted;
                using (var del = _writer.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText =
                        "DELETE FROM points WHERE expires_at IS NOT NULL AND expires_at < $c;";
                    del.Parameters.AddWithValue("$c", cutoff);
                    deleted = del.ExecuteNonQuery();
                }
                tx.Commit();

                if (deleted > 0)
                {
                    _totalDropped += deleted;
                    _droppedByRetention += deleted;
                }
            }
            catch (SqliteException ex)
            {
                tx.Rollback();
                throw TranslateSqliteException(ex, "RetentionSweep");
            }

            _cursors.FastForwardBelow(floor);
            if (minBefore < floor)
            {
                _sinksFastForwarded++;
            }
            if (floor > _tail)
            {
                _tail = floor;
            }
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    // ------------------------------------------------------------------------
    // Stats
    // ------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask<BufferStats> GetStatsAsync()
    {
        await _writerMutex.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            string? oldestSinkId = null;
            DateTime? oldestUnackedAt = null;
            DateTime? oldestMessageAt = null;
            long sizeBytes = 0;

            // Oldest message timestamp.
            if (_head > _tail)
            {
                using var cmd = _reader.CreateCommand();
                cmd.CommandText =
                    "SELECT enqueued_at FROM points WHERE sequence = $s;";
                cmd.Parameters.AddWithValue("$s", _tail);
                var result = cmd.ExecuteScalar();
                if (result is not null && result is not DBNull)
                {
                    oldestMessageAt = DateTimeOffset.FromUnixTimeMilliseconds((long)result).UtcDateTime;
                }
            }

            var minEntry = _cursors.MinEntry();
            if (minEntry is { } e && _head > _tail)
            {
                oldestSinkId = e.SinkId;
                var sinkSeq = Math.Max(e.Cursor, _tail);
                if (sinkSeq < _head)
                {
                    using var cmd = _reader.CreateCommand();
                    cmd.CommandText = "SELECT enqueued_at FROM points WHERE sequence = $s;";
                    cmd.Parameters.AddWithValue("$s", sinkSeq);
                    var result = cmd.ExecuteScalar();
                    if (result is not null && result is not DBNull)
                    {
                        oldestUnackedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)result).UtcDateTime;
                    }
                }
            }

            // Approximate size: 1 row at a time would be slow; use the SUM
            // of payload byte length. This is a single SELECT.
            using (var sizeCmd = _reader.CreateCommand())
            {
                sizeCmd.CommandText = "SELECT COALESCE(SUM(LENGTH(payload)), 0) FROM points;";
                sizeBytes = (long)sizeCmd.ExecuteScalar()!;
            }

            return new BufferStats
            {
                CurrentDepth = _head - _tail,
                TotalEnqueued = _totalEnqueued,
                TotalDrained = _totalDrained,
                TotalDropped = _totalDropped,
                DroppedByCapacity = _droppedByCapacity,
                DroppedByRetention = _droppedByRetention,
                SinksFastForwarded = _sinksFastForwarded,
                Quarantined = _totalQuarantined,
                OldestMessageAt = oldestMessageAt,
                OldestUnackedSinkId = oldestSinkId,
                OldestUnackedAt = oldestUnackedAt,
                SizeBytes = sizeBytes,
                RegisteredSinks = _cursors.Count,
            };
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    // ------------------------------------------------------------------------
    // Dispose
    // ------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Dispose is CLEANUP, NOT CORRECTNESS. Anything we do here is a
        // best-effort housekeeping pass. Correctness must hold even if this
        // method never runs (e.g., the process is killed).
        if (_disposed)
        {
            return;
        }

        _reclaimCts.Cancel();
        if (_reclaimLoopTask is not null)
        {
            try { await _reclaimLoopTask.ConfigureAwait(false); } catch { /* swallow */ }
        }

        await _writerMutex.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            // Best-effort WAL truncation. If this fails, the WAL is still
            // valid and the next open will roll forward normally.
            try
            {
                using var cmd = _writer.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Cleanup, not correctness.
            }

            _disposed = true;
        }
        finally
        {
            _writerMutex.Release();
        }

        // Cancel any waiters before tearing down connections.
        var space = Interlocked.Exchange(ref _spaceWaiter, null);
        space?.TrySetCanceled();
        var reclaimSignal = Interlocked.Exchange(ref _reclaimSignal, null);
        reclaimSignal?.TrySetResult(true);

        try { _writer.Dispose(); } catch { }
        try { _reader.Dispose(); } catch { }
        try { _ownershipLock.Dispose(); } catch { }   // release the exclusive ownership lock (K1.2a)
        _writerMutex.Dispose();
        _reclaimCts.Dispose();
    }

    // Throw with the PUBLIC façade type name (not this internal owner) so the
    // disposed-object identity is unchanged from the pre-K1.2a public surface.
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, typeof(SqliteBuffer));

    /// <summary>
    /// Acquire the exclusive ownership lock for the route database. Opens
    /// <c>&lt;filePath&gt;.lock</c> with <see cref="FileShare.None"/> so a second owner
    /// (this process or another) is refused. The OS releases the handle if the process
    /// dies, so a crashed owner does not permanently brick the route. Only a genuine
    /// sharing/lock conflict maps to <see cref="CoreErrors.RouteStoreAlreadyOwned"/>;
    /// any other I/O or permission failure on the lock file is a real
    /// <see cref="CoreErrors.BufferIoError"/>, not a false "already owned".
    /// </summary>
    private static FileStream AcquireOwnershipLock(string filePath)
    {
        var lockPath = filePath + ".lock";
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            throw new BufferException(
                CoreErrors.RouteStoreAlreadyOwned,
                $"Route store '{filePath}' is already owned by another store instance: {ex.Message}",
                ex);
        }
        catch (IOException ex)
        {
            // A non-sharing I/O failure on the lock file (disk full, bad path, ...) is
            // NOT an ownership collision.
            throw new BufferException(
                CoreErrors.BufferIoError,
                $"Failed to acquire the route-store ownership lock for '{filePath}': {ex.Message}",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            // A permission failure is not an ownership collision either.
            throw new BufferException(
                CoreErrors.BufferIoError,
                $"Access denied acquiring the route-store ownership lock for '{filePath}': {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// True when an <see cref="IOException"/> represents a file sharing/lock conflict
    /// (another live owner) rather than an unrelated I/O failure. Windows reports
    /// ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION; on Unix the check is best-effort
    /// (advisory-lock errnos) until Linux support is qualified.
    /// </summary>
    private static bool IsSharingViolation(IOException ex)
    {
        const int SharingViolation = unchecked((int)0x80070020); // ERROR_SHARING_VIOLATION (32)
        const int LockViolation = unchecked((int)0x80070021);    // ERROR_LOCK_VIOLATION (33)
        if (ex.HResult == SharingViolation || ex.HResult == LockViolation)
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        // Unix best-effort: EACCES(13) / EAGAIN(11) / EWOULDBLOCK(35) surface as the
        // errno in HResult when a FileShare.None open loses the advisory lock race.
        return ex.HResult is 11 or 13 or 35;
    }

    // ------------------------------------------------------------------------
    // Failure translation
    // ------------------------------------------------------------------------

    /// <summary>
    /// Map a raw <see cref="SqliteException"/> into a typed
    /// <see cref="BufferException"/>. The mapping is one of:
    /// <list type="bullet">
    /// <item>BUSY (after busy_timeout exhausted) → <c>BUFFER_IO_ERROR</c></item>
    /// <item>FULL → <c>BUFFER_IO_ERROR</c> with "disk full"</item>
    /// <item>CORRUPT → <c>BUFFER_CORRUPT</c></item>
    /// <item>everything else → <c>BUFFER_IO_ERROR</c></item>
    /// </list>
    /// There is NO outer retry loop. The caller decides whether to retry.
    /// </summary>
    private static BufferException TranslateSqliteException(SqliteException ex, string operation)
    {
        // SQLite primary error codes (subset).
        const int SQLITE_BUSY = 5;
        const int SQLITE_FULL = 13;
        const int SQLITE_CORRUPT = 11;
        const int SQLITE_NOTADB = 26;

        return ex.SqliteErrorCode switch
        {
            SQLITE_BUSY => new BufferException(
                CoreErrors.BufferIoError,
                $"{operation}: SQLite lock contention timeout (busy_timeout exhausted): {ex.Message}"),
            SQLITE_FULL => new BufferException(
                CoreErrors.BufferIoError,
                $"{operation}: disk full: {ex.Message}"),
            SQLITE_CORRUPT or SQLITE_NOTADB => new BufferException(
                CoreErrors.BufferCorrupt,
                $"{operation}: SQLite reports database corruption: {ex.Message}"),
            _ => new BufferException(
                CoreErrors.BufferIoError,
                $"{operation}: SQLite error {ex.SqliteErrorCode}: {ex.Message}"),
        };
    }
}
