// ============================================================================
// File: Buffer/C2aTestFixtures.cs
// Purpose: Helpers for C2a tests — sample CanonicalDataPoint builders and a
//          synthetic realistic-CNC batch for compression and benchmark
//          fixtures.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

internal static class C2aTestFixtures
{
    public static BufferPolicy SmallInMemoryPolicy(int maxDepth = 16, DropPolicy drop = DropPolicy.Block) => new()
    {
        Mode = BufferMode.InMemory,
        MaxDepth = maxDepth,
        DropPolicy = drop,
    };

    public static InMemoryBuffer NewBuffer(int maxDepth = 16, DropPolicy drop = DropPolicy.Block) =>
        new("route-test", SmallInMemoryPolicy(maxDepth, drop));

    public static CanonicalDataPoint Point(
        long sequence,
        string tagName = "spindle.speed",
        string tagPath = "Spindle/Speed",
        object? value = null,
        CanonicalValueType type = CanonicalValueType.Double)
    {
        return new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice("dev-1")
            .WithTag(tagName, tagPath)
            .WithValue(value ?? (double)sequence, type)
            .WithGoodQuality(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sequence))
            .WithSequence(sequence)
            .Build();
    }

    public static IReadOnlyList<CanonicalDataPoint> Batch(long startSeq, int count)
    {
        var result = new CanonicalDataPoint[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = Point(startSeq + i);
        }
        return result;
    }

    /// <summary>
    /// Build a realistic CNC poll-cycle batch: ~1000 points across a small
    /// set of slow-changing tags. Used by compression-ratio tests and
    /// serializer benchmarks.
    /// </summary>
    public static IReadOnlyList<CanonicalDataPoint> RealisticCncBatch(int approxCount = 1000)
    {
        var tags = new (string Name, string Path, double Base)[]
        {
            ("spindle.speed", "Spindle/Speed", 3000.0),
            ("axis.x.load", "Axis/X/Load", 12.5),
            ("axis.y.load", "Axis/Y/Load", 8.0),
            ("axis.z.load", "Axis/Z/Load", 4.2),
            ("override.feed", "Override/Feed", 100.0),
            ("override.rapid", "Override/Rapid", 100.0),
            ("program.line", "Program/Line", 245.0),
            ("alarm.count", "Alarm/Count", 0.0),
        };
        var batch = new List<CanonicalDataPoint>(approxCount);
        var rng = new Random(42);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < approxCount; i++)
        {
            var (name, path, baseV) = tags[i % tags.Length];
            // Slow-changing values: base ± small drift.
            var v = baseV + (rng.NextDouble() - 0.5) * 0.5;
            batch.Add(new CanonicalDataPointBuilder()
                .WithGateway("GW-TEST")
                .WithSource("focas-jyoti17", "focas2")
                .WithDevice("Jyoti17CNC", "Jyoti 17 CNC")
                .WithTag(name, path)
                .WithValue(v, CanonicalValueType.Double)
                .WithUnit("rpm")
                .WithGoodQuality(t0.AddMilliseconds(i * 100))
                .WithSequence(i)
                .Build());
        }
        return batch;
    }
}
