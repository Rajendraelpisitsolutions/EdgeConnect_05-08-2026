// ============================================================================
// File: Scanning/ScanPlannerTests.cs
// Purpose: Unit tests for ScanPlanner — bucketing, sorting, greedy packing,
//          FC-limit safety, gap coalescing, overlap handling, and tag-width
//          validation.
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Scanning;

public sealed class ScanPlannerTests
{
    private static ModbusTagDefinition Reg(
        string name,
        ushort address,
        int scanRateMs = 1000,
        byte unitId = 1,
        string datatype = "uint16",
        ModbusRegisterClass rc = ModbusRegisterClass.HoldingRegister)
        => new()
        {
            Name = name,
            UnitId = unitId,
            RegisterClass = rc,
            Address = address,
            ScanRateMs = scanRateMs,
            Datatype = datatype,
        };

    [Fact]
    public void Build_EmptyTags_ReturnsEmptyPlan()
    {
        var plan = ScanPlanner.Build([]);

        plan.Groups.Should().BeEmpty();
        plan.TotalBlockCount.Should().Be(0);
        plan.TotalTagCount.Should().Be(0);
    }

    [Fact]
    public void Build_SingleTag_ProducesOneGroupOneBlockOneEntry()
    {
        var plan = ScanPlanner.Build([Reg("t1", 10)]);

        plan.Groups.Should().ContainSingle();
        var group = plan.Groups[0];
        group.IntervalMs.Should().Be(1000);
        group.UnitId.Should().Be((byte)1);
        group.RegisterClass.Should().Be(ModbusRegisterClass.HoldingRegister);
        group.FunctionCode.Should().Be((byte)0x03);
        group.Blocks.Should().ContainSingle();
        group.Blocks[0].StartAddress.Should().Be((ushort)10);
        group.Blocks[0].Count.Should().Be((ushort)1);
        group.Blocks[0].Entries.Should().ContainSingle()
            .Which.Tag.Name.Should().Be("t1");
    }

    [Fact]
    public void Build_ContiguousRegisters_CoalesceIntoOneBlock()
    {
        var plan = ScanPlanner.Build([
            Reg("a", 0),
            Reg("b", 1),
            Reg("c", 2),
            Reg("d", 3),
        ]);

        plan.Groups.Should().ContainSingle();
        var block = plan.Groups[0].Blocks.Should().ContainSingle().Subject;
        block.StartAddress.Should().Be((ushort)0);
        block.Count.Should().Be((ushort)4);
        block.Entries.Should().HaveCount(4);
        block.Entries.Select(e => e.Tag.Name).Should().Equal("a", "b", "c", "d");
        block.Entries.Select(e => (int)e.Offset).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void Build_GapWithinMax_Coalesces()
    {
        // Gap of 5 between tag "a" (ends at 1) and tag "b" (starts at 6).
        // With default maxGap = 8 the planner bridges it into one block.
        var plan = ScanPlanner.Build([
            Reg("a", 0),
            Reg("b", 6),
        ]);

        var block = plan.Groups[0].Blocks.Should().ContainSingle().Subject;
        block.StartAddress.Should().Be((ushort)0);
        block.Count.Should().Be((ushort)7); // 0..6 inclusive = 7 registers
        block.Entries.Select(e => e.Tag.Name).Should().Equal("a", "b");
    }

    [Fact]
    public void Build_GapExceedsMax_SplitsBlocks()
    {
        // Gap of 20 between tags — default maxGap (8) refuses to coalesce.
        var plan = ScanPlanner.Build([
            Reg("a", 0),
            Reg("b", 20),
        ]);

        plan.Groups[0].Blocks.Should().HaveCount(2);
        plan.Groups[0].Blocks[0].StartAddress.Should().Be((ushort)0);
        plan.Groups[0].Blocks[0].Count.Should().Be((ushort)1);
        plan.Groups[0].Blocks[1].StartAddress.Should().Be((ushort)20);
        plan.Groups[0].Blocks[1].Count.Should().Be((ushort)1);
    }

    [Fact]
    public void Build_MaxGapZero_DisablesCoalescing()
    {
        // Adjacent tags still get separate blocks when maxGap = 0.
        var plan = ScanPlanner.Build(
            [Reg("a", 0), Reg("b", 1)],
            maxGapRegisters: 0);

        // a ends at 1, b starts at 1 — gap = max(0, 1-1) = 0, qualifies.
        // To actually force a split we need a true positive gap.
        plan.Groups[0].Blocks.Should().ContainSingle()
            .Which.Count.Should().Be((ushort)2);

        // Now verify maxGap=0 refuses a 1-reg gap.
        var plan2 = ScanPlanner.Build(
            [Reg("a", 0), Reg("b", 2)],
            maxGapRegisters: 0);
        plan2.Groups[0].Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void Build_BucketsByScanRate()
    {
        var plan = ScanPlanner.Build([
            Reg("fast", 0, scanRateMs: 100),
            Reg("slow", 1, scanRateMs: 1000),
        ]);

        plan.Groups.Should().HaveCount(2);
        plan.Groups[0].IntervalMs.Should().Be(100);
        plan.Groups[1].IntervalMs.Should().Be(1000);
    }

    [Fact]
    public void Build_BucketsByUnitId()
    {
        var plan = ScanPlanner.Build([
            Reg("u1", 0, unitId: 1),
            Reg("u2", 0, unitId: 2),
        ]);

        plan.Groups.Should().HaveCount(2);
        plan.Groups.Select(g => g.UnitId).Should().Equal((byte)1, (byte)2);
    }

    [Fact]
    public void Build_BucketsByRegisterClass()
    {
        var plan = ScanPlanner.Build([
            Reg("h", 0, rc: ModbusRegisterClass.HoldingRegister),
            Reg("i", 0, rc: ModbusRegisterClass.InputRegister),
            new ModbusTagDefinition
            {
                Name = "c",
                RegisterClass = ModbusRegisterClass.Coil,
                Address = 0,
                ScanRateMs = 1000,
            },
        ]);

        plan.Groups.Should().HaveCount(3);
        plan.Groups.Select(g => g.RegisterClass).Should().Contain(new[]
        {
            ModbusRegisterClass.Coil,
            ModbusRegisterClass.HoldingRegister,
            ModbusRegisterClass.InputRegister,
        });
    }

    [Fact]
    public void Build_Fc03RespectsMax125Registers()
    {
        // 126 contiguous UInt16 tags cannot all fit in one FC03 read.
        var tags = Enumerable.Range(0, 126).Select(i => Reg($"t{i}", (ushort)i)).ToList();

        var plan = ScanPlanner.Build(tags);

        plan.Groups[0].Blocks.Should().HaveCount(2, "one block hits the 125-register cap; the 126th tag overflows into a second block");
        plan.Groups[0].Blocks[0].Count.Should().Be((ushort)125);
        plan.Groups[0].Blocks[1].StartAddress.Should().Be((ushort)125);
        plan.Groups[0].Blocks[1].Count.Should().Be((ushort)1);
    }

    [Fact]
    public void Build_Fc03WideTagAtEndForcesSplit()
    {
        // 123 uint16 (width 1) + 1 float32 (width 2) = 125 regs → fits.
        // Add one more uint16 at 125 → total 126 → second block.
        var tags = Enumerable.Range(0, 123).Select(i => Reg($"t{i}", (ushort)i)).ToList();
        tags.Add(Reg("wide", 123, datatype: "float32"));  // occupies 123..124
        tags.Add(Reg("tail", 125));                        // occupies 125

        var plan = ScanPlanner.Build(tags);

        plan.Groups[0].Blocks[0].Count.Should().Be((ushort)125);
        plan.Groups[0].Blocks[0].Entries.Should().HaveCount(124);
        plan.Groups[0].Blocks[1].StartAddress.Should().Be((ushort)125);
    }

    [Fact]
    public void Build_Fc01RespectsMax2000Coils()
    {
        var tags = Enumerable.Range(0, 2001).Select(i => new ModbusTagDefinition
        {
            Name = $"c{i}",
            RegisterClass = ModbusRegisterClass.Coil,
            Address = (ushort)i,
            ScanRateMs = 1000,
        }).ToList();

        var plan = ScanPlanner.Build(tags);

        plan.Groups[0].Blocks.Should().HaveCount(2);
        plan.Groups[0].Blocks[0].Count.Should().Be((ushort)2000);
        plan.Groups[0].Blocks[1].StartAddress.Should().Be((ushort)2000);
    }

    [Fact]
    public void Build_OverlappingTags_CoexistInSameBlock()
    {
        // Two tags reading the same address with different datatypes
        // (e.g. raw uint16 vs. bitfield) is an unusual but valid config.
        var plan = ScanPlanner.Build([
            Reg("raw", 10, datatype: "uint16"),
            Reg("bits", 10, datatype: "uint16"),
        ]);

        var block = plan.Groups[0].Blocks.Should().ContainSingle().Subject;
        block.StartAddress.Should().Be((ushort)10);
        block.Count.Should().Be((ushort)1);
        block.Entries.Should().HaveCount(2);
        block.Entries.Should().OnlyContain(e => e.Offset == 0);
    }

    [Fact]
    public void Build_PartialOverlap_EmbedsTagsInTheSameBlock()
    {
        // float32 at 0..1 + uint16 at 1..1 — partial overlap.
        // Block covers 0..1 inclusive (count 2), both entries live there.
        var plan = ScanPlanner.Build([
            Reg("wide", 0, datatype: "float32"),
            Reg("narrow", 1, datatype: "uint16"),
        ]);

        var block = plan.Groups[0].Blocks.Should().ContainSingle().Subject;
        block.Count.Should().Be((ushort)2);
        block.Entries.Should().HaveCount(2);
        block.Entries.Should().Contain(e => e.Tag.Name == "wide" && e.Offset == 0 && e.Width == 2);
        block.Entries.Should().Contain(e => e.Tag.Name == "narrow" && e.Offset == 1 && e.Width == 1);
    }

    [Fact]
    public void Build_TagWiderThanFcLimit_Throws()
    {
        // 400-char string → 200 registers, exceeds FC03's 125 limit.
        var tag = new ModbusTagDefinition
        {
            Name = "oversize",
            RegisterClass = ModbusRegisterClass.HoldingRegister,
            Address = 0,
            ScanRateMs = 1000,
            Datatype = "string400",
        };

        var act = () => ScanPlanner.Build([tag]);

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*exceeds the HoldingRegister function-code limit*");
    }

    [Fact]
    public void Build_TagAddressOverflow_Throws()
    {
        var tag = Reg("edge", 65_534, datatype: "float32"); // would occupy 65_534..65_535, which is still within bounds
        var tagOverflow = Reg("over", 65_535, datatype: "float32"); // 65_535 + 2 > 65_536

        var ok = () => ScanPlanner.Build([tag]);
        ok.Should().NotThrow();

        var bad = () => ScanPlanner.Build([tagOverflow]);
        bad.Should().Throw<System.ArgumentException>()
            .WithMessage("*overflows the 16-bit Modbus address space*");
    }

    [Fact]
    public void Build_NegativeMaxGap_Throws()
    {
        var act = () => ScanPlanner.Build([Reg("t", 0)], maxGapRegisters: -1);
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Build_GroupsAreOrdered_ByScanRateThenUnitThenRegisterClass()
    {
        // Input intentionally shuffled; expected order is stable regardless.
        var plan = ScanPlanner.Build([
            Reg("b", 0, scanRateMs: 1000, unitId: 2, rc: ModbusRegisterClass.InputRegister),
            Reg("a", 0, scanRateMs: 100,  unitId: 1, rc: ModbusRegisterClass.HoldingRegister),
            Reg("c", 0, scanRateMs: 1000, unitId: 1, rc: ModbusRegisterClass.InputRegister),
            Reg("d", 0, scanRateMs: 1000, unitId: 2, rc: ModbusRegisterClass.HoldingRegister),
        ]);

        plan.Groups.Select(g => (g.IntervalMs, (int)g.UnitId, (int)g.RegisterClass)).Should().Equal(
            (100,  1, (int)ModbusRegisterClass.HoldingRegister),
            (1000, 1, (int)ModbusRegisterClass.InputRegister),
            (1000, 2, (int)ModbusRegisterClass.HoldingRegister),
            (1000, 2, (int)ModbusRegisterClass.InputRegister));
    }

    [Fact]
    public void Build_BlocksToRequest_MatchesGroupFunctionCodeAndUnit()
    {
        var plan = ScanPlanner.Build([
            Reg("r", 40, unitId: 3, rc: ModbusRegisterClass.InputRegister, datatype: "uint16"),
        ]);

        var group = plan.Groups[0];
        var block = group.Blocks[0];
        var request = block.ToRequest(group.UnitId, group.RegisterClass);

        request.UnitId.Should().Be((byte)3);
        request.RegisterClass.Should().Be(ModbusRegisterClass.InputRegister);
        request.StartAddress.Should().Be((ushort)40);
        request.Quantity.Should().Be((ushort)1);
    }

    [Fact]
    public void Build_EveryInputTagAppearsExactlyOnce()
    {
        var tags = new[]
        {
            Reg("a", 0),
            Reg("b", 10),
            Reg("c", 100, scanRateMs: 500),
            Reg("d", 3, unitId: 2),
            Reg("e", 4, unitId: 2),
        };

        var plan = ScanPlanner.Build(tags);

        plan.TotalTagCount.Should().Be(tags.Length);
        var flattened = plan.Groups.SelectMany(g => g.Blocks).SelectMany(b => b.Entries).ToList();
        flattened.Should().HaveCount(tags.Length);
        flattened.Select(e => e.Tag.Name).Should().BeEquivalentTo(tags.Select(t => t.Name));
    }
}
