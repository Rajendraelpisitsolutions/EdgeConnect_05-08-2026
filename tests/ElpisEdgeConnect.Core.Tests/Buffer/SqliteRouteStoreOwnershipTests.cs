// ============================================================================
// File: Buffer/SqliteRouteStoreOwnershipTests.cs
// Covers: K1.2a ownership-lock lifecycle — the per-route SQLite database has exactly
//         one owning store. The lock is acquired BEFORE any DB open/mutation; a held
//         lock rejects the open before the file is touched; a failed open releases the
//         lock synchronously; only a genuine sharing conflict maps to
//         RouteStoreAlreadyOwned; concurrent cold opens have one deterministic winner;
//         and disposed public calls keep the SqliteBuffer identity. Exercised through
//         the public SqliteBuffer façade.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteRouteStoreOwnershipTests
{
    [Fact]
    public async Task Second_Open_On_Same_Path_While_First_Alive_Fails_AlreadyOwned()
    {
        var path = NewFilePath();
        try
        {
            await using var first = await SqliteBuffer.OpenAsync("route-own", path, SmallSqlitePolicy());

            var act = async () => await SqliteBuffer.OpenAsync("route-own-2", path, SmallSqlitePolicy());
            var ex = (await act.Should().ThrowAsync<BufferException>()).Which;
            ex.Error.Code.Should().Be(CoreErrors.RouteStoreAlreadyOwned);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Reopen_After_Dispose_Succeeds()
    {
        var path = NewFilePath();
        try
        {
            await using (var first = await SqliteBuffer.OpenAsync("route-own", path, SmallSqlitePolicy()))
            {
                // first owns the exclusive lock inside this scope
            }

            await using var second = await SqliteBuffer.OpenAsync("route-own", path, SmallSqlitePolicy());
            second.BufferId.Should().Be("route-own");
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ---- Required 1: a held lock rejects the open BEFORE the database is touched. ----
    [Fact]
    public async Task Externally_Held_Lock_Rejects_Open_And_Does_Not_Create_Db()
    {
        var path = NewFilePath();
        var lockPath = path + ".lock";
        try
        {
            using var external = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            var ex = (await act.Should().ThrowAsync<BufferException>()).Which;
            ex.Error.Code.Should().Be(CoreErrors.RouteStoreAlreadyOwned);

            // Lock is acquired before opening the writer → the DB file is never created.
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ---- Required 2: a failure AFTER lock acquisition releases the lock synchronously. ----
    [Fact]
    public async Task Failed_Open_Releases_Ownership_Lock()
    {
        var path = NewFilePath();
        try
        {
            // Garbage (non-SQLite) file → integrity/PRAGMA fails AFTER the lock is held.
            await File.WriteAllTextAsync(path, "not a sqlite database");

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            await act.Should().ThrowAsync<BufferException>();

            // The lock must have been released on the failed open — we can grab it now.
            using var reacquired = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            reacquired.CanWrite.Should().BeTrue();
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ---- Required 3: a non-sharing lock-file failure is NOT an ownership collision.
    //      Deterministic + parallelism-safe: place a DIRECTORY at the lock path so the
    //      FileStream open fails for a reason that is not a sharing conflict. ----
    [Fact]
    public async Task NonSharing_LockFile_Failure_Is_Classified_As_IoError_Not_AlreadyOwned()
    {
        var path = NewFilePath();
        var lockPath = path + ".lock";
        Directory.CreateDirectory(lockPath); // opening a directory as a file is not a sharing violation
        try
        {
            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            var ex = (await act.Should().ThrowAsync<BufferException>()).Which;
            ex.Error.Code.Should().Be(CoreErrors.BufferIoError);
            ex.Error.Code.Should().NotBe(CoreErrors.RouteStoreAlreadyOwned);
        }
        finally
        {
            try { Directory.Delete(lockPath, recursive: true); } catch { }
            TryDelete(path);
        }
    }

    // ---- Required 4: concurrent cold opens against a new path → one winner, one AlreadyOwned. ----
    [Fact]
    public async Task Concurrent_Cold_Opens_Have_One_Deterministic_Winner()
    {
        var path = NewFilePath();
        try
        {
            var results = await Task.WhenAll(TryOpenAsync(path), TryOpenAsync(path));

            var winners = results.Where(r => r.Buffer is not null).ToList();
            var losers = results.Where(r => r.Error is not null).ToList();

            winners.Should().HaveCount(1);
            losers.Should().HaveCount(1);
            // The loser fails cleanly at lock acquisition — not a schema/busy/corruption shape.
            losers[0].Error!.Error.Code.Should().Be(CoreErrors.RouteStoreAlreadyOwned);

            foreach (var r in winners)
            {
                await r.Buffer!.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ---- Required 5: disposed public calls keep the SqliteBuffer object identity. ----
    [Fact]
    public async Task Disposed_Public_Call_Retains_SqliteBuffer_Identity()
    {
        var path = NewFilePath();
        try
        {
            var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            await buf.DisposeAsync();

            var act = async () => await buf.DequeueBatchAsync("s", 1, CancellationToken.None);
            var ex = (await act.Should().ThrowAsync<ObjectDisposedException>()).Which;
            ex.ObjectName.Should().Be(typeof(SqliteBuffer).FullName);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static async Task<(SqliteBuffer? Buffer, BufferException? Error)> TryOpenAsync(string path)
    {
        try
        {
            return (await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()), null);
        }
        catch (BufferException ex)
        {
            return (null, ex);
        }
    }
}
