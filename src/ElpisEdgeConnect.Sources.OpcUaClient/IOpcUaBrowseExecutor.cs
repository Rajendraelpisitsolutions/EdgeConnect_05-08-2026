// ============================================================================
// File: IOpcUaBrowseExecutor.cs
// Purpose: Test seam wrapping the OPC stack's Session.BrowseAsync /
//          BrowseNextAsync / continuation-point release. Returns the
//          stack's ReferenceDescription collection + the (optional)
//          continuation point so OpcUaBrowseTreeBuilder can drive the
//          recursive walk without touching the Session directly.
//
// LOCKED design rules (same pattern as PR 2 / PR 4 seams):
//   * Tests substitute this whole interface to verify the tree builder's
//     batching, MaxNodes enforcement, and continuation-point release
//     behaviour without standing up a live OPC stack
//   * Production impl (DefaultOpcUaBrowseExecutor) is a thin wrapper —
//     covered by integration tests in PR 7 against UA Sample Server
//   * Continuation points are byte[] per the UA spec; null = no more
//     children
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §5.1
//            OPC UA Part 4 §5.8 (Browse / BrowseNext services)
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Test-substitutable wrapper around <see cref="Session.BrowseAsync"/>
/// and <see cref="Session.BrowseNextAsync"/> for the
/// <see cref="OpcUaBrowseTreeBuilder"/>.
/// </summary>
internal interface IOpcUaBrowseExecutor
{
    /// <summary>
    /// Start a browse at <paramref name="startingNodeId"/>. Returns the
    /// references discovered in the first response plus a continuation
    /// point if more references exist.
    /// </summary>
    Task<BrowseExecutorResult> BrowseAsync(NodeId startingNodeId, CancellationToken ct);

    /// <summary>
    /// Continue a previously-started browse via the supplied
    /// <paramref name="continuationPoint"/>.
    /// </summary>
    Task<BrowseExecutorResult> BrowseNextAsync(byte[] continuationPoint, CancellationToken ct);

    /// <summary>
    /// Release a continuation point WITHOUT consuming the remaining
    /// references. Called when the tree builder hit its MaxNodes cap
    /// mid-walk and wants the server to free the per-session
    /// continuation state. Forgetting this leaks server memory over
    /// long-lived sessions — pinned by a dedicated test.
    /// </summary>
    Task ReleaseContinuationPointAsync(byte[] continuationPoint, CancellationToken ct);
}

/// <summary>
/// Result of a single <see cref="IOpcUaBrowseExecutor.BrowseAsync"/> or
/// <see cref="IOpcUaBrowseExecutor.BrowseNextAsync"/> call.
/// </summary>
public sealed record BrowseExecutorResult
{
    /// <summary>The references returned by the call.</summary>
    public required ReferenceDescriptionCollection References { get; init; }

    /// <summary>
    /// Continuation point for the next <see cref="IOpcUaBrowseExecutor.BrowseNextAsync"/>
    /// call. <see langword="null"/> when no more references exist.
    /// </summary>
    public byte[]? ContinuationPoint { get; init; }
}
