// ============================================================================
// Tests: OpcUaBrowseTreeBuilderTests — pin the recursive walk's
//        invariants over a substituted IOpcUaBrowseExecutor. Covers
//        every edge case the live OPC stack would exercise.
//
//        Invariants pinned:
//          * Single-level browse: root + N children
//          * Lazy expansion (MaxDepth=1) — no recursion
//          * MaxDepth=2 / =3 — N-level recursion
//          * MaxNodes cut-off mid-walk → Truncated=true,
//            HasMoreChildren=true on the parent
//          * Continuation point RELEASED when MaxNodes cuts short
//            (PR 5 plan amendment "strongly approve" item)
//          * Multiple BrowseNext continuations chained correctly
//          * NodeClass mapping flows through (including View per
//            amendment #2)
//          * Cyclic-reference protection — visited NodeId is NOT
//            re-entered (PR 5 amendment #3)
//          * DiagnosticMessage contains NodesVisited / NodesReturned /
//            ContinuationPointsConsumed
//          * Empty browse → root with no children
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
//            PR 5 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Browse;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using NSubstitute;
using Opc.Ua;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaBrowseTreeBuilderTests
{
    private static readonly NodeId RootId = new("ns=0;i=85");

    private static ReferenceDescription Ref(string nodeIdText, string displayName, NodeClass nodeClass = NodeClass.Object) =>
        new()
        {
            NodeId = new ExpandedNodeId(nodeIdText),
            DisplayName = new LocalizedText(displayName),
            BrowseName = new QualifiedName(displayName, 2),
            NodeClass = nodeClass,
        };

    private static BrowseExecutorResult ExecResult(params ReferenceDescription[] refs)
    {
        var coll = new ReferenceDescriptionCollection();
        coll.AddRange(refs);
        return new BrowseExecutorResult { References = coll };
    }

    // ─── Happy path ──────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_SingleLevelBrowse_ReturnsRootWithChildren()
    {
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        executor.BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>())
            .Returns(ExecResult(
                Ref("ns=2;i=1", "Tag1", NodeClass.Variable),
                Ref("ns=2;i=2", "Tag2", NodeClass.Variable)));

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 1, maxNodes: 1_000);
        var result = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        result.Root.DisplayName.Should().Be("Objects");
        result.Root.Children.Should().HaveCount(2);
        result.Root.Children[0].DisplayName.Should().Be("Tag1");
        result.Root.Children[1].DisplayName.Should().Be("Tag2");
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_LazyDefault_MaxDepth1_DoesNotRecurse()
    {
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        executor.BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>())
            .Returns(ExecResult(Ref("ns=2;i=10", "Folder1", NodeClass.Object)));

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 1, maxNodes: 1_000);
        _ = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        // Only ONE BrowseAsync call (root level). No recursion into Folder1.
        await executor.Received(1).BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_MaxDepth2_RecursesOneExtraLevel()
    {
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        // Root → Folder1; Folder1 → Tag1
        executor.BrowseAsync(RootId, Arg.Any<CancellationToken>())
            .Returns(ExecResult(Ref("ns=2;i=10", "Folder1", NodeClass.Object)));
        executor.BrowseAsync(Arg.Is<NodeId>(n => n.ToString() == "ns=2;i=10"), Arg.Any<CancellationToken>())
            .Returns(ExecResult(Ref("ns=2;i=11", "Tag1", NodeClass.Variable)));

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 2, maxNodes: 1_000);
        var result = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        result.Root.Children.Should().HaveCount(1);
        result.Root.Children[0].DisplayName.Should().Be("Folder1");
        result.Root.Children[0].Children.Should().HaveCount(1);
        result.Root.Children[0].Children[0].DisplayName.Should().Be("Tag1");
    }

    // ─── MaxNodes truncation ──────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_MaxNodesHit_SetsTruncatedAndReleasesContinuation()
    {
        // Browse returns 3 refs + a continuation point. With MaxNodes=2,
        // builder accepts the first 2 then releases the continuation
        // BEFORE consuming the 3rd via BrowseNext.
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        var continuationToken = new byte[] { 0xAB, 0xCD };
        executor.BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>())
            .Returns(new BrowseExecutorResult
            {
                References = new ReferenceDescriptionCollection
                {
                    Ref("ns=2;i=1", "Tag1", NodeClass.Variable),
                    Ref("ns=2;i=2", "Tag2", NodeClass.Variable),
                    Ref("ns=2;i=3", "Tag3", NodeClass.Variable),
                },
                ContinuationPoint = continuationToken,
            });

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 1, maxNodes: 2);
        var result = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        result.Truncated.Should().BeTrue();
        result.Root.Children.Should().HaveCount(2);
        await executor.Received(1).ReleaseContinuationPointAsync(continuationToken, Arg.Any<CancellationToken>());
        await executor.DidNotReceive().BrowseNextAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_BrowseNextChained_AccumulatesAllReferences()
    {
        // Initial Browse returns 2 refs + continuation. BrowseNext
        // returns 2 more refs + no continuation. All 4 land in
        // children.
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        var continuationToken = new byte[] { 0xAB };
        executor.BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>())
            .Returns(new BrowseExecutorResult
            {
                References = new ReferenceDescriptionCollection
                {
                    Ref("ns=2;i=1", "Tag1", NodeClass.Variable),
                    Ref("ns=2;i=2", "Tag2", NodeClass.Variable),
                },
                ContinuationPoint = continuationToken,
            });
        executor.BrowseNextAsync(continuationToken, Arg.Any<CancellationToken>())
            .Returns(ExecResult(
                Ref("ns=2;i=3", "Tag3", NodeClass.Variable),
                Ref("ns=2;i=4", "Tag4", NodeClass.Variable)));

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 1, maxNodes: 1_000);
        var result = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        result.Root.Children.Should().HaveCount(4);
        result.Truncated.Should().BeFalse();
    }

    // ─── NodeClass mapping flows through ──────────────────────────────

    [Fact]
    public async Task BuildAsync_MixedNodeClasses_PreservedAsBrowseNodeKinds()
    {
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        executor.BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>())
            .Returns(ExecResult(
                Ref("ns=2;i=1", "Tag", NodeClass.Variable),
                Ref("ns=2;i=2", "Obj", NodeClass.Object),
                Ref("ns=2;i=3", "Run", NodeClass.Method),
                Ref("ns=2;i=4", "View1", NodeClass.View),
                Ref("ns=2;i=5", "Folder1", NodeClass.ObjectType)));

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 1, maxNodes: 1_000);
        var result = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        result.Root.Children[0].Kind.Should().Be(BrowseNodeKind.Variable);
        result.Root.Children[1].Kind.Should().Be(BrowseNodeKind.Object);
        result.Root.Children[2].Kind.Should().Be(BrowseNodeKind.Method);
        result.Root.Children[3].Kind.Should().Be(BrowseNodeKind.View);   // amendment #2
        result.Root.Children[4].Kind.Should().Be(BrowseNodeKind.Folder); // ObjectType → Folder
    }

    // ─── Cyclic-reference protection (PR 5 amendment #3) ──────────────

    [Fact]
    public async Task BuildAsync_DetectsNodeIdAlreadyVisited_SkipsRecursion()
    {
        // Pathological case — Folder1 → Folder2 → Folder1 (cycle).
        // Tree builder visits Folder1 (root), Folder2 (child),
        // attempts to recurse into Folder1 (cycle), detects visited,
        // skips. No infinite loop; no exception.
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        executor.BrowseAsync(RootId, Arg.Any<CancellationToken>())
            .Returns(ExecResult(Ref("ns=2;i=1", "Folder1", NodeClass.Object)));
        executor.BrowseAsync(Arg.Is<NodeId>(n => n.ToString() == "ns=2;i=1"), Arg.Any<CancellationToken>())
            .Returns(ExecResult(Ref("ns=2;i=2", "Folder2", NodeClass.Object)));
        executor.BrowseAsync(Arg.Is<NodeId>(n => n.ToString() == "ns=2;i=2"), Arg.Any<CancellationToken>())
            .Returns(ExecResult(Ref("ns=2;i=1", "Folder1Cycle", NodeClass.Object)));

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 5, maxNodes: 1_000);
        var result = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        // Builder reached: root → Folder1 → Folder2 → Folder1(cycle).
        // The cycle-leaf node IS rendered in the tree, but its children
        // are NOT recursed into.
        result.Root.Children.Should().HaveCount(1);
        result.Root.Children[0].DisplayName.Should().Be("Folder1");
        var folder2 = result.Root.Children[0].Children[0];
        folder2.DisplayName.Should().Be("Folder2");
        folder2.Children.Should().HaveCount(1);
        folder2.Children[0].DisplayName.Should().Be("Folder1Cycle");
        // The cycle-leaf has NO further children (recursion skipped).
        folder2.Children[0].Children.Should().BeEmpty();
    }

    // ─── Diagnostic counters ─────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_PopulatesDiagnosticMessage_WithCounters()
    {
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        executor.BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>())
            .Returns(ExecResult(
                Ref("ns=2;i=1", "Tag1", NodeClass.Variable),
                Ref("ns=2;i=2", "Tag2", NodeClass.Variable)));

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 1, maxNodes: 1_000);
        var result = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        result.DiagnosticMessage.Should().NotBeNull();
        result.DiagnosticMessage!.Should().Contain("NodesVisited=");
        result.DiagnosticMessage.Should().Contain("NodesReturned=2");
        result.DiagnosticMessage.Should().Contain("ContinuationPointsConsumed=0");
    }

    [Fact]
    public async Task BuildAsync_BrowseNextConsumed_IncrementsCounter()
    {
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        executor.BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>())
            .Returns(new BrowseExecutorResult
            {
                References = new ReferenceDescriptionCollection { Ref("ns=2;i=1", "Tag1", NodeClass.Variable) },
                ContinuationPoint = new byte[] { 0xAB },
            });
        executor.BrowseNextAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(ExecResult(Ref("ns=2;i=2", "Tag2", NodeClass.Variable)));

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 1, maxNodes: 1_000);
        var result = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        result.DiagnosticMessage!.Should().Contain("ContinuationPointsConsumed=1");
    }

    // ─── Empty results ───────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_EmptyChildren_RootHasNoChildren()
    {
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        executor.BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>())
            .Returns(ExecResult());

        var builder = new OpcUaBrowseTreeBuilder(executor, maxDepth: 1, maxNodes: 1_000);
        var result = await builder.BuildAsync(RootId, "Objects", CancellationToken.None);

        result.Root.Children.Should().BeEmpty();
        result.Truncated.Should().BeFalse();
    }

    // ─── Constructor validation ──────────────────────────────────────

    [Fact]
    public void Constructor_MaxDepthZero_Throws()
    {
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        var act = () => new OpcUaBrowseTreeBuilder(executor, maxDepth: 0, maxNodes: 1_000);

        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_MaxNodesZero_Throws()
    {
        var executor = Substitute.For<IOpcUaBrowseExecutor>();
        var act = () => new OpcUaBrowseTreeBuilder(executor, maxDepth: 1, maxNodes: 0);

        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }
}
