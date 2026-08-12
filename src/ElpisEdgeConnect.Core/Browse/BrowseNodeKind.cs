// ============================================================================
// File: Browse/BrowseNodeKind.cs
// Purpose: Protocol-agnostic taxonomy for tree nodes returned by a browse
//          service. Used by TagBrowseTreeView (Management.Components.Shared)
//          to render icons + decide which nodes count as selectable
//          tag candidates (Variable only) vs containers (Folder / Object).
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3, §4.1
//            docs/decisions/0015-wizard-contract.md Rule 9 (Browse capability)
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Core.Browse;

/// <summary>
/// Taxonomy for a <see cref="BrowseNode"/>. Per-protocol browse services
/// map their native classifications onto this enum at the boundary so
/// the UI never branches on protocol.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BrowseNodeKind>))]
public enum BrowseNodeKind
{
    /// <summary>
    /// Pure container — operator-visible grouping that holds children but
    /// is not itself a subscribable / readable tag. Used by OPC UA Folder
    /// nodes and EtherNet/IP program scopes (e.g., <c>Program:MainProgram</c>).
    /// </summary>
    Folder = 0,

    /// <summary>
    /// A subscribable / readable scalar or array value — the leaf type
    /// that becomes a canonical tag when added to a source. Wizards
    /// auto-load behaviour (ADR-0015 Rule 10) only counts these.
    /// </summary>
    Variable = 1,

    /// <summary>
    /// A callable method node (OPC UA only; future scope). Surfaced for
    /// completeness; wizards today render them as informational but never
    /// add them as tags.
    /// </summary>
    Method = 2,

    /// <summary>
    /// A structured container that ALSO has its own browseable identity
    /// (e.g., OPC UA Object with EventNotifier, EtherNet/IP UDT instance).
    /// Operators may select an Object to enumerate its child Variables.
    /// </summary>
    Object = 3,

    /// <summary>
    /// A semantically-distinct navigation surface — e.g., OPC UA View
    /// nodes that publish a curated subset of the address space (per-
    /// operator dashboards, supervisor views). Preserved as a distinct
    /// kind (vs collapsing into <see cref="Folder"/>) so future wizard
    /// UX can render Views with their own icon and treat them as a
    /// hint about which subtrees the server's operator considered
    /// "production-relevant." NOT selectable as a tag — wizards expand
    /// into the View's children to pick Variables. Added in PR 5
    /// amendment #2 (user lock 2026-05-29).
    /// </summary>
    View = 4,
}
