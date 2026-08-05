// ============================================================================
// File: Pipeline/RateLimitStepTests.cs
// Covers: first-pass, within-window suppressed, after-window passes, per-tag
//          isolation, zero/negative rejected at construction.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Pipeline.Steps;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Pipeline.C1TestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Pipeline;

public sealed class RateLimitStepTests
{
    [Fact]
    public void FirstPoint_Passes()
    {
        var step = new RateLimitStep(new Dictionary<string, int> { ["T/1"] = 1000 });
        var output = new List<CanonicalDataPoint>();
        step.Apply(new[] { Double("t", "T/1", 1) }, output, Ctx(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        output.Should().HaveCount(1);
    }

    [Fact]
    public void WithinWindow_Suppressed()
    {
        var step = new RateLimitStep(new Dictionary<string, int> { ["T/1"] = 1000 });
        var output = new List<CanonicalDataPoint>();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        step.Apply(new[] { Double("t", "T/1", 1) }, output, Ctx(t0));
        output.Clear();

        step.Apply(new[] { Double("t", "T/1", 2) }, output, Ctx(t0.AddMilliseconds(500)));
        output.Should().BeEmpty();

        step.Apply(new[] { Double("t", "T/1", 3) }, output, Ctx(t0.AddMilliseconds(999)));
        output.Should().BeEmpty();
    }

    [Fact]
    public void ExactBoundary_Passes()
    {
        var step = new RateLimitStep(new Dictionary<string, int> { ["T/1"] = 1000 });
        var output = new List<CanonicalDataPoint>();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        step.Apply(new[] { Double("t", "T/1", 1) }, output, Ctx(t0));
        output.Clear();

        step.Apply(new[] { Double("t", "T/1", 2) }, output, Ctx(t0.AddMilliseconds(1000)));
        output.Should().ContainSingle();
    }

    [Fact]
    public void UnconfiguredTag_PassesThrough()
    {
        var step = new RateLimitStep(new Dictionary<string, int> { ["T/1"] = 1000 });
        var output = new List<CanonicalDataPoint>();
        step.Apply(new[] { Double("x", "X/1", 1) }, output, Ctx());
        step.Apply(new[] { Double("x", "X/1", 2) }, output, Ctx()); // same tick
        output.Should().HaveCount(2);
    }

    [Fact]
    public void PerTag_StateIndependent()
    {
        var step = new RateLimitStep(new Dictionary<string, int> { ["A/1"] = 1000, ["A/2"] = 1000 });
        var output = new List<CanonicalDataPoint>();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        step.Apply(new[] { Double("a", "A/1", 1), Double("b", "A/2", 2) }, output, Ctx(t0));
        output.Should().HaveCount(2);

        output.Clear();
        step.Apply(new[] { Double("a", "A/1", 3), Double("b", "A/2", 4) }, output, Ctx(t0.AddMilliseconds(500)));
        output.Should().BeEmpty();
    }

    /// <summary>
    /// R2 pin (Mutation 17): after a pass, the window must re-anchor to the
    /// pass timestamp. If <c>_lastEmitted</c> were not updated, every point
    /// after the very first would keep passing once the original window ran
    /// out.
    /// </summary>
    [Fact]
    public void Window_ReanchorsAfterEachPass()
    {
        var step = new RateLimitStep(new Dictionary<string, int> { ["T/1"] = 1000 });
        var output = new List<CanonicalDataPoint>();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // t0: first pass.
        step.Apply(new[] { Double("t", "T/1", 1) }, output, Ctx(t0));
        output.Should().HaveCount(1);
        output.Clear();

        // t0 + 1000ms: exact boundary passes and must update last-emitted.
        step.Apply(new[] { Double("t", "T/1", 2) }, output, Ctx(t0.AddMilliseconds(1000)));
        output.Should().HaveCount(1);
        output.Clear();

        // t0 + 1500ms: delta from re-anchored last (t0 + 1000) is 500ms,
        // within the window, must be suppressed. If last-emitted was not
        // updated, delta from t0 is 1500ms and this would wrongly pass.
        step.Apply(new[] { Double("t", "T/1", 3) }, output, Ctx(t0.AddMilliseconds(1500)));
        output.Should().BeEmpty();
    }

    [Fact]
    public void ZeroOrNegative_Rejected()
    {
        System.Action a1 = () => new RateLimitStep(new Dictionary<string, int> { ["T"] = 0 });
        System.Action a2 = () => new RateLimitStep(new Dictionary<string, int> { ["T"] = -1 });
        a1.Should().Throw<System.ArgumentException>();
        a2.Should().Throw<System.ArgumentException>();
    }
}
