// ============================================================================
// File: MqttPayloadSerializerTests.cs
// ============================================================================

using System;
using System.Text.Json;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.Mqtt;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.Mqtt.Tests;

public sealed class MqttPayloadSerializerTests
{
    private static readonly DateTime FixedTime =
        new(2026, 4, 9, 10, 30, 0, DateTimeKind.Utc);

    private static CanonicalDataPoint MakePoint(
        string tag = "SpindleSpeed",
        object? value = null,
        CanonicalValueType valueType = CanonicalValueType.Double,
        string? unit = null)
    {
        return new CanonicalDataPoint
        {
            GatewayId = "gw-1",
            SourceInstanceId = "src-1",
            ProtocolName = "mock",
            DeviceId = "dev-1",
            TagName = tag,
            TagPath = $"dev-1/{tag}",
            Value = value ?? 1200.5,
            ValueType = valueType,
            Quality = DataQuality.Good,
            DeviceTimestamp = FixedTime,
            GatewayTimestamp = FixedTime,
            SequenceNumber = 1,
            Unit = unit,
        };
    }

    [Fact]
    public void Serialize_EmptyBatch_ReturnsEmptyJsonArray()
    {
        var bytes = MqttPayloadSerializer.SerializeBatch(Array.Empty<CanonicalDataPoint>());
        System.Text.Encoding.UTF8.GetString(bytes).Should().Be("[]");
    }

    [Fact]
    public void Serialize_SinglePoint_ProducesExpectedJson()
    {
        var bytes = MqttPayloadSerializer.SerializeBatch(new[] { MakePoint(unit: "rpm") });
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;
        arr.GetArrayLength().Should().Be(1);
        var p = arr[0];
        p.GetProperty("gatewayId").GetString().Should().Be("gw-1");
        p.GetProperty("tagName").GetString().Should().Be("SpindleSpeed");
        p.GetProperty("value").GetDouble().Should().Be(1200.5);
        p.GetProperty("valueType").GetString().Should().Be("Double");
        p.GetProperty("quality").GetString().Should().Be("Good");
        p.GetProperty("unit").GetString().Should().Be("rpm");
        p.GetProperty("sequenceNumber").GetInt64().Should().Be(1);
        p.GetProperty("deviceTimestamp").GetString().Should().Contain("2026-04-09");
    }

    [Fact]
    public void Serialize_MultiplePoints_ProducesJsonArray()
    {
        var pts = new[]
        {
            MakePoint("tag1"),
            MakePoint("tag2"),
            MakePoint("tag3"),
        };
        var bytes = MqttPayloadSerializer.SerializeBatch(pts);
        using var doc = JsonDocument.Parse(bytes);
        doc.RootElement.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void Serialize_NullValue_WritesJsonNull()
    {
        var p = MakePoint(value: null, valueType: CanonicalValueType.Null);
        var bytes = MqttPayloadSerializer.SerializeBatch(new[] { p });
        using var doc = JsonDocument.Parse(bytes);
        doc.RootElement[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Serialize_BooleanValue_WritesJsonBool()
    {
        var p = MakePoint(value: true, valueType: CanonicalValueType.Boolean);
        var bytes = MqttPayloadSerializer.SerializeBatch(new[] { p });
        using var doc = JsonDocument.Parse(bytes);
        doc.RootElement[0].GetProperty("value").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Serialize_StringValue_WritesJsonString()
    {
        var p = MakePoint(value: "MANUAL", valueType: CanonicalValueType.String);
        var bytes = MqttPayloadSerializer.SerializeBatch(new[] { p });
        using var doc = JsonDocument.Parse(bytes);
        doc.RootElement[0].GetProperty("value").GetString().Should().Be("MANUAL");
    }

    [Fact]
    public void Serialize_DoubleValue_WritesJsonNumber()
    {
        var p = MakePoint(value: 3.14, valueType: CanonicalValueType.Double);
        var bytes = MqttPayloadSerializer.SerializeBatch(new[] { p });
        using var doc = JsonDocument.Parse(bytes);
        doc.RootElement[0].GetProperty("value").GetDouble().Should().BeApproximately(3.14, 0.001);
    }

    [Fact]
    public void Serialize_OmitsNullOptionalFields()
    {
        var p = MakePoint(); // unit is null
        var bytes = MqttPayloadSerializer.SerializeBatch(new[] { p });
        using var doc = JsonDocument.Parse(bytes);
        var elem = doc.RootElement[0];
        elem.TryGetProperty("unit", out _).Should().BeFalse();
        elem.TryGetProperty("metadata", out _).Should().BeFalse();
        elem.TryGetProperty("originalTagName", out _).Should().BeFalse();
    }

    [Fact]
    public void Serialize_IncludesNonNullOptionalFields()
    {
        var p = MakePoint(unit: "rpm");
        var bytes = MqttPayloadSerializer.SerializeBatch(new[] { p });
        using var doc = JsonDocument.Parse(bytes);
        doc.RootElement[0].GetProperty("unit").GetString().Should().Be("rpm");
    }
}
