// ============================================================================
// File: RowValidatorsTests.cs
// Purpose: Coverage for RowValidators — the per-cell semantic checks behind
//          the deviceId regex, MTConnect baseUrl scheme, enabled strict
//          casing, and required-cell non-empty findings per v3.1.
//          Implements PR I-1 spec tests T18..T25 + T30.
// ============================================================================

using ElpisEdgeConnect.Management.Api.BulkSourceMerge;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class RowValidatorsTests
{
    // ── ValidateDeviceId — implements v3.1 §5 + spec tests T18..T24 ───────────
    [Theory]
    [InlineData("cnc 007")]      // T18 — contains space
    [InlineData("cnc/007")]      // T19 — contains slash
    [InlineData("cnc.007")]      // T20 — contains dot
    [InlineData("MÜller-CNC")]   // T21 — Unicode
    [InlineData("Mill@2")]       // additional reject — @ symbol
    public void ValidateDeviceId_RejectsIllegalCharacters(string deviceId)
    {
        var finding = RowValidators.ValidateDeviceId(deviceId, lineNumber: 2);

        finding.Should().NotBeNull();
        finding!.Code.Should().Be(BulkSourceMergeErrorCode.DeviceIdFormatInvalid);
        finding.CsvRow.Should().Be(2);
    }

    [Fact]
    public void ValidateDeviceId_RejectsEmpty()  // T22
    {
        var finding = RowValidators.ValidateDeviceId("", lineNumber: 3);

        finding.Should().NotBeNull();
        finding!.Code.Should().Be(BulkSourceMergeErrorCode.DeviceIdFormatInvalid);
    }

    [Fact]
    public void ValidateDeviceId_RejectsOver64Chars()  // T23
    {
        var tooLong = new string('a', 65);

        var finding = RowValidators.ValidateDeviceId(tooLong, lineNumber: 4);

        finding.Should().NotBeNull();
        finding!.Code.Should().Be(BulkSourceMergeErrorCode.DeviceIdFormatInvalid);
        finding.Message.Should().Contain("65 chars");
    }

    [Theory]
    [InlineData("cnc-001")]
    [InlineData("CNC_002")]
    [InlineData("a")]  // 1-char boundary
    public void ValidateDeviceId_AcceptsValidIds(string deviceId)  // T24
    {
        RowValidators.ValidateDeviceId(deviceId, lineNumber: 5).Should().BeNull();
    }

    [Fact]
    public void ValidateDeviceId_AcceptsExactly64CharsLowerBoundary()
    {
        var sixtyFour = new string('a', 64);

        RowValidators.ValidateDeviceId(sixtyFour, lineNumber: 5).Should().BeNull();
    }

    // ── ValidateMtConnectBaseUrl — implements v3.1 §9 + spec test T25 ────────
    [Theory]
    [InlineData("http://192.168.10.51:5000/")]
    [InlineData("http://192.168.10.51:5000")]
    [InlineData("https://example.local/mtconnect/")]
    [InlineData("http://example.local:8080/agent/")]
    public void ValidateMtConnectBaseUrl_AcceptsHttpAndHttps(string baseUrl)
    {
        RowValidators.ValidateMtConnectBaseUrl(baseUrl, lineNumber: 2).Should().BeNull();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.local/")]
    [InlineData("ssh://example.local/")]
    public void ValidateMtConnectBaseUrl_RejectsOtherSchemes(string baseUrl)  // T25
    {
        var finding = RowValidators.ValidateMtConnectBaseUrl(baseUrl, lineNumber: 7);

        finding.Should().NotBeNull();
        finding!.Code.Should().Be(BulkSourceMergeErrorCode.MtConnectBaseUrlInvalid);
        finding.CsvRow.Should().Be(7);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    [InlineData("192.168.10.51:5000")]
    public void ValidateMtConnectBaseUrl_RejectsNonAbsoluteOrMalformed(string baseUrl)
    {
        RowValidators.ValidateMtConnectBaseUrl(baseUrl, lineNumber: 2)
            .Should().NotBeNull()
            .And.Subject.As<BulkSourceMergeFinding>()
            .Code.Should().Be(BulkSourceMergeErrorCode.MtConnectBaseUrlInvalid);
    }

    [Fact]
    public void ValidateMtConnectBaseUrl_RejectsEmpty()
    {
        RowValidators.ValidateMtConnectBaseUrl("", lineNumber: 2)
            .Should().NotBeNull()
            .And.Subject.As<BulkSourceMergeFinding>()
            .Code.Should().Be(BulkSourceMergeErrorCode.MtConnectBaseUrlInvalid);
    }

    // ── ValidateEnabledValue — implements spec test T30 ──────────────────────
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void ValidateEnabledValue_AcceptsExactLowercase(string value)
    {
        RowValidators.ValidateEnabledValue(value, lineNumber: 2).Should().BeNull();
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData("FALSE")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("")]
    public void ValidateEnabledValue_RejectsAnythingOtherThanExactTrueOrFalse(string value)  // T30
    {
        var finding = RowValidators.ValidateEnabledValue(value, lineNumber: 5);

        finding.Should().NotBeNull();
        finding!.Code.Should().Be(BulkSourceMergeErrorCode.EnabledValueInvalid);
        finding.CsvRow.Should().Be(5);
    }

    // ── ValidateRequiredCellNonEmpty ─────────────────────────────────────────
    [Fact]
    public void ValidateRequiredCellNonEmpty_FlagsEmpty()
    {
        var finding = RowValidators.ValidateRequiredCellNonEmpty("deviceName", "", lineNumber: 2);

        finding.Should().NotBeNull();
        finding!.Code.Should().Be(BulkSourceMergeErrorCode.RowMissingValue);
        finding.Subject.Should().Be("deviceName");
    }

    [Fact]
    public void ValidateRequiredCellNonEmpty_FlagsWhitespaceOnly()
    {
        var finding = RowValidators.ValidateRequiredCellNonEmpty("deviceName", "   ", lineNumber: 2);

        finding.Should().NotBeNull();
        finding!.Code.Should().Be(BulkSourceMergeErrorCode.RowMissingValue);
    }

    [Fact]
    public void ValidateRequiredCellNonEmpty_AcceptsNonEmpty()
    {
        RowValidators.ValidateRequiredCellNonEmpty("deviceName", "Lathe-A", lineNumber: 2).Should().BeNull();
    }
}
