// ============================================================================
// File: Store/SparkplugIdentityStateStoreSchema.cs
// Purpose: The versioned SQLite schema for the Sparkplug identity store. Identity
//          columns use explicit COLLATE BINARY (ordinal, case-sensitive per plan
//          v3 §5.7); the alias canonical key is three SEPARATE columns; and CHECK
//          constraints pin BOTH the SQLite storage class AND the range of the
//          numeric invariants — typeof(...) = 'integer' AND range (slice-2 review
//          r2 R1) — so a fractional REAL or numeric-looking TEXT can never be
//          persisted through a valid schema. Existing-schema validation compares
//          each table's stored CREATE text against these canonical statements, so
//          a lost CHECK / UNIQUE / NOT NULL / collation fails readiness (review R2).
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §5.5-§5.7.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Store;

/// <summary>The versioned schema DDL for the Sparkplug identity store.</summary>
internal static class SparkplugIdentityStateStoreSchema
{
    /// <summary>The schema version this build creates and understands.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The meta key under which the schema version is stored.</summary>
    public const string SchemaVersionKey = "schema_version";

    /// <summary>Canonical CREATE for the meta table (also the validation fingerprint).</summary>
    public const string MetaDdl =
        "CREATE TABLE meta (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL)";

    /// <summary>Canonical CREATE for the bdseq table (also the validation fingerprint).</summary>
    public const string BdSeqDdl =
        "CREATE TABLE bdseq (" +
        "broker TEXT NOT NULL COLLATE BINARY, " +
        "grp TEXT NOT NULL COLLATE BINARY, " +
        "node TEXT NOT NULL COLLATE BINARY, " +
        "last_counter INTEGER NOT NULL CHECK (typeof(last_counter) = 'integer' AND last_counter >= 0), " +
        "PRIMARY KEY (broker, grp, node))";

    /// <summary>Canonical CREATE for the alias table (also the validation fingerprint).</summary>
    public const string AliasDdl =
        "CREATE TABLE alias (" +
        "broker TEXT NOT NULL COLLATE BINARY, " +
        "grp TEXT NOT NULL COLLATE BINARY, " +
        "node TEXT NOT NULL COLLATE BINARY, " +
        "source TEXT NOT NULL COLLATE BINARY, " +
        "device TEXT NOT NULL COLLATE BINARY, " +
        "tagpath TEXT NOT NULL COLLATE BINARY, " +
        "alias INTEGER NOT NULL CHECK (typeof(alias) = 'integer' AND alias >= 1), " +
        "PRIMARY KEY (broker, grp, node, source, device, tagpath), " +
        "UNIQUE (broker, grp, node, alias))";

    /// <summary>The exact application table set of a valid v1 store.</summary>
    public static readonly IReadOnlyList<string> Tables = new[] { "meta", "bdseq", "alias" };

    /// <summary>The complete DDL, applied transactionally ONLY to a fresh database.</summary>
    public static readonly IReadOnlyList<string> CreateStatements = new[] { MetaDdl, BdSeqDdl, AliasDdl };

    /// <summary>Map a table name to its canonical CREATE text (the validation fingerprint).</summary>
    public static string DdlFor(string table) => table switch
    {
        "meta" => MetaDdl,
        "bdseq" => BdSeqDdl,
        "alias" => AliasDdl,
        _ => string.Empty,
    };
}
