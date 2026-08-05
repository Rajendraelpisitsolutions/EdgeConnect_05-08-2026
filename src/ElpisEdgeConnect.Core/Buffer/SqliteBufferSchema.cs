// ============================================================================
// File: Buffer/SqliteBufferSchema.cs
// Purpose: Schema DDL, PRAGMAs, and schema-version constant for SqliteBuffer.
//          Kept separate from SqliteBuffer.cs so the storage layout is
//          easy to read and audit.
// Reference: docs/core/buffer-durability.md §3
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Schema constants and DDL for the C2b durable buffer.
/// </summary>
internal static class SqliteBufferSchema
{
    /// <summary>
    /// The current schema version this binary knows how to read/write. v2 (K1.2b) adds
    /// the <c>latest_value</c> manifest table and the replay meta keys additively; a v1
    /// file is upgraded in place (its <c>points</c>/<c>cursors</c> are preserved).
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The first schema version (pre-K1.2b). Upgraded to <see cref="CurrentSchemaVersion"/> on open.</summary>
    public const int SchemaVersionV1 = 1;

    /// <summary>Key under which the schema version is persisted in the <c>meta</c> table.</summary>
    public const string SchemaVersionKey = "schema_version";

    /// <summary>Key under which the durable tail sequence is persisted (advisory).</summary>
    public const string TailSequenceKey = "tail_sequence";

    /// <summary>
    /// Key under which the authoritative monotonic append head is persisted (K1.2b).
    /// Maintained only when replay-state tracking is enabled; when absent, head recovery
    /// falls back to <c>MAX(sequence)+1</c>, the cursor high-water, and
    /// <see cref="TailSequenceKey"/>. Reading it as a recovery floor is always safe.
    /// </summary>
    public const string NextSequenceKey = "next_sequence";

    /// <summary>Key under which the owning route id is persisted (validated on open). K1.2b.</summary>
    public const string RouteIdKey = "route_id";

    /// <summary>Key under which the current route-schema generation is persisted. K1.2b.</summary>
    public const string SchemaGenerationKey = "current_schema_generation";

    /// <summary>Key under which the replay sink id is persisted at activation (validated on re-activation). K1.2b.</summary>
    public const string ReplaySinkIdKey = "replay_sink_id";

    /// <summary>
    /// Key under which the replay-state-tracking flag is persisted (<c>"disabled"</c> /
    /// <c>"enabled"</c>). One-way disabled → enabled. K1.2b.
    /// </summary>
    public const string ReplayStateTrackingKey = "replay_state_tracking";

    /// <summary>Persisted value of <see cref="ReplayStateTrackingKey"/> when tracking is off (default).</summary>
    public const string ReplayTrackingDisabled = "disabled";

    /// <summary>Persisted value of <see cref="ReplayStateTrackingKey"/> when tracking is on.</summary>
    public const string ReplayTrackingEnabled = "enabled";

    /// <summary>
    /// PRAGMAs applied on every connection open. Order matters:
    /// <c>journal_mode=WAL</c> must be set before <c>synchronous</c> for
    /// <c>synchronous=FULL</c> to take effect against the WAL.
    /// </summary>
    public static readonly IReadOnlyList<string> WriterConnectionPragmas = new[]
    {
        "PRAGMA journal_mode = WAL;",
        "PRAGMA synchronous = FULL;",       // C2b approved default — see buffer-durability.md §3
        "PRAGMA busy_timeout = 5000;",      // 5 s; the ONLY retry layer (no outer loop)
        "PRAGMA temp_store = MEMORY;",
        "PRAGMA cache_size = -2000;",       // ~2 MB
        "PRAGMA wal_autocheckpoint = 1000;",
        "PRAGMA foreign_keys = OFF;",
    };

    /// <summary>
    /// PRAGMAs applied to the read-only connection. Mostly the same as the
    /// writer except <c>synchronous</c> is irrelevant for read-only access.
    /// </summary>
    public static readonly IReadOnlyList<string> ReaderConnectionPragmas = new[]
    {
        "PRAGMA journal_mode = WAL;",
        "PRAGMA busy_timeout = 5000;",
        "PRAGMA temp_store = MEMORY;",
        "PRAGMA cache_size = -2000;",
    };

    /// <summary>
    /// v2 (K1.2b) latest-value observed manifest DDL. Created additively; populated in
    /// K1.2c (typed envelope codec + atomic append+upsert). The per-route database holds
    /// the route id once in meta, so the physical key omits it. Kept as its own constant
    /// so a v1→v2 migration can create ONLY this v2 addition without touching v1 tables.
    /// </summary>
    public const string LatestValueTableDdl =
        @"CREATE TABLE IF NOT EXISTS latest_value (
              source_instance_id    TEXT    NOT NULL,
              device_id             TEXT    NOT NULL,
              tag_path              TEXT    NOT NULL,
              value_type            INTEGER NOT NULL,
              route_buffer_sequence INTEGER NOT NULL,
              schema_generation     INTEGER NOT NULL,
              envelope              BLOB    NOT NULL,
              updated_at            INTEGER NOT NULL,
              PRIMARY KEY (source_instance_id, device_id, tag_path)
          );";

    /// <summary>The DDL statements that create the complete v2 schema (used only for a FRESH file).</summary>
    public static readonly IReadOnlyList<string> CreateStatements = new[]
    {
        // Durable backing store for enqueued points.
        @"CREATE TABLE IF NOT EXISTS points (
              sequence    INTEGER PRIMARY KEY,
              payload     BLOB    NOT NULL,
              enqueued_at INTEGER NOT NULL,
              expires_at  INTEGER
          );",

        // Per-sink durable cursor table.
        @"CREATE TABLE IF NOT EXISTS cursors (
              sink_id      TEXT PRIMARY KEY,
              next_unread  INTEGER NOT NULL,
              updated_at   INTEGER NOT NULL
          );",

        // Single-row buffer-level metadata (schema version + advisory tail).
        @"CREATE TABLE IF NOT EXISTS meta (
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL
          );",

        LatestValueTableDdl,

        // Partial index accelerates age-based retention sweeps without
        // bloating when most rows have no expires_at.
        @"CREATE INDEX IF NOT EXISTS idx_points_expires_at
              ON points (expires_at) WHERE expires_at IS NOT NULL;",
    };

    /// <summary>The v2-only additions applied when upgrading a validated v1 file (never recreates v1 core tables).</summary>
    public static readonly IReadOnlyList<string> V2AdditionStatements = new[]
    {
        LatestValueTableDdl,
    };
}
