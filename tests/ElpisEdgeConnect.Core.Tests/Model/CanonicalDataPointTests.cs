// ============================================================================
// File: Model/CanonicalDataPointTests.cs
// Covers: ARCHITECTURE_BLUEPRINT.md Section 4.1 contract guarantees.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Model;

public sealed class CanonicalDataPointTests
{
    private static CanonicalDataPoint CreateValidPoint() =>
        new()
        {
            GatewayId = "GW-TEST-001",
            SourceInstanceId = "focas-jyoti17",
            ProtocolName = "focas2",
            DeviceId = "Jyoti17CNC",
            DeviceName = "Jyoti 17 CNC",
            TagName = "spindle.speed",
            TagPath = "Spindle/Speed",
            OriginalTagName = "Spindle/Speed",
            Value = 3500.0,
            ValueType = CanonicalValueType.Double,
            Unit = "rpm",
            Quality = DataQuality.Good,
            QualityReason = null,
            DeviceTimestamp = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Utc),
            GatewayTimestamp = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Utc),
            Metadata = null,
            SequenceNumber = 42,
        };

    [Fact]
    public void Construction_WithAllRequiredFields_Succeeds()
    {
        var point = CreateValidPoint();

        point.GatewayId.Should().Be("GW-TEST-001");
        point.SourceInstanceId.Should().Be("focas-jyoti17");
        point.ProtocolName.Should().Be("focas2");
        point.DeviceId.Should().Be("Jyoti17CNC");
        point.TagName.Should().Be("spindle.speed");
        point.TagPath.Should().Be("Spindle/Speed");
        point.Value.Should().Be(3500.0);
        point.ValueType.Should().Be(CanonicalValueType.Double);
        point.Unit.Should().Be("rpm");
        point.Quality.Should().Be(DataQuality.Good);
        point.SequenceNumber.Should().Be(42);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = CreateValidPoint();
        var b = CreateValidPoint();

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentSequenceNumber_AreNotEqual()
    {
        var a = CreateValidPoint();
        var b = a with { SequenceNumber = 43 };

        a.Should().NotBe(b);
    }

    [Fact]
    public void CopyWith_NewValue_LeavesOriginalUnchanged()
    {
        var original = CreateValidPoint();
        var updated = original with { Value = 4000.0 };

        updated.Value.Should().Be(4000.0);
        original.Value.Should().Be(3500.0);
    }

    [Fact]
    public void NullValue_WithNullValueType_IsValid()
    {
        var point = CreateValidPoint() with
        {
            Value = null,
            ValueType = CanonicalValueType.Null,
            Quality = DataQuality.Bad,
            QualityReason = "read timeout",
        };

        point.Value.Should().BeNull();
        point.ValueType.Should().Be(CanonicalValueType.Null);
        point.Quality.Should().Be(DataQuality.Bad);
        point.QualityReason.Should().Be("read timeout");
    }

    [Fact]
    public void Metadata_WhenProvided_IsReadOnlyAccessible()
    {
        var metadata = new Dictionary<string, object>
        {
            ["site"] = "Menon",
            ["line"] = "Bay-1",
            ["shift"] = 2,
        };

        var point = CreateValidPoint() with { Metadata = metadata };

        point.Metadata.Should().NotBeNull();
        point.Metadata!["site"].Should().Be("Menon");
        point.Metadata["line"].Should().Be("Bay-1");
        point.Metadata["shift"].Should().Be(2);
    }

    [Theory]
    [InlineData(CanonicalValueType.Boolean, true)]
    [InlineData(CanonicalValueType.Integer, 42)]
    [InlineData(CanonicalValueType.Long, 9999999999L)]
    [InlineData(CanonicalValueType.Float, 3.14f)]
    [InlineData(CanonicalValueType.Double, 2.71828)]
    [InlineData(CanonicalValueType.String, "hello")]
    public void AllPrimitiveValueTypes_CanBeStored(CanonicalValueType type, object value)
    {
        var point = CreateValidPoint() with
        {
            Value = value,
            ValueType = type,
        };

        point.Value.Should().Be(value);
        point.ValueType.Should().Be(type);
    }

    [Fact]
    public void ByteArrayValue_Roundtrips()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var point = CreateValidPoint() with
        {
            Value = bytes,
            ValueType = CanonicalValueType.ByteArray,
        };

        point.Value.Should().BeEquivalentTo(bytes);
        point.ValueType.Should().Be(CanonicalValueType.ByteArray);
    }

    [Fact]
    public void Timestamps_DeviceAndGatewayCanDiffer()
    {
        var device = new DateTime(2026, 4, 7, 10, 29, 59, 800, DateTimeKind.Utc);
        var gateway = new DateTime(2026, 4, 7, 10, 30, 0, 50, DateTimeKind.Utc);

        var point = CreateValidPoint() with
        {
            DeviceTimestamp = device,
            GatewayTimestamp = gateway,
        };

        point.DeviceTimestamp.Should().Be(device);
        point.GatewayTimestamp.Should().Be(gateway);
        (point.GatewayTimestamp - point.DeviceTimestamp).Should().BePositive();
    }
}
