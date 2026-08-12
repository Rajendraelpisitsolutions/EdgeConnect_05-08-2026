// ============================================================================
// Tests: OpcUaNodeIdCanonicalizerTests — pin the PR 7c-1 shared
//        canonicalisation utility's semantic-equality contract.
//
//        Invariants pinned (per PR 6b + PR 7c plans, user lock
//        2026-05-29):
//
//          1. null / empty / whitespace → empty string (defensive
//             dictionary keying)
//          2. Numeric NodeIds with leading zeros canonicalise to the
//             same form ("ns=2;i=00000001" == "ns=2;i=1")
//          3. String NodeIds are CASE-SENSITIVE per OPC UA Part 4
//             ("ns=2;s=Foo" != "ns=2;s=foo")
//          4. Malformed input round-trips unchanged (operator hand-
//             entry errors surface as Add/Remove churn rather than
//             being silently equated)
//          5. NodeId object overload returns null when input is null;
//             otherwise the stack's ToString form
//          6. Idempotent: Canonicalize(Canonicalize(x)) == Canonicalize(x)
// Reference: PR 7c-1 plan + amendments (user lock 2026-05-29)
// ============================================================================

using FluentAssertions;
using Opc.Ua;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaNodeIdCanonicalizerTests
{
    // ─── Defensive empties ───────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Canonicalize_NullOrWhitespace_ReturnsEmptyString(string? input)
    {
        OpcUaNodeIdCanonicalizer.Canonicalize(input).Should().Be(string.Empty);
    }

    // ─── Semantic equality across numeric forms ──────────────────────

    [Fact]
    public void Canonicalize_PaddedNumericNodeId_MatchesUnpaddedForm()
    {
        var padded = OpcUaNodeIdCanonicalizer.Canonicalize("ns=2;i=00000001");
        var unpadded = OpcUaNodeIdCanonicalizer.Canonicalize("ns=2;i=1");

        padded.Should().Be(unpadded,
            "the OPC stack's NodeId.Parse normalises leading zeros — that's the whole point of canonicalisation");
    }

    [Fact]
    public void Canonicalize_DifferentNamespaceIndices_ProduceDistinctKeys()
    {
        var ns2 = OpcUaNodeIdCanonicalizer.Canonicalize("ns=2;i=1");
        var ns3 = OpcUaNodeIdCanonicalizer.Canonicalize("ns=3;i=1");

        ns2.Should().NotBe(ns3,
            "namespace index is part of the identity — different namespaces are different nodes");
    }

    // ─── String NodeIds are case-sensitive (Part 4 §7.6) ─────────────

    [Fact]
    public void Canonicalize_StringNodeIds_AreCaseSensitive()
    {
        var upper = OpcUaNodeIdCanonicalizer.Canonicalize("ns=2;s=Foo");
        var lower = OpcUaNodeIdCanonicalizer.Canonicalize("ns=2;s=foo");

        upper.Should().NotBe(lower,
            "OPC UA Part 4 §7.6 — string identifier equality is case-sensitive; "
            + "operators who hand-type 'Foo' vs 'foo' MUST see them as distinct nodes");
    }

    // ─── Malformed input round-trips ─────────────────────────────────

    [Fact]
    public void Canonicalize_MalformedInput_ReturnsRawString()
    {
        var malformed = "this is not a valid node id";

        var result = OpcUaNodeIdCanonicalizer.Canonicalize(malformed);

        // Defensive — never throws, returns input verbatim so the
        // operator's broken hand-entry surfaces as Add/Remove churn
        // rather than silently equating to other broken entries.
        result.Should().Be(malformed);
    }

    // ─── NodeId-object overload ──────────────────────────────────────

    [Fact]
    public void Canonicalize_NullNodeId_ReturnsNull()
    {
        OpcUaNodeIdCanonicalizer.Canonicalize((NodeId?)null).Should().BeNull();
    }

    [Fact]
    public void Canonicalize_NodeIdObject_RoundTripsThroughToString()
    {
        var nodeId = new NodeId("Foo", namespaceIndex: 2);

        var result = OpcUaNodeIdCanonicalizer.Canonicalize(nodeId);

        result.Should().Be(nodeId.ToString());
    }

    // ─── Idempotency ─────────────────────────────────────────────────

    [Theory]
    [InlineData("ns=2;i=1")]
    [InlineData("ns=2;s=Foo")]
    [InlineData("ns=2;i=00000001")]
    [InlineData("not parseable")]
    public void Canonicalize_IsIdempotent(string input)
    {
        var once = OpcUaNodeIdCanonicalizer.Canonicalize(input);
        var twice = OpcUaNodeIdCanonicalizer.Canonicalize(once);

        twice.Should().Be(once,
            "canonicalising a canonical form must be a no-op");
    }
}
