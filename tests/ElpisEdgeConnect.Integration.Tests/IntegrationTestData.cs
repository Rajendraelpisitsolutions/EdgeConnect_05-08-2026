// ============================================================================
// File: IntegrationTestData.cs
// Purpose: Tiny factory helpers for assembling GatewayConfiguration and
//          route configs in D phase 6 scenarios. Keeps the 13 scenario
//          tests readable.
// Milestone: D — phase 6.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.MockAdapters;

namespace ElpisEdgeConnect.Integration.Tests;

internal static class IntegrationTestData
{
    public const string GatewayId = "gw-integration";

    public static GatewaySettings DefaultGatewaySettings() => new()
    {
        GatewayId = GatewayId,
        GatewayName = "Integration Test Gateway",
    };

    public static BufferPolicyConfig InMemoryBuffer(int maxDepth = 20_000, DropPolicy policy = DropPolicy.DropOldest) => new()
    {
        Mode = BufferMode.InMemory,
        MaxDepth = maxDepth,
        OnOverflow = policy,
    };

    /// <summary>
    /// SQLite-backed durable buffer policy for tests that exercise the
    /// per-route on-disk buffer (host-restart durability, broker-outage
    /// retain-and-replay). Uses a 1-day retention so age-based eviction
    /// never fires inside a unit-test wall-clock window.
    /// </summary>
    public static BufferPolicyConfig StoreAndForwardBuffer(int maxDepth = 20_000, DropPolicy policy = DropPolicy.DropOldest) => new()
    {
        Mode = BufferMode.StoreAndForward,
        MaxDepth = maxDepth,
        MaxAgeDays = 1,
        OnOverflow = policy,
    };

    public static DeliveryPolicyConfig FastAtLeastOnce(int maxRetries = 10_000) => new()
    {
        Mode = DeliveryMode.AtLeastOnce,
        MaxRetries = maxRetries,
        InitialBackoffMs = 1,
        MaxBackoffMs = 10,
        BackoffMultiplier = 2.0,
        JitterPercent = 0,
    };

    public static DeliveryPolicyConfig AtMostOncePolicy() => new()
    {
        Mode = DeliveryMode.AtMostOnce,
        MaxRetries = 0,
        InitialBackoffMs = 1,
        MaxBackoffMs = 1,
        BackoffMultiplier = 2.0,
        JitterPercent = 0,
    };

    public static RouteConfig Route(
        string routeId,
        string sourceInstanceId,
        IReadOnlyList<string> sinkInstanceIds,
        BufferPolicyConfig? buffer = null,
        DeliveryPolicyConfig? delivery = null) => new()
        {
            RouteId = routeId,
            Name = routeId,
            SourceInstanceId = sourceInstanceId,
            SinkInstanceIds = sinkInstanceIds,
            Buffer = buffer ?? InMemoryBuffer(),
            Delivery = delivery ?? FastAtLeastOnce(),
            Enabled = true,
        };

    public static GatewayConfiguration Config(params RouteConfig[] routes) => new()
    {
        Gateway = DefaultGatewaySettings(),
        Routes = routes,
    };

    public static SourceRegistration SourceReg(MockSourceAdapter adapter, string routeId) => new()
    {
        Adapter = adapter,
        Config = new MockSourceConfiguration
        {
            InstanceId = adapter.InstanceId,
            ProtocolName = adapter.ProtocolName,
            DeviceId = "mock-device",
        },
        RouteId = routeId,
    };

    public static SinkRegistration SinkReg(MockSinkAdapter adapter, string routeId) => new()
    {
        Adapter = adapter,
        Config = new MockSinkConfiguration
        {
            InstanceId = adapter.InstanceId,
            ProtocolName = adapter.ProtocolName,
        },
        RouteId = routeId,
    };
}
