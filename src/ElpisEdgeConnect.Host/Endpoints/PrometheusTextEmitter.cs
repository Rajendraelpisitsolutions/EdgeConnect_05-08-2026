// ============================================================================
// File: Endpoints/PrometheusTextEmitter.cs
// Purpose: Hand-rolled Prometheus text-format emitter. Drives a MeterListener
//          against a specific Meter instance, collects one round of
//          observable measurements, and formats them as Prometheus
//          exposition format. Zero NuGet dependencies — uses only the
//          built-in System.Diagnostics.Metrics API.
//
//          Phase 5 of D. Keeping the emitter in the Host project (not Core)
//          is deliberate: the C4 structural pin forbids any exporter
//          dependency in Core, and this emitter satisfies the "metrics
//          shape is Prometheus-exportable without requiring an exporter
//          package" guardrail.
// Reference: https://prometheus.io/docs/instrumenting/exposition_formats/
// Milestone: D — phase 5.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;

namespace ElpisEdgeConnect.Host.Endpoints;

/// <summary>
/// Renders a snapshot of a <see cref="Meter"/>'s observable instruments
/// in Prometheus text exposition format.
/// </summary>
public static class PrometheusTextEmitter
{
    /// <summary>
    /// Drive a <see cref="MeterListener"/> against <paramref name="meter"/>,
    /// collect one round of observable measurements, and return the
    /// Prometheus text representation.
    /// </summary>
    /// <param name="meter">The meter to scrape. Only instruments owned by
    /// this meter are included.</param>
    /// <returns>Prometheus text format. Empty string if the meter has no
    /// published instruments yet.</returns>
    public static string Render(Meter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);

        // name → list of (labels, value). One entry per measurement.
        // We collect into ordered structures so the output is
        // deterministic for tests.
        var byName = new SortedDictionary<string, List<Sample>>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var types = new Dictionary<string, string>(StringComparer.Ordinal);

        void AddSample(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (!byName.TryGetValue(name, out var list))
            {
                list = new List<Sample>();
                byName[name] = list;
            }
            var kvs = new List<KeyValuePair<string, string>>(tags.Length);
            foreach (var t in tags)
            {
                kvs.Add(new KeyValuePair<string, string>(t.Key, t.Value?.ToString() ?? ""));
            }
            list.Add(new Sample(kvs, value));
        }

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter)
                {
                    l.EnableMeasurementEvents(instrument);
                    descriptions[instrument.Name] = instrument.Description ?? "";
                    // Map instrument.GetType() to a Prometheus type. Only
                    // the instrument kinds DiagnosticsMeters uses are
                    // handled here; extending this map is cheap.
                    var typeName = instrument.GetType().Name;
                    types[instrument.Name] = typeName.StartsWith("ObservableCounter", StringComparison.Ordinal)
                        ? "counter"
                        : typeName.StartsWith("ObservableGauge", StringComparison.Ordinal)
                            ? "gauge"
                            : "untyped";
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            AddSample(instrument.Name, value, tags));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            AddSample(instrument.Name, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            AddSample(instrument.Name, value, tags));

        listener.Start();
        listener.RecordObservableInstruments();

        // Format.
        var sb = new StringBuilder();
        foreach (var (name, samples) in byName)
        {
            if (descriptions.TryGetValue(name, out var help) && !string.IsNullOrEmpty(help))
            {
                sb.Append("# HELP ").Append(name).Append(' ').Append(EscapeHelp(help)).Append('\n');
            }
            if (types.TryGetValue(name, out var type))
            {
                sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
            }
            foreach (var sample in samples)
            {
                sb.Append(name);
                if (sample.Labels.Count > 0)
                {
                    sb.Append('{');
                    for (var i = 0; i < sample.Labels.Count; i++)
                    {
                        if (i > 0) { sb.Append(','); }
                        sb.Append(sample.Labels[i].Key);
                        sb.Append("=\"");
                        sb.Append(EscapeLabelValue(sample.Labels[i].Value));
                        sb.Append('"');
                    }
                    sb.Append('}');
                }
                sb.Append(' ');
                sb.Append(sample.Value.ToString("R", CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string EscapeHelp(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string EscapeLabelValue(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);

    private sealed record Sample(IReadOnlyList<KeyValuePair<string, string>> Labels, double Value);
}
