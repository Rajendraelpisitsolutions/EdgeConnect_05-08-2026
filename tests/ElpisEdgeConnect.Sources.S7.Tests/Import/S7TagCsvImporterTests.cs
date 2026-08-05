// ============================================================================
// Tests: S7TagCsvImporter — CSV → S7TagDefinition. Mirrors the Modbus importer
//        test suite for the S7 schema (Name,Address,Datatype,ScanRateMs,Unit,
//        Scale,Offset): happy paths, header errors, row errors, the M.2b.2
//        locked rules (Timer/Counter unsupported, compat blocks, dup-name
//        blocks, dup-address warns), and all-errors-at-once semantics.
// ============================================================================

using System.IO;
using System.Linq;
using ElpisEdgeConnect.Sources.S7.Import;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests.Import;

public class S7TagCsvImporterTests
{
    private static S7TagCsvImportResult Import(string csv) =>
        S7TagCsvImporter.Import(new StringReader(csv));

    // ── happy paths ──────────────────────────────────────────────────────

    [Fact]
    public void Import_SingleValidRow_ProducesTag()
    {
        var result = Import("Name,Address,Datatype,ScanRateMs,Unit,Scale,Offset\nspindle_rpm,DB1.DBW0,Int,200,rpm,1,0\n");

        result.IsSuccess.Should().BeTrue();
        var tag = result.Tags.Should().ContainSingle().Subject;
        tag.Name.Should().Be("spindle_rpm");
        tag.Address.Should().Be("DB1.DBW0");
        tag.Datatype.Should().Be("Int");
        tag.ScanRateMs.Should().Be(200);
        tag.Unit.Should().Be("rpm");
        tag.Scale.Should().Be(1);
        tag.Offset.Should().Be(0);
    }

    [Fact]
    public void Import_MinimalHeader_NameAddressOnly_DerivesDatatypeAndDefaultScan()
    {
        var result = Import("Name,Address\nrpm,DB1.DBW0\nrunning,DB1.DBX0.0\n");

        result.IsSuccess.Should().BeTrue();
        result.Tags.Should().HaveCount(2);
        result.Tags[0].Datatype.Should().BeNull("blank datatype is derived from the address width by the adapter");
        result.Tags[0].ScanRateMs.Should().Be(1000, "default scan rate when the value is absent");
    }

    [Fact]
    public void Import_IgnoresCommentsAndBlanks_AndStripsBom()
    {
        var csv = "﻿# S7 tags for line 3\n\nName,Address,Datatype\n\n# a comment\nrpm,DB1.DBW0,Int\n";
        var result = Import(csv);

        result.IsSuccess.Should().BeTrue();
        result.Tags.Should().ContainSingle();
        result.Summary.CommentRowsIgnored.Should().Be(2);
        result.Summary.BlankRowsIgnored.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Import_FlexibleColumnOrder_Works()
    {
        var result = Import("Address,ScanRateMs,Name,Datatype\nDB1.DBW0,500,rpm,Int\n");
        result.IsSuccess.Should().BeTrue();
        result.Tags.Single().Name.Should().Be("rpm");
        result.Tags.Single().ScanRateMs.Should().Be(500);
    }

    [Fact]
    public void Import_StringDatatypeWithLength_IsAccepted()
    {
        var result = Import("Name,Address,Datatype\njob,DB1.DBB0,string[16]\n");
        result.IsSuccess.Should().BeTrue();
        result.Tags.Single().Datatype.Should().Be("string[16]");
    }

    // ── header errors ──────────────────────────────────────────────────

    [Fact]
    public void Import_EmptyFile_IsEmptyError()
    {
        var result = Import("");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.Empty);
    }

    [Fact]
    public void Import_OnlyCommentsAndBlanks_IsNoHeaderError()
    {
        var result = Import("# just a comment\n\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.NoHeader);
    }

    [Fact]
    public void Import_MissingRequiredColumn_IsMissingColumnError()
    {
        var result = Import("Name,Datatype\nrpm,Int\n"); // no Address
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.MissingColumn && e.Column == "address");
    }

    [Fact]
    public void Import_UnknownColumn_IsUnknownColumnError()
    {
        var result = Import("Name,Address,Bogus\nrpm,DB1.DBW0,x\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.UnknownColumn);
    }

    [Fact]
    public void Import_HeaderOnly_NoDataRows_IsEmptyError()
    {
        var result = Import("Name,Address,Datatype\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.Empty);
    }

    // ── row errors ───────────────────────────────────────────────────────

    [Fact]
    public void Import_ShortRow_IsShortRowError()
    {
        var result = Import("Name,Address,Datatype\nrpm,DB1.DBW0\n"); // only 2 of 3 fields
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.ShortRow);
    }

    [Fact]
    public void Import_BadAddress_IsBadAddressError()
    {
        var result = Import("Name,Address\nrpm,DB1.DBZ0\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.BadAddress && e.Column == "address");
    }

    [Theory]
    [InlineData("T5")]
    [InlineData("C3")]
    public void Import_TimerCounterAddress_IsUnsupportedError(string address)
    {
        var result = Import($"Name,Address\nx,{address}\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.UnsupportedAddress);
    }

    [Fact]
    public void Import_DatatypeWiderThanAddress_IsIncompatibleError()
    {
        var result = Import("Name,Address,Datatype\nx,DB1.DBW0,Real\n"); // Real(4) on a word(2)
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.IncompatibleDatatype && e.Column == "datatype");
    }

    [Fact]
    public void Import_BitAddressWithNonBool_IsIncompatibleError()
    {
        var result = Import("Name,Address,Datatype\nx,DB1.DBX0.0,Int\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.IncompatibleDatatype);
    }

    [Fact]
    public void Import_UnknownDatatype_IsBadFieldError()
    {
        var result = Import("Name,Address,Datatype\nx,DB1.DBW0,NotAType\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.BadField && e.Column == "datatype");
    }

    [Fact]
    public void Import_NonPositiveScanRate_IsBadFieldError()
    {
        var result = Import("Name,Address,ScanRateMs\nx,DB1.DBW0,0\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.BadField && e.Column == "scanRateMs");
    }

    [Fact]
    public void Import_BadScale_IsBadFieldError()
    {
        var result = Import("Name,Address,Scale\nx,DB1.DBW0,notanumber\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.BadField && e.Column == "scale");
    }

    [Fact]
    public void Import_ReportsAllErrorsInOneRun()
    {
        var result = Import("Name,Address\n,DB1.DBZ0\nx,T5\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Count.Should().BeGreaterThanOrEqualTo(3, "empty name + bad address + unsupported address");
    }

    // ── cross-row rules ──────────────────────────────────────────────────

    [Fact]
    public void Import_DuplicateNames_Block()
    {
        var result = Import("Name,Address\ndup,DB1.DBW0\ndup,DB1.DBW2\n");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == S7ImportErrors.DuplicateName);
    }

    [Fact]
    public void Import_DuplicateAddresses_Warn_DoNotBlock()
    {
        var result = Import("Name,Address\na,DB1.DBW0\nb,DB1.DBW0\n");
        result.IsSuccess.Should().BeTrue("duplicate addresses warn, never block");
        result.Warnings.Should().Contain(w => w.Code == S7ImportErrors.DuplicateAddress);
    }
}
