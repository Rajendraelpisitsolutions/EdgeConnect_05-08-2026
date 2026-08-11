// ============================================================================
// File: Topics/SparkplugTopicFactoryTests.cs
// Purpose: Locks the exact spBv1.0 topic strings (ADR-0035 Rules 3/4),
//          including the exact no-wildcard NCMD subscribe topic the
//          conformance requirement demands.
// ============================================================================

using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Topics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Topics;

public sealed class SparkplugTopicFactoryTests
{
    private static readonly SparkplugEdgeNodeIdentity Identity =
        SparkplugEdgeNodeIdentity.Create("PlantA", "gateway-01");

    [Fact]
    public void NBirth_BuildsExactTopic()
    {
        SparkplugTopicFactory.NBirth(Identity).Should().Be("spBv1.0/PlantA/NBIRTH/gateway-01");
    }

    [Fact]
    public void NData_BuildsExactTopic()
    {
        SparkplugTopicFactory.NData(Identity).Should().Be("spBv1.0/PlantA/NDATA/gateway-01");
    }

    [Fact]
    public void NDeath_BuildsExactTopic()
    {
        SparkplugTopicFactory.NDeath(Identity).Should().Be("spBv1.0/PlantA/NDEATH/gateway-01");
    }

    [Fact]
    public void NCmdSubscribe_BuildsExactTopicWithoutWildcards()
    {
        var topic = SparkplugTopicFactory.NCmdSubscribe(Identity);

        topic.Should().Be("spBv1.0/PlantA/NCMD/gateway-01");
        topic.Should().NotContainAny("+", "#");
    }
}
