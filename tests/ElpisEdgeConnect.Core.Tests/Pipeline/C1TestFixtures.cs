// ============================================================================
// File: Pipeline/C1TestFixtures.cs
// Purpose: Shared builders for C1 tests — canonical point helpers and a
//          deterministic test metrics recorder.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Pipeline;

namespace ElpisEdgeConnect.Core.Tests.Pipeline;

internal static class C1TestFixtures
{
    public static CanonicalDataPoint Point(
        string tagName,
        string tagPath,
        object? value,
        CanonicalValueType type,
        DateTime? ts = null)
    {
        return new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST-001")
            .WithSource("src-test", "mock")
            .WithDevice("device-1")
            .WithTag(tagName, tagPath)
            .WithValue(value, type)
            .WithGoodQuality(ts ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithSequence(1)
            .Build();
    }

    public static CanonicalDataPoint Double(string tagName, string tagPath, double value, DateTime? ts = null) =>
        Point(tagName, tagPath, value, CanonicalValueType.Double, ts);

    public static CanonicalDataPoint Text(string tagName, string tagPath, string value) =>
        Point(tagName, tagPath, value, CanonicalValueType.String);

    public static TransformContext Ctx(DateTime? utcNow = null, ITransformMetricsRecorder? metrics = null) => new()
    {
        RouteId = "route-1",
        GatewayId = "GW-TEST-001",
        UtcNow = utcNow ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Metrics = metrics ?? NullTransformMetricsRecorder.Instance,
    };
}

internal sealed class RecordingMetrics : ITransformMetricsRecorder
{
    public sealed record Entry(string Route, string Step, int Input, int Output, int Suppressed);
    public List<Entry> Entries { get; } = new();

    public void RecordStep(string routeId, string stepName, int inputCount, int outputCount, int suppressedCount, TimeSpan duration)
    {
        Entries.Add(new Entry(routeId, stepName, inputCount, outputCount, suppressedCount));
    }
}
