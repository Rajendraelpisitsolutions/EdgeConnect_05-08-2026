// ============================================================================
// File: Buffer/SinkCursorTrackerTests.cs
// Covers: register / deregister / advance / min / fast-forward semantics.
// ============================================================================

using ElpisEdgeConnect.Core.Buffer;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SinkCursorTrackerTests
{
    [Fact]
    public void Register_Idempotent_PreservesExistingCursor()
    {
        var t = new SinkCursorTracker();
        t.Register("a", 5);
        t.TryAdvance("a", 10);
        t.Register("a", 0); // re-register must NOT reset
        t.Get("a").Should().Be(10);
    }

    [Fact]
    public void TryAdvance_BackwardOrEqual_NoOp()
    {
        var t = new SinkCursorTracker();
        t.Register("a", 5);
        t.TryAdvance("a", 5).Should().BeFalse();
        t.TryAdvance("a", 4).Should().BeFalse();
        t.TryAdvance("a", 6).Should().BeTrue();
        t.Get("a").Should().Be(6);
    }

    [Fact]
    public void TryAdvance_UnknownSink_ReturnsFalse()
    {
        var t = new SinkCursorTracker();
        t.TryAdvance("nope", 1).Should().BeFalse();
    }

    [Fact]
    public void Min_ReturnsSmallestCursor_OrDefaultWhenEmpty()
    {
        var t = new SinkCursorTracker();
        t.Min(defaultIfEmpty: 99).Should().Be(99);

        t.Register("a", 10);
        t.Register("b", 5);
        t.Register("c", 20);
        t.Min(defaultIfEmpty: 0).Should().Be(5);
    }

    [Fact]
    public void Deregister_RemovesCursor_AffectsMin()
    {
        var t = new SinkCursorTracker();
        t.Register("a", 10);
        t.Register("b", 5);
        t.Min(0).Should().Be(5);

        t.Deregister("b");
        t.Min(0).Should().Be(10);
    }

    [Fact]
    public void FastForwardBelow_RaisesLaggingCursors()
    {
        var t = new SinkCursorTracker();
        t.Register("fast", 100);
        t.Register("slow", 5);

        t.FastForwardBelow(50);

        t.Get("fast").Should().Be(100); // unchanged
        t.Get("slow").Should().Be(50);  // bumped to floor
    }

    [Fact]
    public void Get_UnknownSink_Throws()
    {
        var t = new SinkCursorTracker();
        System.Action act = () => t.Get("missing");
        act.Should().Throw<System.InvalidOperationException>();
    }
}
