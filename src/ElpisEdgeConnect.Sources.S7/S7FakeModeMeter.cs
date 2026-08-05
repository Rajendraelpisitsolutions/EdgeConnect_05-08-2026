// ============================================================================
// File: S7FakeModeMeter.cs
// Purpose: Publishes the gauge `edgeconnect_s7_fake_mode_enabled` so
//          Prometheus / OpenTelemetry exporters can observe whether the S7
//          demo backend is active. Mirrors Focas2FakeModeMeter. Registered as
//          a singleton in EdgeConnectComposition; the gauge value reads
//          through to S7DemoModeOptions.IsEnabled at scrape time.
//
//          Registered UNCONDITIONALLY (value 0 in production, 1 in demo) so
//          monitoring can alert without absence-vs-zero ambiguity. Pure
//          managed C# — no native dependencies.
// Reference: docs/decisions/0029-s7-demo-mode.md (mirrors ADR-0012)
// ============================================================================

using System;
using System.Diagnostics.Metrics;
using ElpisEdgeConnect.Core.Diagnostics;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Owns a <see cref="Meter"/> instance and publishes the S7 fake-mode gauge.
/// Disposable — the underlying meter is released with this object.
/// </summary>
public sealed class S7FakeModeMeter : IDisposable
{
    /// <summary>The instrument name pinned by tests and monitoring dashboards.</summary>
    public const string GaugeName = "edgeconnect_s7_fake_mode_enabled";

    private readonly Meter _meter;

    /// <summary>Register the gauge on a new <see cref="Meter"/> with the shared diagnostics name.</summary>
    public S7FakeModeMeter()
    {
        _meter = new Meter(DiagnosticsConstants.MeterName);
        _meter.CreateObservableGauge<int>(
            GaugeName,
            CurrentValue,
            unit: "boolean",
            description:
                "1 when EDGECONNECT_S7_FAKE_MODE is truthy and S7 sources are " +
                "backed by the synthetic demo PLC; 0 otherwise.");
    }

    /// <summary>Read the current gauge value (cheap — <see cref="S7DemoModeOptions.IsEnabled"/> is cached).</summary>
    public static int CurrentValue() => S7DemoModeOptions.IsEnabled ? 1 : 0;

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
