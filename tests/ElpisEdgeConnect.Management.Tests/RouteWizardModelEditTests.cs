// ============================================================================
// Tests: RouteWizardModel.HydrateFromExisting — round-trip fidelity.
//        Covers identity, source/sink wiring, buffer policy, delivery
//        policy, filter patterns, and transform rows.
// Reference: docs/sessions/2026-05-26-m2d3-sink-route-editors-plan-v2.md §3.2
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class RouteWizardModelEditTests
{
    [Fact]
    public void HydrateFromExisting_Identity_SourceAndSinks_RoundTrip()
    {
        var original = new RouteWizardModel
        {
            RouteId = "route-line1",
            Name = "Line 1 Route",
            Enabled = false,
            SourceInstanceId = "plc-line1",
        };
        original.SinkInstanceIds.Add("mqtt-eremos");
        original.SinkInstanceIds.Add("opcua-edge");

        var config = original.BuildRouteConfig();
        var hydrated = RouteWizardModel.HydrateFromExisting(config);

        hydrated.RouteId.Should().Be("route-line1");
        hydrated.Name.Should().Be("Line 1 Route");
        hydrated.Enabled.Should().BeFalse();
        hydrated.SourceInstanceId.Should().Be("plc-line1");
        hydrated.SinkInstanceIds.Should().BeEquivalentTo(new[] { "mqtt-eremos", "opcua-edge" });
    }

    [Fact]
    public void HydrateFromExisting_BufferAndDelivery_RoundTrip()
    {
        var original = new RouteWizardModel
        {
            RouteId = "route-buf",
            Name = "Buf Route",
            SourceInstanceId = "plc-1",
            BufferMode = BufferMode.StoreAndForward,
            BufferMaxDepth = 50_000,
            BufferMaxAgeDays = 14,
            BufferOnOverflow = DropPolicy.DropNewest,
            DeliveryMode = DeliveryMode.AtMostOnce,
            DeliveryMaxRetries = 3,
            DeliveryInitialBackoffMs = 200,
            DeliveryMaxBackoffMs = 15_000,
            DeliveryBackoffMultiplier = 1.5,
            DeliveryJitterPercent = 5,
            DeliveryFanoutParallel = false,
        };
        original.SinkInstanceIds.Add("mqtt-eremos");

        var config = original.BuildRouteConfig();
        var hydrated = RouteWizardModel.HydrateFromExisting(config);

        hydrated.BufferMode.Should().Be(BufferMode.StoreAndForward);
        hydrated.BufferMaxDepth.Should().Be(50_000);
        hydrated.BufferMaxAgeDays.Should().Be(14);
        hydrated.BufferOnOverflow.Should().Be(DropPolicy.DropNewest);
        hydrated.DeliveryMode.Should().Be(DeliveryMode.AtMostOnce);
        hydrated.DeliveryMaxRetries.Should().Be(3);
        hydrated.DeliveryInitialBackoffMs.Should().Be(200);
        hydrated.DeliveryMaxBackoffMs.Should().Be(15_000);
        hydrated.DeliveryBackoffMultiplier.Should().Be(1.5);
        hydrated.DeliveryJitterPercent.Should().Be(5);
        hydrated.DeliveryFanoutParallel.Should().BeFalse();
    }

    [Fact]
    public void HydrateFromExisting_FilterPatterns_RoundTrip()
    {
        var original = new RouteWizardModel
        {
            RouteId = "route-filter",
            Name = "Filtered",
            SourceInstanceId = "plc-1",
        };
        original.SinkInstanceIds.Add("mqtt-eremos");
        original.Filter.Include.Clear();
        original.Filter.Include.Add("Axes/*");
        original.Filter.Include.Add("Spindle/*");
        original.Filter.Exclude.Add("Axes/RawCurrent");

        var config = original.BuildRouteConfig();
        var hydrated = RouteWizardModel.HydrateFromExisting(config);

        hydrated.Filter.Include.Should().BeEquivalentTo(new[] { "Axes/*", "Spindle/*" });
        hydrated.Filter.Exclude.Should().BeEquivalentTo(new[] { "Axes/RawCurrent" });
    }

    [Fact]
    public void HydrateFromExisting_TransformRows_RoundTrip()
    {
        var original = new RouteWizardModel
        {
            RouteId = "route-xform",
            Name = "Xform",
            SourceInstanceId = "plc-1",
        };
        original.SinkInstanceIds.Add("mqtt-eremos");
        original.Transforms.TagMappings.Add(new TagMappingRow { SourceTag = "RawTemp", CanonicalTag = "Temperature" });
        original.Transforms.DeadbandRows.Add(new DeadbandRow { Tag = "Pressure", Threshold = 0.5, Mode = DeadbandMode.Absolute });
        original.Transforms.RateLimitRows.Add(new RateLimitRow { Tag = "Vibration", MinIntervalMs = 1000 });

        var config = original.BuildRouteConfig();
        var hydrated = RouteWizardModel.HydrateFromExisting(config);

        hydrated.Transforms.TagMappings.Should().ContainSingle(r => r.SourceTag == "RawTemp" && r.CanonicalTag == "Temperature");
        hydrated.Transforms.DeadbandRows.Should().ContainSingle(r => r.Tag == "Pressure" && r.Threshold == 0.5 && r.Mode == DeadbandMode.Absolute);
        hydrated.Transforms.RateLimitRows.Should().ContainSingle(r => r.Tag == "Vibration" && r.MinIntervalMs == 1000);
    }
}
