// ============================================================================
// File: Wire/GeneratedEncoderCrossCheckTests.cs
// Purpose: Slice-2 harness — bytes produced by the vendored generated encoder
//          are examined through the INDEPENDENT wire decoder (whose own
//          correctness is established by hand-computed vectors in
//          ProtoWireDecoderTests). This is the pattern every golden
//          conformance test builds on from slice 3 onward; same-generated-class
//          round-trips alone are insufficient (ADR-0035 Rule 2).
// ============================================================================

using FluentAssertions;
using Google.Protobuf;
using Org.Eclipse.Tahu.Protobuf;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Wire;

public sealed class GeneratedEncoderCrossCheckTests
{
    // Pinned sparkplug_b.proto field numbers used below.
    private const int PayloadTimestamp = 1;
    private const int PayloadMetrics = 2;
    private const int PayloadSeq = 3;
    private const int MetricIsNull = 7;

    [Fact]
    public void EncodedSeqZero_SeenByIndependentDecoder_IsExactlyOnePresentVarintField()
    {
        var bytes = new Payload { Seq = 0 }.ToByteArray();

        var fields = ProtoWireDecoder.Decode(bytes);

        var field = fields.Should().ContainSingle().Subject;
        field.FieldNumber.Should().Be(PayloadSeq);
        field.WireType.Should().Be(ProtoWireType.Varint);
        field.VarintValue.Should().Be(0UL);
    }

    [Fact]
    public void EncodedPayloadWithMetric_SeenByIndependentDecoder_HasExpectedFieldsInOrderWithNestedIsNull()
    {
        var payload = new Payload
        {
            Timestamp = 123,
            Metrics = { new Payload.Types.Metric { IsNull = true } },
            Seq = 1,
        };

        var fields = ProtoWireDecoder.Decode(payload.ToByteArray());

        fields.Select(f => f.FieldNumber).Should().ContainInOrder(PayloadTimestamp, PayloadMetrics, PayloadSeq);
        fields.Should().HaveCount(3);

        fields[0].VarintValue.Should().Be(123UL);
        fields[2].VarintValue.Should().Be(1UL);

        var metric = ProtoWireDecoder.Decode(fields[1].LengthDelimitedBytes);
        var isNull = metric.Should().ContainSingle("the metric carries is_null and NO value arm").Subject;
        isNull.FieldNumber.Should().Be(MetricIsNull);
        isNull.VarintValue.Should().Be(1UL);
    }

    [Fact]
    public void EncodedEmptyPayload_SeenByIndependentDecoder_HasNoFields()
    {
        var bytes = new Payload().ToByteArray();

        ProtoWireDecoder.Decode(bytes).Should().BeEmpty("unset optional fields must be physically absent (NDEATH profile relies on this)");
    }
}
