// ============================================================================
// Tests: OpcUaTypeMapperTests — pin the DataValue + Variant → CanonicalDataPoint
//        translation for every UA built-in type the v2.1 §1.1 scope
//        commits to.
//
//        Invariants:
//          * Scalar dispatch: each UA built-in → expected CanonicalValueType
//            and runtime Value type
//          * Array (ValueRank > 0) → CanonicalValueType.Array
//          * Null Variant → Null with Value = null
//          * Quality from StatusCode (via OpcUaQualityMapper)
//          * Timestamp resolution: SourceTimestamp wins, ServerTimestamp
//            fallback, UtcNow last-resort
//          * DateTime normalised to UTC regardless of source Kind
//          * ExtensionObject → Object with empty dict + opcua.unexpandedTypeId
//            metadata (structure expansion deferred — PR 4 plan sign-off)
//          * GatewayTimestamp = UtcNow at translation time
//          * CanonicalDataPoint.IsConsistent() returns true on every
//            translation output (the consistency contract holds)
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Opc.Ua;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaTypeMapperTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static OpcUaTypeMapper CreateMapper(Func<DateTime>? utcNow = null) =>
        new(
            gatewayId: "gw-test",
            sourceInstanceId: "opcua-test",
            protocolName: OpcUaClientSourceConfiguration.ProtocolNameConstant,
            deviceId: "factorytalk",
            utcNow: utcNow ?? (() => FixedNow));

    private static DataValue Wrap(Variant v, StatusCode? status = null, DateTime? source = null, DateTime? server = null) =>
        new()
        {
            WrappedValue = v,
            StatusCode = status ?? StatusCodes.Good,
            SourceTimestamp = source ?? DateTime.MinValue,
            ServerTimestamp = server ?? DateTime.MinValue,
        };

    // ─── Identity / shape ────────────────────────────────────────────

    [Fact]
    public void Translate_PopulatesIdentityFromConstructor()
    {
        var mapper = CreateMapper();
        var cdp = mapper.Translate(Wrap(new Variant(42)), tagName: "Speed");

        cdp.GatewayId.Should().Be("gw-test");
        cdp.SourceInstanceId.Should().Be("opcua-test");
        cdp.ProtocolName.Should().Be(OpcUaClientSourceConfiguration.ProtocolNameConstant);
        cdp.DeviceId.Should().Be("factorytalk");
        cdp.TagName.Should().Be("Speed");
        cdp.TagPath.Should().Be("Speed", "TagPath defaults to TagName when not supplied");
    }

    [Fact]
    public void Translate_OutputIsAlwaysConsistent()
    {
        // CanonicalDataPoint contract — IsConsistent() returns true when
        // the runtime type of Value matches ValueType and timestamps are
        // UTC. Every mapper output MUST satisfy this so downstream sinks
        // never see a corrupted shape.
        var mapper = CreateMapper();

        mapper.Translate(Wrap(new Variant(true)), "Bool").IsConsistent().Should().BeTrue();
        mapper.Translate(Wrap(new Variant(42)), "Int").IsConsistent().Should().BeTrue();
        mapper.Translate(Wrap(new Variant(3.14)), "Double").IsConsistent().Should().BeTrue();
        mapper.Translate(Wrap(new Variant("hello")), "String").IsConsistent().Should().BeTrue();
        mapper.Translate(Wrap(Variant.Null), "Null").IsConsistent().Should().BeTrue();
    }

    // ─── Scalar dispatch ─────────────────────────────────────────────

    [Fact]
    public void Translate_Boolean_Scalar()
    {
        var cdp = CreateMapper().Translate(Wrap(new Variant(true)), "B");

        cdp.ValueType.Should().Be(CanonicalValueType.Boolean);
        cdp.Value.Should().Be(true);
    }

    [Theory]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(int))]
    public void Translate_SmallIntegerTypes_MapToInteger(Type smallIntegerType)
    {
        // SByte/Byte/Int16/UInt16/Int32 all fit in int; mapper widens
        // them so downstream sees a uniform canonical Integer.
        object boxed = Convert.ChangeType(7, smallIntegerType);
        var variant = new Variant(boxed);
        var cdp = CreateMapper().Translate(Wrap(variant), "Small");

        cdp.ValueType.Should().Be(CanonicalValueType.Integer);
        cdp.Value.Should().BeOfType<int>().And.Be(7);
    }

    [Fact]
    public void Translate_UInt32_MapsToLong()
    {
        var cdp = CreateMapper().Translate(Wrap(new Variant((uint)4_000_000_000u)), "Big");

        cdp.ValueType.Should().Be(CanonicalValueType.Long);
        cdp.Value.Should().BeOfType<long>().And.Be(4_000_000_000L);
    }

    [Fact]
    public void Translate_Int64_MapsToLong()
    {
        var cdp = CreateMapper().Translate(Wrap(new Variant(123_456_789_012L)), "Long");

        cdp.ValueType.Should().Be(CanonicalValueType.Long);
        cdp.Value.Should().BeOfType<long>().And.Be(123_456_789_012L);
    }

    [Fact]
    public void Translate_Float_MapsToFloat()
    {
        var cdp = CreateMapper().Translate(Wrap(new Variant(2.5f)), "F");

        cdp.ValueType.Should().Be(CanonicalValueType.Float);
        cdp.Value.Should().BeOfType<float>().And.Be(2.5f);
    }

    [Fact]
    public void Translate_Double_MapsToDouble()
    {
        var cdp = CreateMapper().Translate(Wrap(new Variant(2.5d)), "D");

        cdp.ValueType.Should().Be(CanonicalValueType.Double);
        cdp.Value.Should().BeOfType<double>().And.Be(2.5d);
    }

    [Fact]
    public void Translate_String_MapsToString()
    {
        var cdp = CreateMapper().Translate(Wrap(new Variant("hello")), "S");

        cdp.ValueType.Should().Be(CanonicalValueType.String);
        cdp.Value.Should().Be("hello");
    }

    [Fact]
    public void Translate_Guid_MapsToString_DFormat()
    {
        // No canonical Guid type → string with the spec "D" format
        // ("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx").
        var guid = Guid.Parse("12345678-1234-1234-1234-1234567890ab");
        var cdp = CreateMapper().Translate(Wrap(new Variant(new Uuid(guid))), "G");

        cdp.ValueType.Should().Be(CanonicalValueType.String);
        cdp.Value.Should().Be("12345678-1234-1234-1234-1234567890ab");
    }

    [Fact]
    public void Translate_ByteString_MapsToByteArray()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var cdp = CreateMapper().Translate(Wrap(new Variant(bytes)), "BS");

        cdp.ValueType.Should().Be(CanonicalValueType.ByteArray);
        cdp.Value.Should().BeOfType<byte[]>();
        ((byte[])cdp.Value!).Should().Equal(bytes);
    }

    [Fact]
    public void Translate_NodeId_MapsToString()
    {
        var nodeId = new NodeId(10, 2);
        var cdp = CreateMapper().Translate(Wrap(new Variant(nodeId)), "NId");

        cdp.ValueType.Should().Be(CanonicalValueType.String);
        cdp.Value.Should().BeOfType<string>().Subject.Should().Contain("ns=2");
    }

    [Fact]
    public void Translate_LocalizedText_MapsToText()
    {
        var lt = new LocalizedText("en", "Running");
        var cdp = CreateMapper().Translate(Wrap(new Variant(lt)), "LT");

        cdp.ValueType.Should().Be(CanonicalValueType.String);
        cdp.Value.Should().Be("Running");
    }

    [Fact]
    public void Translate_QualifiedName_MapsToString()
    {
        var qn = new QualifiedName("Status", 2);
        var cdp = CreateMapper().Translate(Wrap(new Variant(qn)), "QN");

        cdp.ValueType.Should().Be(CanonicalValueType.String);
        cdp.Value.Should().BeOfType<string>().Subject.Should().Contain("Status");
    }

    // ─── DateTime ────────────────────────────────────────────────────

    [Fact]
    public void Translate_DateTime_UtcKindPreserved()
    {
        var dt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var cdp = CreateMapper().Translate(Wrap(new Variant(dt)), "T");

        cdp.ValueType.Should().Be(CanonicalValueType.DateTime);
        var resolved = (DateTime)cdp.Value!;
        resolved.Kind.Should().Be(DateTimeKind.Utc);
        resolved.Should().Be(dt);
    }

    [Fact]
    public void Translate_DateTime_LocalKind_NormalisedToUtc()
    {
        var dtLocal = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);

        var cdp = CreateMapper().Translate(Wrap(new Variant(dtLocal)), "T");

        ((DateTime)cdp.Value!).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Translate_DateTime_UnspecifiedKind_TreatedAsUtc()
    {
        var dtUnspec = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var cdp = CreateMapper().Translate(Wrap(new Variant(dtUnspec)), "T");

        ((DateTime)cdp.Value!).Kind.Should().Be(DateTimeKind.Utc);
    }

    // ─── Null + arrays ────────────────────────────────────────────────

    [Fact]
    public void Translate_NullVariant_MapsToNullType()
    {
        var cdp = CreateMapper().Translate(Wrap(Variant.Null), "N");

        cdp.ValueType.Should().Be(CanonicalValueType.Null);
        cdp.Value.Should().BeNull();
    }

    [Fact]
    public void Translate_Int32Array_MapsToArrayType()
    {
        var arr = new int[] { 10, 20, 30 };
        var cdp = CreateMapper().Translate(Wrap(new Variant(arr)), "Arr");

        cdp.ValueType.Should().Be(CanonicalValueType.Array);
        cdp.Value.Should().BeOfType<int[]>();
        ((int[])cdp.Value!).Should().Equal(arr);
    }

    [Fact]
    public void Translate_DoubleArray_MapsToArrayType()
    {
        var arr = new double[] { 1.5, 2.5, 3.5 };
        var cdp = CreateMapper().Translate(Wrap(new Variant(arr)), "Arr");

        cdp.ValueType.Should().Be(CanonicalValueType.Array);
    }

    // ─── ExtensionObject (PR 4a stub) ─────────────────────────────────

    [Fact]
    public void Translate_ExtensionObject_MapsToObjectWithEmptyDictAndTypeIdMetadata()
    {
        // Structure expansion deferred per v2.1 §1.1 + PR 4 sign-off.
        // PR 4a: empty dict + opcua.unexpandedTypeId metadata so operators
        // see which vendor schema was bypassed.
        var ext = new ExtensionObject(new NodeId(1234, 2), new byte[] { 0x01, 0x02 });
        var cdp = CreateMapper().Translate(Wrap(new Variant(ext)), "Ext");

        cdp.ValueType.Should().Be(CanonicalValueType.Object);
        cdp.Value.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, object>>();
        cdp.Metadata.Should().NotBeNull();
        cdp.Metadata!.Should().ContainKey("opcua.unexpandedTypeId");
    }

    // ─── Quality ─────────────────────────────────────────────────────

    [Fact]
    public void Translate_GoodStatus_ProducesGoodQuality()
    {
        var cdp = CreateMapper().Translate(Wrap(new Variant(1), status: StatusCodes.Good), "G");

        cdp.Quality.Should().Be(DataQuality.Good);
        cdp.QualityReason.Should().BeNull();
    }

    [Fact]
    public void Translate_BadStatus_ProducesBadQualityWithReason()
    {
        var cdp = CreateMapper().Translate(
            Wrap(new Variant(1), status: StatusCodes.BadNodeIdInvalid), "B");

        cdp.Quality.Should().Be(DataQuality.Bad);
        cdp.QualityReason.Should().NotBeNullOrWhiteSpace();
    }

    // ─── Timestamps ──────────────────────────────────────────────────

    [Fact]
    public void Translate_SourceTimestamp_WinsAsDeviceTimestamp()
    {
        var source = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var server = new DateTime(2026, 1, 1, 10, 0, 5, DateTimeKind.Utc);

        var cdp = CreateMapper().Translate(
            Wrap(new Variant(1), source: source, server: server), "T");

        cdp.DeviceTimestamp.Should().Be(source);
    }

    [Fact]
    public void Translate_NoSourceTimestamp_FallsBackToServerTimestamp()
    {
        var server = new DateTime(2026, 1, 1, 10, 0, 5, DateTimeKind.Utc);

        var cdp = CreateMapper().Translate(
            Wrap(new Variant(1), source: DateTime.MinValue, server: server), "T");

        cdp.DeviceTimestamp.Should().Be(server);
    }

    [Fact]
    public void Translate_NoTimestamps_FallsBackToUtcNow()
    {
        var cdp = CreateMapper().Translate(
            Wrap(new Variant(1), source: DateTime.MinValue, server: DateTime.MinValue), "T");

        cdp.DeviceTimestamp.Should().Be(FixedNow);
    }

    [Fact]
    public void Translate_GatewayTimestamp_IsUtcNow()
    {
        var cdp = CreateMapper().Translate(Wrap(new Variant(1)), "T");

        cdp.GatewayTimestamp.Should().Be(FixedNow);
    }
}
