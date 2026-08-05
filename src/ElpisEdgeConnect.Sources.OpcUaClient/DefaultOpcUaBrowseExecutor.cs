// ============================================================================
// File: DefaultOpcUaBrowseExecutor.cs
// Purpose: Production implementation of IOpcUaBrowseExecutor. Wraps the
//          OPC stack's Session.BrowseAsync / BrowseNextAsync calls so the
//          tree builder can drive recursion without touching the Session
//          type directly.
//
//          Honors the v2.1 §1.1 + ADR-0015 Rule 9 contract: hierarchical
//          references only (HasComponent / HasProperty / Organizes /
//          HasNotifier — the standard "subtype-of HierarchicalReferences"
//          set). Future ReferenceType filtering UX lives on the wizard
//          per PR 5 plan sign-off; this executor always uses the broad
//          hierarchical set.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Production <see cref="IOpcUaBrowseExecutor"/>. Construct per browse
/// call against the Session the caller opened.
/// </summary>
internal sealed class DefaultOpcUaBrowseExecutor : IOpcUaBrowseExecutor
{
    private readonly ISession _session;

    public DefaultOpcUaBrowseExecutor(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <inheritdoc/>
    public async Task<BrowseExecutorResult> BrowseAsync(NodeId startingNodeId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(startingNodeId);
        ct.ThrowIfCancellationRequested();

        var description = new BrowseDescription
        {
            NodeId = startingNodeId,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = 0u, // 0 = all classes; mapper filters at translation time
            ResultMask = (uint)BrowseResultMask.All,
        };
        var descriptions = new BrowseDescriptionCollection { description };

        var response = await _session.BrowseAsync(
            requestHeader: null,
            view: null,
            requestedMaxReferencesPerNode: 0u, // 0 = server default
            nodesToBrowse: descriptions,
            ct: ct).ConfigureAwait(false);

        var result = response.Results?[0];
        return new BrowseExecutorResult
        {
            References = result?.References ?? new ReferenceDescriptionCollection(),
            ContinuationPoint = result?.ContinuationPoint is { Length: > 0 } cp ? cp : null,
        };
    }

    /// <inheritdoc/>
    public async Task<BrowseExecutorResult> BrowseNextAsync(byte[] continuationPoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(continuationPoint);
        ct.ThrowIfCancellationRequested();

        var response = await _session.BrowseNextAsync(
            requestHeader: null,
            releaseContinuationPoints: false,
            continuationPoints: new ByteStringCollection { continuationPoint },
            ct: ct).ConfigureAwait(false);

        var result = response.Results?[0];
        return new BrowseExecutorResult
        {
            References = result?.References ?? new ReferenceDescriptionCollection(),
            ContinuationPoint = result?.ContinuationPoint is { Length: > 0 } cp ? cp : null,
        };
    }

    /// <inheritdoc/>
    public async Task ReleaseContinuationPointAsync(byte[] continuationPoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(continuationPoint);
        ct.ThrowIfCancellationRequested();

        await _session.BrowseNextAsync(
            requestHeader: null,
            releaseContinuationPoints: true,
            continuationPoints: new ByteStringCollection { continuationPoint },
            ct: ct).ConfigureAwait(false);
    }
}
