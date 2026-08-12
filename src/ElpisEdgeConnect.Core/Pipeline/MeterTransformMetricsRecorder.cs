// ============================================================================
// File: Pipeline/MeterTransformMetricsRecorder.cs
// Purpose: Production ITransformMetricsRecorder backed by
//          System.Diagnostics.Metrics. Ships in C1 so the meter name is
//          stable from day one; exporter wiring is host concern (Phase 2).
// Reference: ARCHITECTURE_BLUEPRINT.md §13, PHASE1_EXECUTION_PLAN.md C1
// ============================================================================

using System;
using System.Diagnostics.Metrics;

namespace ElpisEdgeConnect.Core.Pipeline;

/// <summary>
/// <see cref="ITransformMetricsRecorder"/> that emits to a
/// <see cref="Meter"/> named <c>ElpisEdgeConnect.Core.Pipeline</c>.
/// The meter name is LOCKED — downstream dashboards depend on it.
/// </summary>
public sealed class MeterTransformMetricsRecorder : ITransformMetricsRecorder, IDisposable
{
    /// <summary>Locked meter name. Do not change.</summary>
    public const string MeterName = "ElpisEdgeConnect.Core.Pipeline";

    private readonly Meter _meter;
    private readonly Counter<long> _input;
    private readonly Counter<long> _output;
    private readonly Counter<long> _suppressed;
    private readonly Histogram<double> _durationUs;

    /// <summary>Create a new recorder. One instance per host is sufficient.</summary>
    public MeterTransformMetricsRecorder()
    {
        _meter = new Meter(MeterName);
        _input = _meter.CreateCounter<long>("pipeline.step.input", unit: "points");
        _output = _meter.CreateCounter<long>("pipeline.step.output", unit: "points");
        _suppressed = _meter.CreateCounter<long>("pipeline.step.suppressed", unit: "points");
        _durationUs = _meter.CreateHistogram<double>("pipeline.step.duration_us", unit: "us");
    }

    /// <inheritdoc/>
    public void RecordStep(
        string routeId,
        string stepName,
        int inputCount,
        int outputCount,
        int suppressedCount,
        TimeSpan duration)
    {
        var routeTag = new System.Collections.Generic.KeyValuePair<string, object?>("route_id", routeId);
        var stepTag = new System.Collections.Generic.KeyValuePair<string, object?>("step", stepName);
        _input.Add(inputCount, routeTag, stepTag);
        _output.Add(outputCount, routeTag, stepTag);
        _suppressed.Add(suppressedCount, routeTag, stepTag);
        _durationUs.Record(duration.TotalMicroseconds, routeTag, stepTag);
    }

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
