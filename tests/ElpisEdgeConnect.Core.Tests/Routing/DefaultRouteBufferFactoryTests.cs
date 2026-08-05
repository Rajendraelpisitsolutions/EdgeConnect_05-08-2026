// ============================================================================
// File: Routing/DefaultRouteBufferFactoryTests.cs
// Purpose: Pin Chip 4 (Bug 1 P3) — buffer-path realignment + the
//          legacy-path migration shim. Pre-Chip-4 deployments stored
//          buffers under {dataRoot}/config/buffer/; the factory now
//          uses the canonical {dataRoot}/buffer/ path AND moves any
//          pre-existing .db + .db-shm + .db-wal triplet on first
//          open so existing store-and-forward backlog is preserved.
// Reference: docs/sessions/2026-05-21-phase2-wrapup-roadmap-v2.md §3.1
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class DefaultRouteBufferFactoryTests : IDisposable
{
    private readonly string _tempDataRoot;

    public DefaultRouteBufferFactoryTests()
    {
        _tempDataRoot = Path.Combine(
            Path.GetTempPath(), "edgeconnect-buffer-factory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDataRoot, recursive: true); } catch { /* best-effort */ }
    }

    // ── Migration shim — happy path ──────────────────────────────────────

    [Fact]
    public async Task CreateAsync_LegacyDbAlone_MovedToCanonicalBuffer()
    {
        // Pre-place a legacy .db at the buggy path (mimics pre-Chip-4 state).
        var legacyDir = Path.Combine(_tempDataRoot, "config", "buffer");
        Directory.CreateDirectory(legacyDir);
        var legacyDb = Path.Combine(legacyDir, "route-A.db");
        CreateEmptySqliteFile(legacyDb);

        var factory = new DefaultRouteBufferFactory(_tempDataRoot);
        var buffer = await factory.CreateAsync(
            "route-A", new BufferPolicy { Mode = BufferMode.StoreAndForward, MaxDepth = 1000, DropPolicy = DropPolicy.DropOldest },
            CancellationToken.None);

        // After CreateAsync, the canonical path has the .db; legacy is gone.
        var canonicalDb = Path.Combine(_tempDataRoot, "buffer", "route-A.db");
        File.Exists(canonicalDb).Should().BeTrue("buffer must live at the canonical path");
        File.Exists(legacyDb).Should().BeFalse("legacy path must be empty after migration");

        if (buffer is IAsyncDisposable ad) await ad.DisposeAsync();
    }

    [Fact]
    public async Task CreateAsync_LegacyTripletDbShmWal_AllThreeMoveTogether()
    {
        // SQLite stores WAL state in .db-shm + .db-wal alongside .db.
        // Moving .db alone while siblings stay behind produces undetectable
        // corruption. Pin: all three migrate together.
        var legacyDir = Path.Combine(_tempDataRoot, "config", "buffer");
        Directory.CreateDirectory(legacyDir);
        var legacyDb = Path.Combine(legacyDir, "route-B.db");
        var legacyShm = Path.Combine(legacyDir, "route-B.db-shm");
        var legacyWal = Path.Combine(legacyDir, "route-B.db-wal");
        CreateEmptySqliteFile(legacyDb);
        await File.WriteAllTextAsync(legacyShm, "shm-content").ConfigureAwait(true);
        await File.WriteAllTextAsync(legacyWal, "wal-content").ConfigureAwait(true);

        var factory = new DefaultRouteBufferFactory(_tempDataRoot);
        var buffer = await factory.CreateAsync(
            "route-B", new BufferPolicy { Mode = BufferMode.StoreAndForward, MaxDepth = 1000, DropPolicy = DropPolicy.DropOldest },
            CancellationToken.None);

        var canonicalDir = Path.Combine(_tempDataRoot, "buffer");
        File.Exists(Path.Combine(canonicalDir, "route-B.db")).Should().BeTrue();
        // Note: .db-shm + .db-wal may be removed by SqliteBuffer.OpenAsync if
        // it consolidates the WAL. We only assert they're NOT at the legacy
        // path anymore — the migration moved them. Whether they exist at
        // the canonical path after Open is SQLite-internal behaviour we
        // don't pin here (would be brittle).
        File.Exists(legacyShm).Should().BeFalse("legacy .db-shm must not remain after migration");
        File.Exists(legacyWal).Should().BeFalse("legacy .db-wal must not remain after migration");
        File.Exists(legacyDb).Should().BeFalse();

        if (buffer is IAsyncDisposable ad) await ad.DisposeAsync();
    }

    // ── Migration shim — guard cases ─────────────────────────────────────

    [Fact]
    public async Task CreateAsync_BufferAlreadyAtCanonicalPath_LegacyFileNotTouched()
    {
        // If both paths exist (operator manually copied? race?), don't
        // overwrite the canonical with stale legacy data. Canonical wins.
        var legacyDir = Path.Combine(_tempDataRoot, "config", "buffer");
        var canonicalDir = Path.Combine(_tempDataRoot, "buffer");
        Directory.CreateDirectory(legacyDir);
        Directory.CreateDirectory(canonicalDir);
        var legacyDb = Path.Combine(legacyDir, "route-C.db");
        var canonicalDb = Path.Combine(canonicalDir, "route-C.db");
        CreateEmptySqliteFile(legacyDb);
        CreateEmptySqliteFile(canonicalDb);

        var factory = new DefaultRouteBufferFactory(_tempDataRoot);
        var buffer = await factory.CreateAsync(
            "route-C", new BufferPolicy { Mode = BufferMode.StoreAndForward, MaxDepth = 1000, DropPolicy = DropPolicy.DropOldest },
            CancellationToken.None);

        File.Exists(canonicalDb).Should().BeTrue();
        File.Exists(legacyDb).Should().BeTrue("legacy file must NOT be moved when canonical already exists");

        if (buffer is IAsyncDisposable ad) await ad.DisposeAsync();
    }

    [Fact]
    public async Task CreateAsync_NoLegacyFile_NormalNewBufferCreated()
    {
        // Fresh deployment (no legacy buffer anywhere) — the factory creates
        // a new buffer at the canonical path; no migration message logged.
        var factory = new DefaultRouteBufferFactory(_tempDataRoot);
        var buffer = await factory.CreateAsync(
            "route-D", new BufferPolicy { Mode = BufferMode.StoreAndForward, MaxDepth = 1000, DropPolicy = DropPolicy.DropOldest },
            CancellationToken.None);

        var canonicalDb = Path.Combine(_tempDataRoot, "buffer", "route-D.db");
        File.Exists(canonicalDb).Should().BeTrue();

        if (buffer is IAsyncDisposable ad) await ad.DisposeAsync();
    }

    [Fact]
    public async Task CreateAsync_InMemoryMode_NoMigrationAttempted()
    {
        // InMemory routes don't have files; ensure the migration shim
        // isn't invoked for them (it would no-op anyway but should never
        // touch the filesystem under InMemory mode).
        var legacyDir = Path.Combine(_tempDataRoot, "config", "buffer");
        Directory.CreateDirectory(legacyDir);
        var legacyDb = Path.Combine(legacyDir, "route-E.db");
        CreateEmptySqliteFile(legacyDb);

        var factory = new DefaultRouteBufferFactory(_tempDataRoot);
        var buffer = await factory.CreateAsync(
            "route-E", new BufferPolicy { Mode = BufferMode.InMemory, MaxDepth = 1000, DropPolicy = DropPolicy.DropOldest },
            CancellationToken.None);

        // Legacy file untouched; canonical path NOT created (InMemory mode).
        File.Exists(legacyDb).Should().BeTrue("InMemory mode must not migrate StoreAndForward files");
        Directory.Exists(Path.Combine(_tempDataRoot, "buffer")).Should().BeFalse();

        if (buffer is IAsyncDisposable ad) await ad.DisposeAsync();
    }

    // ── Constructor + invalid-policy guards (lightweight smoke) ──────────

    [Fact]
    public void Constructor_NullDataPath_Throws()
    {
        Action act = () => _ = new DefaultRouteBufferFactory(dataPath: null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dataPath");
    }

    [Fact]
    public async Task CreateAsync_BufferModeNone_Throws()
    {
        var factory = new DefaultRouteBufferFactory(_tempDataRoot);
        Func<Task> act = () => factory.CreateAsync(
            "route-F", new BufferPolicy { Mode = BufferMode.None, MaxDepth = 0, DropPolicy = DropPolicy.DropOldest },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a real, empty SQLite database file at <paramref name="path"/>.
    /// The migration shim must move whole files, and after the move
    /// <see cref="SqliteBuffer.OpenAsync"/> must be able to open the result —
    /// so the legacy file must be a valid SQLite database, not just a stub
    /// with the magic header bytes (SQLite Error 26 'file is not a database'
    /// would otherwise fire from the post-migration open).
    /// </summary>
    private static void CreateEmptySqliteFile(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Pooling=false ensures the file handle is released when the
            // connection is disposed — otherwise the SQLite pool keeps it
            // open and the migration shim's File.Move throws IOException.
            Pooling = false,
        }.ToString();
        using (var conn = new SqliteConnection(connectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            cmd.ExecuteScalar();
        }
        SqliteConnection.ClearAllPools();
    }
}
