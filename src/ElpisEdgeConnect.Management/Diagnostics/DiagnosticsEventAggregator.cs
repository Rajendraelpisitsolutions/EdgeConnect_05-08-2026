// ============================================================================
// File: Diagnostics/DiagnosticsEventAggregator.cs
// Purpose: Default implementation of IDiagnosticsEventAggregator.
//          Composes the per-route IRouteEventAggregator (M.1c.1) with
//          IConfigurationManager's audit log, applies filtering server-
//          side, and projects audit entries into the same wire-shape
//          DiagnosticsEventDto the route aggregator produces.
//
//          Stateless beyond its three dependencies — safe to register
//          as a singleton.
//
//          Notes on GatewayId stamping: the aggregator leaves
//          DiagnosticsEventDto.GatewayId null. The API endpoint
//          DiagnosticsApi.GetEvents reads the gateway id from the
//          configuration once per request and stamps every returned
//          event before responding. Keeps the aggregator pure (no
//          dependency on configuration identity); concentrates the
//          identity attribution in the request/response boundary
//          where IConfigurationManager already lives.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Management.Contracts;

// Alias to disambiguate from Microsoft.Extensions.Configuration.IConfigurationManager,
// which is brought into scope implicitly by the ASP.NET Core Web SDK global usings.
using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>
/// Default <see cref="IDiagnosticsEventAggregator"/> backed by Core's
/// diagnostics surface (for per-route events via
/// <see cref="IRouteEventAggregator"/>) and the configuration manager
/// (for audit entries + chain verification).
/// </summary>
public sealed class DiagnosticsEventAggregator : IDiagnosticsEventAggregator
{
    private readonly IDiagnosticsService _diagnostics;
    private readonly IRouteEventAggregator _routeAggregator;
    private readonly IConfigurationManager _configManager;
    private readonly IGatewayStartupEventStore _startupEvents;

    /// <summary>Construct the aggregator with its four dependencies.</summary>
    public DiagnosticsEventAggregator(
        IDiagnosticsService diagnostics,
        IRouteEventAggregator routeAggregator,
        IConfigurationManager configManager,
        IGatewayStartupEventStore startupEvents)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(routeAggregator);
        ArgumentNullException.ThrowIfNull(configManager);
        ArgumentNullException.ThrowIfNull(startupEvents);
        _diagnostics = diagnostics;
        _routeAggregator = routeAggregator;
        _configManager = configManager;
        _startupEvents = startupEvents;
    }

    /// <inheritdoc />
    public async Task<DiagnosticsEventsResponse> GetRecentEventsAsync(
        DiagnosticsEventFilter filter,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var candidates = new List<DiagnosticsEventDto>(capacity: 256);

        // (1) Per-route events. Pull route-scoped events from the M.1c.1
        // aggregator. When the caller already pinned a route, we only
        // pull that one — saves walking every route's three ring buffers
        // for nothing.
        if (filter.RouteId is { Length: > 0 } pinnedRouteId)
        {
            candidates.AddRange(_routeAggregator.GetRecentRouteEvents(pinnedRouteId, DiagnosticsEventFilter.MaxLimit));
        }
        else
        {
            foreach (var routeId in _diagnostics.GetKnownRoutes())
            {
                candidates.AddRange(_routeAggregator.GetRecentRouteEvents(routeId, DiagnosticsEventFilter.MaxLimit));
            }
        }

        // (2) Configuration audit entries. Gateway-scoped (RouteId null),
        // so we skip them when the caller pinned a specific route — they
        // can't match anyway.
        if (filter.RouteId is null)
        {
            try
            {
                await foreach (var entry in _configManager.GetAuditLogAsync(verifyChain: false, ct).ConfigureAwait(false))
                {
                    candidates.Add(MapAuditEntry(entry));
                }
            }
            catch (ConfigurationException)
            {
                // Audit log corrupt or missing — don't fail the entire
                // events endpoint over it. The audit-chain endpoint
                // surfaces this condition cleanly. Leaving the catch
                // silent here is intentional; the chain banner is the
                // operator-facing signal for "your audit log is broken".
            }
        }

        // (3) Gateway-startup events (M.2b.3.1). Gateway-scoped boot-time
        // signals (RouteId null), so same RouteId-filter skip as audit
        // entries. First consumer is FOCAS2 demo-mode activation; future
        // consumers append additional boot-time signals to the same store.
        if (filter.RouteId is null)
        {
            foreach (var startupEvent in _startupEvents.GetAll())
            {
                candidates.Add(MapStartupEvent(startupEvent));
            }
        }

        var totalCandidates = (long)candidates.Count;

        // (3) Apply remaining filters (RouteId already applied at pull-time).
        IEnumerable<DiagnosticsEventDto> filtered = candidates;
        if (filter.EventCodes is { Count: > 0 } codes)
        {
            var codeSet = new HashSet<string>(codes, StringComparer.Ordinal);
            filtered = filtered.Where(e => codeSet.Contains(e.EventCode));
        }
        if (filter.Severity is { } sev)
        {
            filtered = filtered.Where(e => e.Severity == sev);
        }
        if (filter.SinceUtc is { } since)
        {
            filtered = filtered.Where(e => e.OccurredAtUtc >= since);
        }

        // (4) Sort desc by time, cap at the caller's effective limit.
        var matched = filtered
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(filter.EffectiveLimit)
            .ToList();

        // (5) Retention metadata — earliest entry we observed in the
        // pre-filter merged set. Operators read this as "history goes
        // back to T0"; if T0 is newer than their query window, older
        // events did NOT rotate out within the window.
        DateTime? retainedSince = candidates.Count > 0
            ? candidates.Min(e => e.OccurredAtUtc)
            : null;

        return new DiagnosticsEventsResponse
        {
            Events = matched,
            RetainedSinceUtc = retainedSince,
            ApproximateTotalEvents = totalCandidates,
        };
    }

    /// <inheritdoc />
    public async Task<AuditChainStatus> VerifyAuditChainAsync(CancellationToken ct)
    {
        long count = 0;
        var checkedAt = DateTime.UtcNow;
        try
        {
            await foreach (var _ in _configManager.GetAuditLogAsync(verifyChain: true, ct).ConfigureAwait(false))
            {
                count++;
            }
            return new AuditChainStatus
            {
                Verified = true,
                EntriesChecked = count,
                CheckedAtUtc = checkedAt,
            };
        }
        catch (ConfigurationException ex)
        {
            // Either corrupt JSON or broken hash chain. The exception
            // message already carries the entry index + the expected-vs-
            // found hash; we surface that verbatim so the banner shows
            // the same diagnostic an admin would see on the CLI.
            return new AuditChainStatus
            {
                Verified = false,
                EntriesChecked = count,
                FailureReason = ex.Message,
                CheckedAtUtc = checkedAt,
            };
        }
    }

    /// <summary>
    /// Project a configuration audit entry into the wire-shape
    /// DiagnosticsEventDto. Maps the four-value
    /// <see cref="ConfigurationAuditAction"/> onto the corresponding
    /// CONFIG.* event codes with operationally-appropriate severities
    /// (RolledBack rendered as Warning, the rest as Info).
    /// </summary>
    internal static DiagnosticsEventDto MapAuditEntry(ConfigurationAuditEntry entry)
    {
        var (code, severity) = entry.Action switch
        {
            ConfigurationAuditAction.Applied =>
                (DiagnosticsEventCodes.ConfigApplied, DiagnosticsSeverity.Info),
            ConfigurationAuditAction.RolledBack =>
                (DiagnosticsEventCodes.ConfigRolledBack, DiagnosticsSeverity.Warning),
            ConfigurationAuditAction.DraftCreated =>
                (DiagnosticsEventCodes.ConfigDraftCreated, DiagnosticsSeverity.Info),
            ConfigurationAuditAction.DraftDiscarded =>
                (DiagnosticsEventCodes.ConfigDraftDiscarded, DiagnosticsSeverity.Info),
            // Defensive fallback if Core adds a new ConfigurationAuditAction
            // value before the catalog catches up. Better to mis-bucket once
            // than to throw on a perfectly valid audit entry.
            _ => (DiagnosticsEventCodes.ConfigApplied, DiagnosticsSeverity.Info),
        };

        return new DiagnosticsEventDto
        {
            OccurredAtUtc = entry.Timestamp,
            EventCode = code,
            Severity = severity,
            Summary = entry.Summary,
            RouteId = null,        // gateway-scoped event
            Actor = entry.Actor,
            // GatewayId is stamped by the API endpoint, not here.
        };
    }

    /// <summary>
    /// Project a gateway-startup event (M.2b.3.1) into the wire-shape
    /// <see cref="DiagnosticsEventDto"/>. Maps known boot-time
    /// <see cref="GatewayStartupEvent.EventCode"/> values to the management
    /// catalog; unknown codes fall through with the raw code (defensive —
    /// the store can grow new event codes without an aggregator update).
    /// Severity strings map: "Critical"/"Error" → Error; "Warning" → Warning;
    /// anything else → Info.
    /// </summary>
    internal static DiagnosticsEventDto MapStartupEvent(GatewayStartupEvent ev)
    {
        // EventCode catalog mapping. Today there's only one known code,
        // but the switch keeps the integration point obvious as more
        // boot-time signals are added.
        var code = ev.EventCode switch
        {
            "focas2.fake-mode.activated" => DiagnosticsEventCodes.Focas2FakeModeActivated,
            _ => ev.EventCode,
        };

        var severity = ev.Severity switch
        {
            "Critical" or "Error" => DiagnosticsSeverity.Error,
            "Warning" => DiagnosticsSeverity.Warning,
            _ => DiagnosticsSeverity.Info,
        };

        return new DiagnosticsEventDto
        {
            OccurredAtUtc = ev.EmittedAtUtc,
            EventCode = code,
            Severity = severity,
            Summary = ev.Message,
            RouteId = null,    // gateway-scoped
            Actor = "system",  // boot-time, not operator-triggered
        };
    }
}
