// ============================================================================
// Tests: S7AddressValidationService (M.2b.2) — parser-only validation for the
//        wizard's live address field. Pins: valid addresses return the
//        normalized form + parsed parts + datatype suggestions; malformed
//        addresses return IsValid=false with friendly copy (never raw
//        exception text); Timer/Counter parse but are reported as UNSUPPORTED
//        (not malformed) per the M.2b.2 v2 decision.
// ============================================================================

using ElpisEdgeConnect.Management.Api;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class S7AddressValidationServiceTests
{
    private static S7AddressValidationResult Validate(string address) =>
        new S7AddressValidationService().Validate(new S7AddressValidationRequest { Address = address });

    [Fact]
    public void Validate_ValidWordAddress_ReturnsNormalizedAndSuggestions()
    {
        var result = Validate("DB1.DBW0");

        result.IsValid.Should().BeTrue();
        result.NormalizedAddress.Should().Be("DB1.DBW0");
        result.Area.Should().Be("DataBlock");
        result.DbNumber.Should().Be(1);
        result.ByteOffset.Should().Be(0);
        result.WidthHint.Should().Be("Word");
        result.SuggestedDatatypes.Should().Equal("Int", "Word");
    }

    [Fact]
    public void Validate_ValidBitAddress_SuggestsBool_AndReportsBitOffset()
    {
        var result = Validate("DB1.DBX2.3");

        result.IsValid.Should().BeTrue();
        result.WidthHint.Should().Be("Bit");
        result.BitOffset.Should().Be(3);
        result.SuggestedDatatypes.Should().Equal("Bool");
    }

    [Fact]
    public void Validate_ValidDWordAddress_SuggestsDIntDWordReal()
    {
        Validate("DB1.DBD4").SuggestedDatatypes.Should().Equal("DInt", "DWord", "Real");
    }

    [Theory]
    [InlineData("")]
    [InlineData("DB1.DBZ0")]
    [InlineData("DB.DBW0")]
    [InlineData("DB1.DBX0.8")]
    [InlineData("X1.DBW0")]
    public void Validate_MalformedAddress_IsInvalid_WithFriendlyMessage(string address)
    {
        var result = Validate(address);

        result.IsValid.Should().BeFalse();
        result.Message.Should().NotBeNullOrWhiteSpace();
        result.ErrorCode.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_UnknownDbWidth_MentionsValidWidths()
    {
        var result = Validate("DB1.DBZ0");
        result.Message.Should().Contain("DBW");
    }

    [Fact]
    public void Validate_MissingDbNumber_SaysSo()
    {
        Validate("DB.DBW0").Message.Should().Contain("DB number");
    }

    [Fact]
    public void Validate_BitOutOfRange_SaysBitOffset0To7()
    {
        Validate("DB1.DBX0.8").Message.Should().Contain("0–7");
    }

    [Theory]
    [InlineData("T5", "Timer")]
    [InlineData("C3", "Counter")]
    public void Validate_TimerCounter_IsUnsupported_NotMalformed(string address, string areaWord)
    {
        var result = Validate(address);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("S7.UNSUPPORTED_TIMER_COUNTER_ADDRESS");
        result.Message.Should().Contain(areaWord);
        // The parsed parts are still surfaced so the UI can explain.
        result.NormalizedAddress.Should().Be(address);
        result.SuggestedDatatypes.Should().BeEmpty();
    }
}
