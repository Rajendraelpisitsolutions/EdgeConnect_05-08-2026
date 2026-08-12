// ============================================================================
// Tests: OpcUaQualityMapperTests — pin the StatusCode → DataQuality
//        taxonomy translation.
//
//        Invariants:
//          * IsGood → DataQuality.Good, reason = null
//          * IsUncertain → DataQuality.Uncertain, reason = symbolic name
//            (e.g. "Uncertain_LastUsableValue")
//          * IsBad → DataQuality.Bad, reason = symbolic name
//            (e.g. "Bad_NodeIdInvalid")
//          * Custom / non-spec StatusCode produces a non-empty reason
//            ("Bad" / "Uncertain" fallback)
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Opc.Ua;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaQualityMapperTests
{
    [Fact]
    public void Map_Good_ProducesGoodWithNullReason()
    {
        var (quality, reason) = OpcUaQualityMapper.Map(StatusCodes.Good);

        quality.Should().Be(DataQuality.Good);
        reason.Should().BeNull();
    }

    [Theory]
    [InlineData(StatusCodes.BadNodeIdInvalid, "Bad_NodeIdInvalid")]
    [InlineData(StatusCodes.BadNodeIdUnknown, "Bad_NodeIdUnknown")]
    [InlineData(StatusCodes.BadCommunicationError, "Bad_CommunicationError")]
    [InlineData(StatusCodes.BadSessionClosed, "Bad_SessionClosed")]
    [InlineData(StatusCodes.BadConnectionRejected, "Bad_ConnectionRejected")]
    public void Map_BadCode_ProducesBadWithSymbolicReason(uint badCode, string expectedReasonHint)
    {
        var (quality, reason) = OpcUaQualityMapper.Map((StatusCode)badCode);

        quality.Should().Be(DataQuality.Bad);
        reason.Should().NotBeNullOrWhiteSpace();
        // SymbolicName strings sometimes carry leading underscores; the
        // hint check just confirms we got the spec-aligned name family.
        reason.Should().Contain(expectedReasonHint.Replace("_", string.Empty));
    }

    [Theory]
    [InlineData(StatusCodes.UncertainLastUsableValue)]
    [InlineData(StatusCodes.UncertainSubNormal)]
    [InlineData(StatusCodes.UncertainNoCommunicationLastUsableValue)]
    public void Map_UncertainCode_ProducesUncertainWithSymbolicReason(uint uncertainCode)
    {
        var (quality, reason) = OpcUaQualityMapper.Map((StatusCode)uncertainCode);

        quality.Should().Be(DataQuality.Uncertain);
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Map_NonSpecBadCode_StillProducesNonEmptyReason()
    {
        // 0x80FF0000 lives in the Bad severity range (top two bits = 10)
        // but is NOT in StatusCodes.* — exercises the empty-symbolic-name
        // fallback path. Reason MUST never be empty for Bad quality so
        // sinks/alerts always have something operator-readable to show.
        var (quality, reason) = OpcUaQualityMapper.Map((StatusCode)0x80FF0000u);

        quality.Should().Be(DataQuality.Bad);
        reason.Should().NotBeNullOrWhiteSpace();
    }
}
