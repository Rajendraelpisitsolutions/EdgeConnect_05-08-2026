// ============================================================================
// File: OpcUaBrowseTreeBuilder.cs
// Purpose: Recursive Browse + BrowseNext loop that assembles a tree of
//          BrowseNodes from an OPC UA address space. Pure logic over an
//          IOpcUaBrowseExecutor seam (the tests substitute the executor
//          to verify every edge case without a live server).
//
// LOCKED behaviour (per PR 5 plan + amendments, user lock 2026-05-29):
//
//   1. Recursive level-by-level walk — MaxDepth decrements at each level
//   2. Shared MaxNodes counter across the whole traversal — prevents
//      explosion on broad trees
//   3. Truncation when MaxNodes hit:
//        * Truncated = true on the BrowseResult
//        * HasMoreChildren = true on the parent BrowseNode whose
//          children were cut short
//        * Continuation points released back to the server (amendment
//          #1 "strongly approve")
//   4. Cyclic-reference protection (amendment #3) — HashSet<string> of
//      visited NodeIds; re-encountering a visited NodeId terminates
//      recursion at that branch without throwing
//   5. NodeClass mapping via OpcUaBrowseNodeKindMapper (amendment #2:
//      View preserved as distinct kind)
//   6. Diagnostic counters packed into BrowseResult.DiagnosticMessage:
//        NodesVisited / NodesReturned / ContinuationPointsConsumed
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
//            PR 5 plan + amendments (user lock 2026-05-29)
//            OPC UA Part 4 §5.8 (Browse / BrowseNext)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Browse;
using Opc.Ua;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Recursive browse + tree-build logic. Constructs a single instance
/// per <see cref="BuildAsync"/> call so MaxNodes state and visited-set
/// are scoped to one tree.
/// </summary>
internal sealed class OpcUaBrowseTreeBuilder
{
    private readonly IOpcUaBrowseExecutor _executor;
    private readonly int _maxDepth;
    private readonly int _maxNodes;
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);

    private int _nodesVisited;
    private int _nodesReturned;
    private int _continuationPointsConsumed;
    private bool _truncated;

    public OpcUaBrowseTreeBuilder(IOpcUaBrowseExecutor executor, int maxDepth, int maxNodes)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (maxDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "MaxDepth must be >= 1.");
        }
        if (maxNodes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNodes), maxNodes, "MaxNodes must be >= 1.");
        }
        _executor = executor;
        _maxDepth = maxDepth;
        _maxNodes = maxNodes;
    }

    /// <summary>
    /// Build a <see cref="Core.Browse.BrowseResult"/> starting at
    /// <paramref name="startingNodeId"/>. When <paramref name="startingNodeId"/>
    /// is the OPC root folder (<c>i=84</c>), the returned tree's
    /// <see cref="BrowseNode.NodeId"/> reflects that root.
    /// </summary>
    public async Task<Core.Browse.BrowseResult> BuildAsync(NodeId startingNodeId, string startingDisplayName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(startingNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(startingDisplayName);

        // Mark the starting node as visited up front so a cycle pointing
        // back to it doesn't recurse.
        _visited.Add(startingNodeId.ToString() ?? string.Empty);

        var rootChildren = await BrowseLevelAsync(startingNodeId, depthRemaining: _maxDepth, ct).ConfigureAwait(false);

        var rootNode = new BrowseNode
        {
            NodeId = startingNodeId.ToString() ?? string.Empty,
            DisplayName = startingDisplayName,
            Kind = BrowseNodeKind.Folder,  // The "starting node" is treated as a folder for display purposes
            Children = rootChildren.Children,
            HasMoreChildren = rootChildren.HasMoreChildren,
        };

        return new Core.Browse.BrowseResult
        {
            Root = rootNode,
            Truncated = _truncated,
            DiagnosticMessage = $"NodesVisited={_nodesVisited}, NodesReturned={_nodesReturned}, "
                + $"ContinuationPointsConsumed={_continuationPointsConsumed}",
        };
    }

    /// <summary>
    /// Browse a single level (Browse + BrowseNext loop) and return the
    /// children list plus a flag indicating whether more references
    /// existed beyond the MaxNodes cap.
    /// </summary>
    private async Task<(IReadOnlyList<BrowseNode> Children, bool HasMoreChildren)> BrowseLevelAsync(
        NodeId parentId,
        int depthRemaining,
        CancellationToken ct)
    {
        var children = new List<BrowseNode>();
        _nodesVisited++;

        // Initial Browse call.
        var result = await _executor.BrowseAsync(parentId, ct).ConfigureAwait(false);
        var hasMoreChildren = await AccumulateAndContinueAsync(result, children, depthRemaining, ct).ConfigureAwait(false);

        return (children, hasMoreChildren);
    }

    /// <summary>
    /// Accumulate references into <paramref name="children"/>, walking
    /// BrowseNext continuation points until we exhaust them OR hit the
    /// MaxNodes cap. Returns <c>true</c> when MaxNodes cut us short
    /// (parent gets HasMoreChildren = true).
    /// </summary>
    private async Task<bool> AccumulateAndContinueAsync(
        BrowseExecutorResult result,
        List<BrowseNode> children,
        int depthRemaining,
        CancellationToken ct)
    {
        while (true)
        {
            foreach (var reference in result.References)
            {
                if (_nodesReturned >= _maxNodes)
                {
                    _truncated = true;
                    // Release continuation point so the server frees state.
                    if (result.ContinuationPoint is { } cp)
                    {
                        await _executor.ReleaseContinuationPointAsync(cp, ct).ConfigureAwait(false);
                    }
                    return true;
                }

                var childNode = await BuildChildNodeAsync(reference, depthRemaining, ct).ConfigureAwait(false);
                children.Add(childNode);
                _nodesReturned++;
            }

            if (result.ContinuationPoint is not { } continuation)
            {
                return false;
            }
            _continuationPointsConsumed++;
            result = await _executor.BrowseNextAsync(continuation, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Translate a single <see cref="ReferenceDescription"/> into a
    /// <see cref="BrowseNode"/>. Recurses into descendants when
    /// <paramref name="depthRemaining"/> &gt; 1 AND the child is a
    /// container kind. Cycle-protected via <see cref="_visited"/>.
    /// </summary>
    private async Task<BrowseNode> BuildChildNodeAsync(
        ReferenceDescription reference,
        int depthRemaining,
        CancellationToken ct)
    {
        var kind = OpcUaBrowseNodeKindMapper.Map(reference.NodeClass);
        var displayName = reference.DisplayName?.Text ?? reference.BrowseName?.Name ?? string.Empty;
        var nodeIdText = reference.NodeId?.ToString() ?? string.Empty;

        // Recurse only when:
        //   * We have remaining depth budget
        //   * The child is a container kind (Object / Folder / View) —
        //     Variables / Methods don't recurse
        //   * We haven't already visited this NodeId (cycle protection)
        var shouldRecurse = depthRemaining > 1
            && IsContainerKind(kind)
            && _visited.Add(nodeIdText);

        IReadOnlyList<BrowseNode> grandchildren = Array.Empty<BrowseNode>();
        var hasMoreGrandchildren = false;
        if (shouldRecurse && reference.NodeId is not null && !reference.NodeId.IsNull)
        {
            var childNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, namespaceTable: null) ?? new NodeId(0);
            if (!childNodeId.IsNullNodeId)
            {
                var sub = await BrowseLevelAsync(childNodeId, depthRemaining - 1, ct).ConfigureAwait(false);
                grandchildren = sub.Children;
                hasMoreGrandchildren = sub.HasMoreChildren;
            }
        }

        return new BrowseNode
        {
            NodeId = nodeIdText,
            DisplayName = displayName,
            Kind = kind,
            DataType = reference.TypeDefinition?.ToString(),
            Children = grandchildren,
            // HasMoreChildren is true when EITHER the recursion was
            // capped by MaxNodes OR the recursion was skipped due to a
            // cycle / depth limit AND the child is a container.
            HasMoreChildren = hasMoreGrandchildren
                || (!shouldRecurse && IsContainerKind(kind) && reference.NodeId is { } id && !id.IsNull && !id.IsAbsolute),
        };
    }

    /// <summary>
    /// Container kinds drive recursion. Variables and Methods are leaves
    /// from a tree-walk perspective even though Variables CAN have
    /// children in the UA address space (e.g. Variable with HasProperty
    /// → MetaData) — for the wizard's selection picker, treating them
    /// as leaves keeps the tree at a reasonable size. Future UDT
    /// expansion lives in a follow-up per v2.1 §1.1 sign-off.
    /// </summary>
    private static bool IsContainerKind(BrowseNodeKind kind) =>
        kind is BrowseNodeKind.Folder or BrowseNodeKind.Object or BrowseNodeKind.View;
}
