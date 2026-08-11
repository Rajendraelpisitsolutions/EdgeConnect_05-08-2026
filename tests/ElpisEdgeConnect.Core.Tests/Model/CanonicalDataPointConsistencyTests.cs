// ============================================================================
// File: Model/CanonicalDataPointConsistencyTests.cs
// Covers: CanonicalDataPoint.IsConsistent() and TryValidateConsistency().
// Reference: docs/core/canonical-data-model.md "The Value/ValueType consistency rule"
//
// These tests pin the opt-in consistency contract: every CanonicalValueType
// must be matched against the runtime type of Value, and every mismatch must
// produce a clear reason string.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Model;

public sealed class CanonicalDataPointConsistencyTests
{
    private static CanonicalDataPoint BasePoint(object? value, CanonicalValueType type) =>
        new()
        {
            GatewayId = "GW-TEST",
            SourceInstanceId = "test-source",
            ProtocolName = "test",
            DeviceId = "dev",
            TagName = "tag",
            TagPath = "tag",
            Value = value,
            ValueType = type,
            Quality = DataQuality.Good,
            DeviceTimestamp = DateTime.UtcNow,
            GatewayTimestamp = DateTime.UtcNow,
            SequenceNumber = 1,
        };

    // ========================================================================
    // Positive cases — every value type with its matching runtime type
    // ========================================================================

    [Fact]
    public void IsConsistent_Null_WithNullValue_ReturnsTrue()
    {
        var point = BasePoint(null, CanonicalValueType.Null);
        point.IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_Boolean_WithBool_ReturnsTrue()
    {
        BasePoint(true, CanonicalValueType.Boolean).IsConsistent().Should().BeTrue();
        BasePoint(false, CanonicalValueType.Boolean).IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_Integer_WithInt_ReturnsTrue()
    {
        BasePoint(42, CanonicalValueType.Integer).IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_Long_WithLong_ReturnsTrue()
    {
        BasePoint(9_999_999_999L, CanonicalValueType.Long).IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_Float_WithFloat_ReturnsTrue()
    {
        BasePoint(3.14f, CanonicalValueType.Float).IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_Double_WithDouble_ReturnsTrue()
    {
        BasePoint(2.71828, CanonicalValueType.Double).IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_String_WithString_ReturnsTrue()
    {
        BasePoint("hello", CanonicalValueType.String).IsConsistent().Should().BeTrue();
        BasePoint(string.Empty, CanonicalValueType.String).IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_DateTime_WithDateTime_ReturnsTrue()
    {
        BasePoint(new DateTime(2026, 4, 7, 0, 0, 0, DateTimeKind.Utc), CanonicalValueType.DateTime)
            .IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_ByteArray_WithByteArray_ReturnsTrue()
    {
        BasePoint(new byte[] { 1, 2, 3 }, CanonicalValueType.ByteArray).IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_Array_WithAnyArray_ReturnsTrue()
    {
        BasePoint(new int[] { 1, 2, 3 }, CanonicalValueType.Array).IsConsistent().Should().BeTrue();
        BasePoint(new double[] { 1.0, 2.0 }, CanonicalValueType.Array).IsConsistent().Should().BeTrue();
        BasePoint(new string[] { "a", "b" }, CanonicalValueType.Array).IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_Object_WithReadOnlyDictionary_ReturnsTrue()
    {
        IReadOnlyDictionary<string, object> dict = new Dictionary<string, object>
        {
            ["a"] = 1,
            ["b"] = "two",
        };
        BasePoint(dict, CanonicalValueType.Object).IsConsistent().Should().BeTrue();
    }

    // ========================================================================
    // Negative cases — mismatches produce false and a reason
    // ========================================================================

    [Fact]
    public void IsConsistent_Null_WithNonNullValue_ReturnsFalseWithReason()
    {
        var point = BasePoint(42, CanonicalValueType.Null);

        point.IsConsistent().Should().BeFalse();
        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().NotBeNull().And.Contain("Null").And.Contain("Int32");
    }

    [Fact]
    public void IsConsistent_Integer_WithLong_ReturnsFalse()
    {
        var point = BasePoint(42L, CanonicalValueType.Integer);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("Integer").And.Contain("Int64");
    }

    [Fact]
    public void IsConsistent_Double_WithFloat_ReturnsFalse()
    {
        var point = BasePoint(3.14f, CanonicalValueType.Double);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("Double").And.Contain("Single");
    }

    [Fact]
    public void IsConsistent_String_WithNull_ReturnsFalse()
    {
        // The canonical rule: to represent a missing string, use ValueType.Null.
        // Pairing String + null is treated as a consistency violation.
        var point = BasePoint(null, CanonicalValueType.String);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("String").And.Contain("null");
    }

    [Fact]
    public void IsConsistent_Boolean_WithString_ReturnsFalseWithReason()
    {
        var point = BasePoint("true", CanonicalValueType.Boolean);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("Boolean").And.Contain("String");
    }

    [Fact]
    public void IsConsistent_Array_WithScalar_ReturnsFalse()
    {
        var point = BasePoint(42, CanonicalValueType.Array);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("Array").And.Contain("Int32");
    }

    [Fact]
    public void IsConsistent_Object_WithPlainPoco_ReturnsFalse()
    {
        var point = BasePoint(new { A = 1 }, CanonicalValueType.Object);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("Object").And.Contain("IReadOnlyDictionary");
    }

    [Fact]
    public void IsConsistent_ByteArray_WithIntArray_ReturnsFalse()
    {
        var point = BasePoint(new int[] { 1, 2 }, CanonicalValueType.ByteArray);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("ByteArray");
    }

    // ========================================================================
    // TryValidateConsistency contract: reason is null on success
    // ========================================================================

    [Fact]
    public void TryValidateConsistency_OnSuccess_ReasonIsNull()
    {
        var point = BasePoint(3500.0, CanonicalValueType.Double);

        point.TryValidateConsistency(out var reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    // ========================================================================
    // DateTime kind enforcement — Value with DateTime ValueType must be UTC.
    // Reference: docs/core/canonical-data-model.md "consistency rule" §3 + §4.
    // ========================================================================

    [Fact]
    public void IsConsistent_DateTime_WithUtcValue_ReturnsTrue()
    {
        var utc = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Utc);
        var point = BasePoint(utc, CanonicalValueType.DateTime);

        point.IsConsistent().Should().BeTrue();
    }

    [Fact]
    public void IsConsistent_DateTime_WithLocalValue_ReturnsFalseWithReason()
    {
        var local = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Local);
        var point = BasePoint(local, CanonicalValueType.DateTime);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("DateTime").And.Contain("Local").And.Contain("Utc");
    }

    [Fact]
    public void IsConsistent_DateTime_WithUnspecifiedValue_ReturnsFalseWithReason()
    {
        var unspecified = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Unspecified);
        var point = BasePoint(unspecified, CanonicalValueType.DateTime);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("DateTime").And.Contain("Unspecified").And.Contain("Utc");
    }

    // ========================================================================
    // Timestamp UTC enforcement — DeviceTimestamp and GatewayTimestamp must be UTC.
    // ========================================================================

    [Fact]
    public void IsConsistent_LocalDeviceTimestamp_ReturnsFalseWithReason()
    {
        var point = BasePoint(3500.0, CanonicalValueType.Double) with
        {
            DeviceTimestamp = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Local),
        };

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("DeviceTimestamp").And.Contain("Local").And.Contain("Utc");
    }

    [Fact]
    public void IsConsistent_UnspecifiedDeviceTimestamp_ReturnsFalseWithReason()
    {
        var point = BasePoint(3500.0, CanonicalValueType.Double) with
        {
            DeviceTimestamp = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Unspecified),
        };

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("DeviceTimestamp").And.Contain("Unspecified");
    }

    [Fact]
    public void IsConsistent_LocalGatewayTimestamp_ReturnsFalseWithReason()
    {
        var point = BasePoint(3500.0, CanonicalValueType.Double) with
        {
            GatewayTimestamp = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Local),
        };

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("GatewayTimestamp").And.Contain("Local").And.Contain("Utc");
    }

    [Fact]
    public void IsConsistent_UnspecifiedGatewayTimestamp_ReturnsFalseWithReason()
    {
        var point = BasePoint(3500.0, CanonicalValueType.Double) with
        {
            GatewayTimestamp = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Unspecified),
        };

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().Contain("GatewayTimestamp").And.Contain("Unspecified");
    }

    // ========================================================================
    // Out-of-range CanonicalValueType — pins the `default:` arm of the switch.
    // Without this test, deleting the default arm would not be caught.
    // ========================================================================

    [Fact]
    public void IsConsistent_OutOfRangeValueType_ReturnsFalseWithReason()
    {
        var point = BasePoint(42, (CanonicalValueType)999);

        point.TryValidateConsistency(out var reason).Should().BeFalse();
        reason.Should().NotBeNull().And.Contain("999");
    }
}
