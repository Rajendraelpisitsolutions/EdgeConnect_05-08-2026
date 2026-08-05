// ============================================================================
// File: Import/ModbusTagCsvImporterTests.cs
// Purpose: Unit tests for ModbusTagCsvImporter. Covers every locked F4
//          rule: schema parsing, strict whole-file validation, duplicate
//          names, overlap warnings, BOM handling, comments, unknown
//          columns, legacy-vendor-address rejection.
// ============================================================================

using System.IO;
using System.Linq;
using ElpisEdgeConnect.Sources.ModbusTcp;
using ElpisEdgeConnect.Sources.ModbusTcp.Import;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Import;

public sealed class ModbusTagCsvImporterTests
{
    private static ModbusTagCsvImportResult Parse(string csv)
    {
        using var reader = new StringReader(csv);
        return ModbusTagCsvImporter.Import(reader);
    }

    private const string MinimalHeader =
        "name,unitId,registerClass,address,datatype,byteOrder,scanRateMs,scale,offset,unit,description";

    // -------------------------------------------------------------
    // Happy paths
    // -------------------------------------------------------------

    [Fact]
    public void Import_SingleValidRow_Succeeds()
    {
        var csv = $"""
            {MinimalHeader}
            spindle_rpm,1,HoldingRegister,0,uint16,,200,,,rpm,Spindle RPM
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Tags.Should().ContainSingle();
        var tag = result.Tags[0];
        tag.Name.Should().Be("spindle_rpm");
        tag.UnitId.Should().Be((byte)1);
        tag.RegisterClass.Should().Be(ModbusRegisterClass.HoldingRegister);
        tag.Address.Should().Be((ushort)0);
        tag.Datatype.Should().Be("uint16");
        tag.ByteOrder.Should().BeNull();
        tag.ScanRateMs.Should().Be(200);
        tag.Unit.Should().Be("rpm");
    }

    [Fact]
    public void Import_MultipleRows_AllDatatypesAndClasses_Succeeds()
    {
        var csv = $"""
            {MinimalHeader}
            running,1,Coil,0,bool,,250,,,,Line running
            door_closed,1,DiscreteInput,0,bool,,250,,,,
            spindle_rpm,1,HoldingRegister,0,uint16,,200,,,rpm,
            feed_rate,1,HoldingRegister,10,float32,ABCD,200,,,mm/min,
            parts_count,1,HoldingRegister,20,uint32,CDAB,500,,,,Production counter
            temperature,1,InputRegister,0,int16,,500,0.1,,C,Motor temp
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        result.Tags.Should().HaveCount(6);
        result.Summary.RowsRead.Should().Be(6);
        result.Summary.TagsProduced.Should().Be(6);
    }

    [Fact]
    public void Import_CommentLinesAndBlanks_Ignored()
    {
        var csv = $"""
            # this is a comment

            {MinimalHeader}
            # another comment

            x,1,Coil,0,bool,,250,,,,

            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        result.Tags.Should().ContainSingle();
        result.Summary.CommentRowsIgnored.Should().Be(2);
        result.Summary.BlankRowsIgnored.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Import_Utf8Bom_StrippedFromFirstLine()
    {
        var csv = "\uFEFF" + $"""
            {MinimalHeader}
            x,1,Coil,0,bool,,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        result.Tags.Should().ContainSingle();
    }

    [Fact]
    public void Import_QuotedDescriptionWithComma_Preserved()
    {
        var csv = $"""
            {MinimalHeader}
            alarm,1,HoldingRegister,0,int16,,500,,,,"Current alarm, if any"
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        result.Tags.Should().ContainSingle();
        // description field isn't surfaced on ModbusTagDefinition yet, but the
        // importer must accept the quoted form without splitting on the comma.
    }

    [Fact]
    public void Import_HeaderOrder_Flexible()
    {
        // Alternate order still parses — columns are name-indexed.
        var csv = """
            registerClass,name,datatype,address,scanRateMs,unitId,byteOrder,scale,offset,unit,description
            HoldingRegister,rpm,uint16,5,200,1,,,,rpm,Spindle
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        result.Tags.Should().ContainSingle();
        result.Tags[0].Address.Should().Be((ushort)5);
    }

    // -------------------------------------------------------------
    // Header errors
    // -------------------------------------------------------------

    [Fact]
    public void Import_Empty_Fails()
    {
        var result = Parse(string.Empty);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ModbusErrors.ImportEmpty);
    }

    [Fact]
    public void Import_NoHeader_Fails()
    {
        var result = Parse("# only comments\n\n# and blank lines\n");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ModbusErrors.ImportNoHeader);
    }

    [Fact]
    public void Import_MissingRequiredColumn_Fails()
    {
        // Missing 'scanRateMs'.
        var csv = """
            name,unitId,registerClass,address,datatype
            x,1,Coil,0,bool
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == ModbusErrors.ImportMissingColumn && e.Column == "scanRateMs");
    }

    [Fact]
    public void Import_UnknownColumn_Fails()
    {
        var csv = $"""
            {MinimalHeader},mystery
            x,1,Coil,0,bool,,250,,,,,whatever
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == ModbusErrors.ImportUnknownColumn && e.Column == "mystery");
    }

    [Fact]
    public void Import_HeaderOnly_Fails()
    {
        var result = Parse(MinimalHeader + "\n");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ModbusErrors.ImportEmpty);
    }

    // -------------------------------------------------------------
    // Row errors
    // -------------------------------------------------------------

    [Fact]
    public void Import_BadRegisterClass_FailsWithLineNumber()
    {
        var csv = $"""
            {MinimalHeader}
            x,1,SuperFastRegister,0,uint16,,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        var err = result.Errors.Should().ContainSingle(e => e.Column == "registerClass").Subject;
        err.Code.Should().Be(ModbusErrors.ImportBadField);
        err.Line.Should().Be(2, "header is line 1, first data row is line 2");
        err.RawValue.Should().Be("SuperFastRegister");
    }

    [Fact]
    public void Import_BadByteOrder_Fails()
    {
        var csv = $"""
            {MinimalHeader}
            x,1,HoldingRegister,0,uint32,WXYZ,500,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Column == "byteOrder");
    }

    [Fact]
    public void Import_MissingUnitId_Fails()
    {
        var csv = $"""
            {MinimalHeader}
            x,,HoldingRegister,0,uint16,,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Column == "unitId");
    }

    [Fact]
    public void Import_NegativeAddress_Fails()
    {
        var csv = $"""
            {MinimalHeader}
            x,1,HoldingRegister,-5,uint16,,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Column == "address");
    }

    [Fact]
    public void Import_LegacyVendorAddress_40001_Fails()
    {
        var csv = $"""
            {MinimalHeader}
            x,1,HoldingRegister,40001,uint16,,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == ModbusErrors.ImportLegacyAddress && e.RawValue == "40001");
    }

    [Fact]
    public void Import_LegacyVendorAddress_10001_Fails()
    {
        var csv = $"""
            {MinimalHeader}
            x,1,DiscreteInput,10001,bool,,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ModbusErrors.ImportLegacyAddress);
    }

    [Fact]
    public void Import_ShortRow_Fails()
    {
        // Header declares 11 columns; this row has 5.
        var csv = $"""
            {MinimalHeader}
            x,1,Coil,0,bool
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ModbusErrors.ImportShortRow);
    }

    [Fact]
    public void Import_AllErrorsReportedInOneRun()
    {
        // Three rows, each with a distinct error — importer must surface all.
        var csv = $"""
            {MinimalHeader}
            a,not-a-byte,Coil,0,bool,,250,,,,
            b,1,NotAClass,0,uint16,,250,,,,
            c,1,HoldingRegister,40001,uint16,,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    // -------------------------------------------------------------
    // Cross-row rules
    // -------------------------------------------------------------

    [Fact]
    public void Import_DuplicateName_Fails()
    {
        var csv = $"""
            {MinimalHeader}
            rpm,1,HoldingRegister,0,uint16,,200,,,rpm,
            rpm,1,HoldingRegister,1,uint16,,200,,,rpm,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ModbusErrors.ImportDuplicateName);
    }

    [Fact]
    public void Import_OverlappingRange_EmitsWarningButSucceeds()
    {
        // Both tags read register 0 — allowed (e.g. raw + bitfield overlay).
        var csv = $"""
            {MinimalHeader}
            raw,1,HoldingRegister,0,uint16,,500,,,,
            overlay,1,HoldingRegister,0,uint16,,500,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Code == ModbusErrors.ImportOverlappingRange);
    }

    [Fact]
    public void Import_Float32OverlapsWithUInt16_EmitsWarning()
    {
        // float32@0 spans 0..1, uint16@1 overlaps.
        var csv = $"""
            {MinimalHeader}
            flow,1,HoldingRegister,0,float32,ABCD,500,,,,
            narrow,1,HoldingRegister,1,uint16,,500,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Code == ModbusErrors.ImportOverlappingRange);
    }

    [Fact]
    public void Import_DifferentUnits_NoOverlapWarning()
    {
        // Same address but different unitId — cannot overlap on the wire.
        var csv = $"""
            {MinimalHeader}
            a,1,HoldingRegister,0,uint16,,500,,,,
            b,2,HoldingRegister,0,uint16,,500,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    // -------------------------------------------------------------
    // ModbusTagValidator integration
    // -------------------------------------------------------------

    [Fact]
    public void Import_BoolOnHoldingRegister_Fails()
    {
        var csv = $"""
            {MinimalHeader}
            x,1,HoldingRegister,0,bool,,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ModbusErrors.ConfigInvalid);
    }

    [Fact]
    public void Import_ScaleOnBool_Fails()
    {
        var csv = $"""
            {MinimalHeader}
            x,1,Coil,0,bool,,250,2.0,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == ModbusErrors.ConfigInvalid &&
            e.Message.Contains("Scale/Offset", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Import_ByteOrderWidthMismatch_Fails()
    {
        // AB is 2-byte; float32 needs a 4-byte ordering.
        var csv = $"""
            {MinimalHeader}
            x,1,HoldingRegister,0,float32,AB,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ModbusErrors.ConfigInvalid);
    }

    // -------------------------------------------------------------
    // ScanPlan round-trip
    // -------------------------------------------------------------

    [Fact]
    public void Import_ProducedTags_BuildValidScanPlan()
    {
        var csv = $"""
            {MinimalHeader}
            rpm,1,HoldingRegister,0,uint16,,200,,,rpm,
            load,1,HoldingRegister,1,int16,,200,,,%,
            feed,1,HoldingRegister,10,float32,ABCD,200,,,mm/min,
            running,1,Coil,0,bool,,250,,,,
            """;

        var result = Parse(csv);

        result.IsSuccess.Should().BeTrue();
        var plan = ScanPlanner.Build(result.Tags);
        plan.Groups.Should().NotBeEmpty();
        plan.TotalTagCount.Should().Be(4);
    }
}
