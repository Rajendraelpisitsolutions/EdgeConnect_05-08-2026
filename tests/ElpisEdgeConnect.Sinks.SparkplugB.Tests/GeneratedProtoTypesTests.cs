// ============================================================================
// File: GeneratedProtoTypesTests.cs
// Purpose: Slice-1 smoke tests for the vendored generated protobuf types
//          (ADR-0035 Rule 2). Proves the critical proto2 presence property the
//          whole golden suite depends on: a zero-valued optional field (e.g.
//          NBIRTH seq=0) is PHYSICALLY encoded on the wire, never omitted as a
//          default (the documented Tahu C# bug class). Full byte-level
//          conformance via the independent decoder lands in slice 2+.
// ============================================================================

using FluentAssertions;
using Google.Protobuf;
using Org.Eclipse.Tahu.Protobuf;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests;

public sealed class GeneratedProtoTypesTests
{
    [Fact]
    public void Payload_WithSeqZero_PhysicallyEncodesTheSeqField()
    {
        var payload = new Payload { Seq = 0 };

        var bytes = payload.ToByteArray();

        // Field 3 (seq), wire type 0 (varint) => tag byte 0x18, value byte 0x00.
        // proto2 optional presence: an explicitly-set zero MUST appear on the wire.
        bytes.Should().Equal(new byte[] { 0x18, 0x00 },
            "an explicitly-set seq=0 must be physically present (proto2 presence semantics; Tahu C# omission-bug canary)");
    }

    [Fact]
    public void Payload_WithSeqUnset_OmitsTheSeqField()
    {
        var payload = new Payload();

        payload.HasSeq.Should().BeFalse();
        payload.ToByteArray().Should().BeEmpty("an unset optional field must produce no bytes (required for NDEATH, which carries no seq)");
    }

    [Fact]
    public void Payload_SeqZero_RoundTrips_WithPresence()
    {
        var payload = new Payload { Seq = 0 };

        var parsed = Payload.Parser.ParseFrom(payload.ToByteArray());

        parsed.HasSeq.Should().BeTrue();
        parsed.Seq.Should().Be(0UL);
    }

    [Fact]
    public void Metric_IsNullTrue_PhysicallyEncodes_WithNoValueArm()
    {
        var metric = new Payload.Types.Metric { IsNull = true };

        var bytes = metric.ToByteArray();

        // Field 7 (is_null), wire type 0 => tag 0x38, value 0x01; and nothing else.
        bytes.Should().Equal(new byte[] { 0x38, 0x01 },
            "is_null=true must be physically encoded with no value arm bytes (ADR-0035 Rule 5 null treatment)");
    }
}
