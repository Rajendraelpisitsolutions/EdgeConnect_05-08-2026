// ============================================================================
// File: Focas2FakeModeMeter.cs
// Purpose: Publishes the gauge `edgeconnect_focas2_fake_mode_enabled` so
//          Prometheus / OpenTelemetry exporters can observe whether the
//          FOCAS2 demo backend is active. Registered as a singleton in
//          EdgeConnectComposition; the gauge value reads through to
//          Focas2DemoModeOptions.IsEnabled at scrape time.
//
//          The gauge is registered UNCONDITIONALLY (value 0 in production,
//          1 in demo mode) so monitoring can alert on the metric without
//          having to handle absence-vs-zero ambiguity.
//
//          NO P/Invoke (Locked I). Pure managed C# wrapping
//          System.Diagnostics.Metrics.Meter.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v2.md
//            §2 Locked E + §11 (ADR-0012 outline)
// ============================================================================

using System;
using System.Diagnostics.Metrics;
using ElpisEdgeConnect.Core.Diagnostics;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Owns a <see cref="Meter"/> instance and publishes the FOCAS2 fake-mode
/// gauge. Disposable — the underlying meter is released with this object.
/// </summary>
public sealed class Focas2FakeModeMeter : IDisposable
{
    /// <summary>The instrument name pinned by tests and monitoring dashboards.</summary>
    public const string GaugeName = "edgeconnect_focas2_fake_mode_enabled";

    private readonly Meter _meter;

    /// <summary>
    /// Register the gauge on a new <see cref="Meter"/> with the shared
    /// diagnostics name (<see cref="DiagnosticsConstants.MeterName"/>).
    /// Multiple Meters with the same name coexist — Prometheus exporters
    /// see the union of instruments.
    /// </summary>
    public Focas2FakeModeMeter()
    {
        _meter = new Meter(DiagnosticsConstants.MeterName);
        _meter.CreateObservableGauge<int>(
            GaugeName,
            CurrentValue,
            unit: "boolean",
            description:
                "1 when EDGECONNECT_FOCAS2_FAKE_MODE is truthy and FOCAS2 sources are " +
                "backed by the synthetic demo controller; 0 otherwise.");
    }

    /// <summary>
    /// Read the current gauge value. Cheap (<see cref="Focas2DemoModeOptions.IsEnabled"/>
    /// is cached after first read).
    /// </summary>
    public static int CurrentValue() => Focas2DemoModeOptions.IsEnabled ? 1 : 0;

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
