// ============================================================================
// File: RawValueFormatterTests.cs
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.Mqtt;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.Mqtt.Tests;

public sealed class RawValueFormatterTests
{
    [Fact]
    public void Format_Null_ReturnsEmptyString()
        => RawValueFormatter.Format(null, CanonicalValueType.Null).Should().Be("");

    [Fact]
    public void Format_Boolean_LowercaseTrueFalse()
    {
        RawValueFormatter.Format(true, CanonicalValueType.Boolean).Should().Be("true");
        RawValueFormatter.Format(false, CanonicalValueType.Boolean).Should().Be("false");
    }

    [Fact]
    public void Format_Integer_InvariantCulture()
        => RawValueFormatter.Format(42, CanonicalValueType.Integer).Should().Be("42");

    [Fact]
    public void Format_Long_InvariantCulture()
        => RawValueFormatter.Format(12345678L, CanonicalValueType.Long).Should().Be("12345678");

    [Fact]
    public void Format_Double_InvariantCulture()
        => RawValueFormatter.Format(1200.5, CanonicalValueType.Double).Should().Be("1200.5");

    [Fact]
    public void Format_String_Passthrough()
        => RawValueFormatter.Format("MANUAL", CanonicalValueType.String).Should().Be("MANUAL");

    [Fact]
    public void Format_DateTime_Iso8601Utc()
    {
        var dt = new DateTime(2026, 4, 9, 10, 30, 0, DateTimeKind.Utc);
        RawValueFormatter.Format(dt, CanonicalValueType.DateTime)
            .Should().Be("2026-04-09T10:30:00.000Z");
    }

    [Fact]
    public void Format_DateTime_UnspecifiedAssumedUtc()
    {
        var dt = new DateTime(2026, 4, 9, 10, 30, 0, DateTimeKind.Unspecified);
        var result = RawValueFormatter.Format(dt, CanonicalValueType.DateTime);
        // Unspecified is assumed UTC — the suffix must be Z, not local offset.
        result.Should().EndWith("Z");
        result.Should().Be("2026-04-09T10:30:00.000Z");
    }

    [Fact]
    public void Format_ByteArray_ReturnsNull_ForRejection()
        => RawValueFormatter.Format(new byte[] { 1, 2, 3 }, CanonicalValueType.ByteArray)
            .Should().BeNull();
}
