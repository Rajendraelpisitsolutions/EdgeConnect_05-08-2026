// ============================================================================
// File: Configuration/RouteConfigTests.cs
// Covers: RouteConfig + nested filter, transforms, buffer, delivery records.
//         Verifies defaults, multi-sink fanout, and the four BufferMode values.
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class RouteConfigTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Deserialize_MinimalRoute_AppliesAllDefaults()
    {
        // The smallest valid route: required identity fields plus one sink.
        // Every other field should fall back to its default.
        const string json = """
            {
              "RouteId": "minimal-route",
              "Name": "Minimal",
              "SourceInstanceId": "src-1",
              "SinkInstanceIds": ["sink-1"]
            }
            """;

        var route = JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        route.Should().NotBeNull();
        route!.RouteId.Should().Be("minimal-route");
        route.Name.Should().Be("Minimal");
        route.SourceInstanceId.Should().Be("src-1");
        route.SinkInstanceIds.Should().Equal("sink-1");
        route.Enabled.Should().BeTrue();

        // Filter default per blueprint §4.4
        route.Filter.Should().NotBeNull();
        route.Filter.Include.Should().Equal("*");
        route.Filter.Exclude.Should().BeNull();

        // Transforms default to null (identity route)
        route.Transforms.Should().BeNull();

        // Buffer defaults
        route.Buffer.Mode.Should().Be(BufferMode.StoreAndForward);
        route.Buffer.MaxDepth.Should().Be(10_000);
        route.Buffer.MaxAgeDays.Should().Be(7);
        route.Buffer.OnOverflow.Should().Be(DropPolicy.DropOldest);

        // Delivery defaults — locked C3 values per PHASE1_EXECUTION_PLAN.md C3.
        route.Delivery.Mode.Should().Be(DeliveryMode.AtLeastOnce);
        route.Delivery.MaxRetries.Should().Be(5);
        route.Delivery.InitialBackoffMs.Should().Be(100);
        route.Delivery.MaxBackoffMs.Should().Be(30_000);
        route.Delivery.BackoffMultiplier.Should().Be(2.0);
        route.Delivery.JitterPercent.Should().Be(10);
        route.Delivery.FanoutParallel.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_MultiSinkFanout_PreservesAllSinks()
    {
        // Per blueprint §3, one source can fan out to many sinks via one route.
        const string json = """
            {
              "RouteId": "fanout-route",
              "Name": "Fanout",
              "SourceInstanceId": "src-1",
              "SinkInstanceIds": ["mqtt-primary", "http-backup", "tcp-archive"]
            }
            """;

        var route = JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        route!.SinkInstanceIds.Should().HaveCount(3);
        route.SinkInstanceIds.Should().Equal("mqtt-primary", "http-backup", "tcp-archive");
    }

    [Fact]
    public void Deserialize_TagFilterIncludeExclude_RoundTrips()
    {
        const string json = """
            {
              "RouteId": "filtered",
              "Name": "Filtered",
              "SourceInstanceId": "src-1",
              "SinkInstanceIds": ["sink-1"],
              "Filter": {
                "Include": ["spindle.*", "axis.x.*"],
                "Exclude": ["*.debug", "*.internal"]
              }
            }
            """;

        var route = JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        route!.Filter.Include.Should().Equal("spindle.*", "axis.x.*");
        route.Filter.Exclude.Should().Equal("*.debug", "*.internal");
    }

    [Fact]
    public void Deserialize_FullTransformProfile_AllSubBlocksPresent()
    {
        const string json = """
            {
              "RouteId": "transformed",
              "Name": "Transformed",
              "SourceInstanceId": "src-1",
              "SinkInstanceIds": ["sink-1"],
              "Transforms": {
                "TagMapping": { "rawTag": "canonical.tag" },
                "Deadband": { "spindle.speed": 0.5 },
                "RateLimitMs": { "axis.x.position": 100 },
                "EnrichmentTags": { "site": "Mumbai", "shift": 2 }
              }
            }
            """;

        var route = JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        route!.Transforms.Should().NotBeNull();
        route.Transforms!.TagMapping.Should().NotBeNull();
        route.Transforms.TagMapping!["rawTag"].Should().Be("canonical.tag");
        route.Transforms.Deadband!["spindle.speed"].Should().Be(0.5);
        route.Transforms.RateLimitMs!["axis.x.position"].Should().Be(100);
        route.Transforms.EnrichmentTags!["site"].ToString().Should().Be("Mumbai");
    }

    [Fact]
    public void Deserialize_EmptyTransforms_AllSubBlocksNull()
    {
        const string json = """
            {
              "RouteId": "no-transforms",
              "Name": "No Transforms",
              "SourceInstanceId": "src-1",
              "SinkInstanceIds": ["sink-1"],
              "Transforms": {}
            }
            """;

        var route = JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        route!.Transforms.Should().NotBeNull();
        route.Transforms!.TagMapping.Should().BeNull();
        route.Transforms.Deadband.Should().BeNull();
        route.Transforms.RateLimitMs.Should().BeNull();
        route.Transforms.EnrichmentTags.Should().BeNull();
    }

    [Theory]
    [InlineData("None", BufferMode.None)]
    [InlineData("InMemory", BufferMode.InMemory)]
    [InlineData("StoreAndForward", BufferMode.StoreAndForward)]
    public void Deserialize_BufferPolicyMode_AcceptsAllValues(string text, BufferMode expected)
    {
        var json = $$"""
            {
              "RouteId": "r",
              "Name": "r",
              "SourceInstanceId": "src",
              "SinkInstanceIds": ["sink"],
              "Buffer": { "Mode": "{{text}}" }
            }
            """;

        var route = JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        route!.Buffer.Mode.Should().Be(expected);
    }

    [Theory]
    [InlineData("DropOldest", DropPolicy.DropOldest)]
    [InlineData("DropNewest", DropPolicy.DropNewest)]
    [InlineData("Block", DropPolicy.Block)]
    public void Deserialize_BufferPolicyOverflow_AcceptsAllValues(string text, DropPolicy expected)
    {
        var json = $$"""
            {
              "RouteId": "r",
              "Name": "r",
              "SourceInstanceId": "src",
              "SinkInstanceIds": ["sink"],
              "Buffer": { "OnOverflow": "{{text}}" }
            }
            """;

        var route = JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        route!.Buffer.OnOverflow.Should().Be(expected);
    }

    [Theory]
    [InlineData("AtMostOnce", DeliveryMode.AtMostOnce)]
    [InlineData("AtLeastOnce", DeliveryMode.AtLeastOnce)]
    public void Deserialize_DeliveryPolicyMode_AcceptsBothV1Modes(string text, DeliveryMode expected)
    {
        var json = $$"""
            {
              "RouteId": "r",
              "Name": "r",
              "SourceInstanceId": "src",
              "SinkInstanceIds": ["sink"],
              "Delivery": { "Mode": "{{text}}" }
            }
            """;

        var route = JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        route!.Delivery.Mode.Should().Be(expected);
    }

    [Fact]
    public void Deserialize_MissingRouteId_Throws()
    {
        const string json = """
            {
              "Name": "Missing id",
              "SourceInstanceId": "src",
              "SinkInstanceIds": ["sink"]
            }
            """;

        var act = () => JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_MissingSinkInstanceIds_Throws()
    {
        // SinkInstanceIds is `required`, so omitting it from JSON must throw.
        const string json = """
            {
              "RouteId": "r",
              "Name": "r",
              "SourceInstanceId": "src"
            }
            """;

        var act = () => JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_DisabledRoute_Honored()
    {
        const string json = """
            {
              "RouteId": "off",
              "Name": "off",
              "SourceInstanceId": "src",
              "SinkInstanceIds": ["sink"],
              "Enabled": false
            }
            """;

        var route = JsonSerializer.Deserialize<RouteConfig>(json, JsonOptions);

        route!.Enabled.Should().BeFalse();
    }

    [Fact]
    public void TagFilterConfig_DefaultInclude_IsStarWildcard()
    {
        // Pin the blueprint §4.4 default explicitly. The shared property test
        // ensures the default is exactly ["*"], not empty or null.
        var filter = new TagFilterConfig();

        filter.Include.Should().Equal("*");
        filter.Exclude.Should().BeNull();
    }
}
