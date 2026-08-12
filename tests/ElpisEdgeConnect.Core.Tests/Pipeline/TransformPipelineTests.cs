// ============================================================================
// File: Pipeline/TransformPipelineTests.cs
// Covers: ordering, short-circuit, metrics recording, double-buffering,
//          empty-input short-circuit.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Pipeline;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Pipeline.C1TestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Pipeline;

public sealed class TransformPipelineTests
{
    private sealed class PassStep : ITransformStep
    {
        public PassStep(string name) { Name = name; }
        public string Name { get; }
        public TransformStepKind Kind => TransformStepKind.TagMapping;
        public void Apply(IReadOnlyList<CanonicalDataPoint> input, List<CanonicalDataPoint> output, TransformContext context)
        {
            foreach (var p in input) { output.Add(p); }
        }
    }

    private sealed class DropAllStep : ITransformStep
    {
        public string Name => "drop-all";
        public TransformStepKind Kind => TransformStepKind.Filter;
        public void Apply(IReadOnlyList<CanonicalDataPoint> input, List<CanonicalDataPoint> output, TransformContext context) { }
    }

    private sealed class ExplodeStep : ITransformStep
    {
        public string Name => "explode";
        public TransformStepKind Kind => TransformStepKind.Deadband;
        public void Apply(IReadOnlyList<CanonicalDataPoint> input, List<CanonicalDataPoint> output, TransformContext context)
            => throw new InvalidOperationException("should not run");
    }

    [Fact]
    public void EmptyInput_ShortCircuits_NoStepsInvoked()
    {
        var pipeline = new TransformPipeline(new ITransformStep[] { new ExplodeStep() });
        var result = pipeline.Execute(System.Array.Empty<CanonicalDataPoint>(), Ctx());
        result.Should().BeEmpty();
    }

    [Fact]
    public void EmptyAfterStep_ShortCircuits_LaterStepsNotInvoked()
    {
        var pipeline = new TransformPipeline(new ITransformStep[]
        {
            new PassStep("s1"),
            new DropAllStep(),
            new ExplodeStep(), // would throw if called
        });
        var metrics = new RecordingMetrics();
        var result = pipeline.Execute(new[] { Double("t", "T", 1) }, Ctx(metrics: metrics));

        result.Should().BeEmpty();
        metrics.Entries.Should().HaveCount(2);
        metrics.Entries[0].Step.Should().Be("s1");
        metrics.Entries[1].Step.Should().Be("drop-all");
    }

    [Fact]
    public void Metrics_InputOutputSuppressed_InvariantHolds()
    {
        var pipeline = new TransformPipeline(new ITransformStep[]
        {
            new PassStep("a"),
            new DropAllStep(),
        });
        var metrics = new RecordingMetrics();
        pipeline.Execute(new[] { Double("t1", "T/1", 1), Double("t2", "T/2", 2) }, Ctx(metrics: metrics));

        foreach (var e in metrics.Entries)
        {
            (e.Output + e.Suppressed).Should().Be(e.Input);
        }
    }

    /// <summary>
    /// R7 pin (Mutation 24): the meter name is LOCKED per architecture.md
    /// C1 section. Downstream dashboards depend on this string; any rename
    /// must be a deliberate, visible change.
    /// </summary>
    [Fact]
    public void MeterTransformMetricsRecorder_MeterName_IsLocked()
    {
        MeterTransformMetricsRecorder.MeterName.Should().Be("ElpisEdgeConnect.Core.Pipeline");
    }

    [Fact]
    public void ZeroSteps_PassThrough()
    {
        var pipeline = new TransformPipeline(System.Array.Empty<ITransformStep>());
        var input = new[] { Double("t", "T", 1) };
        var result = pipeline.Execute(input, Ctx());
        result.Should().BeSameAs(input);
    }
}
