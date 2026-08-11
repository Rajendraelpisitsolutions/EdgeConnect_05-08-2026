// ============================================================================
// File: Session/SparkplugNodeCommandTests.cs
// Purpose: Locks the fail-safe NCMD classifier (plan v3 §1.6, slice-7 review B1):
//          a well-formed Node Control/Rebirth = true is RebirthRequested (or
//          RebirthRequestedWithUnknownExtras when other metrics are present); every
//          other case classifies to a distinct, redacted Ignored* kind (false,
//          null, wrong-type, missing/unknown-only, malformed) — a no-op that is now
//          DISTINGUISHABLE for diagnostics, never throwing.
// ============================================================================

using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
using ElpisEdgeConnect.Sinks.SparkplugB.Session;
using FluentAssertions;
using Google.Protobuf;
using Org.Eclipse.Tahu.Protobuf;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;

public sealed class SparkplugNodeCommandTests
{
    private const string Rebirth = "Node Control/Rebirth";

    private static byte[] Encode(Payload payload) => payload.ToByteArray();

    private static Payload WithMetric(Payload.Types.Metric metric)
    {
        var payload = new Payload();
        payload.Metrics.Add(metric);
        return payload;
    }

    [Fact]
    public void Classify_RebirthTrue_IsRebirthRequested()
    {
        var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true }));

        var kind = SparkplugNodeCommand.Classify(bytes);

        kind.Should().Be(SparkplugNodeCommandKind.RebirthRequested);
        kind.IsActionableRebirth().Should().BeTrue();
        kind.DiagnosticCode().Should().Be("rebirth");
    }

    [Fact]
    public void Classify_RebirthTruePlusUnknownExtras_IsRebirthRequestedWithUnknownExtras()
    {
        var payload = new Payload();
        payload.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true });
        payload.Metrics.Add(new Payload.Types.Metric { Name = "Some/Other", IntValue = 7 });

        var kind = SparkplugNodeCommand.Classify(Encode(payload));

        kind.Should().Be(SparkplugNodeCommandKind.RebirthRequestedWithUnknownExtras);
        kind.IsActionableRebirth().Should().BeTrue(); // still actionable: rebirth once, extras diagnosed
        kind.DiagnosticCode().Should().Be("rebirth+unknown-extras");
    }

    [Fact]
    public void Classify_RebirthFalse_IsIgnoredFalse()
    {
        var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = Rebirth, BooleanValue = false }));

        var kind = SparkplugNodeCommand.Classify(bytes);

        kind.Should().Be(SparkplugNodeCommandKind.IgnoredFalse);
        kind.IsActionableRebirth().Should().BeFalse();
        kind.DiagnosticCode().Should().Be("ignored:false");
    }

    [Fact]
    public void Classify_RebirthExplicitlyNull_IsIgnoredNull()
    {
        var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true, IsNull = true }));

        SparkplugNodeCommand.Classify(bytes).Should().Be(SparkplugNodeCommandKind.IgnoredNull);
    }

    [Fact]
    public void Classify_RebirthWrongValueArm_IsIgnoredWrongType()
    {
        // The protobuf oneof value arm is authoritative: a "Node Control/Rebirth" whose value is on the Int
        // arm (not the Boolean arm) is not a valid rebirth command, regardless of any Datatype field.
        var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = Rebirth, IntValue = 1 }));

        SparkplugNodeCommand.Classify(bytes).Should().Be(SparkplugNodeCommandKind.IgnoredWrongType);
    }

    [Fact]
    public void Classify_UnknownOnlyCommand_IsIgnoredMissing()
    {
        var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = "Node Control/Reboot", BooleanValue = true }));

        SparkplugNodeCommand.Classify(bytes).Should().Be(SparkplugNodeCommandKind.IgnoredMissing);
    }

    [Fact]
    public void Classify_EmptyPayload_IsIgnoredMissing()
    {
        SparkplugNodeCommand.Classify(Encode(new Payload())).Should().Be(SparkplugNodeCommandKind.IgnoredMissing);
    }

    [Fact]
    public void Classify_MalformedBytes_IsIgnoredMalformed()
    {
        // Random bytes that are not a valid protobuf Payload must classify as malformed, never throw.
        SparkplugNodeCommand.Classify(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })
            .Should().Be(SparkplugNodeCommandKind.IgnoredMalformed);
    }

    [Fact]
    public void Classify_DuplicateRebirthMetrics_IsIgnoredAmbiguous_OrderIndependent()
    {
        // Two Node Control/Rebirth metrics with conflicting value arms — the meaning would depend on which
        // one "wins" by ordering, so it must be ignored regardless of order (fail-safe, review r2).
        var payload = new Payload();
        payload.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true });
        payload.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, IntValue = 0 });

        var forward = SparkplugNodeCommand.Classify(Encode(payload));

        var reversed = new Payload();
        reversed.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, IntValue = 0 });
        reversed.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true });

        forward.Should().Be(SparkplugNodeCommandKind.IgnoredAmbiguous);
        SparkplugNodeCommand.Classify(Encode(reversed)).Should().Be(SparkplugNodeCommandKind.IgnoredAmbiguous);
        forward.IsActionableRebirth().Should().BeFalse();
        forward.DiagnosticCode().Should().Be("ignored:ambiguous");
    }

    [Fact]
    public void DiagnosticCode_IsSecretFree_ForEveryKind()
    {
        // No classification code carries a raw metric name or payload byte — only stable, redacted labels.
        foreach (SparkplugNodeCommandKind kind in System.Enum.GetValues(typeof(SparkplugNodeCommandKind)))
        {
            kind.DiagnosticCode().Should().MatchRegex("^[a-z:+-]+$");
        }
    }
}
