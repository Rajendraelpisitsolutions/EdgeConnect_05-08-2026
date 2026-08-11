// ============================================================================
// Tests: DiagnosticsEventAggregator — system-wide event merging across
//        every route + configuration audit projection + filter handling
//        + chain verification result. The DTO this produces is the
//        canonical operational-event envelope; its severity, code
//        assignment, RouteId nullable-on-audit-events, and Actor
//        stamping all need pinning to prevent silent regression.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class DiagnosticsEventAggregatorTests
{
    [Fact]
    public async Task GetRecentEventsAsync_MergesRouteEventsAndAuditEntries()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var fakes = BuildFakes();
        fakes.Diagnostics.AddRoute("r1");
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Starting,
            To = RouteState.Running,
            ObservedAtUtc = t0.AddSeconds(10),
        });
        fakes.Config.AddAuditEntry(new ConfigurationAuditEntry
        {
            Timestamp = t0.AddSeconds(20),
            VersionId = new ConfigurationVersionId("v1"),
            Action = ConfigurationAuditAction.Applied,
            Actor = "alice",
            Summary = "Initial config applied",
            PreviousHash = ConfigurationAuditLog.GenesisHash,
        });

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);

        var response = await agg.GetRecentEventsAsync(new DiagnosticsEventFilter(), CancellationToken.None);

        response.Events.Should().HaveCount(2);
        // Newest first
        response.Events[0].EventCode.Should().Be(DiagnosticsEventCodes.ConfigApplied);
        response.Events[0].RouteId.Should().BeNull("audit events are gateway-scoped, not route-scoped");
        response.Events[0].Actor.Should().Be("alice");
        response.Events[1].EventCode.Should().Be(DiagnosticsEventCodes.RouteStateChanged);
        response.Events[1].RouteId.Should().Be("r1");
        response.Events[1].Actor.Should().BeNull("route-scoped events aren't operator-triggered");
    }

    [Fact]
    public async Task GetRecentEventsAsync_FilterByRouteIdSkipsAuditEntries()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var fakes = BuildFakes();
        fakes.Diagnostics.AddRoute("r1");
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Starting,
            To = RouteState.Running,
            ObservedAtUtc = t0,
        });
        fakes.Config.AddAuditEntry(new ConfigurationAuditEntry
        {
            Timestamp = t0.AddSeconds(1),
            VersionId = new ConfigurationVersionId("v1"),
            Action = ConfigurationAuditAction.Applied,
            Actor = "alice",
            Summary = "config applied",
            PreviousHash = ConfigurationAuditLog.GenesisHash,
        });

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);

        var response = await agg.GetRecentEventsAsync(
            new DiagnosticsEventFilter { RouteId = "r1" },
            CancellationToken.None);

        response.Events.Should().HaveCount(1, "audit entries have null RouteId so they can't match a route filter");
        response.Events[0].RouteId.Should().Be("r1");
    }

    // ─── M.2b.3.1 — gateway-startup events ────────────────────────────────

    [Fact]
    public async Task GetRecentEventsAsync_IncludesGatewayStartupEvents_WhenNoRouteFilter()
    {
        var t0 = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var fakes = BuildFakes();
        fakes.StartupEvents.Append(new GatewayStartupEvent
        {
            EventCode = "focas2.fake-mode.activated",
            Severity = "Critical",
            Message = "FOCAS2 fake mode is active.",
            EmittedAtUtc = t0,
        });

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);

        var response = await agg.GetRecentEventsAsync(new DiagnosticsEventFilter(), CancellationToken.None);

        response.Events.Should().HaveCount(1);
        var ev = response.Events[0];
        ev.EventCode.Should().Be(DiagnosticsEventCodes.Focas2FakeModeActivated,
            "the known startup code must map to the management catalog constant");
        ev.Severity.Should().Be(DiagnosticsSeverity.Error,
            "'Critical' severity strings project to DiagnosticsSeverity.Error (the most severe wire value)");
        ev.RouteId.Should().BeNull("gateway-startup events are gateway-scoped, not route-scoped");
        ev.Actor.Should().Be("system", "boot-time events are not operator-triggered");
        ev.Summary.Should().Be("FOCAS2 fake mode is active.");
    }

    [Fact]
    public async Task GetRecentEventsAsync_FilterByRouteId_SkipsGatewayStartupEvents()
    {
        var t0 = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var fakes = BuildFakes();
        fakes.Diagnostics.AddRoute("r1");
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Starting,
            To = RouteState.Running,
            ObservedAtUtc = t0.AddSeconds(5),
        });
        fakes.StartupEvents.Append(new GatewayStartupEvent
        {
            EventCode = "focas2.fake-mode.activated",
            Severity = "Critical",
            Message = "FOCAS2 fake mode is active.",
            EmittedAtUtc = t0,
        });

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);

        var response = await agg.GetRecentEventsAsync(
            new DiagnosticsEventFilter { RouteId = "r1" },
            CancellationToken.None);

        response.Events.Should().HaveCount(1,
            "gateway-startup events have null RouteId so they can't match a route filter — same skip as audit entries");
        response.Events[0].RouteId.Should().Be("r1");
        response.Events.Should().NotContain(e => e.EventCode == DiagnosticsEventCodes.Focas2FakeModeActivated);
    }

    [Fact]
    public async Task GetRecentEventsAsync_FilterByEventCodesSet()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var fakes = BuildFakes();
        fakes.Diagnostics.AddRoute("r1", sinkIds: new[] { "sink-a" });
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Running,
            To = RouteState.Degraded,
            ObservedAtUtc = t0,
        });
        fakes.Diagnostics.AddSinkEvent("r1", "sink-a", new SinkEventEntry
        {
            Kind = SinkEventKind.Degraded,
            SinkInstanceId = "sink-a",
            RouteId = "r1",
            ObservedAtUtc = t0.AddSeconds(1),
        });
        fakes.Diagnostics.AddSinkEvent("r1", "sink-a", new SinkEventEntry
        {
            Kind = SinkEventKind.Recovered,
            SinkInstanceId = "sink-a",
            RouteId = "r1",
            ObservedAtUtc = t0.AddSeconds(2),
        });

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);

        // Filter to "all SINK.*" — explicit list, no glob
        var response = await agg.GetRecentEventsAsync(
            new DiagnosticsEventFilter
            {
                EventCodes = new[]
                {
                    DiagnosticsEventCodes.SinkDegraded,
                    DiagnosticsEventCodes.SinkDraining,
                    DiagnosticsEventCodes.SinkRecovered,
                },
            },
            CancellationToken.None);

        response.Events.Should().HaveCount(2);
        response.Events.Select(e => e.EventCode).Should().OnlyContain(c => c.StartsWith("SINK."));
    }

    [Fact]
    public async Task GetRecentEventsAsync_FilterBySeverity()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var fakes = BuildFakes();
        fakes.Diagnostics.AddRoute("r1", sinkIds: new[] { "sink-a" });
        // Info-severity event
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Starting,
            To = RouteState.Running,
            ObservedAtUtc = t0,
        });
        // Warning-severity event
        fakes.Diagnostics.AddSinkEvent("r1", "sink-a", new SinkEventEntry
        {
            Kind = SinkEventKind.Degraded,
            SinkInstanceId = "sink-a",
            RouteId = "r1",
            ObservedAtUtc = t0.AddSeconds(1),
        });
        // Error-severity event
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Running,
            To = RouteState.Failed,
            ObservedAtUtc = t0.AddSeconds(2),
        });

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);

        var warnings = await agg.GetRecentEventsAsync(
            new DiagnosticsEventFilter { Severity = DiagnosticsSeverity.Warning },
            CancellationToken.None);

        warnings.Events.Should().HaveCount(1);
        warnings.Events[0].Severity.Should().Be(DiagnosticsSeverity.Warning);
    }

    [Fact]
    public async Task GetRecentEventsAsync_FilterBySinceUtc()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var fakes = BuildFakes();
        fakes.Diagnostics.AddRoute("r1");
        // Old event — before the window
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Starting,
            To = RouteState.Running,
            ObservedAtUtc = t0.AddSeconds(-3600),
        });
        // Recent event — inside the window
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Running,
            To = RouteState.Degraded,
            ObservedAtUtc = t0,
        });

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);

        var response = await agg.GetRecentEventsAsync(
            new DiagnosticsEventFilter { SinceUtc = t0.AddSeconds(-60) },
            CancellationToken.None);

        response.Events.Should().HaveCount(1);
        response.Events[0].ToState.Should().Be("Degraded");
    }

    [Fact]
    public async Task GetRecentEventsAsync_PopulatesRetentionMetadata()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var fakes = BuildFakes();
        fakes.Diagnostics.AddRoute("r1");
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Starting,
            To = RouteState.Running,
            ObservedAtUtc = t0,
        });
        fakes.Diagnostics.AddRouteStateEvent("r1", new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Running,
            To = RouteState.Degraded,
            ObservedAtUtc = t0.AddSeconds(10),
        });

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);

        var response = await agg.GetRecentEventsAsync(new DiagnosticsEventFilter(), CancellationToken.None);

        response.ApproximateTotalEvents.Should().Be(2);
        response.RetainedSinceUtc.Should().Be(t0, "the older event time is the retention floor");
    }

    [Fact]
    public async Task GetRecentEventsAsync_AuditActionsMapToCorrectCodesAndSeverity()
    {
        var t0 = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var fakes = BuildFakes();
        fakes.Diagnostics.AddRoute("r1");  // no route events; only audit
        fakes.Config.AddAuditEntry(MakeAudit(t0.AddSeconds(1), ConfigurationAuditAction.DraftCreated, "alice"));
        fakes.Config.AddAuditEntry(MakeAudit(t0.AddSeconds(2), ConfigurationAuditAction.Applied, "alice"));
        fakes.Config.AddAuditEntry(MakeAudit(t0.AddSeconds(3), ConfigurationAuditAction.RolledBack, "bob"));
        fakes.Config.AddAuditEntry(MakeAudit(t0.AddSeconds(4), ConfigurationAuditAction.DraftDiscarded, "bob"));

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);
        var response = await agg.GetRecentEventsAsync(new DiagnosticsEventFilter(), CancellationToken.None);

        // Sort by code so assertions are stable regardless of timestamp order
        var byCode = response.Events.ToDictionary(e => e.EventCode);

        byCode[DiagnosticsEventCodes.ConfigDraftCreated].Severity.Should().Be(DiagnosticsSeverity.Info);
        byCode[DiagnosticsEventCodes.ConfigApplied].Severity.Should().Be(DiagnosticsSeverity.Info);
        byCode[DiagnosticsEventCodes.ConfigRolledBack].Severity.Should().Be(DiagnosticsSeverity.Warning,
            "rollback is unusual enough operationally to deserve a Warning chip");
        byCode[DiagnosticsEventCodes.ConfigDraftDiscarded].Severity.Should().Be(DiagnosticsSeverity.Info);

        byCode[DiagnosticsEventCodes.ConfigRolledBack].Actor.Should().Be("bob");
    }

    [Fact]
    public async Task VerifyAuditChainAsync_ReturnsVerifiedWhenChainIntact()
    {
        var fakes = BuildFakes();
        fakes.Config.AddAuditEntry(MakeAudit(DateTime.UtcNow, ConfigurationAuditAction.DraftCreated, "alice"));
        fakes.Config.AddAuditEntry(MakeAudit(DateTime.UtcNow, ConfigurationAuditAction.Applied, "alice"));

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);
        var status = await agg.VerifyAuditChainAsync(CancellationToken.None);

        status.Verified.Should().BeTrue();
        status.EntriesChecked.Should().Be(2);
        status.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAuditChainAsync_ReturnsFailureWhenConfigManagerThrows()
    {
        var fakes = BuildFakes();
        fakes.Config.ThrowOnVerifyChain = "audit log corrupt — chain broken at entry 3";
        // Even if entries exist, verification short-circuits via exception
        fakes.Config.AddAuditEntry(MakeAudit(DateTime.UtcNow, ConfigurationAuditAction.Applied, "alice"));

        var agg = new DiagnosticsEventAggregator(fakes.Diagnostics, fakes.RouteAgg, fakes.Config, fakes.StartupEvents);
        var status = await agg.VerifyAuditChainAsync(CancellationToken.None);

        status.Verified.Should().BeFalse();
        status.FailureReason.Should().Contain("entry 3");
    }

    [Fact]
    public void ParseFilter_ParsesMultiCodeQueryString()
    {
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["code"] = new Microsoft.Extensions.Primitives.StringValues(new[]
            {
                "SINK.DEGRADED",
                "SINK.RECOVERED",
            }),
            ["severity"] = "Warning",
            ["limit"] = "50",
        });

        var (filter, error) = DiagnosticsApi.ParseFilter(query);

        error.Should().BeNull();
        filter.EventCodes.Should().BeEquivalentTo(new[] { "SINK.DEGRADED", "SINK.RECOVERED" });
        filter.Severity.Should().Be(DiagnosticsSeverity.Warning);
        filter.EffectiveLimit.Should().Be(50);
    }

    [Fact]
    public void ParseFilter_RejectsTooManyEventCodes()
    {
        var codes = new string[DiagnosticsEventFilter.MaxEventCodes + 1];
        for (var i = 0; i < codes.Length; i++) codes[i] = $"FAKE.{i}";
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["code"] = new Microsoft.Extensions.Primitives.StringValues(codes),
        });

        var (_, error) = DiagnosticsApi.ParseFilter(query);

        error.Should().NotBeNull().And.Contain("max is");
    }

    [Fact]
    public void ParseFilter_RejectsUnrecognisedSeverity()
    {
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["severity"] = "Catastrophic",
        });

        var (_, error) = DiagnosticsApi.ParseFilter(query);

        error.Should().NotBeNull().And.Contain("severity");
    }

    [Fact]
    public void StampGatewayId_PopulatesEveryEvent()
    {
        var response = new DiagnosticsEventsResponse
        {
            Events = new[]
            {
                new DiagnosticsEventDto
                {
                    OccurredAtUtc = DateTime.UtcNow,
                    EventCode = DiagnosticsEventCodes.RouteStateChanged,
                    Severity = DiagnosticsSeverity.Info,
                    Summary = "x",
                    RouteId = "r1",
                },
                new DiagnosticsEventDto
                {
                    OccurredAtUtc = DateTime.UtcNow,
                    EventCode = DiagnosticsEventCodes.ConfigApplied,
                    Severity = DiagnosticsSeverity.Info,
                    Summary = "y",
                    RouteId = null,
                    Actor = "alice",
                },
            },
        };

        var stamped = DiagnosticsApi.StampGatewayId(response, "gw-blr-01");

        stamped.Events.Should().AllSatisfy(e => e.GatewayId.Should().Be("gw-blr-01"));
        // RouteId and Actor preserved through `with`
        stamped.Events[0].RouteId.Should().Be("r1");
        stamped.Events[1].Actor.Should().Be("alice");
    }

    [Fact]
    public void StampGatewayId_NoopWhenIdIsNull()
    {
        var response = new DiagnosticsEventsResponse
        {
            Events = new[]
            {
                new DiagnosticsEventDto
                {
                    OccurredAtUtc = DateTime.UtcNow,
                    EventCode = DiagnosticsEventCodes.RouteStateChanged,
                    Severity = DiagnosticsSeverity.Info,
                    Summary = "x",
                    RouteId = "r1",
                },
            },
        };
        var stamped = DiagnosticsApi.StampGatewayId(response, null);
        stamped.Should().BeSameAs(response, "no-op when nothing to stamp avoids the allocation");
    }

    // ----- Helpers -----------------------------------------------------------

    private static ConfigurationAuditEntry MakeAudit(DateTime ts, ConfigurationAuditAction action, string actor) => new()
    {
        Timestamp = ts,
        VersionId = new ConfigurationVersionId("v1"),
        Action = action,
        Actor = actor,
        Summary = $"{action} by {actor}",
        PreviousHash = ConfigurationAuditLog.GenesisHash,
    };

    private static Fakes BuildFakes()
    {
        var diag = new FakeDiagnosticsService();
        var cfg = new FakeConfigurationManager();
        var routeAgg = new RouteEventAggregator(diag);
        var startupEvents = new GatewayStartupEventStore();
        return new Fakes(diag, cfg, routeAgg, startupEvents);
    }

    private readonly record struct Fakes(
        FakeDiagnosticsService Diagnostics,
        FakeConfigurationManager Config,
        RouteEventAggregator RouteAgg,
        GatewayStartupEventStore StartupEvents);

    /// <summary>
    /// Bare-bones IConfigurationManager fake. Only implements
    /// GetAuditLogAsync (with a hook to simulate chain failures);
    /// every other method throws NotImplementedException because the
    /// aggregator under test never calls them.
    /// </summary>
    private sealed class FakeConfigurationManager : IConfigurationManager
    {
        private readonly List<ConfigurationAuditEntry> _entries = new();
        public string? ThrowOnVerifyChain { get; set; }

        public void AddAuditEntry(ConfigurationAuditEntry entry) => _entries.Add(entry);

        public async IAsyncEnumerable<ConfigurationAuditEntry> GetAuditLogAsync(
            bool verifyChain,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (verifyChain && ThrowOnVerifyChain is not null)
            {
                throw new ConfigurationException(CoreErrors.ConfigAuditCorrupt, ThrowOnVerifyChain);
            }
            foreach (var e in _entries)
            {
                yield return e;
                await Task.Yield();
            }
        }

        // ---- unused methods ----
        public ConfigurationVersionId CurrentVersionId => throw new NotImplementedException();
        public Task InitializeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public ValueTask<GatewayConfiguration> GetCurrentAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DraftId> CreateDraftAsync(GatewayConfiguration draft, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<GatewayConfiguration?> GetDraftAsync(DraftId draftId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ValidationResult> ValidateDraftAsync(DraftId draftId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ConfigurationApplyResult> ApplyDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DiscardDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ConfigurationApplyResult> RollbackAsync(ConfigurationVersionId targetVersionId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<ConfigurationHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public ValueTask<ConfigurationAuditEntry> AppendRuntimeFaultAsync(ElpisEdgeConnect.Core.Diagnostics.ConfigurationFault fault, CancellationToken cancellationToken) => throw new NotImplementedException();
        public event EventHandler<ConfigurationChangeEventArgs>? CurrentChanged { add { } remove { } }
    }

    /// <summary>
    /// In-memory IDiagnosticsService fake. Duplicate of the one in
    /// RouteEventAggregatorTests; the test projects share assemblies but
    /// not private nested types, so we re-declare it here for self-
    /// containment.
    /// </summary>
    private sealed class FakeDiagnosticsService : IDiagnosticsService
    {
        private readonly Dictionary<string, RouteHealthSnapshot> _snapshots = new();
        private readonly Dictionary<string, List<RouteStateChangedEvent>> _stateEvents = new();
        private readonly Dictionary<(string Route, string Sink), List<SinkEventEntry>> _sinkEvents = new();
        private readonly Dictionary<string, List<BackpressureDroppedEvent>> _backpressureEvents = new();

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

        public BoundedEventLogSnapshot<RoutePointQuarantinedEvent>? GetQuarantineEvents(string routeId) => null;
    }
}
