// ============================================================================
// Tests: TagBrowseTreeViewModelTests — pins the state machine + selection
//        helpers driving the TagBrowseTreeView Razor component. Same
//        model-shell-over-POCO convention used by
//        DestinationProtocolPickerModel / ReloadOutcomePanelModel —
//        testing the model gives bUnit-equivalent regression coverage
//        without standing up the Razor renderer.
//
//        Invariants pinned:
//          * Select / Unselect / Expand / Collapse are idempotent
//          * ToggleSelection alternates Select / Unselect cleanly
//          * EnumerateLeavesUnder yields only BrowseNodeKind.Variable
//            nodes (Rule 10 — only Variables become canonical tags)
//          * EnumerateLeavesUnder traversal order is depth-first
//            pre-order (matches what the operator sees in the rendered
//            tree, so "Add all under" preserves rendered order)
//          * ResolveSelectedVariables filters non-Variable kinds and
//            silently drops node ids absent from the current Root
//            (handles re-browse churn cleanly)
//          * Default auto-load confirmation threshold is 500
// Reference: docs/decisions/0015-wizard-contract.md Rule 9, Rule 10
//            docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §4.1, §4.2
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Core.Browse;
using ElpisEdgeConnect.Management.Components.Shared;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class TagBrowseTreeViewModelTests
{
    // ─── Defaults ─────────────────────────────────────────────────────

    [Fact]
    public void Defaults_NoRoot_NoSelection_NoExpansion()
    {
        var model = new TagBrowseTreeViewModel();

        model.Root.Should().BeNull();
        model.HasNoSelection.Should().BeTrue();
        model.SelectedNodeIds.Should().BeEmpty();
        model.ExpandedNodeIds.Should().BeEmpty();
    }

    [Fact]
    public void DefaultAutoLoadConfirmationThreshold_Is500()
    {
        // Rule 10 lock — operator-configurable per wizard, but the
        // shared default is 500. Changing this requires v2.1 amendment.
        TagBrowseTreeViewModel.DefaultAutoLoadConfirmationThreshold.Should().Be(500);
    }

    // ─── Selection state ──────────────────────────────────────────────

    [Fact]
    public void SelectNode_AddsToSelection_IsIdempotent()
    {
        var model = new TagBrowseTreeViewModel();

        model.SelectNode("a");
        model.SelectNode("a"); // idempotent
        model.SelectNode("b");

        model.SelectedNodeIds.Should().BeEquivalentTo(new[] { "a", "b" });
        model.IsSelected("a").Should().BeTrue();
        model.IsSelected("b").Should().BeTrue();
        model.IsSelected("c").Should().BeFalse();
    }

    [Fact]
    public void UnselectNode_RemovesFromSelection_IsIdempotent()
    {
        var model = new TagBrowseTreeViewModel();
        model.SelectNode("a");
        model.SelectNode("b");

        model.UnselectNode("a");
        model.UnselectNode("a"); // idempotent
        model.UnselectNode("never-selected"); // idempotent

        model.SelectedNodeIds.Should().BeEquivalentTo(new[] { "b" });
    }

    [Fact]
    public void ToggleSelection_AlternatesCleanly()
    {
        var model = new TagBrowseTreeViewModel();

        model.ToggleSelection("a"); // → selected
        model.IsSelected("a").Should().BeTrue();

        model.ToggleSelection("a"); // → unselected
        model.IsSelected("a").Should().BeFalse();

        model.ToggleSelection("a"); // → selected again
        model.IsSelected("a").Should().BeTrue();
    }

    [Fact]
    public void ClearSelection_EmptiesSelectionButPreservesExpansion()
    {
        var model = new TagBrowseTreeViewModel();
        model.SelectNode("a");
        model.SelectNode("b");
        model.ExpandNode("a");

        model.ClearSelection();

        model.SelectedNodeIds.Should().BeEmpty();
        model.ExpandedNodeIds.Should().BeEquivalentTo(new[] { "a" },
            "clearing selection must NOT collapse the tree the operator just expanded");
    }

    // ─── Expansion state ──────────────────────────────────────────────

    [Fact]
    public void ExpandNode_CollapseNode_AreIdempotent()
    {
        var model = new TagBrowseTreeViewModel();

        model.ExpandNode("a");
        model.ExpandNode("a"); // idempotent
        model.CollapseNode("never-expanded"); // idempotent

        model.IsExpanded("a").Should().BeTrue();
        model.IsExpanded("b").Should().BeFalse();
    }

    // ─── EnumerateLeavesUnder — Rule 10 contract ─────────────────────

    [Fact]
    public void EnumerateLeavesUnder_OnlyVariables_IgnoresFoldersObjectsMethods()
    {
        // Tree shape:
        //   Folder(root)
        //   ├── Variable(v1)
        //   ├── Object(obj)
        //   │   ├── Variable(v2)
        //   │   └── Method(m1)
        //   └── Folder(sub)
        //       └── Variable(v3)
        var tree = new BrowseNode
        {
            NodeId = "root",
            DisplayName = "Root",
            Kind = BrowseNodeKind.Folder,
            Children = new[]
            {
                new BrowseNode { NodeId = "v1", DisplayName = "V1", Kind = BrowseNodeKind.Variable },
                new BrowseNode
                {
                    NodeId = "obj",
                    DisplayName = "Obj",
                    Kind = BrowseNodeKind.Object,
                    Children = new[]
                    {
                        new BrowseNode { NodeId = "v2", DisplayName = "V2", Kind = BrowseNodeKind.Variable },
                        new BrowseNode { NodeId = "m1", DisplayName = "M1", Kind = BrowseNodeKind.Method },
                    },
                },
                new BrowseNode
                {
                    NodeId = "sub",
                    DisplayName = "Sub",
                    Kind = BrowseNodeKind.Folder,
                    Children = new[]
                    {
                        new BrowseNode { NodeId = "v3", DisplayName = "V3", Kind = BrowseNodeKind.Variable },
                    },
                },
            },
        };

        var leaves = TagBrowseTreeViewModel.EnumerateLeavesUnder(tree).ToArray();

        leaves.Select(n => n.NodeId).Should().Equal(new[] { "v1", "v2", "v3" },
            "Variables are emitted depth-first pre-order; Folder/Object/Method are NOT counted as tags per Rule 10.");
    }

    [Fact]
    public void EnumerateLeavesUnder_ViewNode_IsFilteredOutLikeFolders()
    {
        // PR 5 amendment #2 — View is a distinct kind (vs collapsing
        // into Folder) so future wizard UX can render it differently.
        // But like Folders / Objects / Methods, View nodes are NOT
        // selectable as tags. EnumerateLeavesUnder must NEVER yield a
        // View node.
        var tree = new BrowseNode
        {
            NodeId = "root",
            DisplayName = "Root",
            Kind = BrowseNodeKind.Folder,
            Children = new[]
            {
                new BrowseNode
                {
                    NodeId = "supervisor-view",
                    DisplayName = "Supervisor view",
                    Kind = BrowseNodeKind.View,
                    Children = new[]
                    {
                        new BrowseNode { NodeId = "v1", DisplayName = "V1", Kind = BrowseNodeKind.Variable },
                    },
                },
            },
        };

        var leaves = TagBrowseTreeViewModel.EnumerateLeavesUnder(tree).ToArray();

        leaves.Select(n => n.NodeId).Should().Equal(new[] { "v1" },
            "View descendants ARE traversed (so operators can pick Variables under a View) "
            + "but the View node itself is NOT a leaf.");
    }

    [Fact]
    public void EnumerateLeavesUnder_VariableRoot_YieldsItself()
    {
        var leaf = new BrowseNode { NodeId = "v", DisplayName = "V", Kind = BrowseNodeKind.Variable };

        TagBrowseTreeViewModel.EnumerateLeavesUnder(leaf).Should().HaveCount(1);
    }

    [Fact]
    public void CountLeavesUnder_MatchesEnumeration()
    {
        var tree = MakeTreeWithLeafCount(7);

        TagBrowseTreeViewModel.CountLeavesUnder(tree).Should().Be(7);
    }

    // ─── ResolveSelectedVariables — Rule 10 strict-Variable filter ───

    [Fact]
    public void ResolveSelectedVariables_FiltersNonVariableKinds()
    {
        // Operator could select a Folder node by checkbox (UI may allow
        // it in future) — but ResolveSelectedVariables MUST NOT return
        // it as a tag.
        var tree = new BrowseNode
        {
            NodeId = "root",
            DisplayName = "Root",
            Kind = BrowseNodeKind.Folder,
            Children = new[]
            {
                new BrowseNode { NodeId = "v1", DisplayName = "V1", Kind = BrowseNodeKind.Variable },
                new BrowseNode { NodeId = "obj", DisplayName = "Obj", Kind = BrowseNodeKind.Object },
            },
        };
        var model = new TagBrowseTreeViewModel { Root = tree };
        model.SelectNode("root");  // Folder
        model.SelectNode("v1");    // Variable
        model.SelectNode("obj");   // Object

        var resolved = model.ResolveSelectedVariables();

        resolved.Select(n => n.NodeId).Should().Equal(new[] { "v1" });
    }

    [Fact]
    public void ResolveSelectedVariables_NoRoot_ReturnsEmpty()
    {
        var model = new TagBrowseTreeViewModel();
        model.SelectNode("a");

        model.ResolveSelectedVariables().Should().BeEmpty();
    }

    [Fact]
    public void ResolveSelectedVariables_SilentlyDropsUnknownNodeIds()
    {
        // After a re-browse, the operator's previously-selected ids
        // may no longer exist in the new Root. Resolution silently
        // drops them rather than throwing — better operator UX.
        var freshRoot = new BrowseNode
        {
            NodeId = "root",
            DisplayName = "Root",
            Kind = BrowseNodeKind.Folder,
            Children = new[]
            {
                new BrowseNode { NodeId = "current-v1", DisplayName = "V1", Kind = BrowseNodeKind.Variable },
            },
        };
        var model = new TagBrowseTreeViewModel { Root = freshRoot };
        model.SelectNode("stale-from-prior-browse");
        model.SelectNode("current-v1");

        var resolved = model.ResolveSelectedVariables();

        resolved.Select(n => n.NodeId).Should().Equal(new[] { "current-v1" });
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static BrowseNode MakeTreeWithLeafCount(int leafCount)
    {
        var children = new BrowseNode[leafCount];
        for (var i = 0; i < leafCount; i++)
        {
            children[i] = new BrowseNode
            {
                NodeId = $"v{i}",
                DisplayName = $"V{i}",
                Kind = BrowseNodeKind.Variable,
            };
        }
        return new BrowseNode
        {
            NodeId = "root",
            DisplayName = "Root",
            Kind = BrowseNodeKind.Folder,
            Children = children,
        };
    }
}
