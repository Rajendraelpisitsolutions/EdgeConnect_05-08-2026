// ============================================================================
// Tests: RouteEventAggregator — pure merge/normalize/severity-map/sort/cap
//        across the three Core event-log streams. The wire shape this
//        produces (DiagnosticsEventDto) is consumed by the M.1c.1 Route
//        detail page and (soon) the M.1c.2 system-wide Diagnostics page,
//        so its severity + EventCode + sort-order guarantees are pinned
//        here to prevent silent regressions.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class RouteEventAggregatorTests
{
    [Fact]
    public void GetRecentRouteEvents_ReturnsEmpty_WhenRouteIsUnknown()
    {
        var diag = new FakeDiagnosticsService();
        var agg = new RouteEventAggregator(diag);

        agg.GetRecentRouteEvents("unknown-route", limit: 50).Should().BeEmpty();
    }

    [Fact]
    public void GetRecentRouteEvents_ReturnsEmpty_WhenLimitIsZero()
    {
        var diag = new FakeDiagnosticsService();
        diag.AddRoute("r1");
        var agg = new RouteEventAggregator(diag);

        agg.GetRecentRouteEvents("r1", limit: 0).Should().BeEmpty();
    }

    [Fact]
    public void GetRecentRouteEvents_MergesFromAllThreeSources_AndSortsDescendingByTime()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var diag = new FakeDiagnosticsService();
        diag.AddRoute("r1", sinkIds: new[] { "sink-a" });
        diag.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Starting,
            To = RouteState.Running,
            ObservedAtUtc = t0.AddSeconds(10),
            Reason = null,
        });
        diag.AddSinkEvent("r1", "sink-a", new SinkEventEntry
        {
            Kind = SinkEventKind.Degraded,
            SinkInstanceId = "sink-a",
            RouteId = "r1",
            ObservedAtUtc = t0.AddSeconds(20),
            Error = new AdapterError
            {
                Code = "MQTT.NOT_CONNECTED",
                Category = ErrorCategory.Network,
                Message = "broker disconnected",
            },
        });
        diag.AddBackpressureEvent("r1", new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 14,
            Reason = "buffer-capacity",
            ObservedAtUtc = t0.AddSeconds(15),
        });

        var agg = new RouteEventAggregator(diag);

        var events = agg.GetRecentRouteEvents("r1", limit: 50);

        events.Should().HaveCount(3, "one from each source");
        // Newest first
        events[0].EventCode.Should().Be(DiagnosticsEventCodes.SinkDegraded);
        events[1].EventCode.Should().Be(DiagnosticsEventCodes.BackpressureDropped);
        events[2].EventCode.Should().Be(DiagnosticsEventCodes.RouteStateChanged);
    }

    [Fact]
    public void GetRecentRouteEvents_MapsSeverityCorrectlyPerEventKind()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var diag = new FakeDiagnosticsService();
        diag.AddRoute("r1", sinkIds: new[] { "sink-a" });
        diag.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Running,
            To = RouteState.Failed,
            ObservedAtUtc = t0.AddSeconds(1),
        });
        diag.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Running,
            To = RouteState.Degraded,
            ObservedAtUtc = t0.AddSeconds(2),
        });
        diag.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Starting,
            To = RouteState.Running,
            ObservedAtUtc = t0.AddSeconds(3),
        });
        diag.AddSinkEvent("r1", "sink-a", new SinkEventEntry
        {
            Kind = SinkEventKind.Degraded,
            SinkInstanceId = "sink-a",
            RouteId = "r1",
            ObservedAtUtc = t0.AddSeconds(4),
        });
        diag.AddSinkEvent("r1", "sink-a", new SinkEventEntry
        {
            Kind = SinkEventKind.Recovered,
            SinkInstanceId = "sink-a",
            RouteId = "r1",
            ObservedAtUtc = t0.AddSeconds(5),
        });
        diag.AddBackpressureEvent("r1", new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 3,
            Reason = "buffer-capacity",
            ObservedAtUtc = t0.AddSeconds(6),
        });

        var agg = new RouteEventAggregator(diag);
        var events = agg.GetRecentRouteEvents("r1", limit: 50);

        SeverityFor(events, code: DiagnosticsEventCodes.RouteStateChanged, atOffset: 1).Should().Be(DiagnosticsSeverity.Error,
            "route entered Failed");
        SeverityFor(events, code: DiagnosticsEventCodes.RouteStateChanged, atOffset: 2).Should().Be(DiagnosticsSeverity.Warning,
            "route entered Degraded");
        SeverityFor(events, code: DiagnosticsEventCodes.RouteStateChanged, atOffset: 3).Should().Be(DiagnosticsSeverity.Info,
            "route entered Running");
        SeverityFor(events, code: DiagnosticsEventCodes.SinkDegraded, atOffset: 4).Should().Be(DiagnosticsSeverity.Warning);
        SeverityFor(events, code: DiagnosticsEventCodes.SinkRecovered, atOffset: 5).Should().Be(DiagnosticsSeverity.Info);
        SeverityFor(events, code: DiagnosticsEventCodes.BackpressureDropped, atOffset: 6).Should().Be(DiagnosticsSeverity.Warning);
    }

    [Fact]
    public void GetRecentRouteEvents_MapsQuarantineEvent_AsDataQualityWarning()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var diag = new FakeDiagnosticsService();
        diag.AddRoute("r1");
        diag.AddQuarantineEvent("r1", new RoutePointQuarantinedEvent
        {
            RouteId = "r1",
            TagName = "production/parts_count",
            Reason = "InvalidDataException: Unsupported metadata value runtime type: Decimal",
            ObservedAtUtc = t0.AddSeconds(2),
        });

        var agg = new RouteEventAggregator(diag);
        var events = agg.GetRecentRouteEvents("r1", limit: 50);

        var q = events.Should()
            .ContainSingle(e => e.EventCode == DiagnosticsEventCodes.RoutePointQuarantined)
            .Subject;
        q.Severity.Should().Be(DiagnosticsSeverity.Warning);
        q.RouteId.Should().Be("r1");
        q.Summary.Should().Contain("production/parts_count");
        q.DroppedCount.Should().Be(1);
    }

    [Fact]
    public void GetRecentRouteEvents_RespectsLimit()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var diag = new FakeDiagnosticsService();
        diag.AddRoute("r1");
        for (var i = 0; i < 10; i++)
        {
            diag.AddBackpressureEvent("r1", new BackpressureDroppedEvent
            {
                RouteId = "r1",
                DroppedCount = i + 1,
                Reason = "buffer-capacity",
                ObservedAtUtc = t0.AddSeconds(i),
            });
        }

        var agg = new RouteEventAggregator(diag);

        agg.GetRecentRouteEvents("r1", limit: 3).Should().HaveCount(3);
        agg.GetRecentRouteEvents("r1", limit: 100).Should().HaveCount(10);
    }

    [Fact]
    public void GetRecentRouteEvents_PopulatesRouteScopedFields()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var diag = new FakeDiagnosticsService();
        diag.AddRoute("r1", sinkIds: new[] { "sink-a" });
        diag.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Running,
            To = RouteState.Degraded,
            ObservedAtUtc = t0,
        });
        diag.AddSinkEvent("r1", "sink-a", new SinkEventEntry
        {
            Kind = SinkEventKind.Degraded,
            SinkInstanceId = "sink-a",
            RouteId = "r1",
            ObservedAtUtc = t0.AddSeconds(1),
        });
        diag.AddBackpressureEvent("r1", new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 7,
            Reason = "buffer-capacity",
            ObservedAtUtc = t0.AddSeconds(2),
        });

        var agg = new RouteEventAggregator(diag);
        var events = agg.GetRecentRouteEvents("r1", limit: 50);

        var stateChange = FindFirst(events, DiagnosticsEventCodes.RouteStateChanged);
        stateChange.FromState.Should().Be("Running");
        stateChange.ToState.Should().Be("Degraded");
        stateChange.SinkInstanceId.Should().BeNull();
        stateChange.DroppedCount.Should().BeNull();

        var sinkEvt = FindFirst(events, DiagnosticsEventCodes.SinkDegraded);
        sinkEvt.SinkInstanceId.Should().Be("sink-a");
        sinkEvt.FromState.Should().BeNull();
        sinkEvt.DroppedCount.Should().BeNull();

        var bp = FindFirst(events, DiagnosticsEventCodes.BackpressureDropped);
        bp.DroppedCount.Should().Be(7);
        bp.SinkInstanceId.Should().BeNull();

        events.Should().AllSatisfy(e => e.CorrelationId.Should().BeNull(
            "M.1c.1 ships CorrelationId as forward-looking; Core supervisors don't populate it yet"));
    }

    [Fact]
    public void DiagnosticsSeverity_RoundTripsAsJsonString()
    {
        // Wire-contract guard: the type-level JsonStringEnumConverter must
        // keep severity as a string ("Warning"), never an integer (1).
        // External consumers (JS/TS, Python, future EREMOS bridge) read
        // the string form; an int-on-the-wire regression would silently
        // break them.
        var evt = new DiagnosticsEventDto
        {
            OccurredAtUtc = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            EventCode = DiagnosticsEventCodes.SinkDegraded,
            Severity = DiagnosticsSeverity.Warning,
            Summary = "test",
            RouteId = "r1",
        };

        var json = System.Text.Json.JsonSerializer.Serialize(evt);
        json.Should().Contain("\"Severity\":\"Warning\"",
            "DiagnosticsSeverity must serialize as a string, not an int");

        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<DiagnosticsEventDto>(json);
        roundTripped.Should().NotBeNull();
        roundTripped!.Severity.Should().Be(DiagnosticsSeverity.Warning);
    }

    // ----- Helpers -----------------------------------------------------------

    private static DiagnosticsSeverity SeverityFor(IReadOnlyList<DiagnosticsEventDto> events, string code, int atOffset)
    {
        // Match by the observed-at second offset (the test seeds each event
        // with a distinct second so we can disambiguate same-code duplicates).
        foreach (var e in events)
        {
            if (e.EventCode == code && e.OccurredAtUtc.Second == atOffset)
            {
                return e.Severity;
            }
        }
        throw new InvalidOperationException($"No event with code={code} at offset={atOffset}");
    }

    private static DiagnosticsEventDto FindFirst(IReadOnlyList<DiagnosticsEventDto> events, string code)
    {
        foreach (var e in events)
        {
            if (e.EventCode == code) return e;
        }
        throw new InvalidOperationException($"No event with code={code}");
    }

    /// <summary>
    /// Hand-rolled in-memory fake. NSubstitute would need to stub a lot
    /// of IDiagnosticsService methods we don't care about — this is simpler
    /// and the test intent stays visible.
    /// </summary>
    private sealed class FakeDiagnosticsService : IDiagnosticsService
    {
        private readonly Dictionary<string, RouteHealthSnapshot> _snapshots = new();
        private readonly Dictionary<string, List<RouteStateChangedEvent>> _stateEvents = new();
        private readonly Dictionary<(string Route, string Sink), List<SinkEventEntry>> _sinkEvents = new();
        private readonly Dictionary<string, List<BackpressureDroppedEvent>> _backpressureEvents = new();
        private readonly Dictionary<string, List<RoutePointQuarantinedEvent>> _quarantineEvents = new();

        public void AddRoute(string routeId, IReadOnlyList<string>? sinkIds = null)
        {
            var sinks = new List<SinkHealthSnapshot>();
            if (sinkIds is not null)
            {
                foreach (var id in sinkIds)
                {
                    sinks.Add(new SinkHealthSnapshot
                    {
                        SinkInstanceId = id,
                        RouteId = routeId,
                        IsDegraded = false,
                        IsDraining = false,
                        DegradationEventCount = 0,
                        RecoveryEventCount = 0,
                    });
                }
            }
            _snapshots[routeId] = new RouteHealthSnapshot
            {
                RouteId = routeId,
                ObservedAtUtc = DateTime.UtcNow,
                State = RouteState.Running,
                StateTransitionCount = 0,
                BackpressureDropCount = 0,
                Pipeline = new PipelineHealthSnapshot
                {
                    RouteId = routeId,
                    BatchesProcessed = 0,
                    PointsIn = 0,
                    PointsOut = 0,
                    Steps = Array.Empty<TransformStepStats>(),
                },
                Sinks = sinks,
            };
        }

        public void AddRouteStateEvent(string routeId, RouteStateChangedEvent evt)
        {
            if (!_stateEvents.TryGetValue(routeId, out var list))
            {
                list = new();
                _stateEvents[routeId] = list;
            }
            list.Add(evt);
        }

        public void AddSinkEvent(string routeId, string sinkInstanceId, SinkEventEntry evt)
        {
            var key = (routeId, sinkInstanceId);
            if (!_sinkEvents.TryGetValue(key, out var list))
            {
                list = new();
                _sinkEvents[key] = list;
            }
            list.Add(evt);
        }

        public void AddBackpressureEvent(string routeId, BackpressureDroppedEvent evt)
        {
            if (!_backpressureEvents.TryGetValue(routeId, out var list))
            {
                list = new();
                _backpressureEvents[routeId] = list;
            }
            list.Add(evt);
        }

        public void AddQuarantineEvent(string routeId, RoutePointQuarantinedEvent evt)
        {
            if (!_quarantineEvents.TryGetValue(routeId, out var list))
            {
                list = new();
                _quarantineEvents[routeId] = list;
            }
            list.Add(evt);
        }

        public IReadOnlyList<string> GetKnownRoutes() => new List<string>(_snapshots.Keys);

        public RouteHealthSnapshot? GetRouteSnapshot(string routeId) =>
            _snapshots.TryGetValue(routeId, out var s) ? s : null;

        public IReadOnlyList<RouteHealthSnapshot> GetAllRouteSnapshots() =>
            new List<RouteHealthSnapshot>(_snapshots.Values);

        public BoundedEventLogSnapshot<RouteStateChangedEvent>? GetRouteStateEvents(string routeId) =>
            _stateEvents.TryGetValue(routeId, out var list)
                ? new BoundedEventLogSnapshot<RouteStateChangedEvent>
                {
                    Entries = list.AsReadOnly(),
                    TotalAdded = list.Count,
                    TotalDropped = 0,
                    LiveCount = list.Count,
                    Capacity = 256,
                }
                : null;

        public BoundedEventLogSnapshot<SinkEventEntry>? GetSinkEvents(string routeId, string sinkInstanceId) =>
            _sinkEvents.TryGetValue((routeId, sinkInstanceId), out var list)
                ? new BoundedEventLogSnapshot<SinkEventEntry>
                {
                    Entries = list.AsReadOnly(),
                    TotalAdded = list.Count,
                    TotalDropped = 0,
                    LiveCount = list.Count,
                    Capacity = 256,
                }
                : null;

        public BoundedEventLogSnapshot<BackpressureDroppedEvent>? GetBackpressureEvents(string routeId) =>
            _backpressureEvents.TryGetValue(routeId, out var list)
                ? new BoundedEventLogSnapshot<BackpressureDroppedEvent>
                {
                    Entries = list.AsReadOnly(),
                    TotalAdded = list.Count,
                    TotalDropped = 0,
                    LiveCount = list.Count,
                    Capacity = 256,
                }
                : null;

        public BoundedEventLogSnapshot<RoutePointQuarantinedEvent>? GetQuarantineEvents(string routeId) =>
            _quarantineEvents.TryGetValue(routeId, out var list)
                ? new BoundedEventLogSnapshot<RoutePointQuarantinedEvent>
                {
                    Entries = list.AsReadOnly(),
                    TotalAdded = list.Count,
                    TotalDropped = 0,
                    LiveCount = list.Count,
                    Capacity = 256,
                }
                : null;
    }
}
