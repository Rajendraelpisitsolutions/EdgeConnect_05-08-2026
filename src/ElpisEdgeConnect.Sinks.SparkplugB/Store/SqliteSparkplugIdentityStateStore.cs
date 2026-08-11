// ============================================================================
// File: Store/SqliteSparkplugIdentityStateStore.cs
// Purpose: The production SQLite Sparkplug identity store (K3 slice 2, review r2).
//          Hardened persistence boundaries:
//            R1 - bdSeq/alias reads validate the SQLite STORAGE CLASS (typeof =
//                 'integer') as well as the value/range, so a fractional REAL or
//                 numeric-looking TEXT/BLOB is rejected, not coerced; a malformed
//                 persisted alias-key component is wrapped as store corruption (not
//                 IDENTITY_INVALID); the wire bdSeq is built before the write.
//            R2 - existing-schema validation compares each table's stored CREATE
//                 text against the canonical fingerprint (columns, types, NOT NULL,
//                 COLLATE BINARY, CHECK, PK, UNIQUE) and requires the exact table set
//                 and version == CurrentVersion; missing/malformed/future/damaged
//                 fails readiness. WAL is verified (not assumed) on open.
//          A test-only constructor can skip existing-schema validation to exercise
//          the runtime read-validation over a deliberately damaged schema.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §5.5-§5.7;
//            k0-evidence-bundle.md (WS5); src/ElpisEdgeConnect.Core/Buffer/SqliteRouteStore.cs.
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using Microsoft.Data.Sqlite;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Store;

/// <summary>
/// SQLite-backed <see cref="ISparkplugIdentityStateStore"/>. Constructed with an
/// absolute database path (K4 resolves the gateway data root). Thread-safe via the
/// SQLite write lock; operations open pooled connections per call.
/// </summary>
public sealed class SqliteSparkplugIdentityStateStore : ISparkplugIdentityStateStore
{
    private const int BdSeqWireModulus = 256;
    private const int SqliteConstraint = 19; // SQLITE_CONSTRAINT

    private readonly string _connectionString;

    /// <summary>Test-only fault injected AFTER the bdSeq write but BEFORE COMMIT.</summary>
    internal Action? PreCommitFault { get; set; }

    /// <summary>Test-only fault injected AFTER all alias inserts but BEFORE COMMIT.</summary>
    internal Action? AliasPreCommitFault { get; set; }

    /// <summary>Open (creating if needed) the identity store at an absolute path.</summary>
    /// <param name="databasePath">The absolute database file path.</param>
    /// <exception cref="ArgumentException">Thrown when the path is empty or not fully qualified.</exception>
    /// <exception cref="AdapterException">Thrown fail-closed on corruption/inaccessibility/unsupported schema.</exception>
    public SqliteSparkplugIdentityStateStore(string databasePath)
        : this(databasePath, validateExistingSchema: true)
    {
    }

    // Test seam: bypass existing-schema fingerprint validation to exercise the
    // runtime read-validation over a deliberately damaged (but still openable) schema.
    internal SqliteSparkplugIdentityStateStore(string databasePath, bool validateExistingSchema)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        if (!Path.IsPathFullyQualified(databasePath))
        {
            throw new ArgumentException(
                $"The Sparkplug identity store path must be absolute (was '{databasePath}'); " +
                "a relative path would root gateway identity state in the process working directory.",
                nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw Unavailable($"cannot create the identity-store directory '{directory}': {ex.Message}", ex);
            }
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        Guard<object?>(() =>
        {
            using var conn = Open();
            EnsureIntegrity(conn);
            MigrateSchema(conn, validateExistingSchema);
            return null;
        });
    }

    /// <inheritdoc/>
    public SparkplugBirthDeathSequence ReserveNextBdSeq(SparkplugStoreIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return Guard(() =>
        {
            using var conn = Open();
            Execute(conn, "BEGIN IMMEDIATE;");
            try
            {
                var persisted = ReadBdSeqCounter(conn, identity);

                long nextCounter;
                if (persisted is null)
                {
                    nextCounter = 0L;
                }
                else if (persisted.Value < 0L || persisted.Value == long.MaxValue)
                {
                    throw Unavailable(
                        $"persisted bdSeq counter is out of range ({persisted.Value}); refusing to reset to 0.");
                }
                else
                {
                    nextCounter = persisted.Value + 1L;
                }

                // Build the wire value BEFORE the write/commit.
                var wireValue = SparkplugBirthDeathSequence.Create((int)(nextCounter % BdSeqWireModulus));

                WriteBdSeqCounter(conn, identity, nextCounter);
                PreCommitFault?.Invoke();
                Execute(conn, "COMMIT;");
                return wireValue;
            }
            catch
            {
                TryRollback(conn);
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<SparkplugAliasKey, ulong> ResolveAliases(
        SparkplugStoreIdentity identity, IReadOnlyCollection<SparkplugAliasKey> manifest)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(manifest);

        var requested = manifest.Distinct().ToList();
        if (requested.Count == 0)
        {
            return FrozenDictionary<SparkplugAliasKey, ulong>.Empty;
        }

        return Guard(() =>
        {
            using var conn = Open();
            Execute(conn, "BEGIN IMMEDIATE;");
            try
            {
                var resolved = ReadAliases(conn, identity);
                var maxAlias = resolved.Count == 0 ? 0UL : resolved.Values.Max();

                var missing = requested
                    .Where(k => !resolved.ContainsKey(k))
                    .OrderBy(k => k.SourceInstanceId, StringComparer.Ordinal)
                    .ThenBy(k => k.DeviceId, StringComparer.Ordinal)
                    .ThenBy(k => k.TagPath, StringComparer.Ordinal);

                foreach (var key in missing)
                {
                    var alias = checked(maxAlias + 1UL); // alias 0 reserved; app aliases begin at 1
                    maxAlias = alias;
                    InsertAlias(conn, identity, key, alias);
                    resolved[key] = alias;
                }

                AliasPreCommitFault?.Invoke();
                Execute(conn, "COMMIT;");

                return (IReadOnlyDictionary<SparkplugAliasKey, ulong>)
                    requested.ToDictionary(k => k, k => resolved[k]).ToFrozenDictionary();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraint)
            {
                TryRollback(conn);
                throw new AdapterException(new AdapterError
                {
                    Code = SparkplugErrors.AliasAllocationConflict,
                    Category = ErrorCategory.Internal,
                    Message = $"Sparkplug alias allocation violated a uniqueness/range invariant: {ex.Message}",
                    Retryable = false,
                }, ex);
            }
            catch
            {
                TryRollback(conn);
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        using var conn = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(conn);
    }

    /// <summary>Test hook: the persisted monotonic counter for an identity (null if none).</summary>
    internal long? PeekBdSeqCounter(SparkplugStoreIdentity identity) =>
        Guard(() =>
        {
            using var conn = Open();
            return ReadBdSeqCounter(conn, identity);
        });

    /// <summary>Test hook: the effective journal mode, synchronous level, and busy timeout.</summary>
    internal (string JournalMode, long Synchronous, long BusyTimeout) ReadEffectivePragmas() =>
        Guard(() =>
        {
            using var conn = Open();
            var journal = (string)ExecuteScalar(conn, "PRAGMA journal_mode;")!;
            var synchronous = (long)ExecuteScalar(conn, "PRAGMA synchronous;")!;
            var busy = (long)ExecuteScalar(conn, "PRAGMA busy_timeout;")!;
            return (journal, synchronous, busy);
        });

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // journal_mode returns the mode SQLite actually retained — verify, don't assume.
        var journal = ExecuteScalar(conn, "PRAGMA journal_mode = WAL;") as string;
        if (!string.Equals(journal, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw Unavailable($"could not establish WAL journal mode (effective: '{journal ?? "null"}').");
        }

        Execute(conn, "PRAGMA synchronous = FULL;");
        Execute(conn, "PRAGMA busy_timeout = 5000;");
        Execute(conn, "PRAGMA foreign_keys = OFF;");
        return conn;
    }

    private static void EnsureIntegrity(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check;";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || !string.Equals(reader.GetString(0), "ok", StringComparison.Ordinal))
        {
            throw Unavailable("PRAGMA quick_check did not return 'ok' (the database is corrupt).");
        }
    }

    // ----- Schema classification (no DDL before it is decided) -----

    private static void MigrateSchema(SqliteConnection conn, bool validateExistingSchema)
    {
        if (ClassifyFresh(conn))
        {
            ApplyDdlAndStamp(conn);
        }
        else if (validateExistingSchema)
        {
            ValidateSchema(conn);
        }
    }

    private static bool ClassifyFresh(SqliteConnection conn)
    {
        // Consider EVERY non-internal object (table/view/trigger/explicit index), not just
        // tables — a file with an unrelated view/trigger is NOT a clean fresh store, and a
        // subversive trigger must not survive readiness (slice-2 review r3).
        var objects = ListUserObjects(conn);
        if (objects.Count == 0)
        {
            return true;
        }

        if (!objects.Any(o => o is { Type: "table", Name: "meta" }))
        {
            throw Unavailable(
                $"file is not a clean Sparkplug identity store: it has objects ({DescribeObjects(objects)}) but no meta table.");
        }

        var version = ReadMetaValue(conn, SparkplugIdentityStateStoreSchema.SchemaVersionKey)
            ?? throw Unavailable("identity store meta has no schema_version row.");
        if (!int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
        {
            throw Unavailable($"identity store schema version is malformed: '{version}'.");
        }

        // Until migrations exist, the ONLY supported version is CurrentVersion.
        if (parsed != SparkplugIdentityStateStoreSchema.CurrentVersion)
        {
            throw new AdapterException(new AdapterError
            {
                Code = SparkplugErrors.IdentityStoreSchemaUnsupported,
                Category = ErrorCategory.Configuration,
                Message = $"identity store schema version {parsed} is not supported by this build " +
                          $"(requires exactly {SparkplugIdentityStateStoreSchema.CurrentVersion}).",
                Retryable = false,
            });
        }

        return false;
    }

    private static void ApplyDdlAndStamp(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction(deferred: false);
        try
        {
            foreach (var ddl in SparkplugIdentityStateStoreSchema.CreateStatements)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = ddl;
                cmd.ExecuteNonQuery();
            }

            using (var stamp = conn.CreateCommand())
            {
                stamp.Transaction = tx;
                stamp.CommandText = "INSERT INTO meta (key, value) VALUES ($k, $v);";
                stamp.Parameters.AddWithValue("$k", SparkplugIdentityStateStoreSchema.SchemaVersionKey);
                stamp.Parameters.AddWithValue(
                    "$v", SparkplugIdentityStateStoreSchema.CurrentVersion.ToString(CultureInfo.InvariantCulture));
                stamp.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // Validate the COMPLETE v1 object set: exactly the meta/bdseq/alias tables and nothing
    // else — any extra table, view, trigger, or explicit user index is rejected (a trigger
    // could subvert durability without altering any table DDL). Then compare each table's
    // stored CREATE text against the canonical fingerprint (columns, types, NOT NULL,
    // COLLATE BINARY, CHECK, PK, UNIQUE). Never any DDL; fail closed on any gap.
    private static void ValidateSchema(SqliteConnection conn)
    {
        var actual = ListUserObjects(conn).Select(o => (o.Type, o.Name)).ToHashSet();
        var expected = SparkplugIdentityStateStoreSchema.Tables
            .Select(t => ("table", t)).ToHashSet();
        if (!actual.SetEquals(expected))
        {
            throw Unavailable(
                $"identity store has an unexpected object set ({DescribeObjects(ListUserObjects(conn))}); " +
                "expected exactly the meta, bdseq, and alias tables.");
        }

        foreach (var table in SparkplugIdentityStateStoreSchema.Tables)
        {
            var sql = ReadTableSql(conn, table)
                ?? throw Unavailable($"identity store is missing required table '{table}'.");
            if (!SchemaTextMatches(sql, SparkplugIdentityStateStoreSchema.DdlFor(table)))
            {
                throw Unavailable(
                    $"identity store table '{table}' does not match the expected v{SparkplugIdentityStateStoreSchema.CurrentVersion} schema (damaged or altered).");
            }
        }
    }

    private static bool SchemaTextMatches(string actual, string expected) =>
        string.Equals(NormalizeSql(actual), NormalizeSql(expected), StringComparison.Ordinal);

    // Collapse insignificant whitespace ONLY — never lower-case, because quoted literals are
    // case-significant: typeof(...) = 'integer' and 'INTEGER' behave differently (SQLite
    // returns lower-case storage-class names), so a lower-casing compare would accept a
    // broken schema (slice-2 review r3).
    private static string NormalizeSql(string sql) =>
        Regex.Replace(sql, @"\s+", " ").Trim().TrimEnd(';').Trim();

    // ----- Row access -----

    private static long? ReadBdSeqCounter(SqliteConnection conn, SparkplugStoreIdentity identity)
    {
        using var select = conn.CreateCommand();
        select.CommandText =
            "SELECT typeof(last_counter), last_counter FROM bdseq WHERE broker = $b AND grp = $g AND node = $n;";
        BindIdentity(select, identity);
        using var reader = select.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var storageClass = reader.GetString(0);
        if (!string.Equals(storageClass, "integer", StringComparison.Ordinal))
        {
            throw Unavailable($"persisted bdSeq counter has SQLite storage class '{storageClass}', expected 'integer'.");
        }

        return reader.GetInt64(1);
    }

    private static void WriteBdSeqCounter(SqliteConnection conn, SparkplugStoreIdentity identity, long counter)
    {
        using var upsert = conn.CreateCommand();
        upsert.CommandText =
            "INSERT INTO bdseq (broker, grp, node, last_counter) VALUES ($b, $g, $n, $v) " +
            "ON CONFLICT(broker, grp, node) DO UPDATE SET last_counter = $v;";
        BindIdentity(upsert, identity);
        upsert.Parameters.AddWithValue("$v", counter);
        upsert.ExecuteNonQuery();
    }

    private static Dictionary<SparkplugAliasKey, ulong> ReadAliases(
        SqliteConnection conn, SparkplugStoreIdentity identity)
    {
        var map = new Dictionary<SparkplugAliasKey, ulong>();
        var seenAliases = new HashSet<ulong>();
        using var select = conn.CreateCommand();
        select.CommandText =
            "SELECT source, device, tagpath, typeof(alias), alias FROM alias WHERE broker = $b AND grp = $g AND node = $n;";
        BindIdentity(select, identity);
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            var storageClass = reader.GetString(3);
            if (!string.Equals(storageClass, "integer", StringComparison.Ordinal))
            {
                throw Unavailable($"persisted alias has SQLite storage class '{storageClass}', expected 'integer'.");
            }

            var raw = reader.GetInt64(4);
            if (raw < 1L) // rejects 0 and negatives (a negative would become a huge ulong)
            {
                throw Unavailable($"identity store holds an invalid alias value {raw} (must be >= 1).");
            }

            SparkplugAliasKey key;
            try
            {
                key = SparkplugAliasKey.Create(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            }
            catch (AdapterException ex)
            {
                // A malformed persisted canonical key is store corruption — surface it as such,
                // not as the encoder's IDENTITY_INVALID.
                throw Unavailable($"identity store holds a malformed alias-key component: {ex.Message}", ex);
            }

            if (!map.TryAdd(key, (ulong)raw))
            {
                throw Unavailable($"identity store holds a duplicate alias mapping for metric '{key.MetricName}'.");
            }

            if (!seenAliases.Add((ulong)raw))
            {
                throw Unavailable($"identity store holds a duplicate alias value {raw} for the Edge Node.");
            }
        }

        return map;
    }

    private static void InsertAlias(
        SqliteConnection conn, SparkplugStoreIdentity identity, SparkplugAliasKey key, ulong alias)
    {
        using var insert = conn.CreateCommand();
        insert.CommandText =
            "INSERT INTO alias (broker, grp, node, source, device, tagpath, alias) " +
            "VALUES ($b, $g, $n, $s, $d, $t, $a);";
        BindIdentity(insert, identity);
        insert.Parameters.AddWithValue("$s", key.SourceInstanceId);
        insert.Parameters.AddWithValue("$d", key.DeviceId);
        insert.Parameters.AddWithValue("$t", key.TagPath);
        insert.Parameters.AddWithValue("$a", checked((long)alias)); // reject a value crossing the signed boundary
        insert.ExecuteNonQuery();
    }

    private static void BindIdentity(SqliteCommand command, SparkplugStoreIdentity identity)
    {
        command.Parameters.AddWithValue("$b", identity.BrokerKey);
        command.Parameters.AddWithValue("$g", identity.Node.GroupId);
        command.Parameters.AddWithValue("$n", identity.Node.EdgeNodeId);
    }

    // ----- SQLite helpers -----

    private static string? ReadMetaValue(SqliteConnection conn, string key)
    {
        using var select = conn.CreateCommand();
        select.CommandText = "SELECT value FROM meta WHERE key = $k;";
        select.Parameters.AddWithValue("$k", key);
        return select.ExecuteScalar() as string;
    }

    private static string? ReadTableSql(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $t;";
        cmd.Parameters.AddWithValue("$t", table);
        return cmd.ExecuteScalar() as string;
    }

    // Every non-internal schema object (only SQLite's actual reserved 'sqlite_' prefix —
    // incl. the PK/UNIQUE auto-indexes — is excluded). A user index/view/trigger appears
    // here and is rejected. NOTE: a literal-prefix test, NOT `NOT LIKE 'sqlite_%'`, because
    // '_' is a single-char LIKE wildcard — that pattern would also hide a legal lookalike
    // like a `sqliteXdelete` trigger, re-opening the durability-subversion vector (r4).
    private static List<(string Type, string Name)> ListUserObjects(SqliteConnection conn)
    {
        var objects = new List<(string, string)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT type, name FROM sqlite_master WHERE lower(substr(name, 1, 7)) <> 'sqlite_';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            objects.Add((reader.GetString(0), reader.GetString(1)));
        }

        return objects;
    }

    private static string DescribeObjects(IEnumerable<(string Type, string Name)> objects) =>
        string.Join(", ", objects.Select(o => $"{o.Type} {o.Name}"));

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var command = conn.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection conn, string sql)
    {
        using var command = conn.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void TryRollback(SqliteConnection conn)
    {
        try { Execute(conn, "ROLLBACK;"); }
        catch { /* the connection may already be unusable — nothing was committed */ }
    }

    private static AdapterException Unavailable(string detail, Exception? inner = null)
    {
        var error = new AdapterError
        {
            Code = SparkplugErrors.IdentityStoreUnavailable,
            Category = ErrorCategory.Internal,
            Message = $"Sparkplug identity store is unavailable or corrupt: {detail}",
            Retryable = false,
        };
        return inner is null ? new AdapterException(error) : new AdapterException(error, inner);
    }

    private static T Guard<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (AdapterException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqliteException or OverflowException or FormatException or InvalidCastException)
        {
            throw Unavailable(ex.Message, ex);
        }
    }
}
