// ============================================================================
// Tests: MelsecScanPlanner — deterministic word-unit block planning, coalescing
//        rules (scan rate + device + gap + 960-word cap), bit-device word-unit
//        mapping, and typed planning errors. Pure; no transport/adapter.
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Melsec.Tests;

public class MelsecScanPlannerTests
{
    private static MelsecTagDefinition Tag(string name, string address, string? datatype = null, int scanRateMs = 1000) =>
        new() { Name = name, Address = address, Datatype = datatype, ScanRateMs = scanRateMs };

    private static MelsecScanPlan Plan(int maxGapWords, int cap, params MelsecTagDefinition[] tags) =>
        MelsecScanPlanner.Build(tags, maxGapWords, cap);

    // ---- Word-device coalescing -------------------------------------------

    [Fact]
    public void Contiguous_word_tags_coalesce_into_one_block()
    {
        var plan = Plan(8, 960, Tag("a", "D100"), Tag("b", "D101"), Tag("c", "D102"));

        plan.Errors.Should().BeEmpty();
        plan.Blocks.Should().ContainSingle();
        var block = plan.Blocks[0];
        block.DeviceSymbol.Should().Be("D");
        block.HeadDeviceNumber.Should().Be(100);
        block.WordCount.Should().Be(3);
        block.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void Mixed_width_word_tags_cover_correct_range()
    {
        var plan = Plan(8, 960, Tag("a", "D100", "uint32"), Tag("b", "D102", "uint16"));

        plan.Blocks.Should().ContainSingle();
        plan.Blocks[0].HeadDeviceNumber.Should().Be(100);
        plan.Blocks[0].WordCount.Should().Be(3); // D100+D101 (u32) + D102 (u16)
    }

    [Fact]
    public void Two_word_bits_on_same_word_share_one_block_count_one()
    {
        var plan = Plan(8, 960, Tag("a", "D100.3"), Tag("b", "D100.F"));

        plan.Blocks.Should().ContainSingle();
        var block = plan.Blocks[0];
        block.HeadDeviceNumber.Should().Be(100);
        block.WordCount.Should().Be(1);
        block.Entries.Select(e => e.BitIndex).Should().BeEquivalentTo(new int?[] { 3, 15 });
    }

    [Fact]
    public void Adjacent_word_bits_coalesce_into_count_two()
    {
        var plan = Plan(8, 960, Tag("a", "D100.3"), Tag("b", "D101.0"));

        plan.Blocks.Should().ContainSingle();
        plan.Blocks[0].HeadDeviceNumber.Should().Be(100);
        plan.Blocks[0].WordCount.Should().Be(2);
    }

    // ---- Never coalesce across device -------------------------------------

    [Fact]
    public void Different_word_devices_do_not_coalesce_D_and_R()
    {
        var plan = Plan(8, 960, Tag("a", "D100"), Tag("b", "R100"));

        plan.Blocks.Should().HaveCount(2);
        plan.Blocks.Select(b => b.DeviceSymbol).Should().BeEquivalentTo(new[] { "D", "R" });
    }

    [Fact]
    public void Different_word_devices_do_not_coalesce_D_and_W()
    {
        var plan = Plan(8, 960, Tag("a", "D100"), Tag("b", "W100"));
        plan.Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void Different_bit_devices_do_not_coalesce_X_and_Y()
    {
        var plan = Plan(8, 960, Tag("a", "X20"), Tag("b", "Y20"));

        plan.Blocks.Should().HaveCount(2);
        plan.Blocks.Select(b => b.DeviceSymbol).Should().BeEquivalentTo(new[] { "X", "Y" });
    }

    // ---- Bit-device word-unit mapping -------------------------------------

    [Fact]
    public void Bit_device_points_within_16_fit_one_returned_word()
    {
        var plan = Plan(8, 960, Tag("a", "M100"), Tag("b", "M115"));

        plan.Blocks.Should().ContainSingle();
        var block = plan.Blocks[0];
        block.DeviceSymbol.Should().Be("M");
        block.HeadDeviceNumber.Should().Be(100);
        block.WordCount.Should().Be(1);
        block.Entries.Select(e => e.BitIndex).Should().BeEquivalentTo(new int?[] { 0, 15 });
    }

    [Fact]
    public void Bit_device_points_spanning_16_need_two_returned_words()
    {
        var plan = Plan(8, 960, Tag("a", "M100"), Tag("b", "M116"));

        plan.Blocks.Should().ContainSingle();
        plan.Blocks[0].WordCount.Should().Be(2);
        var m116 = plan.Blocks[0].Entries.Single(e => e.TagName == "b");
        m116.ByteOffset.Should().Be(2); // second returned word
        m116.BitIndex.Should().Be(0);
    }

    // ---- Hex addressing ---------------------------------------------------

    [Fact]
    public void Hex_word_addresses_coalesce()
    {
        var plan = Plan(8, 960, Tag("a", "W1A"), Tag("b", "W1B"));

        plan.Blocks.Should().ContainSingle();
        plan.Blocks[0].HeadDeviceNumber.Should().Be(0x1A);
        plan.Blocks[0].WordCount.Should().Be(2);
    }

    [Fact]
    public void ZR_hex_addresses_coalesce()
    {
        // ZR1F = 31, ZR20 = 0x20 = 32 -> contiguous.
        var plan = Plan(8, 960, Tag("a", "ZR1F"), Tag("b", "ZR20"));

        plan.Blocks.Should().ContainSingle();
        plan.Blocks[0].DeviceSymbol.Should().Be("ZR");
        plan.Blocks[0].HeadDeviceNumber.Should().Be(0x1F);
        plan.Blocks[0].WordCount.Should().Be(2);
    }

    // ---- Dedup + scan-rate grouping ---------------------------------------

    [Fact]
    public void Two_tags_on_same_address_share_one_block()
    {
        var plan = Plan(8, 960, Tag("a", "D100"), Tag("b", "D100"));

        plan.Blocks.Should().ContainSingle();
        plan.Blocks[0].WordCount.Should().Be(1);
        plan.Blocks[0].Entries.Should().HaveCount(2);
        plan.Blocks[0].Entries.Should().OnlyContain(e => e.ByteOffset == 0);
    }

    [Fact]
    public void Same_range_different_scan_rate_does_not_coalesce()
    {
        var plan = Plan(8, 960, Tag("a", "D100", scanRateMs: 1000), Tag("b", "D100", scanRateMs: 500));

        plan.Blocks.Should().HaveCount(2);
        plan.Blocks.Select(b => b.ScanRateMs).Should().BeEquivalentTo(new[] { 500, 1000 });
    }

    // ---- Gap rules --------------------------------------------------------

    [Fact]
    public void MaxGapWords_zero_merges_only_touching_ranges()
    {
        Plan(0, 960, Tag("a", "D100"), Tag("b", "D101")).Blocks.Should().ContainSingle();
        Plan(0, 960, Tag("a", "D100"), Tag("b", "D102")).Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void MaxGapWords_positive_permits_bounded_gap_coalescing()
    {
        var plan = Plan(1, 960, Tag("a", "D100"), Tag("b", "D102"));

        plan.Blocks.Should().ContainSingle();
        plan.Blocks[0].WordCount.Should().Be(3); // covers the D101 gap word
    }

    // ---- 960-word cap split -----------------------------------------------

    [Fact]
    public void Oversized_demand_splits_into_legal_blocks()
    {
        var tags = Enumerable.Range(0, 1001)
            .Select(i => Tag($"t{i}", $"D{i}", "int16"))
            .ToArray();

        var plan = Plan(0, 960, tags);

        plan.Errors.Should().BeEmpty();
        plan.Blocks.Should().HaveCount(2);
        plan.Blocks.Should().OnlyContain(b => b.WordCount <= MelsecScanPlanner.HardWordCap);
        plan.Blocks[0].WordCount.Should().Be(960);
        plan.Blocks[1].WordCount.Should().Be(41);
        plan.Blocks.Sum(b => b.Entries.Count).Should().Be(1001);
    }

    // ---- Typed errors -----------------------------------------------------

    [Fact]
    public void Unplannable_tags_surface_typed_errors_and_valid_tags_still_plan()
    {
        var plan = Plan(8, 960, Tag("bad1", "T0"), Tag("bad2", "Q1"), Tag("good", "D100"));

        plan.Blocks.Should().ContainSingle();
        plan.Blocks[0].DeviceSymbol.Should().Be("D");
        plan.Errors.Should().HaveCount(2);
        plan.Errors.Should().Contain(e => e.TagName == "bad1" && e.Code == MelsecAddressParser.DeviceNotImplemented);
        plan.Errors.Should().Contain(e => e.TagName == "bad2" && e.Code == MelsecAddressParser.InvalidAddress);
    }

    [Fact]
    public void Bool_datatype_on_plain_word_device_is_a_typed_mismatch()
    {
        var plan = Plan(8, 960, Tag("x", "D100", "bool"));

        plan.Blocks.Should().BeEmpty();
        plan.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MelsecScanPlanner.DatatypeMismatch);
    }

    [Fact]
    public void Nonbool_datatype_on_bit_device_is_a_typed_mismatch()
    {
        var plan = Plan(8, 960, Tag("y", "M100", "int16"));

        plan.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MelsecScanPlanner.DatatypeMismatch);
    }

    [Fact]
    public void Invalid_scan_rate_is_a_typed_error()
    {
        var plan = Plan(8, 960, Tag("z", "D100", scanRateMs: 0));

        plan.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MelsecScanPlanner.InvalidScanRate);
    }
}
