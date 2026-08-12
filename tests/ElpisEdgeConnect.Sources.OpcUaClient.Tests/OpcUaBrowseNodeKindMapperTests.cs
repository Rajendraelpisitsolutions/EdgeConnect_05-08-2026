// ============================================================================
// Tests: OpcUaBrowseNodeKindMapperTests — pin the UA NodeClass → Core.Browse
//        BrowseNodeKind mapping per PR 5 amendment #2 (user lock 2026-05-29:
//        View preserved as distinct kind).
//
//        Invariants:
//          * Variable / Object / Method / View map to their canonical
//            counterparts (1:1)
//          * Type-system NodeClasses (ObjectType / VariableType /
//            ReferenceType / DataType) → Folder
//          * Unspecified → Folder (conservative — never accidentally
//            make a non-tag selectable)
//          * Future NodeClass values default to Folder via the switch
//            expression's _ arm
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
//            PR 5 amendment #2 (user lock 2026-05-29)
// ============================================================================

using ElpisEdgeConnect.Core.Browse;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Opc.Ua;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaBrowseNodeKindMapperTests
{
    [Theory]
    [InlineData(NodeClass.Variable, BrowseNodeKind.Variable)]
    [InlineData(NodeClass.Object, BrowseNodeKind.Object)]
    [InlineData(NodeClass.Method, BrowseNodeKind.Method)]
    [InlineData(NodeClass.View, BrowseNodeKind.View)]
    public void Map_KnownClasses_MapToCanonicalKinds(NodeClass input, BrowseNodeKind expected)
    {
        OpcUaBrowseNodeKindMapper.Map(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(NodeClass.ObjectType)]
    [InlineData(NodeClass.VariableType)]
    [InlineData(NodeClass.ReferenceType)]
    [InlineData(NodeClass.DataType)]
    public void Map_TypeSystemClasses_MapToFolder(NodeClass input)
    {
        // Type-system nodes are containers but NEVER tags — wizard
        // expands them visually, doesn't allow selection.
        OpcUaBrowseNodeKindMapper.Map(input).Should().Be(BrowseNodeKind.Folder);
    }

    [Fact]
    public void Map_Unspecified_MapsToFolder()
    {
        // Defensive — a server returning Unspecified would otherwise
        // surface as an unknown kind. Folder is the safest fallback
        // (visible but not selectable).
        OpcUaBrowseNodeKindMapper.Map(NodeClass.Unspecified).Should().Be(BrowseNodeKind.Folder);
    }
}
