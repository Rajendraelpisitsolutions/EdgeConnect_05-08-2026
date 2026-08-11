// ============================================================================
// File: Configuration/B2TestFixtures.cs
// Purpose: Shared builders for valid GatewayConfiguration instances used by
//          B2 tests. Keeps individual test methods focused on the assertion
//          rather than fixture construction.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

internal static class B2TestFixtures
{
    /// <summary>
    /// A minimal valid configuration: one source, one sink, one route.
    /// Passes every B2 validation rule. Tests modify it via <c>with</c>
    /// expressions to introduce specific failures.
    /// </summary>
    public static GatewayConfiguration ValidMinimal() => new()
    {
        Gateway = new GatewaySettings
        {
            GatewayId = "GW-TEST-001",
            GatewayName = "Test Gateway",
        },
        Sources =
        [
            new SourceInstanceConfig
            {
                InstanceId = "src-1",
                ProtocolName = "focas2",
                DeviceId = "Dev-1",
            },
        ],
        Sinks =
        [
            new SinkInstanceConfig
            {
                InstanceId = "sink-1",
                ProtocolName = "mqtt",
            },
        ],
        Routes =
        [
            new RouteConfig
            {
                RouteId = "route-1",
                Name = "Test Route",
                SourceInstanceId = "src-1",
                SinkInstanceIds = ["sink-1"],
            },
        ],
    };

    /// <summary>
    /// A configuration with multiple sources, sinks, and routes for diff
    /// and listing tests.
    /// </summary>
    public static GatewayConfiguration ValidWithMultiple() => new()
    {
        Gateway = new GatewaySettings
        {
            GatewayId = "GW-TEST-001",
            GatewayName = "Test Gateway",
        },
        Sources =
        [
            new SourceInstanceConfig { InstanceId = "src-a", ProtocolName = "focas2", DeviceId = "DevA" },
            new SourceInstanceConfig { InstanceId = "src-b", ProtocolName = "modbus", DeviceId = "DevB" },
        ],
        Sinks =
        [
            new SinkInstanceConfig { InstanceId = "sink-a", ProtocolName = "mqtt" },
            new SinkInstanceConfig { InstanceId = "sink-b", ProtocolName = "http" },
        ],
        Routes =
        [
            new RouteConfig
            {
                RouteId = "route-a",
                Name = "Route A",
                SourceInstanceId = "src-a",
                SinkInstanceIds = ["sink-a", "sink-b"],
            },
            new RouteConfig
            {
                RouteId = "route-b",
                Name = "Route B",
                SourceInstanceId = "src-b",
                SinkInstanceIds = ["sink-b"],
            },
        ],
    };
}
