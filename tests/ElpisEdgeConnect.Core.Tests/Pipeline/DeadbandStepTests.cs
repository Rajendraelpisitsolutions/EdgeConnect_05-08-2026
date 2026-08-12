// ============================================================================
// File: Pipeline/DeadbandStepTests.cs
// Covers: absolute mode, percent mode, first-value pass, per-tag isolation,
//          non-numeric pass-through, null pass-through.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Pipeline.Steps;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Pipeline.C1TestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Pipeline;

public sealed class DeadbandStepTests
{
    [Fact]
    public void Absolute_FirstValue_AlwaysPasses()
    {
        var step = new DeadbandStep(
            absolute: new Dictionary<string, double> { ["Spindle/Speed"] = 10 },
            percent: null);
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[] { Double("rpm", "Spindle/Speed", 3000) }, output, Ctx());

        output.Should().HaveCount(1);
    }

    [Fact]
    public void Absolute_BelowThreshold_Suppressed()
    {
        var step = new DeadbandStep(
            absolute: new Dictionary<string, double> { ["Spindle/Speed"] = 10 },
            percent: null);

        var output = new List<CanonicalDataPoint>();
        step.Apply(new[] { Double("rpm", "Spindle/Speed", 3000) }, output, Ctx());
        output.Clear();

        step.Apply(new[] { Double("rpm", "Spindle/Speed", 3005) }, output, Ctx());
        output.Should().BeEmpty();

        step.Apply(new[] { Double("rpm", "Spindle/Speed", 3015) }, output, Ctx());
        output.Should().HaveCount(1);
    }

    [Fact]
    public void Percent_FivePercent_SuppressesSmallMoves()
    {
        var step = new DeadbandStep(
            absolute: null,
            percent: new Dictionary<string, double> { ["Spindle/Speed"] = 0.05 });

        var output = new List<CanonicalDataPoint>();
        step.Apply(new[] { Double("rpm", "Spindle/Speed", 1000) }, output, Ctx()); // baseline

        output.Clear();
        step.Apply(new[] { Double("rpm", "Spindle/Speed", 1040) }, output, Ctx()); // 4% → suppressed
        output.Should().BeEmpty();

        step.Apply(new[] { Double("rpm", "Spindle/Speed", 1060) }, output, Ctx()); // 6% (from 1000) → passes
        output.Should().HaveCount(1);
    }

    [Fact]
    public void PerTag_StateIsIndependent()
    {
        var step = new DeadbandStep(
            absolute: new Dictionary<string, double>
            {
                ["A/1"] = 5,
                ["A/2"] = 5,
            },
            percent: null);
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[]
        {
            Double("a1", "A/1", 100),
            Double("a2", "A/2", 200),
        }, output, Ctx());
        output.Should().HaveCount(2); // first-of-tag each

        output.Clear();
        step.Apply(new[]
        {
            Double("a1", "A/1", 101), // delta 1 < 5 → suppressed
            Double("a2", "A/2", 210), // delta 10 → passes
        }, output, Ctx());
        output.Should().ContainSingle().Which.TagPath.Should().Be("A/2");
    }

    [Fact]
    public void NonNumeric_AlwaysPasses()
    {
        var step = new DeadbandStep(
            absolute: new Dictionary<string, double> { ["State/Mode"] = 1 },
            percent: null);
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[] { Text("mode", "State/Mode", "AUTO") }, output, Ctx());
        step.Apply(new[] { Text("mode", "State/Mode", "AUTO") }, output, Ctx());
        output.Should().HaveCount(2);
    }

    [Fact]
    public void NullValue_AlwaysPasses()
    {
        var step = new DeadbandStep(
            absolute: new Dictionary<string, double> { ["T"] = 10 },
            percent: null);
        var nullPoint = Point("t", "T", null, CanonicalValueType.Null);
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[] { nullPoint }, output, Ctx());
        step.Apply(new[] { nullPoint }, output, Ctx());
        output.Should().HaveCount(2);
    }

    // ========================================================================
    // C1 close-out pins (mutation coverage)
    // ========================================================================

    /// <summary>
    /// R1 pin (Mutation 11 — absolute): at delta == threshold the point must
    /// pass because the comparison is <c>&gt;=</c>, not <c>&gt;</c>.
    /// </summary>
    [Fact]
    public void Absolute_ExactThreshold_Passes()
    {
        var step = new DeadbandStep(
            absolute: new Dictionary<string, double> { ["T"] = 10 },
            percent: null);
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[] { Double("t", "T", 100) }, output, Ctx());
        output.Clear();

        step.Apply(new[] { Double("t", "T", 110) }, output, Ctx()); // delta == threshold
        output.Should().HaveCount(1);
    }

    /// <summary>
    /// R1 pin (Mutation 11 — percent): at the exact percentage boundary the
    /// point must pass.
    /// </summary>
    [Fact]
    public void Percent_ExactThreshold_Passes()
    {
        var step = new DeadbandStep(
            absolute: null,
            percent: new Dictionary<string, double> { ["T"] = 0.05 });
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[] { Double("t", "T", 1000) }, output, Ctx());
        output.Clear();

        // Exact 5% delta: (1050 - 1000) / 1000 == 0.05.
        step.Apply(new[] { Double("t", "T", 1050) }, output, Ctx());
        output.Should().HaveCount(1);
    }

    /// <summary>
    /// R4 pin (Mutation 12): on suppression the baseline must NOT drift to
    /// the suppressed current value. A subsequent point is compared against
    /// the last EMITTED value.
    /// </summary>
    [Fact]
    public void Absolute_Suppression_DoesNotDriftBaseline()
    {
        var step = new DeadbandStep(
            absolute: new Dictionary<string, double> { ["T"] = 10 },
            percent: null);
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[] { Double("t", "T", 100) }, output, Ctx()); // baseline = 100
        output.Clear();

        // Walk the value in 5-unit steps, each below threshold individually.
        // If the step were to update the baseline on suppression, every hop
        // would pass. With correct anchoring, they all suppress until the
        // delta from 100 reaches 10.
        step.Apply(new[] { Double("t", "T", 105) }, output, Ctx());
        step.Apply(new[] { Double("t", "T", 108) }, output, Ctx());
        step.Apply(new[] { Double("t", "T", 109) }, output, Ctx());

        output.Should().BeEmpty("baseline must stay at 100 until a passing delta emits");
    }

    /// <summary>
    /// R5 pin (Mutation 14): a null value must NOT clear the stored numeric
    /// baseline. A subsequent numeric reading is still compared against the
    /// last emitted numeric.
    /// </summary>
    [Fact]
    public void NullValue_PreservesNumericBaseline()
    {
        var step = new DeadbandStep(
            absolute: new Dictionary<string, double> { ["T"] = 10 },
            percent: null);
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[] { Double("t", "T", 100) }, output, Ctx()); // baseline = 100
        output.Clear();

        step.Apply(new[] { Point("t", "T", null, CanonicalValueType.Null) }, output, Ctx());
        output.Should().HaveCount(1, "null values always pass through");
        output.Clear();

        // If the null wrongly cleared the baseline, this would pass as "first
        // observation". With the baseline preserved, delta = 5 < 10 → suppress.
        step.Apply(new[] { Double("t", "T", 105) }, output, Ctx());
        output.Should().BeEmpty();
    }
}
