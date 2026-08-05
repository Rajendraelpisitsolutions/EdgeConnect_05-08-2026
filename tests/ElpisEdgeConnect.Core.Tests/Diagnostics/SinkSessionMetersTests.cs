// ============================================================================
// File: Diagnostics/SinkSessionMetersTests.cs
// Purpose: H.2 pin tests for the three sink session-tracking gauges
//          added to DiagnosticsMeters:
//            * elpis_edgeconnect_sink_active_sessions
//            * elpis_edgeconnect_sink_subscriptions
//            * elpis_edgeconnect_sink_monitored_items
//          Verifies instrument names, tag keys, and that the values
//          come from the collector's per-sink ActiveSessions snapshot
//          (latest-wins, with subscription / monitored-item totals
//          summed across sessions).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class SinkSessionMetersTests
{
    private static List<(string Name, int Value, Dictionary<string, object?> Tags)> RecordIntMeasurements(Meter meter)
    {
        var records = new List<(string, int, Dictionary<string, object?>)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
        {
            var d = new Dictionary<string, object?>(tags.Length);
            foreach (var t in tags) d[t.Key] = t.Value;
            records.Add((instrument.Name, value, d));
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return records;
    }

    private static SinkSessionSummary Session(string id, int subs = 0, int items = 0) => new()
    {
        SessionId = id,
        SessionName = $"client-{id}",
        ConnectedAtUtc = DateTime.UtcNow,
        SubscriptionCount = subs,
        MonitoredItemCount = items,
    };

    [Fact]
    public void NewInstruments_ExistWithLockedNames()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.RecordSinkAdapterState("r1", "opcua-1", AdapterState.Running, lastError: null);
        collector.RecordActiveSessions("r1", "opcua-1", new[] { Session("s1") });

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var records = RecordIntMeasurements(meter);

        var names = records.Select(r => r.Name).Distinct().ToArray();
        names.Should().Contain(DiagnosticsMeters.SinkActiveSessionsInstrument);
        names.Should().Contain(DiagnosticsMeters.SinkSubscriptionsInstrument);
        names.Should().Contain(DiagnosticsMeters.SinkMonitoredItemsInstrument);
    }

    [Fact]
    public void ActiveSessionsGauge_ReadsSessionCountFromCollector()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.RecordSinkAdapterState("r1", "opcua-1", AdapterState.Running, lastError: null);
        collector.RecordActiveSessions("r1", "opcua-1", new[]
        {
            Session("s1"), Session("s2"), Session("s3"),
        });

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var records = RecordIntMeasurements(meter);

        var sessionGauge = records.Single(r => r.Name == DiagnosticsMeters.SinkActiveSessionsInstrument);
        sessionGauge.Value.Should().Be(3);
        sessionGauge.Tags[DiagnosticsMeters.TagRouteId].Should().Be("r1");
        sessionGauge.Tags[DiagnosticsMeters.TagSinkId].Should().Be("opcua-1");
    }

    [Fact]
    public void SubscriptionAndMonitoredItemGauges_SumAcrossSessions()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.RecordActiveSessions("r1", "opcua-1", new[]
        {
            Session("s1", subs: 2, items: 10),
            Session("s2", subs: 3, items: 25),
        });

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var records = RecordIntMeasurements(meter);

        var subs = records.Single(r => r.Name == DiagnosticsMeters.SinkSubscriptionsInstrument);
        subs.Value.Should().Be(5);

        var items = records.Single(r => r.Name == DiagnosticsMeters.SinkMonitoredItemsInstrument);
        items.Value.Should().Be(35);
    }

    [Fact]
    public void Gauges_DoNotEmitForSinksWithoutSessionPush()
    {
        // A sink whose adapter state was reported but no session push has
        // happened (typical: an MQTT sink with no SessionTracking
        // capability) should not produce any session-* gauge measurements.
        var collector = new RuntimeDiagnosticsCollector();
        collector.RecordSinkAdapterState("r1", "mqtt-1", AdapterState.Running, lastError: null);

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var records = RecordIntMeasurements(meter);

        records.Should().NotContain(r => r.Name == DiagnosticsMeters.SinkActiveSessionsInstrument);
        records.Should().NotContain(r => r.Name == DiagnosticsMeters.SinkSubscriptionsInstrument);
        records.Should().NotContain(r => r.Name == DiagnosticsMeters.SinkMonitoredItemsInstrument);
    }

    [Fact]
    public void Gauges_EmitZeroForTrackedSinkWithNoClients()
    {
        // Empty observation is meaningful: "tracked, no clients connected"
        // should produce a 0-valued measurement, not no measurement.
        var collector = new RuntimeDiagnosticsCollector();
        collector.RecordActiveSessions("r1", "opcua-1", Array.Empty<SinkSessionSummary>());

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var records = RecordIntMeasurements(meter);

        var sessionGauge = records.Single(r => r.Name == DiagnosticsMeters.SinkActiveSessionsInstrument);
        sessionGauge.Value.Should().Be(0);
    }
}
