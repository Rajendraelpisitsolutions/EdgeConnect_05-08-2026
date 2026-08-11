// ============================================================================
// Tests: OpcUaQualityMapping — DataQuality → OPC UA StatusCode.
//        Locked by shared-knowledge/contracts/opcua-namespace-policy.md
//        § Quality propagation. Any drift from these mappings is a v2
//        namespace bump.
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Opc.Ua;
using Xunit;

namespace ElpisEdgeConnect.Sinks.OpcUaServer.Tests;

public class OpcUaQualityMappingTests
{
    [Theory]
    [InlineData(DataQuality.Good)]
    public void Good_MapsTo_Good(DataQuality input)
    {
        OpcUaQualityMapping.ToStatusCode(input).Code.Should().Be(StatusCodes.Good);
    }

    [Theory]
    [InlineData(DataQuality.Uncertain)]
    public void Uncertain_MapsTo_Uncertain(DataQuality input)
    {
        OpcUaQualityMapping.ToStatusCode(input).Code.Should().Be(StatusCodes.Uncertain);
    }

    [Theory]
    [InlineData(DataQuality.Bad)]
    [InlineData(DataQuality.Unknown)]
    public void BadOrUnknown_MapsTo_Bad(DataQuality input)
    {
        OpcUaQualityMapping.ToStatusCode(input).Code.Should().Be(StatusCodes.Bad);
    }

    [Fact]
    public void Stale_MapsTo_UncertainSubstituteValue()
    {
        OpcUaQualityMapping.ToStatusCode(DataQuality.Stale).Code
            .Should().Be(StatusCodes.UncertainSubstituteValue);
    }
}
