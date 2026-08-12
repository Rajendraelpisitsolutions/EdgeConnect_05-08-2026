// ============================================================================
// File: MqttTopicResolverTests.cs
// ============================================================================

using System;
using ElpisEdgeConnect.Sinks.Mqtt;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.Mqtt.Tests;

public sealed class MqttTopicResolverTests
{
    [Fact]
    public void Resolve_AllPlaceholders_SubstitutesCorrectly()
    {
        var result = MqttTopicResolver.Resolve(
            "eremos/{gatewayId}/cnc/{sourceId}/{tagName}",
            gatewayId: "gw-1",
            sourceId: "CNC-001",
            tagName: "SpindleSpeed");

        result.Should().Be("eremos/gw-1/cnc/CNC-001/SpindleSpeed");
    }

    [Fact]
    public void Resolve_NoPlaceholders_ReturnsTemplateUnchanged()
    {
        MqttTopicResolver.Resolve("fixed/topic/path")
            .Should().Be("fixed/topic/path");
    }

    [Fact]
    public void Resolve_NullValues_SubstituteFallbacks()
    {
        var result = MqttTopicResolver.Resolve(
            "{gatewayId}/{sourceId}/{routeId}/{tagName}");

        result.Should().Be("unknown/unknown/default/_unknown_");
    }

    [Fact]
    public void Resolve_MqttWildcards_StrippedFromValues()
    {
        var result = MqttTopicResolver.Resolve(
            "data/{sourceId}/{tagName}",
            sourceId: "src+1",
            tagName: "tag#bad/name");

        result.Should().Be("data/src_1/tag_bad_name");
    }

    [Fact]
    public void Resolve_EmptyTemplate_ThrowsArgumentException()
    {
        var act = () => MqttTopicResolver.Resolve("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_TagNameSubstitution_ForPerTag()
    {
        var result = MqttTopicResolver.Resolve(
            "eremos/{gatewayId}/cnc/{sourceId}/{tagName}",
            gatewayId: "GW-PROD",
            sourceId: "AMS1CNC",
            tagName: "Mode_path1_CNC");

        result.Should().Be("eremos/GW-PROD/cnc/AMS1CNC/Mode_path1_CNC");
    }

    [Fact]
    public void Resolve_VeryLongSegments_ProducesValidTopic()
    {
        var longTag = new string('A', 500);
        var result = MqttTopicResolver.Resolve(
            "data/{tagName}",
            tagName: longTag);

        result.Should().Be($"data/{longTag}");
        result.Length.Should().BeGreaterThan(500);
    }
}
