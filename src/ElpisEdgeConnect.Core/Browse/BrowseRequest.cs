// ============================================================================
// File: Browse/BrowseRequest.cs
// Purpose: Protocol-agnostic input shape for the ITagBrowseService.BrowseAsync
//          contract.
//
// LOCKED: This record is the contract between wizard code (Management)
//         and per-protocol browse services (Sources.*). Renaming or
//         removing fields requires v2.1 amendment.
// ============================================================================

namespace ElpisEdgeConnect.Core.Browse;

/// <summary>
/// Input to a single <see cref="ITagBrowseService.BrowseAsync"/> call.
/// </summary>
public sealed record BrowseRequest
{
    /// <summary>
    /// Source configuration serialized as JSON. The wizard's edited values
    /// flow through here so the probe verifies what the operator is about
    /// to save (matches ADR-0015 Rule 6 "uses edited values, not running
    /// values" for the browse equivalent).
    /// </summary>
    public required string SourceConfigJson { get; init; }

    /// <summary>
    /// Identifier of the node to expand. <see langword="null"/> requests
    /// the root of the address space; non-<see langword="null"/> requests
    /// the children of a previously-returned <see cref="BrowseNode.NodeId"/>.
    /// Per-protocol implementations honour this for lazy expansion.
    /// </summary>
    public string? StartingNodeId { get; init; }

    /// <summary>
    /// Maximum depth to traverse from <see cref="StartingNodeId"/> in a
    /// single call. <c>1</c> = direct children only (the lazy-expand
    /// default per ADR-0015 Rule 9). Higher values let wizards pre-fetch
    /// multiple levels when the operator clicks "Auto-load all under this
    /// node" (Rule 10).
    /// </summary>
    public int MaxDepth { get; init; } = 1;

    /// <summary>
    /// Soft cap on the number of <see cref="BrowseNode"/>s the service
    /// returns in a single call. Per-protocol implementations MAY return
    /// fewer; they MUST set <see cref="BrowseResult.Truncated"/> when this
    /// limit clipped the result.
    /// </summary>
    public int MaxNodes { get; init; } = 1_000;
}
