// ============================================================================
// File: Focas2SharedCncEndpointTests.cs
// Purpose: Unit tests for Focas2RegistrationExtensions.FindSharedCncEndpoints —
//          the connection-limit advisory that warns when 2+ enabled FOCAS2
//          sources target the same CNC endpoint (cause of SOCKET_ERROR drops).
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class Focas2SharedCncEndpointTests
{
    private static SourceInstanceConfig Focas2Source(
        string id, string ip, int port = 8193, bool enabled = true)
    {
        using var doc = JsonDocument.Parse(
            $$"""{ "ipAddress": "{{ip}}", "port": {{port}} }""");
        return new SourceInstanceConfig
        {
            InstanceId = id,
            ProtocolName = "focas2",
            DeviceId = id + "-dev",
            Enabled = enabled,
            Connection = doc.RootElement.Clone(),
        };
    }

    private static GatewayConfiguration Config(params SourceInstanceConfig[] sources) => new()
    {
        Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
        Sources = sources,
        Routes = System.Array.Empty<RouteConfig>(),
    };

    [Fact]
    public void FindSharedCncEndpoints_TwoSourcesSameEndpoint_ReportsGroup()
    {
        var cfg = Config(
            Focas2Source("cnc-a", "192.168.1.10"),
            Focas2Source("cnc-b", "192.168.1.10"));

        var shared = Focas2RegistrationExtensions.FindSharedCncEndpoints(cfg);

        shared.Should().ContainSingle();
        shared[0].Endpoint.Should().Be("192.168.1.10:8193");
        shared[0].SourceIds.Should().BeEquivalentTo(new[] { "cnc-a", "cnc-b" });
    }

    [Fact]
    public void FindSharedCncEndpoints_DistinctEndpoints_ReportsNothing()
    {
        var cfg = Config(
            Focas2Source("cnc-a", "192.168.1.10"),
            Focas2Source("cnc-b", "192.168.1.11"));

        Focas2RegistrationExtensions.FindSharedCncEndpoints(cfg).Should().BeEmpty();
    }

    [Fact]
    public void FindSharedCncEndpoints_SameIpDifferentPort_NotShared()
    {
        var cfg = Config(
            Focas2Source("cnc-a", "192.168.1.10", port: 8193),
            Focas2Source("cnc-b", "192.168.1.10", port: 8194));

        Focas2RegistrationExtensions.FindSharedCncEndpoints(cfg).Should().BeEmpty();
    }

    [Fact]
    public void FindSharedCncEndpoints_DisabledSource_Excluded()
    {
        var cfg = Config(
            Focas2Source("cnc-a", "192.168.1.10"),
            Focas2Source("cnc-b", "192.168.1.10", enabled: false));

        // Only one ENABLED source on the endpoint → no contention.
        Focas2RegistrationExtensions.FindSharedCncEndpoints(cfg).Should().BeEmpty();
    }
}
