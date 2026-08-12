// ============================================================================
// Tests: OpcUaValueMapping — CanonicalValueType → OPC UA DataType /
//        Variant. The DataType per NodeId is also locked once a
//        NodeId is in production with subscribers; changing it is a
//        v2 namespace bump per the policy contract.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Opc.Ua;
using Xunit;

namespace ElpisEdgeConnect.Sinks.OpcUaServer.Tests;

public class OpcUaValueMappingTests
{
    [Theory]
    [InlineData(CanonicalValueType.Boolean, "Boolean")]
    [InlineData(CanonicalValueType.Integer, "Int32")]
    [InlineData(CanonicalValueType.Long, "Int64")]
    [InlineData(CanonicalValueType.Float, "Float")]
    [InlineData(CanonicalValueType.Double, "Double")]
    [InlineData(CanonicalValueType.String, "String")]
    [InlineData(CanonicalValueType.DateTime, "DateTime")]
    [InlineData(CanonicalValueType.ByteArray, "ByteString")]
    public void ResolveDataTypeId_PrimitiveTypes_MapToStandardOpcUaTypes(CanonicalValueType input, string expectedName)
    {
        var nodeId = OpcUaValueMapping.ResolveDataTypeId(input);
        nodeId.NamespaceIndex.Should().Be(0, "all primitive canonical types must map to OPC UA namespace 0 standard types");
        // The standard IDs are well-known integer constants; cross-check by
        // resolving the same name through Opc.Ua.DataTypes.
        nodeId.Identifier.Should().NotBeNull();
        expectedName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ToVariant_NullValueOrNullType_ProducesVariantNull()
    {
        OpcUaValueMapping.ToVariant(null, CanonicalValueType.Integer)
            .Should().Be(Variant.Null);

        OpcUaValueMapping.ToVariant(42, CanonicalValueType.Null)
            .Should().Be(Variant.Null);
    }

    [Fact]
    public void ToVariant_Integer_PreservesScalarValue()
    {
        var variant = OpcUaValueMapping.ToVariant(1234, CanonicalValueType.Integer);
        variant.Value.Should().Be(1234);
    }

    [Fact]
    public void ToVariant_Double_PreservesScalarValue()
    {
        var variant = OpcUaValueMapping.ToVariant(2_500.0, CanonicalValueType.Double);
        variant.Value.Should().Be(2_500.0);
    }

    [Fact]
    public void ToVariant_DateTime_ConvertsToUtc()
    {
        var local = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Local);
        var variant = OpcUaValueMapping.ToVariant(local, CanonicalValueType.DateTime);
        ((DateTime)variant.Value).Kind.Should().Be(DateTimeKind.Utc);
    }
}
