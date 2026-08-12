// ============================================================================
// File: Browse/BrowseNode.cs
// Purpose: Protocol-agnostic data shape for a single node in a browse
//          result tree. Per-protocol browse services (OPC UA Client uses
//          UA Browse + BrowseNext; EtherNet/IP uses @tags + @udt/<id>)
//          map their native shapes onto this record at the boundary.
//
// LOCKED: This record is the contract between Core.Browse implementations
//         (per-protocol) and TagBrowseTreeView (Management). Adding
//         optional fields is acceptable; renaming or removing fields
//         requires v2.1 amendment.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Browse;

/// <summary>
/// A single node in the browse result tree. Immutable; per-protocol
/// services construct it once at the boundary; UI / tests consume it
/// without mutating.
/// </summary>
public sealed record BrowseNode
{
    /// <summary>
    /// Stable, protocol-native identifier (e.g., OPC UA NodeId string
    /// <c>"ns=2;i=10000"</c>, EtherNet/IP tag path <c>"Program:MainProgram.MotorSpeed"</c>).
    /// Operators don't type these — they're surfaced for diagnostics +
    /// audit + the eventual canonical tag identity.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Operator-facing label rendered in the tree. Defaults to the
    /// protocol's native browse name (UA <c>BrowseName</c>, EtherNet/IP
    /// tag short-name). Wizards MAY apply protocol-specific cosmetic
    /// transforms (e.g., camelCase splitting) before display.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Per <see cref="BrowseNodeKind"/>. Drives icon choice + selectability
    /// in the UI per ADR-0015 Rule 9.
    /// </summary>
    public required BrowseNodeKind Kind { get; init; }

    /// <summary>
    /// Per-protocol data-type descriptor (e.g., UA <c>"i=10"</c> for Int32,
    /// EtherNet/IP <c>"DINT"</c>, <c>"REAL"</c>, <c>"BOOL"</c>, UDT name).
    /// <see langword="null"/> when the protocol doesn't supply one or when
    /// the node is a Folder / Object container.
    /// </summary>
    public string? DataType { get; init; }

    /// <summary>
    /// Children at the next level. May be empty for a leaf
    /// <see cref="BrowseNodeKind.Variable"/>. May be empty AND
    /// <see cref="HasMoreChildren"/> = <c>true</c> when the protocol
    /// supports lazy expansion (UI calls
    /// <see cref="ITagBrowseService.BrowseAsync"/> with this node's
    /// <see cref="NodeId"/> as <c>StartingNodeId</c> to load them).
    /// </summary>
    public IReadOnlyList<BrowseNode> Children { get; init; } = System.Array.Empty<BrowseNode>();

    /// <summary>
    /// <c>true</c> when the protocol indicated more children exist beyond
    /// what's present in <see cref="Children"/> — typically because the
    /// browse service applied a depth or count limit. UI should render
    /// an "expand to load" affordance.
    /// </summary>
    public bool HasMoreChildren { get; init; }
}
