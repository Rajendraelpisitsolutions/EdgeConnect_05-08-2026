// ============================================================================
// File: Checklist/ChecklistEvaluator.cs
// Purpose: Default IChecklistEvaluator. One private method per check,
//          eight categories' worth of operational signals composed
//          from existing M.1c.* surfaces.
//
//          Per CLAUDE.md locked rule #1 the work lives in Management,
//          not Core — severity bucketing, threshold tuning, and
//          operator-facing detail strings are presentation concerns.
//
//          Structured logging at the per-check level provides support
//          traceability without leaking source-method names into the
//          wire DTO (see M.1d plan + review).
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1d
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Diagnostics;
using Microsoft.Extensions.Logging;

// Alias to disambiguate from Microsoft.Extensions.Configuration.IConfigurationManager,
// which is brought into scope implicitly by the ASP.NET Core Web SDK global usings.
using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;

namespace ElpisEdgeConnect.Management.Checklist;

/// <summary>
/// Default <see cref="IChecklistEvaluator"/>. Pulls live state from
/// <see cref="IDiagnosticsService"/>, the diagnostics event aggregator,
/// the configuration manager, and the license manager; emits one
/// <see cref="ChecklistItemDto"/> per check.
/// </summary>
/// <remarks>
/// <para><strong>Catalog categories</strong> emitted today:</para>
/// <list type="bullet">
///   <item><c>Gateway</c>        — identity, route count, license presence</item>
///   <item><c>Data flow</c>      — route health, source data observed, sink health, backpressure</item>
///   <item><c>Recoverability</c> — audit chain integrity, audit activity</item>
///   <item><c>Connectivity</c>   — OPC UA client sessions on session-tracking sinks</item>
/// </list>
/// <para><strong>Reserved future categories</strong> — not implemented today;
/// listed so the taxonomy is documented before the first contributor
/// adds checks in these areas:</para>
/// <list type="bullet">
///   <item><c>Security</c>    — TLS cert expiry, auth-failure rate, hashed-cred age
///                              (lands when Phase-4 cert rotation ships).</item>
///   <item><c>Performance</c> — pipeline-step p99 latency, buffer depth trend,
///                              sink backlog age (lands when long-haul soak surface
///                              is built).</item>
///   <item><c>Redundancy</c>  — HA pair health, failover sync state, replication lag
///                              (lands when HA architecture is designed).</item>
/// </list>
/// <para>Operators viewing /checklist today see only the four implemented
/// sections. The reservation just prevents future category-naming churn.</para>
/// </remarks>
public sealed class ChecklistEvaluator : IChecklistEvaluator
{
    /// <summary>
    /// Grace period for DF-2 — sources need this long to start observing
    /// data before "0 points" counts as a Fail. 30s is comfortable for
    /// any sane source poll interval (typical Modbus scan 200ms, FOCAS2
    /// 100ms, MTConnect 1s).
    /// </summary>
    public static readonly TimeSpan DataFlowGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Look-back window for DF-4 (backpressure drops). One hour is the
    /// commissioning-relevant window — sustained stability rather than
    /// instantaneous health.
    /// </summary>
    public static readonly TimeSpan BackpressureLookback = TimeSpan.FromHours(1);

    /// <summary>
    /// Process start time captured once at class load. Subtracting from
    /// <see cref="DateTime.UtcNow"/> gives gateway uptime — used to drive
    /// the DF-2 grace period without dragging time-provider abstractions
    /// into the evaluator constructor.
    /// </summary>
    private static readonly DateTime ProcessStartUtc =
        Process.GetCurrentProcess().StartTime.ToUniversalTime();

    private readonly IDiagnosticsService _diagnostics;
    private readonly IDiagnosticsEventAggregator _eventAggregator;
    private readonly IConfigurationManager _configManager;
    private readonly ILicenseManager _licenseManager;
    private readonly ILogger<ChecklistEvaluator> _logger;

    /// <summary>Construct an evaluator with its four dependencies.</summary>
    public ChecklistEvaluator(
        IDiagnosticsService diagnostics,
        IDiagnosticsEventAggregator eventAggregator,
        IConfigurationManager configManager,
        ILicenseManager licenseManager,
        ILogger<ChecklistEvaluator> logger)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(eventAggregator);
        ArgumentNullException.ThrowIfNull(configManager);
        ArgumentNullException.ThrowIfNull(licenseManager);
        ArgumentNullException.ThrowIfNull(logger);
        _diagnostics = diagnostics;
        _eventAggregator = eventAggregator;
        _configManager = configManager;
        _licenseManager = licenseManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChecklistResponse> EvaluateAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var checkedAt = DateTime.UtcNow;

        // Pull shared snapshot inputs once. Each check then queries the
        // bits it needs without re-walking the diagnostics dictionaries.
        var routes = _diagnostics.GetAllRouteSnapshots();

        ElpisEdgeConnect.Core.Configuration.GatewayConfiguration? config = null;
        try
        {
            config = await _configManager.GetCurrentAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Uninitialised gateway — config is null, the GW-1 check
            // will Fail with a clear message rather than the whole
            // evaluation 500-ing.
        }

        var items = new List<ChecklistItemDto>(capacity: 16);

        items.Add(SafeEvaluate("GW-1", () => EvaluateGW1Identity(config)));
        items.Add(SafeEvaluate("GW-2", () => EvaluateGW2RoutesConfigured(routes)));
        items.Add(SafeEvaluate("GW-3", EvaluateGW3License));

        items.Add(SafeEvaluate("DF-1", () => EvaluateDF1RoutesHealthy(routes)));
        items.Add(SafeEvaluate("DF-2", () => EvaluateDF2SourcesObservingData(routes)));
        items.Add(SafeEvaluate("DF-3", () => EvaluateDF3SinksHealthy(routes)));
        items.Add(await SafeEvaluateAsync("DF-4", () => EvaluateDF4NoRecentBackpressureAsync(ct)).ConfigureAwait(false));

        items.Add(await SafeEvaluateAsync("RC-1", () => EvaluateRC1AuditChainAsync(ct)).ConfigureAwait(false));
        items.Add(await SafeEvaluateAsync("RC-2", () => EvaluateRC2AuditActivityAsync(ct)).ConfigureAwait(false));

        items.Add(SafeEvaluate("CN-1", () => EvaluateCN1OpcUaSessions(routes)));

        sw.Stop();
        var summary = BuildSummary(items);
        return new ChecklistResponse
        {
            Items = items,
            Summary = summary,
            CheckedAtUtc = checkedAt,
            EvaluationDurationMs = (int)sw.ElapsedMilliseconds,
            GatewayId = config?.Gateway.GatewayId,
        };
    }

    // ─── Gateway category ────────────────────────────────────────────────

    /// <summary>
    /// GW-1: gateway has a stable identity.
    /// Source: <c>IConfigurationManager.GetCurrentAsync().Gateway.GatewayId</c>
    /// </summary>
    private static ChecklistItemDto EvaluateGW1Identity(
        ElpisEdgeConnect.Core.Configuration.GatewayConfiguration? config)
    {
        const string id = "GW-1";
        const string category = "Gateway";
        const string title = "Gateway has a stable identity";

        if (config is null)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Fail,
                Detail = "Gateway configuration has not been loaded. " +
                         "Verify gateway.json is present at the configured data root.",
            };
        }

        var gid = config.Gateway.GatewayId;
        if (string.IsNullOrWhiteSpace(gid) ||
            gid.Equals("gateway", StringComparison.OrdinalIgnoreCase) ||
            gid.Equals("unset", StringComparison.OrdinalIgnoreCase))
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Fail,
                Detail = $"Gateway id is unset or default ('{gid ?? "<null>"}'). " +
                         "Set a stable gatewayId in gateway.json before deployment.",
            };
        }

        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Pass,
            Detail = gid,
        };
    }

    /// <summary>
    /// GW-2: at least one route configured.
    /// Source: <c>IDiagnosticsService.GetAllRouteSnapshots()</c>
    /// </summary>
    private static ChecklistItemDto EvaluateGW2RoutesConfigured(IReadOnlyList<RouteHealthSnapshot> routes)
    {
        const string id = "GW-2";
        const string category = "Gateway";
        const string title = "At least one route configured";

        if (routes.Count == 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Fail,
                Detail = "No routes configured. Add at least one route in gateway.json before deployment.",
                Link = "/routes",
            };
        }

        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Pass,
            Detail = $"{routes.Count} route{(routes.Count == 1 ? "" : "s")}",
            Link = "/routes",
        };
    }

    /// <summary>
    /// GW-3: license module enabled.
    /// Source: <c>ILicenseManager.Current</c>
    /// </summary>
    private ChecklistItemDto EvaluateGW3License()
    {
        const string id = "GW-3";
        const string category = "Gateway";
        const string title = "License module enabled";

        var current = _licenseManager.Current;
        if (current is null)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.NotApplicable,
                Detail = "Running permissive (no license file loaded). " +
                         "Provision a license before production deployment.",
            };
        }

        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Pass,
            Detail = $"License loaded — status {_licenseManager.Status}",
        };
    }

    // ─── Data flow category ──────────────────────────────────────────────

    /// <summary>
    /// DF-1: every route in <c>Running</c> or <c>Draining</c> state.
    /// Source: <c>RouteHealthSnapshot.State</c>
    /// </summary>
    private static ChecklistItemDto EvaluateDF1RoutesHealthy(IReadOnlyList<RouteHealthSnapshot> routes)
    {
        const string id = "DF-1";
        const string category = "Data flow";
        const string title = "All routes in healthy state";

        if (routes.Count == 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.NotApplicable,
                Detail = "No routes configured (covered by GW-2).",
            };
        }

        var unhealthy = routes
            .Where(r => r.State != RouteState.Running && r.State != RouteState.Draining)
            .ToList();

        if (unhealthy.Count == 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Pass,
                Detail = $"{routes.Count} of {routes.Count} healthy",
                Link = "/routes",
            };
        }

        var names = string.Join(", ", unhealthy.Select(r => $"{r.RouteId} ({r.State})"));
        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Fail,
            Detail = $"{unhealthy.Count} route{(unhealthy.Count == 1 ? "" : "s")} not in Running/Draining: {names}",
            Link = "/routes",
        };
    }

    /// <summary>
    /// DF-2: every source has observed at least one data point.
    /// Pending during the grace period after gateway start.
    /// Source: <c>RouteHealthSnapshot.Source.PointsObserved</c>
    /// </summary>
    private static ChecklistItemDto EvaluateDF2SourcesObservingData(IReadOnlyList<RouteHealthSnapshot> routes)
    {
        const string id = "DF-2";
        const string category = "Data flow";
        const string title = "Every source has observed data";

        var sources = routes.Where(r => r.Source is not null).ToList();
        if (sources.Count == 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.NotApplicable,
                Detail = "No source-bearing routes configured.",
            };
        }

        var idle = sources.Where(r => (r.Source?.PointsObserved ?? 0) == 0).ToList();
        if (idle.Count == 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Pass,
                Detail = string.Join("; ", sources.Select(r =>
                    $"{r.Source!.SourceInstanceId}: {r.Source.PointsObserved:N0} points")),
                Link = "/sources",
            };
        }

        var uptime = DateTime.UtcNow - ProcessStartUtc;
        if (uptime < DataFlowGracePeriod)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Pending,
                Detail = $"Gateway uptime {(int)uptime.TotalSeconds}s — within the {(int)DataFlowGracePeriod.TotalSeconds}s grace period. " +
                         "Sources may still be polling for the first data point.",
                Link = "/sources",
            };
        }

        var names = string.Join(", ", idle.Select(r => r.Source!.SourceInstanceId));
        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Fail,
            Detail = $"{idle.Count} source{(idle.Count == 1 ? "" : "s")} have not observed any data after " +
                     $"{(int)uptime.TotalSeconds}s: {names}",
            Link = "/sources",
        };
    }

    /// <summary>
    /// DF-3: every sink reports healthy (not degraded, Running adapter state).
    /// Source: <c>RouteHealthSnapshot.Sinks[*].IsDegraded</c> +
    ///         <c>RouteHealthSnapshot.Sinks[*].AdapterState</c>
    /// </summary>
    private static ChecklistItemDto EvaluateDF3SinksHealthy(IReadOnlyList<RouteHealthSnapshot> routes)
    {
        const string id = "DF-3";
        const string category = "Data flow";
        const string title = "Every sink reports healthy";

        var allSinks = routes.SelectMany(r => r.Sinks).ToList();
        if (allSinks.Count == 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.NotApplicable,
                Detail = "No sinks configured.",
            };
        }

        var degraded = allSinks.Where(s =>
                s.IsDegraded ||
                (s.AdapterState is { } st && st != AdapterState.Running))
            .ToList();

        if (degraded.Count == 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Pass,
                Detail = $"{allSinks.Count} of {allSinks.Count} sinks Running",
                Link = "/sinks",
            };
        }

        var names = string.Join(", ", degraded.Select(s =>
        {
            var stateLabel = s.IsDegraded ? "degraded" : (s.AdapterState?.ToString() ?? "unknown");
            return $"{s.SinkInstanceId} ({stateLabel})";
        }));
        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Fail,
            Detail = $"{degraded.Count} sink{(degraded.Count == 1 ? "" : "s")} not healthy: {names}",
            Link = "/sinks",
        };
    }

    /// <summary>
    /// DF-4: no backpressure drops in the last hour.
    /// Source: <c>IDiagnosticsEventAggregator.GetRecentEventsAsync</c>
    ///         filtered to BUFFER.BACKPRESSURE_DROPPED + last 1h.
    /// </summary>
    private async Task<ChecklistItemDto> EvaluateDF4NoRecentBackpressureAsync(CancellationToken ct)
    {
        const string id = "DF-4";
        const string category = "Data flow";
        const string title = "No backpressure drops in last hour";

        var since = DateTime.UtcNow - BackpressureLookback;
        var filter = new DiagnosticsEventFilter
        {
            EventCodes = new[] { DiagnosticsEventCodes.BackpressureDropped },
            SinceUtc = since,
            Limit = DiagnosticsEventFilter.MaxLimit,
        };
        var response = await _eventAggregator.GetRecentEventsAsync(filter, ct).ConfigureAwait(false);
        if (response.Events.Count == 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Pass,
                Detail = $"No backpressure drops since {since.ToLocalTime():g}",
            };
        }

        var totalDropped = response.Events.Sum(e => e.DroppedCount ?? 0);
        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Fail,
            Detail = $"{response.Events.Count} drop event{(response.Events.Count == 1 ? "" : "s")} " +
                     $"totalling {totalDropped:N0} points in the last hour — investigate sink throughput or buffer sizing.",
            Link = "/diagnostics",
        };
    }

    // ─── Recoverability category ────────────────────────────────────────

    /// <summary>
    /// RC-1: configuration audit hash chain is intact.
    /// Source: <c>IDiagnosticsEventAggregator.VerifyAuditChainAsync</c>
    /// </summary>
    private async Task<ChecklistItemDto> EvaluateRC1AuditChainAsync(CancellationToken ct)
    {
        const string id = "RC-1";
        const string category = "Recoverability";
        const string title = "Audit chain verified intact";

        var status = await _eventAggregator.VerifyAuditChainAsync(ct).ConfigureAwait(false);
        if (status.Verified)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Pass,
                Detail = $"Verified {status.EntriesChecked} entr{(status.EntriesChecked == 1 ? "y" : "ies")}",
            };
        }

        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Fail,
            Detail = $"Chain broken after {status.EntriesChecked} entries: {status.FailureReason ?? "see diagnostics page"}",
            Link = "/diagnostics",
        };
    }

    /// <summary>
    /// RC-2: at least one configuration applied via API.
    /// Source: <c>IDiagnosticsEventAggregator.VerifyAuditChainAsync</c> (entry count).
    /// </summary>
    /// <remarks>
    /// Empty audit log is Pending, not Fail — fresh gateways legitimately
    /// have no API-applied configs yet, and showing red on day-zero is bad UX.
    /// </remarks>
    private async Task<ChecklistItemDto> EvaluateRC2AuditActivityAsync(CancellationToken ct)
    {
        const string id = "RC-2";
        const string category = "Recoverability";
        const string title = "Audit log has activity";

        var status = await _eventAggregator.VerifyAuditChainAsync(ct).ConfigureAwait(false);
        if (status.EntriesChecked > 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Pass,
                Detail = $"{status.EntriesChecked} audit entr{(status.EntriesChecked == 1 ? "y" : "ies")} recorded",
                Link = "/diagnostics",
            };
        }

        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Pending,
            Detail = "No configuration has been applied via API yet. " +
                     "The audit trail will start once the M.2a draft/apply pipeline is used.",
            Link = "/diagnostics",
        };
    }

    // ─── Connectivity category ──────────────────────────────────────────

    /// <summary>
    /// CN-1: at least one OPC UA client connected to every session-tracking sink.
    /// Source: <c>SinkHealthSnapshot.ActiveSessions</c> (null when sink doesn't track).
    /// </summary>
    private static ChecklistItemDto EvaluateCN1OpcUaSessions(IReadOnlyList<RouteHealthSnapshot> routes)
    {
        const string id = "CN-1";
        const string category = "Connectivity";
        const string title = "OPC UA clients connected";

        var sessionTrackingSinks = routes
            .SelectMany(r => r.Sinks)
            .Where(s => s.ActiveSessions is not null)
            .ToList();

        if (sessionTrackingSinks.Count == 0)
        {
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.NotApplicable,
                Detail = "No session-tracking sinks (e.g. opcua-server) configured.",
            };
        }

        var empty = sessionTrackingSinks.Where(s => (s.ActiveSessions?.Count ?? 0) == 0).ToList();
        if (empty.Count == 0)
        {
            var totalSessions = sessionTrackingSinks.Sum(s => s.ActiveSessions!.Count);
            return new ChecklistItemDto
            {
                Id = id, Category = category, Title = title,
                Status = ChecklistStatus.Pass,
                Detail = $"{totalSessions} active session{(totalSessions == 1 ? "" : "s")} across " +
                         $"{sessionTrackingSinks.Count} sink{(sessionTrackingSinks.Count == 1 ? "" : "s")}",
                Link = "/sinks",
            };
        }

        var names = string.Join(", ", empty.Select(s => s.SinkInstanceId));
        return new ChecklistItemDto
        {
            Id = id, Category = category, Title = title,
            Status = ChecklistStatus.Fail,
            Detail = $"{empty.Count} session-tracking sink{(empty.Count == 1 ? "" : "s")} have no clients connected: {names}. " +
                     "Verify the OPC UA endpoint URL and security policy match what your SCADA expects.",
            Link = "/sinks",
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Wrap a sync check so an unexpected exception becomes a Fail with
    /// the message in <c>Detail</c>, not a 500 from the API endpoint.
    /// Logs the exception via the standard logger so support sees the
    /// underlying error.
    /// </summary>
    private ChecklistItemDto SafeEvaluate(string id, Func<ChecklistItemDto> check)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var result = check();
            sw.Stop();
            LogCheck(result, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Checklist check {CheckId} threw — surfacing as Fail.", id);
            return new ChecklistItemDto
            {
                Id = id, Category = "Unknown", Title = id,
                Status = ChecklistStatus.Fail,
                Detail = $"Check threw: {ex.GetType().Name}: {ex.Message}",
            };
        }
    }

    /// <summary>Async overload of <see cref="SafeEvaluate"/>.</summary>
    private async Task<ChecklistItemDto> SafeEvaluateAsync(string id, Func<Task<ChecklistItemDto>> check)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var result = await check().ConfigureAwait(false);
            sw.Stop();
            LogCheck(result, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Checklist check {CheckId} threw — surfacing as Fail.", id);
            return new ChecklistItemDto
            {
                Id = id, Category = "Unknown", Title = id,
                Status = ChecklistStatus.Fail,
                Detail = $"Check threw: {ex.GetType().Name}: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Per-check structured log entry. Gives support engineers
    /// id/status/duration without leaking source-method names into
    /// the wire DTO.
    /// </summary>
    private void LogCheck(ChecklistItemDto item, long elapsedMs)
    {
        _logger.LogDebug(
            "Checklist {CheckId} ({Category}) evaluated to {Status} in {DurationMs}ms.",
            item.Id, item.Category, item.Status, elapsedMs);
    }

    /// <summary>Build the roll-up summary from a finalized item list.</summary>
    internal static ChecklistSummary BuildSummary(IReadOnlyList<ChecklistItemDto> items)
    {
        var pass = 0;
        var fail = 0;
        var na = 0;
        var pending = 0;
        foreach (var item in items)
        {
            switch (item.Status)
            {
                case ChecklistStatus.Pass: pass++; break;
                case ChecklistStatus.Fail: fail++; break;
                case ChecklistStatus.NotApplicable: na++; break;
                case ChecklistStatus.Pending: pending++; break;
            }
        }
        return new ChecklistSummary
        {
            Total = items.Count,
            Pass = pass,
            Fail = fail,
            NotApplicable = na,
            Pending = pending,
        };
    }
}
