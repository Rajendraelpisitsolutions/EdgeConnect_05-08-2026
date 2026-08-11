// ============================================================================
// Tests: S7TagCompatibility — the shared datatype/address-width rule used by
//        the S7 source wizard. The headline guarantee (M.2b.2 v2) is PARITY
//        with S7ScanPlanner: the checker reports Error for EXACTLY the
//        combinations the planner rejects at adapter Initialize, so a config
//        the wizard accepts never fails Initialize for a deterministic
//        compatibility reason.
// ============================================================================

using System;
using ElpisEdgeConnect.Sources.S7;
using ElpisEdgeConnect.Sources.S7.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests;

public class S7TagCompatibilityTests
{
    private static S7DatatypeSpec Spec(string wireDatatype) =>
        S7DatatypeParser.Parse(wireDatatype, defaultSpec: default);

    [Fact]
    public void Check_BitAddress_WithBool_IsCompatible()
    {
        var v = S7TagCompatibility.Check(S7AddressParser.Parse("DB1.DBX0.0"), Spec("bool"));
        v.Severity.Should().Be(S7CompatibilitySeverity.Compatible);
        v.BlocksSave.Should().BeFalse();
    }

    [Theory]
    [InlineData("DB1.DBX0.0", "int")]
    [InlineData("DB1.DBX0.0", "word")]
    [InlineData("M10.2", "real")]
    [InlineData("I0.0", "int")]
    [InlineData("Q0.1", "byte")]
    public void Check_BitAddress_WithNonBool_IsBlockingError(string address, string datatype)
    {
        var v = S7TagCompatibility.Check(S7AddressParser.Parse(address), Spec(datatype));

        v.Severity.Should().Be(S7CompatibilitySeverity.Error);
        v.BlocksSave.Should().BeTrue();
        v.Code.Should().Be(S7CompatibilityVerdict.BitRequiresBoolCode);
    }

    [Theory]
    [InlineData("DB1.DBW0", "real")] // 4 bytes into a 2-byte word
    [InlineData("DB1.DBW0", "dint")]
    [InlineData("MW4", "dint")]
    [InlineData("DB1.DBD0", "lreal")] // 8 bytes into a 4-byte dword
    [InlineData("DB1.DBB0", "word")] // 2 bytes into a 1-byte byte
    public void Check_DatatypeWiderThanAddress_IsBlockingError(string address, string datatype)
    {
        var v = S7TagCompatibility.Check(S7AddressParser.Parse(address), Spec(datatype));

        v.Severity.Should().Be(S7CompatibilitySeverity.Error);
        v.BlocksSave.Should().BeTrue();
        v.Code.Should().Be(S7CompatibilityVerdict.DatatypeTooWideCode);
    }

    [Theory]
    [InlineData("DB1.DBW0", "bool")] // 1 byte under a 2-byte word
    [InlineData("DB1.DBD0", "int")]  // 2 bytes under a 4-byte dword
    public void Check_DatatypeNarrowerThanAddress_IsNonBlockingWarning(string address, string datatype)
    {
        var v = S7TagCompatibility.Check(S7AddressParser.Parse(address), Spec(datatype));

        v.Severity.Should().Be(S7CompatibilitySeverity.Warning);
        v.BlocksSave.Should().BeFalse();
        v.Code.Should().Be(S7CompatibilityVerdict.DatatypeNarrowerCode);
    }

    [Theory]
    [InlineData("DB1.DBW0", "int")]
    [InlineData("DB1.DBW0", "word")]
    [InlineData("DB1.DBD0", "dint")]
    [InlineData("DB1.DBD0", "real")]
    [InlineData("DB1.DBB0", "byte")]
    public void Check_ExactWidthMatch_IsCompatible(string address, string datatype)
    {
        S7TagCompatibility.Check(S7AddressParser.Parse(address), Spec(datatype))
            .Severity.Should().Be(S7CompatibilitySeverity.Compatible);
    }

    [Theory]
    [InlineData("DB1.DBW0")]
    [InlineData("DB1.DBD0")]
    [InlineData("DB1.DBB0")]
    public void Check_StringDatatype_IsExempt_FromWidthRules(string address)
    {
        // Strings declare their own length and ignore the address width —
        // the planner allows them, so the checker must too.
        S7TagCompatibility.Check(S7AddressParser.Parse(address), Spec("string[16]"))
            .Severity.Should().Be(S7CompatibilitySeverity.Compatible);
    }

    // ── PARITY: checker Error ⟺ planner throws ───────────────────────────
    // The single most important guarantee. Build a one-tag scan plan and
    // assert the planner throws for exactly the combinations the checker
    // marks as a blocking Error.
    [Theory]
    // bit-form addresses
    [InlineData("DB1.DBX0.0", "Bool")]
    [InlineData("DB1.DBX0.0", "Int")]
    [InlineData("DB1.DBX0.0", "Word")]
    [InlineData("DB1.DBX0.0", "Real")]
    [InlineData("M10.2", "Bool")]
    [InlineData("M10.2", "Byte")]
    [InlineData("I0.0", "Int")]
    [InlineData("Q0.1", "Bool")]
    // word addresses
    [InlineData("DB1.DBW0", "Int")]
    [InlineData("DB1.DBW0", "Word")]
    [InlineData("DB1.DBW0", "Bool")]
    [InlineData("DB1.DBW0", "Byte")]
    [InlineData("DB1.DBW0", "DInt")]
    [InlineData("DB1.DBW0", "Real")]
    [InlineData("DB1.DBW0", "String")]
    [InlineData("MW4", "Int")]
    [InlineData("IW2", "DInt")]
    // dword addresses
    [InlineData("DB1.DBD0", "DInt")]
    [InlineData("DB1.DBD0", "DWord")]
    [InlineData("DB1.DBD0", "Real")]
    [InlineData("DB1.DBD0", "Int")]
    [InlineData("DB1.DBD0", "LReal")]
    // byte addresses
    [InlineData("DB1.DBB0", "Byte")]
    [InlineData("DB1.DBB0", "SInt")]
    [InlineData("DB1.DBB0", "USInt")]
    [InlineData("DB1.DBB0", "Char")]
    [InlineData("DB1.DBB0", "Bool")]
    [InlineData("DB1.DBB0", "Int")]
    [InlineData("DB1.DBB0", "Word")]
    public void Check_BlockingError_MatchesPlannerRejection(string address, string datatypeName)
    {
        var wire = ToWire(datatypeName);
        var checkerIsError =
            S7TagCompatibility.Check(S7AddressParser.Parse(address), Spec(wire)).Severity
                == S7CompatibilitySeverity.Error;

        var plannerThrew = PlannerRejects(address, wire);

        checkerIsError.Should().Be(
            plannerThrew,
            "the wizard checker must block exactly the combos the scan planner rejects at Initialize " +
            "({0} @ {1})", datatypeName, address);
    }

    private static bool PlannerRejects(string address, string wireDatatype)
    {
        var tag = new S7TagDefinition { Name = "t", Address = address, Datatype = wireDatatype, ScanRateMs = 1000 };
        try
        {
            S7ScanPlanner.Build(new[] { tag }, maxGapBytes: 16, maxReadBytes: 200);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static string ToWire(string datatypeName) =>
        string.Equals(datatypeName, "String", StringComparison.OrdinalIgnoreCase)
            ? "string[16]"
            : datatypeName;
}
