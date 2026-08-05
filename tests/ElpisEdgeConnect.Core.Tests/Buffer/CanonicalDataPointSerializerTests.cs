// ============================================================================
// File: Buffer/CanonicalDataPointSerializerTests.cs
// Covers: round-trip every CanonicalValueType + metadata + DateTime UTC
//         preservation, on BOTH BinaryWriterFormat and MessagePackFormat.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class CanonicalDataPointSerializerTests
{
    public static IEnumerable<object[]> AllFormats =>
        new[]
        {
            new object[] { CanonicalDataPointSerializer.Binary },
            new object[] { CanonicalDataPointSerializer.MessagePack },
        };

    private static CanonicalDataPoint Sample(object? value, CanonicalValueType type, IReadOnlyDictionary<string, object>? metadata = null)
    {
        return new CanonicalDataPointBuilder()
            .WithGateway("GW-001")
            .WithSource("focas-1", "focas2")
            .WithDevice("Jyoti17", "Jyoti 17 CNC")
            .WithTag("spindle.speed", "Spindle/Speed", originalTagName: "raw.rpm")
            .WithValue(value, type)
            .WithUnit("rpm")
            .WithGoodQuality(new DateTime(2026, 4, 7, 12, 30, 0, DateTimeKind.Utc))
            .WithMetadata(metadata)
            .WithSequence(42)
            .Build();
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_NullValue(CanonicalDataPointSerializer s)
    {
        var p = Sample(null, CanonicalValueType.Null);
        var r = s.Deserialize(s.Serialize(p));
        r.ValueType.Should().Be(CanonicalValueType.Null);
        r.Value.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_Boolean(CanonicalDataPointSerializer s)
    {
        var r = s.Deserialize(s.Serialize(Sample(true, CanonicalValueType.Boolean)));
        r.Value.Should().Be(true);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_Integer(CanonicalDataPointSerializer s)
    {
        var r = s.Deserialize(s.Serialize(Sample(1234, CanonicalValueType.Integer)));
        r.Value.Should().Be(1234);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_Long(CanonicalDataPointSerializer s)
    {
        var r = s.Deserialize(s.Serialize(Sample(9_999_999_999L, CanonicalValueType.Long)));
        r.Value.Should().Be(9_999_999_999L);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_Float(CanonicalDataPointSerializer s)
    {
        var r = s.Deserialize(s.Serialize(Sample(3.14f, CanonicalValueType.Float)));
        ((float)r.Value!).Should().BeApproximately(3.14f, 0.0001f);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_Double(CanonicalDataPointSerializer s)
    {
        var r = s.Deserialize(s.Serialize(Sample(2.71828, CanonicalValueType.Double)));
        ((double)r.Value!).Should().BeApproximately(2.71828, 0.000001);
    }

    // ------------------------------------------------------------------------
    // Numeric-boxing tolerance (regression).
    //
    // An adapter may box a value whose CLR runtime type differs from the
    // declared CanonicalValueType — e.g. MTConnect production/parts_count is a
    // counter read via a long path but historically mapped to ValueType.Integer.
    // BinaryWriterFormat previously hard-unboxed (`(int)value!`), which threw
    // InvalidCastException on a boxed long. That exception is not a
    // SqliteException, so it escaped the buffer write path uncaught and
    // permanently stranded the route's intake pump — every store-and-forward
    // route carrying such a point silently stopped delivering. Both formats
    // must coerce compatible numeric boxes losslessly instead of throwing.
    // ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_Integer_AcceptsBoxedLong(CanonicalDataPointSerializer s)
    {
        // The exact production bug: ValueType.Integer with a boxed System.Int64.
        object boxedLong = 42L;
        var act = () => s.Deserialize(s.Serialize(Sample(boxedLong, CanonicalValueType.Integer)));

        act.Should().NotThrow();
        act().Value.Should().Be(42);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_Long_AcceptsBoxedInt(CanonicalDataPointSerializer s)
    {
        object boxedInt = 7;
        var r = s.Deserialize(s.Serialize(Sample(boxedInt, CanonicalValueType.Long)));
        r.Value.Should().Be(7L);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_Double_AcceptsBoxedIntegralTypes(CanonicalDataPointSerializer s)
    {
        object boxedLong = 9L;
        var r = s.Deserialize(s.Serialize(Sample(boxedLong, CanonicalValueType.Double)));
        ((double)r.Value!).Should().Be(9.0);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_String(CanonicalDataPointSerializer s)
    {
        var r = s.Deserialize(s.Serialize(Sample("hello world", CanonicalValueType.String)));
        r.Value.Should().Be("hello world");
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_DateTime_PreservesUtcKind(CanonicalDataPointSerializer s)
    {
        var dt = new DateTime(2026, 4, 7, 12, 30, 0, DateTimeKind.Utc);
        var r = s.Deserialize(s.Serialize(Sample(dt, CanonicalValueType.DateTime)));
        var roundTripped = (DateTime)r.Value!;
        roundTripped.Should().Be(dt);
        roundTripped.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_ByteArray(CanonicalDataPointSerializer s)
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var r = s.Deserialize(s.Serialize(Sample(bytes, CanonicalValueType.ByteArray)));
        ((byte[])r.Value!).Should().Equal(bytes);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_Metadata(CanonicalDataPointSerializer s)
    {
        var metadata = new Dictionary<string, object>
        {
            ["site"] = "Plant A",
            ["line"] = 3,
            ["shift"] = "B",
            ["enabled"] = true,
        };
        var r = s.Deserialize(s.Serialize(Sample(42.0, CanonicalValueType.Double, metadata)));
        r.Metadata.Should().NotBeNull();
        r.Metadata!["site"].Should().Be("Plant A");
        r.Metadata["enabled"].Should().Be(true);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_PreservesAllScalarFields(CanonicalDataPointSerializer s)
    {
        var p = Sample(3000.0, CanonicalValueType.Double);
        var r = s.Deserialize(s.Serialize(p));

        r.GatewayId.Should().Be(p.GatewayId);
        r.SourceInstanceId.Should().Be(p.SourceInstanceId);
        r.ProtocolName.Should().Be(p.ProtocolName);
        r.DeviceId.Should().Be(p.DeviceId);
        r.DeviceName.Should().Be(p.DeviceName);
        r.TagName.Should().Be(p.TagName);
        r.TagPath.Should().Be(p.TagPath);
        r.OriginalTagName.Should().Be(p.OriginalTagName);
        r.Unit.Should().Be(p.Unit);
        r.Quality.Should().Be(p.Quality);
        r.SequenceNumber.Should().Be(p.SequenceNumber);
        r.DeviceTimestamp.Should().Be(p.DeviceTimestamp);
        r.GatewayTimestamp.Should().Be(p.GatewayTimestamp);
    }

    // ========================================================================
    // C2a close-out pins (independent review mutation coverage)
    // ========================================================================

    /// <summary>
    /// R3 pin (Mutation 17): large integer values must round-trip exactly as
    /// <c>int</c>, not silently widen to <c>long</c> via the typeless resolver.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_LargeInteger_PreservesIntType(CanonicalDataPointSerializer s)
    {
        var p = Sample(int.MaxValue, CanonicalValueType.Integer);
        var r = s.Deserialize(s.Serialize(p));
        r.Value.Should().BeOfType<int>();
        r.Value.Should().Be(int.MaxValue);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_NegativeInteger(CanonicalDataPointSerializer s)
    {
        var p = Sample(int.MinValue, CanonicalValueType.Integer);
        var r = s.Deserialize(s.Serialize(p));
        r.Value.Should().Be(int.MinValue);
    }

    /// <summary>
    /// R6 pin: <see cref="CanonicalValueType.Object"/> (nested dictionary)
    /// must round-trip end-to-end.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RoundTrip_ObjectValue(CanonicalDataPointSerializer s)
    {
        var nested = new Dictionary<string, object>
        {
            ["alarm"] = "spindle-stall",
            ["severity"] = 3,
            ["latched"] = true,
        };
        var p = Sample(nested, CanonicalValueType.Object);
        var r = s.Deserialize(s.Serialize(p));

        r.ValueType.Should().Be(CanonicalValueType.Object);
        var dict = (System.Collections.Generic.IReadOnlyDictionary<string, object>)r.Value!;
        dict["alarm"].Should().Be("spindle-stall");
        dict["latched"].Should().Be(true);
    }

    /// <summary>
    /// R2 pin (Mutations 13 + 16): non-UTC <see cref="DateTime"/> values must
    /// be REJECTED by the serializer rather than silently shifted via
    /// <c>ToUniversalTime()</c>. Catches the regression where a Local-kind
    /// timestamp would be off by the host's UTC offset on the wire.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllFormats))]
    public void Serialize_NonUtcDeviceTimestamp_Throws(CanonicalDataPointSerializer s)
    {
        // Bypass the builder which forces UTC, by constructing the record directly.
        var local = new DateTime(2026, 4, 7, 12, 30, 0, DateTimeKind.Local);
        var bad = Sample(1.0, CanonicalValueType.Double) with
        {
            DeviceTimestamp = local,
        };

        System.Action act = () => s.Serialize(bad);
        act.Should().Throw<System.IO.InvalidDataException>()
            .WithMessage("*Utc*");
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void Serialize_NonUtcDateTimeValue_Throws(CanonicalDataPointSerializer s)
    {
        var local = new DateTime(2026, 4, 7, 12, 30, 0, DateTimeKind.Local);
        var bad = Sample(local, CanonicalValueType.DateTime);

        System.Action act = () => s.Serialize(bad);
        act.Should().Throw<System.IO.InvalidDataException>();
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void Batch_RoundTrips(CanonicalDataPointSerializer s)
    {
        var input = new[]
        {
            Sample(1.0, CanonicalValueType.Double),
            Sample(2L, CanonicalValueType.Long),
            Sample("text", CanonicalValueType.String),
        };
        var bytes = s.SerializeBatch(input);
        var output = s.DeserializeBatch(bytes);

        output.Should().HaveCount(3);
        output[0].Value.Should().Be(1.0);
        output[1].Value.Should().Be(2L);
        output[2].Value.Should().Be("text");
    }
}
