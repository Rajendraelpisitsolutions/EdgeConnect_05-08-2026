// ============================================================================
// File: Buffer/SqliteBufferQuarantineTests.cs
// Purpose: Prove the "quarantine-and-continue" policy — a single point the
//          buffer cannot serialize is SKIPPED (not fatal), the rest of the
//          batch persists with contiguous sequences, the skip is counted in
//          BufferStats.Quarantined, and the optional callback fires once per
//          skipped point. Regression guard for the class of bug where one
//          malformed point's serialization exception (NOT a SqliteException)
//          escaped the buffer and silently stranded the whole route.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2aTestFixtures;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferQuarantineTests
{
    // A point the BinaryWriterFormat cannot serialize: a metadata value of an
    // unsupported runtime type (decimal) makes WriteMetadataValue throw
    // InvalidDataException — deterministic, and NOT a SqliteException.
    private static CanonicalDataPoint UnserializablePoint(long sequence, string tag = "bad.tag")
    {
        return new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice("dev-1")
            .WithTag(tag, "Bad/Tag")
            .WithValue(1.0, CanonicalValueType.Double)
            .WithGoodQuality(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithMetadata(new Dictionary<string, object> { ["amount"] = 3.14m })
            .WithSequence(sequence)
            .Build();
    }

    [Fact]
    public async Task Enqueue_OneUnserializablePoint_SkipsIt_RestPersistContiguously_CallbackAndCounterFire()
    {
        var path = NewFilePath();
        var quarantined = new List<QuarantinedPoint>();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "route-test", path, SmallSqlitePolicy(maxDepth: 64),
                utcNow: null, onQuarantine: quarantined.Add);
            await buf.RegisterSinkAsync("sink-1", default);

            // [good, BAD, good, good] — the bad point sits in the middle.
            var batch = new[] { Point(0), UnserializablePoint(1), Point(2), Point(3) };
            await buf.EnqueueAsync(batch, default);

            // Only the three good points persist.
            var drained = await buf.DequeueBatchAsync("sink-1", 10, default);
            drained.Points.Should().HaveCount(3);
            drained.Points.Should().OnlyContain(p => p.TagName == "spindle.speed");

            // Survivors keep CONTIGUOUS sequences (no gap where the bad one was).
            drained.FirstSequence.Should().Be(0);
            drained.LastSequence.Should().Be(2);

            var stats = await buf.GetStatsAsync();
            stats.TotalEnqueued.Should().Be(3);
            stats.Quarantined.Should().Be(1);

            // The callback fired exactly once, naming the bad tag + the reason.
            quarantined.Should().ContainSingle();
            quarantined[0].TagName.Should().Be("bad.tag");
            quarantined[0].Reason.Should().Contain("Decimal");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Enqueue_AllPointsUnserializable_DoesNotThrow_NothingPersists_AllQuarantined()
    {
        var path = NewFilePath();
        var quarantined = new List<QuarantinedPoint>();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "route-test", path, SmallSqlitePolicy(maxDepth: 64),
                utcNow: null, onQuarantine: quarantined.Add);
            await buf.RegisterSinkAsync("sink-1", default);

            var batch = new[] { UnserializablePoint(0, "a"), UnserializablePoint(1, "b") };

            // The whole point of quarantine-and-continue: NO throw escapes.
            var act = async () => await buf.EnqueueAsync(batch, default);
            await act.Should().NotThrowAsync();

            var drained = await buf.DequeueBatchAsync("sink-1", 10, default);
            drained.IsEmpty.Should().BeTrue();

            var stats = await buf.GetStatsAsync();
            stats.TotalEnqueued.Should().Be(0);
            stats.Quarantined.Should().Be(2);
            quarantined.Should().HaveCount(2);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Enqueue_Unserializable_WithoutCallback_StillSkipsAndCounts()
    {
        // Null observer: the point is still skipped and counted; only reporting
        // is disabled. Proves the buffer never depends on the callback for
        // correctness.
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "route-test", path, SmallSqlitePolicy(maxDepth: 64));
            await buf.RegisterSinkAsync("sink-1", default);

            await buf.EnqueueAsync(new[] { Point(0), UnserializablePoint(1), Point(2) }, default);

            var drained = await buf.DequeueBatchAsync("sink-1", 10, default);
            drained.Points.Should().HaveCount(2);

            var stats = await buf.GetStatsAsync();
            stats.Quarantined.Should().Be(1);
        }
        finally
        {
            TryDelete(path);
        }
    }
}
