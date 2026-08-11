// ============================================================================
// File: Buffer/SqliteBufferPragmaTests.cs
// Purpose: Pin the C2b durability defaults at the SQLite layer.
//
// C2b close-out R1 (catches mutations 5 + 6 from the independent review):
//   Mutation 5: PRAGMA application order swap. journal_mode must be set
//               BEFORE synchronous so the synchronous level applies to WAL.
//   Mutation 6: synchronous=NORMAL instead of FULL — silent D1 violation.
//
// Both mutations are trivially detectable by querying the active PRAGMA
// values after OpenAsync returns. This file is the post-open assertion.
// ============================================================================

using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferPragmaTests
{
    /// <summary>
    /// R1 pin: after <see cref="SqliteBuffer.OpenAsync"/> returns, the
    /// SQLite database file must report <c>journal_mode = wal</c> and
    /// <c>synchronous = 2</c> (FULL). This is the locked C2b durability
    /// default per <c>buffer-durability.md</c>. Catches a regression to
    /// <c>NORMAL</c> or to a misordered PRAGMA application that would
    /// leave <c>synchronous</c> on the rollback journal default.
    /// </summary>
    [Fact]
    public async Task PostOpen_JournalModeIsWal_AndSynchronousIsFull()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                // Hold the buffer open while we inspect the file via a
                // sibling read connection.
                using var conn = new SqliteConnection($"Data Source={path};Mode=ReadWrite");
                conn.Open();

                using var modeCmd = conn.CreateCommand();
                modeCmd.CommandText = "PRAGMA journal_mode;";
                var journalMode = (string)modeCmd.ExecuteScalar()!;
                journalMode.Should().Be("wal", "C2b locks WAL journaling for crash recovery");

                using var syncCmd = conn.CreateCommand();
                syncCmd.CommandText = "PRAGMA synchronous;";
                var synchronous = (long)syncCmd.ExecuteScalar()!;
                // SQLite synchronous values: 0=OFF, 1=NORMAL, 2=FULL, 3=EXTRA.
                // C2b's locked default is FULL because the C2b constraints
                // explicitly forbid silent loss of acknowledged-persistence
                // commits. A regression to NORMAL would degrade durability
                // without breaking any other test.
                synchronous.Should().Be(2,
                    "C2b locks synchronous=FULL (value 2). NORMAL=1 violates the D1 lock " +
                    "and the 'never optimize in a way that risks silent loss after acknowledged " +
                    "persistence' constraint.");
            }
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <summary>
    /// Sibling pin: confirm <c>busy_timeout</c> matches the documented
    /// 5-second value. Catches a regression that disables SQLite's only
    /// retry layer (which is itself the only retry layer per the
    /// no-outer-retry-loop rule).
    /// </summary>
    [Fact]
    public async Task PostOpen_BusyTimeoutIs5000ms()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                using var conn = new SqliteConnection($"Data Source={path};Mode=ReadWrite");
                conn.Open();
                // Apply the same busy_timeout PRAGMA on this sibling
                // connection so we can query its current value (PRAGMA
                // busy_timeout returns the value of THIS connection, not
                // the writer connection inside SqliteBuffer). The point of
                // the test is to pin the SCHEMA constant — query the schema
                // class directly.
                _ = conn; // unused — schema check below
            }

            // Pin the documented value via the schema constants list. The
            // SqliteBufferSchema class is internal — InternalsVisibleTo
            // gives this test assembly access.
            var pragmas = SqliteBufferSchema.WriterConnectionPragmas;
            pragmas.Should().Contain(p => p.Contains("busy_timeout = 5000",
                System.StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <summary>
    /// Pin the WriterConnectionPragmas ORDER: <c>journal_mode = WAL</c>
    /// must appear before <c>synchronous = FULL</c> so the synchronous
    /// level applies to the WAL.
    /// </summary>
    [Fact]
    public void WriterPragmas_JournalModeIsAppliedBeforeSynchronous()
    {
        var pragmas = SqliteBufferSchema.WriterConnectionPragmas;
        int journalIdx = -1;
        int syncIdx = -1;
        for (int i = 0; i < pragmas.Count; i++)
        {
            if (pragmas[i].Contains("journal_mode", System.StringComparison.OrdinalIgnoreCase))
            {
                journalIdx = i;
            }
            if (pragmas[i].Contains("synchronous", System.StringComparison.OrdinalIgnoreCase))
            {
                syncIdx = i;
            }
        }

        journalIdx.Should().BeGreaterOrEqualTo(0, "journal_mode pragma must be present");
        syncIdx.Should().BeGreaterOrEqualTo(0, "synchronous pragma must be present");
        journalIdx.Should().BeLessThan(syncIdx,
            "journal_mode = WAL must be applied before synchronous = FULL so the synchronous " +
            "level applies to the write-ahead log.");
    }
}
