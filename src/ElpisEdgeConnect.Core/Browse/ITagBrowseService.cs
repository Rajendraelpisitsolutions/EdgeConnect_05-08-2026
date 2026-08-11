// ============================================================================
// File: Browse/ITagBrowseService.cs
// Purpose: Protocol-agnostic tag-browse contract per ADR-0015 Rule 9 and
//          plan v2.1 §4.1. Per-protocol implementations live with their
//          adapter project (OpcUaClientBrowseService in
//          Sources.OpcUaClient, EthernetIpBrowseService in
//          Sources.EthernetIp); the wizard depends only on this
//          interface so its tag-browse render path is protocol-blind.
//
// LOCKED: This interface is the wedge between wizard UI and per-protocol
//         browse implementations. Adding optional members is acceptable
//         (provided they ship with default-interface impl); renaming or
//         removing requires v2.1 amendment.
//
// Reference: docs/decisions/0015-wizard-contract.md Rule 9
//            docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3, §4.1
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Browse;

/// <summary>
/// Per-protocol tag-browse contract. Implementations live with their
/// adapter project; the wizard depends on the interface only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Failure semantics</b> — implementations MUST throw a typed
/// <see cref="Errors.AdapterException"/> (or a protocol-specific subclass)
/// when the browse request fails. The wizard surfaces the message via
/// its <c>WizardValidationBanner</c> per ADR-0015 Rule 5.
/// Implementations MUST NOT return a
/// <see cref="BrowseResult"/> with a sentinel "failed" root — silent
/// failure modes obscure the operator's view.
/// </para>
/// <para>
/// <b>Side-effect rules</b> — browse is a read-only probe per ADR-0015
/// Rule 6's spirit. Implementations MUST NOT subscribe, write, or
/// otherwise mutate the device's state. Connecting to the device is
/// expected and acceptable; persisting that connection beyond the
/// browse call is NOT acceptable (each call opens, browses, closes).
/// </para>
/// <para>
/// <b>Lazy vs eager expansion</b> — implementations MAY lazy-load (return
/// only the requested level, set <see cref="BrowseNode.HasMoreChildren"/>
/// on container nodes) when the protocol's natural shape allows it. The
/// wizard's tree view handles lazy loading by calling
/// <see cref="BrowseAsync"/> again with the expanded node's
/// <see cref="BrowseNode.NodeId"/> as the new
/// <see cref="BrowseRequest.StartingNodeId"/>.
/// </para>
/// </remarks>
public interface ITagBrowseService
{
    /// <summary>
    /// Browse the device's address space starting at
    /// <see cref="BrowseRequest.StartingNodeId"/>.
    /// </summary>
    Task<BrowseResult> BrowseAsync(BrowseRequest request, CancellationToken ct);
}
