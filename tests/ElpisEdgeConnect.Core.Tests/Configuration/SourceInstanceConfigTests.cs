// ============================================================================
// File: Configuration/SourceInstanceConfigTests.cs
// Covers: SourceInstanceConfig deserialization with protocol-specific
//         Connection blocks held opaquely as JsonElement.
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class SourceInstanceConfigTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Deserialize_Focas2ShapedConnection_PreservesAsOpaqueJson()
    {
        // FOCAS2 connection: IpAddress, Port, TimeoutSeconds. Core does not
        // type these — the FOCAS2 adapter parses them in InitializeAsync.
        const string json = """
            {
              "InstanceId": "focas-jyoti17",
              "ProtocolName": "focas2",
              "DeviceId": "Jyoti17CNC",
              "Connection": {
                "IpAddress": "192.168.2.34",
                "Port": 8193,
                "TimeoutSeconds": 10
              }
            }
            """;

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);

        source.Should().NotBeNull();
        source!.InstanceId.Should().Be("focas-jyoti17");
        source.ProtocolName.Should().Be("focas2");
        source.DeviceId.Should().Be("Jyoti17CNC");

        source.Connection.Should().NotBeNull();
        var conn = source.Connection!.Value;
        conn.GetProperty("IpAddress").GetString().Should().Be("192.168.2.34");
        conn.GetProperty("Port").GetInt32().Should().Be(8193);
        conn.GetProperty("TimeoutSeconds").GetInt32().Should().Be(10);
    }

    [Fact]
    public void Deserialize_MtLinkiShapedConnection_PreservesAsOpaqueJson()
    {
        // MT-LINKi connection: BaseUrl, ApiVersion, EquipmentType. Different
        // shape than FOCAS2; the source instance record doesn't care.
        const string json = """
            {
              "InstanceId": "mtlinki-ams1",
              "ProtocolName": "mtlinki",
              "DeviceId": "AMS1CNC",
              "Connection": {
                "BaseUrl": "http://192.168.2.167:3000",
                "ApiVersion": "v1",
                "EquipmentType": "AMS1CNC"
              }
            }
            """;

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);

        var conn = source!.Connection!.Value;
        conn.GetProperty("BaseUrl").GetString().Should().Be("http://192.168.2.167:3000");
        conn.GetProperty("ApiVersion").GetString().Should().Be("v1");
        conn.GetProperty("EquipmentType").GetString().Should().Be("AMS1CNC");
    }

    [Fact]
    public void Deserialize_NoPollingBlock_DefaultsApplied()
    {
        const string json = """
            {
              "InstanceId": "default-polling",
              "ProtocolName": "focas2",
              "DeviceId": "dev-1"
            }
            """;

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);

        source!.Polling.Should().NotBeNull();
        source.Polling.IntervalMs.Should().Be(1000);          // PollingSettings default
        source.Polling.MaxConsecutiveErrors.Should().Be(3);   // PollingSettings default
    }

    [Fact]
    public void Deserialize_ExplicitPollingBlock_OverridesDefaults()
    {
        const string json = """
            {
              "InstanceId": "custom-polling",
              "ProtocolName": "modbus",
              "DeviceId": "dev-1",
              "Polling": { "IntervalMs": 500, "MaxConsecutiveErrors": 20 }
            }
            """;

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);

        source!.Polling.IntervalMs.Should().Be(500);
        source.Polling.MaxConsecutiveErrors.Should().Be(20);
    }

    [Fact]
    public void Deserialize_TagsArray_RoundTripsExactly()
    {
        const string json = """
            {
              "InstanceId": "tagged",
              "ProtocolName": "focas2",
              "DeviceId": "dev-1",
              "Tags": ["mill", "bay-1", "focas2", "critical"]
            }
            """;

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);

        source!.Tags.Should().Equal("mill", "bay-1", "focas2", "critical");
    }

    [Fact]
    public void Deserialize_NoConnectionBlock_LeavesConnectionNull()
    {
        // The Connection block is opaque and may be null at the JSON level.
        // Some adapters (e.g., a future loopback adapter) need no connection.
        const string json = """
            {
              "InstanceId": "no-connection",
              "ProtocolName": "loopback",
              "DeviceId": "dev-1"
            }
            """;

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);

        source!.Connection.Should().BeNull();
    }

    // ========================================================================
    // Connection shape — pinning the documented lax behaviour.
    //
    // Per the B1 design (Q2 decision), Connection is JsonElement? and is
    // intentionally opaque so each protocol can describe its own connection
    // format. JsonElement? accepts ANY JSON value type, including non-object
    // values. The runtime does not enforce that Connection is a JSON object.
    //
    // The protocol adapter is responsible for validating the shape during
    // InitializeAsync; B2's recursive validator may also enforce
    // ValueKind == Object at apply time. The tests below DOCUMENT the
    // current laxness so any future change to it is visible at review.
    // ========================================================================

    [Fact]
    public void Deserialize_NonObjectConnection_Primitive_RoundTripsLaxly()
    {
        // A primitive Connection value (a number) is accepted by B1.
        // This is intentional documented laxness — no protocol would
        // legitimately use this, but the runtime cannot tell at load time.
        const string json = """
            {
              "InstanceId": "weird",
              "ProtocolName": "loopback",
              "DeviceId": "dev-1",
              "Connection": 42
            }
            """;

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);

        source!.Connection.Should().NotBeNull();
        source.Connection!.Value.ValueKind.Should().Be(JsonValueKind.Number);
        source.Connection.Value.GetInt32().Should().Be(42);
    }

    [Fact]
    public void Deserialize_NonObjectConnection_Array_RoundTripsLaxly()
    {
        // An array Connection value is accepted. Same rationale.
        const string json = """
            {
              "InstanceId": "weird",
              "ProtocolName": "loopback",
              "DeviceId": "dev-1",
              "Connection": [1, 2, 3]
            }
            """;

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);

        source!.Connection.Should().NotBeNull();
        source.Connection!.Value.ValueKind.Should().Be(JsonValueKind.Array);
        source.Connection.Value.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void Deserialize_NonObjectConnection_String_RoundTripsLaxly()
    {
        // A bare string Connection value is accepted.
        const string json = """
            {
              "InstanceId": "weird",
              "ProtocolName": "loopback",
              "DeviceId": "dev-1",
              "Connection": "broker.example.com"
            }
            """;

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);

        source!.Connection!.Value.ValueKind.Should().Be(JsonValueKind.String);
        source.Connection.Value.GetString().Should().Be("broker.example.com");
    }

    [Fact]
    public void Deserialize_NonObjectConnection_Pinned_AsKnownLaxness()
    {
        // Summary pin: by reading these tests, a future reviewer knows that
        // B1 does NOT enforce Connection.ValueKind == Object. If you want
        // to enforce that, the change must (a) update this test or remove
        // it, (b) update the source-adapter SDK doc, and (c) be a deliberate
        // contract change reviewed against blueprint §4 and the B1 Q2
        // decision. Do not silently tighten the runtime without all three.
        var assertion = () =>
        {
            const string json = """
                {
                  "InstanceId": "weird",
                  "ProtocolName": "loopback",
                  "DeviceId": "dev-1",
                  "Connection": true
                }
                """;
            return JsonSerializer.Deserialize<SourceInstanceConfig>(json, JsonOptions);
        };

        assertion.Should().NotThrow(
            because: "B1's documented Q2 decision is that Connection is opaque JsonElement? " +
                     "and accepts any JSON value type. Tightening this is a deliberate contract change.");
    }
}
