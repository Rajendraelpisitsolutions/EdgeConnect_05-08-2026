// ============================================================================
// File: Buffer/LatestValueEnvelopeV1Tests.cs
// Covers: K1.2c LatestValueEnvelopeV1 codec — round-trips every persisted metric
//         arm (Boolean, Integer, Long, Float, Double, String, DateTime, ByteArray,
//         known-null-with-real-datatype), quality + quality reason, unit, and the
//         immutable static-property scalar set; and fails closed
//         (RouteStoreEnvelopeUnsupported) on an unknown codec version, a datatype↔
//         column mismatch, a negative sequence column, a corrupted BLOB, and trailing
//         bytes. Encoding is fixed-layout with explicit discriminators — never typeless.
// Reference: plan v3 §5 / v3.1-amendment §5; K1.2c handoff §3 item 1.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class LatestValueEnvelopeV1Tests
{
    private static readonly CanonicalMetricKey Key =
        CanonicalMetricKey.Create("src-1", "dev-1", "Spindle/Speed");

    private static readonly DateTimeOffset Ts =
        new(2026, 7, 14, 10, 30, 15, TimeSpan.Zero);

    private static LatestMetricValue Roundtrip(LatestMetricValue original)
    {
        var bytes = LatestValueEnvelopeV1.Encode(original);
        return LatestValueEnvelopeV1.Decode(bytes, original.Metric, original.ValueType, original.RouteBufferSequence);
    }

    private static LatestMetricValue Make(
        CanonicalValueType type,
        object? value,
        bool isNull = false,
        DataQuality quality = DataQuality.Good,
        string? qualityReason = null,
        string? unit = null,
        IReadOnlyDictionary<string, object?>? staticProps = null,
        long seq = 7) =>
        LatestMetricValue.Create(Key, type, value, isNull, Ts, quality, seq, qualityReason, unit, staticProps);

    // ---- scalar value arms round-trip ---------------------------------------

    [Fact]
    public void Roundtrip_Boolean()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Boolean, true));
        decoded.ValueType.Should().Be(CanonicalValueType.Boolean);
        decoded.Value.Should().Be(true);
        decoded.IsNull.Should().BeFalse();
    }

    [Fact]
    public void Roundtrip_Integer()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Integer, -12345));
        decoded.Value.Should().Be(-12345);
    }

    [Fact]
    public void Roundtrip_Long()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Long, 9_000_000_000L));
        decoded.Value.Should().Be(9_000_000_000L);
    }

    [Fact]
    public void Roundtrip_Float()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Float, 3.5f));
        decoded.Value.Should().Be(3.5f);
    }

    [Fact]
    public void Roundtrip_Double()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Double, 2.718281828459045));
        decoded.Value.Should().Be(2.718281828459045);
    }

    [Fact]
    public void Roundtrip_String()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.String, "hëllo•世界"));
        decoded.Value.Should().Be("hëllo•世界");
    }

    [Fact]
    public void Roundtrip_DateTime_Preserves_Ticks_And_Kind()
    {
        var dt = new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc);
        var decoded = Roundtrip(Make(CanonicalValueType.DateTime, dt));
        decoded.Value.Should().BeOfType<DateTime>();
        var got = (DateTime)decoded.Value!;
        got.Ticks.Should().Be(dt.Ticks);
        got.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Roundtrip_ByteArray_As_Raw_Bytes()
    {
        var raw = new byte[] { 0x00, 0xDE, 0xAD, 0xBE, 0xEF, 0xFF };
        var decoded = Roundtrip(Make(CanonicalValueType.ByteArray, raw));
        decoded.Value.Should().BeOfType<ImmutableArray<byte>>();
        ((ImmutableArray<byte>)decoded.Value!).AsSpan().ToArray().Should().Equal(raw);
    }

    [Fact]
    public void Roundtrip_Empty_ByteArray()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.ByteArray, Array.Empty<byte>()));
        ((ImmutableArray<byte>)decoded.Value!).Length.Should().Be(0);
    }

    // ---- null-with-real-datatype -------------------------------------------

    [Fact]
    public void Roundtrip_Null_Value_Keeps_Real_Datatype()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Double, null, isNull: true, quality: DataQuality.Bad));
        decoded.IsNull.Should().BeTrue();
        decoded.Value.Should().BeNull();
        decoded.ValueType.Should().Be(CanonicalValueType.Double);
        decoded.Quality.Should().Be(DataQuality.Bad);
    }

    // ---- quality / reason / unit -------------------------------------------

    [Fact]
    public void Roundtrip_Quality_Reason_And_Unit()
    {
        var decoded = Roundtrip(Make(
            CanonicalValueType.Integer, 42,
            quality: DataQuality.Uncertain, qualityReason: "sensor drift", unit: "rpm"));
        decoded.Quality.Should().Be(DataQuality.Uncertain);
        decoded.QualityReason.Should().Be("sensor drift");
        decoded.Unit.Should().Be("rpm");
    }

    [Fact]
    public void Roundtrip_Null_Reason_And_Unit()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Integer, 42));
        decoded.QualityReason.Should().BeNull();
        decoded.Unit.Should().BeNull();
    }

    [Fact]
    public void Roundtrip_Preserves_RouteBufferSequence_From_Column()
    {
        var original = Make(CanonicalValueType.Integer, 1, seq: 12345);
        var bytes = LatestValueEnvelopeV1.Encode(original);
        // The sequence is authoritative from the column, not the envelope.
        var decoded = LatestValueEnvelopeV1.Decode(bytes, Key, CanonicalValueType.Integer, 999);
        decoded.RouteBufferSequence.Should().Be(999);
    }

    [Fact]
    public void Roundtrip_Preserves_Timestamp()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Integer, 1));
        decoded.TimestampUtc.Should().Be(Ts);
    }

    // ---- static properties (the immutable scalar set) -----------------------

    [Fact]
    public void Roundtrip_Static_Properties_All_Scalar_Types()
    {
        var props = new Dictionary<string, object?>
        {
            ["nullProp"] = null,
            ["bool"] = true,
            ["sbyte"] = (sbyte)-7,
            ["byte"] = (byte)200,
            ["short"] = (short)-30000,
            ["ushort"] = (ushort)60000,
            ["int"] = 123,
            ["uint"] = 4_000_000_000u,
            ["long"] = -9_000_000_000L,
            ["ulong"] = 18_000_000_000_000_000_000UL,
            ["float"] = 1.25f,
            ["double"] = 6.02e23,
            ["decimal"] = 79228162514264337593543950335m, // decimal.MaxValue
            ["string"] = "eng-unit",
            ["datetime"] = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ["dto"] = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(330)),
            ["guid"] = Guid.Parse("12345678-1234-1234-1234-123456789abc"),
            ["bytes"] = new byte[] { 1, 2, 3 },
        };

        var decoded = Roundtrip(Make(CanonicalValueType.Integer, 1, staticProps: props));
        var got = decoded.StaticProperties!;

        got["nullProp"].Should().BeNull();
        got["bool"].Should().Be(true);
        got["sbyte"].Should().Be((sbyte)-7);
        got["byte"].Should().Be((byte)200);
        got["short"].Should().Be((short)-30000);
        got["ushort"].Should().Be((ushort)60000);
        got["int"].Should().Be(123);
        got["uint"].Should().Be(4_000_000_000u);
        got["long"].Should().Be(-9_000_000_000L);
        got["ulong"].Should().Be(18_000_000_000_000_000_000UL);
        got["float"].Should().Be(1.25f);
        got["double"].Should().Be(6.02e23);
        got["decimal"].Should().Be(79228162514264337593543950335m);
        got["string"].Should().Be("eng-unit");
        got["datetime"].Should().Be(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        got["dto"].Should().Be(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(330)));
        got["guid"].Should().Be(Guid.Parse("12345678-1234-1234-1234-123456789abc"));
        ((ImmutableArray<byte>)got["bytes"]!).AsSpan().ToArray().Should().Equal(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void Roundtrip_Empty_Static_Properties_Map()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Integer, 1, staticProps: new Dictionary<string, object?>()));
        decoded.StaticProperties.Should().NotBeNull();
        decoded.StaticProperties!.Count.Should().Be(0);
    }

    [Fact]
    public void Roundtrip_Null_Static_Properties_Stays_Null()
    {
        var decoded = Roundtrip(Make(CanonicalValueType.Integer, 1, staticProps: null));
        decoded.StaticProperties.Should().BeNull();
    }

    // ---- fail-closed cases --------------------------------------------------

    [Fact]
    public void Decode_Unknown_Codec_Version_Fails_Closed()
    {
        var bytes = LatestValueEnvelopeV1.Encode(Make(CanonicalValueType.Integer, 1));
        // Field [1] is the codec-version int (== 1) right after the 9-element array header
        // (0x99). The header is one byte; the version int follows as a fixint 0x01.
        bytes[1] = 0x63; // fixint 99 → unknown version

        var act = () => LatestValueEnvelopeV1.Decode(bytes, Key, CanonicalValueType.Integer, 7);

        act.Should().Throw<BufferException>()
            .Which.Error.Code.Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);
    }

    [Fact]
    public void Decode_Datatype_Column_Mismatch_Fails_Closed()
    {
        var bytes = LatestValueEnvelopeV1.Encode(Make(CanonicalValueType.Integer, 1));

        // Envelope says Integer; the column claims Long.
        var act = () => LatestValueEnvelopeV1.Decode(bytes, Key, CanonicalValueType.Long, 7);

        act.Should().Throw<BufferException>()
            .Which.Error.Code.Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);
    }

    [Fact]
    public void Decode_Negative_Sequence_Column_Fails_Closed()
    {
        var bytes = LatestValueEnvelopeV1.Encode(Make(CanonicalValueType.Integer, 1));

        var act = () => LatestValueEnvelopeV1.Decode(bytes, Key, CanonicalValueType.Integer, -1);

        act.Should().Throw<BufferException>()
            .Which.Error.Code.Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);
    }

    [Fact]
    public void Decode_Garbage_Bytes_Fails_Closed()
    {
        var garbage = new byte[] { 0xC1, 0x00, 0xFF, 0x42 };

        var act = () => LatestValueEnvelopeV1.Decode(garbage, Key, CanonicalValueType.Integer, 7);

        act.Should().Throw<BufferException>()
            .Which.Error.Code.Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);
    }

    [Fact]
    public void Decode_Trailing_Bytes_Fails_Closed()
    {
        var bytes = LatestValueEnvelopeV1.Encode(Make(CanonicalValueType.Integer, 1));
        var padded = new byte[bytes.Length + 1];
        Array.Copy(bytes, padded, bytes.Length);
        padded[^1] = 0x2A;

        var act = () => LatestValueEnvelopeV1.Decode(padded, Key, CanonicalValueType.Integer, 7);

        act.Should().Throw<BufferException>()
            .Which.Error.Code.Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);
    }

    [Fact]
    public void Decode_Empty_Buffer_Fails_Closed()
    {
        var act = () => LatestValueEnvelopeV1.Decode(Array.Empty<byte>(), Key, CanonicalValueType.Integer, 7);

        act.Should().Throw<BufferException>()
            .Which.Error.Code.Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);
    }
}
