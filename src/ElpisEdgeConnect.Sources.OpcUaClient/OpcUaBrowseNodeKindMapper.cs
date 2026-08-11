// ============================================================================
// File: OpcUaBrowseNodeKindMapper.cs
// Purpose: Pure-logic translator from OPC UA NodeClass → Core.Browse.
//          BrowseNodeKind. Used by OpcUaBrowseTreeBuilder when assembling
//          the BrowseNode tree.
//
// LOCKED mapping (PR 5 amendment #2, user lock 2026-05-29):
//   UA NodeClass     → BrowseNodeKind
//   Variable         → Variable    (the only kind that becomes a tag)
//   Object           → Object      (container with EventNotifier/methods)
//   Method           → Method      (callable — never a tag)
//   View             → View        (semantic navigation surface — preserved
//                                   as distinct vs collapsing into Folder,
//                                   per PR 5 amendment #2)
//   ObjectType / VariableType /
//   ReferenceType / DataType    → Folder    (type-system; not real tags)
//   Unspecified                 → Folder    (conservative — never accidentally
//                                            make a non-tag selectable)
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
//            docs/decisions/0015-wizard-contract.md Rule 9
//            PR 5 plan + amendments (user lock 2026-05-29)
// ============================================================================

using ElpisEdgeConnect.Core.Browse;
using Opc.Ua;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Pure-logic NodeClass → BrowseNodeKind translator.
/// </summary>
internal static class OpcUaBrowseNodeKindMapper
{
    /// <summary>
    /// Map a UA <see cref="NodeClass"/> to the canonical
    /// <see cref="BrowseNodeKind"/>. Unknown / unspecified classes fall
    /// back to <see cref="BrowseNodeKind.Folder"/> so the UI never
    /// accidentally surfaces a non-tag as selectable.
    /// </summary>
    public static BrowseNodeKind Map(NodeClass nodeClass) => nodeClass switch
    {
        NodeClass.Variable => BrowseNodeKind.Variable,
        NodeClass.Object => BrowseNodeKind.Object,
        NodeClass.Method => BrowseNodeKind.Method,
        NodeClass.View => BrowseNodeKind.View,
        // Type-system NodeClasses are containers but NOT tags — they
        // never become canonical points. Conservative fall-through into
        // Folder so wizards expand them visually but don't permit
        // selection.
        _ => BrowseNodeKind.Folder,
    };
}
