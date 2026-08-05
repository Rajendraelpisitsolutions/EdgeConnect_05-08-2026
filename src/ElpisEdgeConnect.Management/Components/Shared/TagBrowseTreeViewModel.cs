// ============================================================================
// File: TagBrowseTreeViewModel.cs
// Purpose: State machine for the TagBrowseTreeView Razor component. Holds
//          the currently-expanded node set + the currently-selected node
//          set + helpers for ADR-0015 Rules 9 / 10 (browse + auto-load).
//          Extracted from the Razor so selection / expansion logic can be
//          tested without bUnit — matches the model-shell-over-POCO
//          convention used elsewhere in this project
//          (DestinationProtocolPickerModel, ReloadOutcomePanelModel, etc.).
//
// LOCKED behaviour:
//   * SelectNode / UnselectNode are idempotent — repeated calls leave the
//     selection set in a consistent state, no exceptions on
//     already-selected / not-selected nodes
//   * ExpandNode / CollapseNode are idempotent for the same reason
//   * EnumerateLeavesUnder respects ADR-0015 Rule 10 — only
//     BrowseNodeKind.Variable leaves count toward auto-load. Folder /
//     Object / Method nodes are NOT added as tags
//   * CountLeavesUnder is the cheap-to-compute count Rule 10's
//     confirmation prompt is gated on (>500 → confirm)
//
// Reference: docs/decisions/0015-wizard-contract.md Rule 9, Rule 10
//            docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §4.1, §4.2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Browse;

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>
/// State machine + selection helpers for the
/// <c>TagBrowseTreeView</c> Razor component. Holds expanded / selected
/// node-id sets and provides protocol-agnostic enumeration helpers.
/// </summary>
public sealed class TagBrowseTreeViewModel
{
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    /// <summary>
    /// Rule 10 default confirmation threshold. Auto-load actions that
    /// would add more than this many tags prompt the operator before
    /// committing. Configurable per wizard via the
    /// <c>AutoLoadConfirmationThreshold</c> Razor parameter.
    /// </summary>
    public const int DefaultAutoLoadConfirmationThreshold = 500;

    /// <summary>
    /// The Root of the most-recently-rendered browse result. Updated by
    /// the Razor when the parent passes a new
    /// <see cref="BrowseResult"/>. May be <see langword="null"/> before
    /// the first browse call returns.
    /// </summary>
    public BrowseNode? Root { get; set; }

    /// <summary>Snapshot of currently-expanded node ids.</summary>
    public IReadOnlySet<string> ExpandedNodeIds => _expanded;

    /// <summary>Snapshot of currently-selected node ids.</summary>
    public IReadOnlySet<string> SelectedNodeIds => _selected;

    /// <summary>True if <see cref="SelectedNodeIds"/> is empty.</summary>
    public bool HasNoSelection => _selected.Count == 0;

    // ─── Expansion state ───────────────────────────────────────────────

    /// <summary>Mark <paramref name="nodeId"/> as expanded. Idempotent.</summary>
    public void ExpandNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        _expanded.Add(nodeId);
    }

    /// <summary>Mark <paramref name="nodeId"/> as collapsed. Idempotent.</summary>
    public void CollapseNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        _expanded.Remove(nodeId);
    }

    /// <summary>True when the node is currently expanded.</summary>
    public bool IsExpanded(string nodeId) => _expanded.Contains(nodeId);

    // ─── Selection state ───────────────────────────────────────────────

    /// <summary>Add <paramref name="nodeId"/> to the selection. Idempotent.</summary>
    public void SelectNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        _selected.Add(nodeId);
    }

    /// <summary>Remove <paramref name="nodeId"/> from the selection. Idempotent.</summary>
    public void UnselectNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        _selected.Remove(nodeId);
    }

    /// <summary>Toggle the selection state of <paramref name="nodeId"/>.</summary>
    public void ToggleSelection(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        if (!_selected.Add(nodeId))
        {
            _selected.Remove(nodeId);
        }
    }

    /// <summary>True when the node is currently in the selection set.</summary>
    public bool IsSelected(string nodeId) => _selected.Contains(nodeId);

    /// <summary>Clear all selection. Does NOT clear expansion state.</summary>
    public void ClearSelection() => _selected.Clear();

    // ─── ADR-0015 Rule 10 — Auto-load helpers ─────────────────────────

    /// <summary>
    /// Enumerate the leaf Variable nodes reachable from
    /// <paramref name="root"/>. Folder / Object / Method nodes are
    /// recursed into but NOT included in the output — only Variables
    /// become canonical tags per Rule 10. Ordering is depth-first,
    /// pre-order (matches what the operator sees in the rendered tree).
    /// </summary>
    public static IEnumerable<BrowseNode> EnumerateLeavesUnder(BrowseNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return EnumerateLeavesUnderImpl(root);
    }

    private static IEnumerable<BrowseNode> EnumerateLeavesUnderImpl(BrowseNode node)
    {
        if (node.Kind == BrowseNodeKind.Variable)
        {
            yield return node;
        }

        foreach (var child in node.Children)
        {
            foreach (var leaf in EnumerateLeavesUnderImpl(child))
            {
                yield return leaf;
            }
        }
    }

    /// <summary>
    /// Cheap count for the leaf Variable nodes reachable from
    /// <paramref name="root"/>. Used by the Razor to gate the
    /// confirmation prompt per Rule 10.
    /// </summary>
    public static int CountLeavesUnder(BrowseNode root) =>
        EnumerateLeavesUnder(root).Count();

    /// <summary>
    /// Snapshot the currently-selected node ids resolved against the
    /// most-recently-rendered <see cref="Root"/>. Non-Variable selections
    /// (Folder / Object / Method) are FILTERED OUT — Rule 10 only emits
    /// Variables as canonical tags. Selected node ids that no longer
    /// exist in the current Root (because a re-browse happened) are
    /// silently dropped.
    /// </summary>
    public IReadOnlyList<BrowseNode> ResolveSelectedVariables()
    {
        if (Root is null || _selected.Count == 0)
        {
            return Array.Empty<BrowseNode>();
        }

        var lookup = BuildLookup(Root);
        var result = new List<BrowseNode>(_selected.Count);
        foreach (var nodeId in _selected)
        {
            if (lookup.TryGetValue(nodeId, out var node) && node.Kind == BrowseNodeKind.Variable)
            {
                result.Add(node);
            }
        }
        return result;
    }

    private static Dictionary<string, BrowseNode> BuildLookup(BrowseNode root)
    {
        var dict = new Dictionary<string, BrowseNode>(StringComparer.Ordinal);
        AddRecursive(root, dict);
        return dict;

        static void AddRecursive(BrowseNode n, Dictionary<string, BrowseNode> dict)
        {
            dict[n.NodeId] = n;
            foreach (var child in n.Children)
            {
                AddRecursive(child, dict);
            }
        }
    }
}
