// ============================================================================
// Tests: S7ScanPlanner — group by (interval, area, db), sort, pack.
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Sources.S7;
using ElpisEdgeConnect.Sources.S7.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests.Scanning;

public class S7ScanPlannerTests
{
    private static S7TagDefinition Tag(string name, string address, string? datatype = null, int scanRateMs = 1000) =>
        new()
        {
            Name = name,
            Address = address,
            Datatype = datatype,
            ScanRateMs = scanRateMs,
        };

    [Fact]
    public void Build_EmptyTagList_ProducesEmptyPlan()
    {
        var plan = S7ScanPlanner.Build(System.Array.Empty<S7TagDefinition>(), maxGapBytes: 16, maxReadBytes: 200);
        plan.Groups.Should().BeEmpty();
    }

    [Fact]
    public void Build_BucketsByInterval_Area_AndDbNumber()
    {
        var tags = new[]
        {
            Tag("a", "DB10.DBW0", "int", scanRateMs: 100),
            Tag("b", "DB10.DBW2", "int", scanRateMs: 100),
            Tag("c", "DB10.DBW0", "int", scanRateMs: 500), // different interval
            Tag("d", "DB20.DBW0", "int", scanRateMs: 100), // different DB
            Tag("e", "MW10",       "word", scanRateMs: 100), // different area
        };

        var plan = S7ScanPlanner.Build(tags, maxGapBytes: 8, maxReadBytes: 200);

        // Should produce 4 groups: (100,DB,10), (500,DB,10), (100,DB,20), (100,M,0).
        plan.Groups.Should().HaveCount(4);
        plan.Groups.Should().ContainSingle(g => g.IntervalMs == 100 && g.Area == S7MemoryArea.DataBlock && g.DbNumber == 10);
        plan.Groups.Should().ContainSingle(g => g.IntervalMs == 500 && g.Area == S7MemoryArea.DataBlock && g.DbNumber == 10);
        plan.Groups.Should().ContainSingle(g => g.IntervalMs == 100 && g.Area == S7MemoryArea.DataBlock && g.DbNumber == 20);
        plan.Groups.Should().ContainSingle(g => g.IntervalMs == 100 && g.Area == S7MemoryArea.Marker);
    }

    [Fact]
    public void Build_CoalescesContiguousReadsWithinMaxGap()
    {
        // Three Int tags at DBW0, DBW2, DBW8 in the same DB+interval.
        // Gap from DBW0..DBW2 = 0 bytes (contiguous).
        // Gap from DBW2..DBW8 = 4 bytes (still within 8-byte tolerance).
        // Expected: all three coalesced into one block of 10 bytes.
        var tags = new[]
        {
            Tag("a", "DB1.DBW0", "int"),
            Tag("b", "DB1.DBW2", "int"),
            Tag("c", "DB1.DBW8", "int"),
        };
        var plan = S7ScanPlanner.Build(tags, maxGapBytes: 8, maxReadBytes: 200);
        var group = plan.Groups.Single();
        group.Blocks.Should().HaveCount(1);
        var block = group.Blocks[0];
        block.StartByte.Should().Be(0);
        block.ByteCount.Should().Be(10); // 0..9 inclusive
        block.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void Build_SplitsBlocksOnLargeGaps()
    {
        var tags = new[]
        {
            Tag("a", "DB1.DBW0",   "int"),
            Tag("b", "DB1.DBW100", "int"), // far away — gap > maxGap
        };
        var plan = S7ScanPlanner.Build(tags, maxGapBytes: 8, maxReadBytes: 200);
        var group = plan.Groups.Single();
        group.Blocks.Should().HaveCount(2);
        group.Blocks[0].StartByte.Should().Be(0);
        group.Blocks[1].StartByte.Should().Be(100);
    }

    [Fact]
    public void Build_SplitsBlocksOnMaxReadBytes()
    {
        // Two Int tags 195 bytes apart with maxGap=200 (would coalesce)
        // but maxReadBytes=100 forces a split.
        var tags = new[]
        {
            Tag("a", "DB1.DBW0",   "int"),
            Tag("b", "DB1.DBW195", "int"),
        };
        var plan = S7ScanPlanner.Build(tags, maxGapBytes: 200, maxReadBytes: 100);
        var group = plan.Groups.Single();
        group.Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void Build_DerivesDatatypeFromAddressWidth_WhenHintAbsent()
    {
        var tags = new[]
        {
            Tag("bit",   "DB1.DBX0.0"), // → Bool
            Tag("byte",  "DB1.DBB1"),   // → Byte
            Tag("word",  "DB1.DBW2"),   // → Word
            Tag("dword", "DB1.DBD4"),   // → DWord
        };
        var plan = S7ScanPlanner.Build(tags, maxGapBytes: 8, maxReadBytes: 200);
        var block = plan.Groups.Single().Blocks.Single();
        var entryByName = block.Entries.ToDictionary(e => e.Tag.Name);
        entryByName["bit"].Spec.Datatype.Should().Be(S7Datatype.Bool);
        entryByName["byte"].Spec.Datatype.Should().Be(S7Datatype.Byte);
        entryByName["word"].Spec.Datatype.Should().Be(S7Datatype.Word);
        entryByName["dword"].Spec.Datatype.Should().Be(S7Datatype.DWord);
    }

    [Fact]
    public void Build_RejectsBitAddressWithNonBoolDatatype()
    {
        var tags = new[] { Tag("x", "DB1.DBX0.0", "int") };
        var act = () => S7ScanPlanner.Build(tags, maxGapBytes: 8, maxReadBytes: 200);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*bit-form*must use datatype 'bool'*");
    }

    [Fact]
    public void Build_RejectsDatatypeWiderThanAddressHint()
    {
        // DBW (2 bytes) with Real datatype (4 bytes) — mismatch.
        var tags = new[] { Tag("x", "DB1.DBW0", "real") };
        var act = () => S7ScanPlanner.Build(tags, maxGapBytes: 8, maxReadBytes: 200);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*does not fit the address width*");
    }

    [Fact]
    public void Build_BlockRelativeByteOffset_IsCorrectAfterCoalesce()
    {
        var tags = new[]
        {
            Tag("a", "DB1.DBW0", "int"),
            Tag("b", "DB1.DBW4", "int"),
        };
        var plan = S7ScanPlanner.Build(tags, maxGapBytes: 8, maxReadBytes: 200);
        var block = plan.Groups.Single().Blocks.Single();
        var a = block.Entries.Single(e => e.Tag.Name == "a");
        var b = block.Entries.Single(e => e.Tag.Name == "b");
        block.StartByte.Should().Be(0);
        a.BlockRelativeByteOffset.Should().Be(0);
        b.BlockRelativeByteOffset.Should().Be(4);
    }
}
