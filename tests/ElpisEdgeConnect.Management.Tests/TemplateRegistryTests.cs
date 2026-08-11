// ============================================================================
// File: TemplateRegistryTests.cs
// Purpose: Sanity coverage for TemplateRegistry — the per-protocol catalog
//          driving BulkSourceMergeService. These tests run the registry
//          entry's templates through the substitution engine with realistic
//          values and verify the rendered text deserializes to the canonical
//          Core configuration types. Catches drift between template body and
//          placeholder spec (occurrence counts, missing names, raw vs string
//          position, etc.) before the integration tests do.
// ============================================================================

using System.Collections.Generic;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Api.BulkSourceMerge;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class TemplateRegistryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IEnumerable<object[]> AllProtocols() =>
        new[]
        {
            new object[] { BulkSourceMergeProtocol.Focas2,      "focas2",       "host",    "192.168.10.21" },
            new object[] { BulkSourceMergeProtocol.BrotherHttp, "brother-http", "host",    "192.168.10.22" },
            new object[] { BulkSourceMergeProtocol.ModbusTcp,   "modbus-tcp",   "host",    "192.168.10.23" },
            new object[] { BulkSourceMergeProtocol.Mtconnect,   "mtconnect",    "baseUrl", "http://192.168.10.24:5000/" },
        };

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void SourceTemplate_RegistryAndEngineRoundTrip_DeserializesToSourceInstanceConfig(
        BulkSourceMergeProtocol protocol,
        string expectedProtocolName,
        string expectedAddressPlaceholder,
        string addressValue)
    {
        var entry = TemplateRegistry.Get(protocol);
        entry.ProtocolName.Should().Be(expectedProtocolName);
        entry.AddressPlaceholderName.Should().Be(expectedAddressPlaceholder);

        var engine = new TemplateSubstitutionEngine(entry.SourcePlaceholders);
        var values = new Dictionary<string, string>
        {
            ["instanceId"]                  = "cnc-007-source",
            ["deviceId"]                    = "cnc-007",
            ["deviceName"]                  = "Lathe-Bay-7",
            ["enabled"]                     = "true",
            [expectedAddressPlaceholder]    = addressValue,
        };

        var rendered = engine.Render(entry.SourceTemplate, values);

        var source = JsonSerializer.Deserialize<SourceInstanceConfig>(rendered, JsonOptions);
        source.Should().NotBeNull();
        source!.InstanceId.Should().Be("cnc-007-source");
        source.ProtocolName.Should().Be(expectedProtocolName);
        source.DeviceId.Should().Be("cnc-007");
        source.DeviceName.Should().Be("Lathe-Bay-7");
        source.Enabled.Should().BeTrue();
        source.Connection.Should().NotBeNull();
    }

    [Fact]
    public void RouteTemplate_RegistryAndEngineRoundTrip_DeserializesToRouteConfig()
    {
        var entry = TemplateRegistry.Get(BulkSourceMergeProtocol.Focas2);
        var engine = new TemplateSubstitutionEngine(entry.RoutePlaceholders);
        var values = new Dictionary<string, string>
        {
            ["routeId"]        = "route-cnc-007",
            ["routeName"]      = "Lathe-Bay-7 to acme-mqtt",
            ["instanceId"]     = "cnc-007-source",
            ["sinkInstanceId"] = "acme-mqtt",
        };

        var rendered = engine.Render(entry.RouteTemplate, values);

        var route = JsonSerializer.Deserialize<RouteConfig>(rendered, JsonOptions);
        route.Should().NotBeNull();
        route!.RouteId.Should().Be("route-cnc-007");
        route.Name.Should().Be("Lathe-Bay-7 to acme-mqtt");
        route.SourceInstanceId.Should().Be("cnc-007-source");
        route.SinkInstanceIds.Should().ContainSingle().Which.Should().Be("acme-mqtt");
        route.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Entries_CoverEveryProtocolEnumValue()
    {
        TemplateRegistry.Entries.Keys.Should().BeEquivalentTo(new[]
        {
            BulkSourceMergeProtocol.Focas2,
            BulkSourceMergeProtocol.BrotherHttp,
            BulkSourceMergeProtocol.ModbusTcp,
            BulkSourceMergeProtocol.Mtconnect,
        });
    }
}
