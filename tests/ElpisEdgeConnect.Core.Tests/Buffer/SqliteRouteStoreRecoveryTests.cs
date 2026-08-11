// ============================================================================
// File: Buffer/SqliteRouteStoreRecoveryTests.cs
// Covers: K1.2b — the append head is monotonic across restarts. After a full drain
//         plus reclaim (which persists tail_sequence == head), the head must recover
//         from that persisted floor even when no cursor high-water survives — so the
//         head can never regress to 0. Exercised through the public SqliteBuffer façade.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteRouteStoreRecoveryTests
{
    [Fact]
    public async Task Head_Recovers_From_Persisted_Floor_When_Points_And_Cursors_Are_Gone()
    {
        var path = NewFilePath();
        try
        {
            // 1. Enqueue 5 (sequences 0..4), register a sink, and ack the whole backlog.
            await using (var buf1 = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                await buf1.RegisterSinkAsync("s", CancellationToken.None);
                await buf1.EnqueueAsync(C2aTestFixtures.Batch(startSeq: 0, count: 5), CancellationToken.None);
                await buf1.AckAsync("s", 4, CancellationToken.None);
            }

            // 2. Reopen → the synchronous reclaim pass on open drains all points and
            //    persists tail_sequence = 5 (== head). Then deregister the sink, deleting
            //    the only surviving cursor high-water.
            await using (var buf2 = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                buf2.Head.Should().Be(5);
                await buf2.DeregisterSinkAsync("s", CancellationToken.None);
            }

            // 3. Reopen with no points and no cursors. Head must recover from the persisted
            //    floor (tail_sequence), NOT reset to 0.
            await using var buf3 = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            buf3.Head.Should().Be(5);

            // A newly attached sink + enqueue continues at 5, proving monotonic sequences.
            await buf3.RegisterSinkAsync("s2", CancellationToken.None);
            await buf3.EnqueueAsync(C2aTestFixtures.Batch(startSeq: 5, count: 1), CancellationToken.None);
            var batch = await buf3.DequeueBatchAsync("s2", 10, CancellationToken.None);
            batch.FirstSequence.Should().Be(5);
        }
        finally
        {
            TryDelete(path);
        }
    }
}
