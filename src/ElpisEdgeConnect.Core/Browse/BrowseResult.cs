// ============================================================================
// File: Browse/BrowseResult.cs
// Purpose: Protocol-agnostic output shape from ITagBrowseService.BrowseAsync.
//
// LOCKED: Renaming or removing fields requires v2.1 amendment.
// ============================================================================

namespace ElpisEdgeConnect.Core.Browse;

/// <summary>
/// Result of a single <see cref="ITagBrowseService.BrowseAsync"/> call.
/// </summary>
public sealed record BrowseResult
{
    /// <summary>
    /// The root of the subtree rooted at
    /// <see cref="BrowseRequest.StartingNodeId"/> (or the protocol's
    /// natural root when the request's starting node was
    /// <see langword="null"/>).
    /// </summary>
    public required BrowseNode Root { get; init; }

    /// <summary>
    /// <c>true</c> when the result was clipped by
    /// <see cref="BrowseRequest.MaxNodes"/> or
    /// <see cref="BrowseRequest.MaxDepth"/>. UI should render an
    /// affordance to continue the browse from any
    /// <see cref="BrowseNode.HasMoreChildren"/> = <c>true</c> leaf.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// Per-protocol diagnostic message attached to the result. Wizards
    /// surface this via the validation banner (ADR-0015 Rule 5) when
    /// the protocol returned a partial result with explanation
    /// (e.g., "stopped at 1000-node soft cap").
    /// </summary>
    public string? DiagnosticMessage { get; init; }
}
