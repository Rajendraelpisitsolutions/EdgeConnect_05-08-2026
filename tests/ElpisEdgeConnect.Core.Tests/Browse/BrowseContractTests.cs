// ============================================================================
// Tests: BrowseContractTests — pins the protocol-agnostic data shapes
//        + ITagBrowseService contract from plan v2.1 §4.1 + ADR-0015
//        Rule 9. These shapes are the wedge between Core.Browse-aware
//        wizards (Management) and per-protocol implementations
//        (Sources.OpcUaClient, Sources.EthernetIp).
//
//        Invariants pinned:
//          * BrowseNode default Children is empty (not null) — UI safely
//            iterates without null checks
//          * BrowseNode HasMoreChildren defaults false
//          * BrowseRequest defaults MaxDepth=1 (lazy expansion per Rule 9),
//            MaxNodes=1000 (soft cap)
//          * BrowseResult Truncated defaults false; DiagnosticMessage null
//          * BrowseNodeKind serialises as string (operator-readable JSON)
//          * BrowseNodeKind has the 4 named members in stable order
// Reference: docs/decisions/0015-wizard-contract.md Rule 9
//            docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §4.1
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Browse;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Browse;

public sealed class BrowseContractTests
{
    // ─── BrowseNode defaults ──────────────────────────────────────────────

    [Fact]
    public void BrowseNode_Defaults_ChildrenEmpty_HasMoreChildrenFalse()
    {
        // Wizards iterate Children without null-checks; the default must
        // be an empty list, not null.
        var node = new BrowseNode
        {
            NodeId = "ns=2;i=42",
            DisplayName = "Speed",
            Kind = BrowseNodeKind.Variable,
        };

        node.Children.Should().NotBeNull();
        node.Children.Should().BeEmpty();
        node.HasMoreChildren.Should().BeFalse();
        node.DataType.Should().BeNull();
    }

    [Fact]
    public void BrowseNode_AcceptsChildren_PreservesOrder()
    {
        var child1 = new BrowseNode { NodeId = "1", DisplayName = "A", Kind = BrowseNodeKind.Variable };
        var child2 = new BrowseNode { NodeId = "2", DisplayName = "B", Kind = BrowseNodeKind.Variable };

        var root = new BrowseNode
        {
            NodeId = "root",
            DisplayName = "Root",
            Kind = BrowseNodeKind.Folder,
            Children = new[] { child1, child2 },
        };

        root.Children.Should().HaveCount(2);
        root.Children[0].NodeId.Should().Be("1");
        root.Children[1].NodeId.Should().Be("2");
    }

    // ─── BrowseRequest defaults ───────────────────────────────────────────

    [Fact]
    public void BrowseRequest_Defaults_MaxDepth1_MaxNodes1000()
    {
        // Rule 9 says lazy expansion is the default; MaxDepth=1 enforces
        // that. MaxNodes=1000 matches the OPC UA stack subscription ceiling
        // for sensible default fan-out.
        var request = new BrowseRequest { SourceConfigJson = "{}" };

        request.MaxDepth.Should().Be(1);
        request.MaxNodes.Should().Be(1_000);
        request.StartingNodeId.Should().BeNull();
    }

    [Fact]
    public void BrowseRequest_OverridesAccepted()
    {
        var request = new BrowseRequest
        {
            SourceConfigJson = "{}",
            StartingNodeId = "ns=2;i=10",
            MaxDepth = 3,
            MaxNodes = 5_000,
        };

        request.StartingNodeId.Should().Be("ns=2;i=10");
        request.MaxDepth.Should().Be(3);
        request.MaxNodes.Should().Be(5_000);
    }

    // ─── BrowseResult defaults ────────────────────────────────────────────

    [Fact]
    public void BrowseResult_Defaults_TruncatedFalse_DiagnosticMessageNull()
    {
        var root = new BrowseNode { NodeId = "0", DisplayName = "Root", Kind = BrowseNodeKind.Folder };
        var result = new BrowseResult { Root = root };

        result.Truncated.Should().BeFalse();
        result.DiagnosticMessage.Should().BeNull();
        result.Root.Should().BeSameAs(root);
    }

    [Fact]
    public void BrowseResult_TruncatedWithDiagnostic_RoundTrips()
    {
        var root = new BrowseNode { NodeId = "0", DisplayName = "Root", Kind = BrowseNodeKind.Folder };
        var result = new BrowseResult
        {
            Root = root,
            Truncated = true,
            DiagnosticMessage = "stopped at 1000-node soft cap",
        };

        result.Truncated.Should().BeTrue();
        result.DiagnosticMessage.Should().Be("stopped at 1000-node soft cap");
    }

    // ─── BrowseNodeKind taxonomy ──────────────────────────────────────────

    [Fact]
    public void BrowseNodeKind_HasFiveMembers_InStableOrder()
    {
        // Operator-facing JSON serialisation depends on the names; if
        // a member is renamed or reordered, persisted config layouts
        // (when browse results land in config drafts in M.2c+) break.
        // View added in PR 5 amendment #2 (user lock 2026-05-29) — UA
        // View nodes are semantically distinct navigation surfaces;
        // preserved as their own kind so future wizard UX can render
        // them with their own icon (vs collapsing into Folder).
        System.Enum.GetNames(typeof(BrowseNodeKind)).Should().Equal(
            new[] { "Folder", "Variable", "Method", "Object", "View" });
        ((int)BrowseNodeKind.Folder).Should().Be(0);
        ((int)BrowseNodeKind.Variable).Should().Be(1);
        ((int)BrowseNodeKind.Method).Should().Be(2);
        ((int)BrowseNodeKind.Object).Should().Be(3);
        ((int)BrowseNodeKind.View).Should().Be(4);
    }

    [Theory]
    [InlineData(BrowseNodeKind.Folder, "\"Folder\"")]
    [InlineData(BrowseNodeKind.Variable, "\"Variable\"")]
    [InlineData(BrowseNodeKind.Method, "\"Method\"")]
    [InlineData(BrowseNodeKind.Object, "\"Object\"")]
    [InlineData(BrowseNodeKind.View, "\"View\"")]
    public void BrowseNodeKind_SerialisesAsString(BrowseNodeKind kind, string expectedJson)
    {
        // JsonStringEnumConverter is wired on the enum — confirm the
        // operator-readable JSON shape so persisted artefacts stay
        // human-debuggable.
        var json = JsonSerializer.Serialize(kind);
        json.Should().Be(expectedJson);
    }

    // ─── BrowseNode required fields ──────────────────────────────────────

    [Fact]
    public void BrowseNode_RequiredFields_AreEnforced()
    {
        // Construction without required fields fails at compile time;
        // this is a runtime smoke that the required modifier is on the
        // expected fields. A future ADR change that drops "required"
        // from one of these would silently break wizard code that
        // assumes the field is always populated.
        var nodeType = typeof(BrowseNode);
        var nodeIdProp = nodeType.GetProperty(nameof(BrowseNode.NodeId))!;
        var displayNameProp = nodeType.GetProperty(nameof(BrowseNode.DisplayName))!;
        var kindProp = nodeType.GetProperty(nameof(BrowseNode.Kind))!;

        IsRequired(nodeIdProp).Should().BeTrue("NodeId is the stable identity");
        IsRequired(displayNameProp).Should().BeTrue("DisplayName drives the UI");
        IsRequired(kindProp).Should().BeTrue("Kind drives icon + selectability");

        static bool IsRequired(System.Reflection.PropertyInfo prop) =>
            prop.GetCustomAttributes(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), false).Length > 0;
    }
}
