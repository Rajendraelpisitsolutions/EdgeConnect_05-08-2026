// ============================================================================
// File: BrotherPollSession.cs
// Purpose: Per-poll mutable signal aggregator. Each of the six endpoint
//          collectors populates its portion; the adapter (step 7) reads the
//          assembled session, applies the v3 §6.2 precedence chain, and
//          emits canonical points via CanonicalDataPointFactory.
//
//          Collectors NEVER construct CanonicalDataPoints directly —
//          v3.1 §B.1 atomic-batch lock. Collectors are pure functions:
//          (responseText, session) → session-mutated.
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §6 catalog,
//            §6.2 precedence chain; v3.1 §B.1 atomic batch
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Mutable signal aggregator populated by the six endpoint collectors over
/// the course of one poll cycle. The adapter constructs a fresh session
/// per cycle, runs the collectors, then post-processes to emit canonical
/// points.
/// </summary>
internal sealed class BrotherPollSession
{
    // ── MachineInfo (/HTTPD_MCNINFO) ──────────────────────────────────────
    public bool MachineInfoEndpointFailed { get; set; }
    public string? Hostname { get; set; }
    public string? Model { get; set; }
    public int? StatusCode { get; set; }

    // ── CycleTime (/MNTP_CYCLETIME) ───────────────────────────────────────
    public bool CycleTimeEndpointFailed { get; set; }
    public string? Program { get; set; }
    public double? CycleSeconds { get; set; }
    public double? CuttingSeconds { get; set; }
    public double? OperationHours { get; set; }
    public double? PowerOnHours { get; set; }
    public int? EndCounter { get; set; }
    public int? CuttingRatioPercent { get; set; }

    // ── WorkCounters (/MNTP_WKCNTR) ───────────────────────────────────────
    public bool WorkCountersEndpointFailed { get; set; }
    public long? PartsCount { get; set; }
    /// <summary>Counter index (1..4) → (Count, Target). Sparse — only non-zero entries appear.</summary>
    public IDictionary<int, CounterEntry> Counters { get; } = new Dictionary<int, CounterEntry>();

    // ── AtcTools (/ATC_TOOLS) ─────────────────────────────────────────────
    public bool AtcToolsEndpointFailed { get; set; }
    public int? ActiveToolNumber { get; set; }
    public IList<ToolMagazineEntry> Magazine { get; } = new List<ToolMagazineEntry>();

    // ── Alarms (/ALARM_CURALMLIST) ────────────────────────────────────────
    public bool AlarmsEndpointFailed { get; set; }
    /// <summary>Active alarms after informational + maintenance filtering.</summary>
    public IList<AlarmEntry> ActiveAlarms { get; } = new List<AlarmEntry>();
    /// <summary>Message from an informational alarm (e.g. 501 standby). Null if none.</summary>
    public string? InformationalAlarmMessage { get; set; }
    /// <summary>Messages filtered out by maintenance keywords (GREASING, FILTER, etc.).</summary>
    public IList<string> MaintenanceAlarmMessages { get; } = new List<string>();

    // ── MaintenanceNotices (/MNTP_MAINTNOTICE) ────────────────────────────
    public bool MaintenanceNoticesEndpointFailed { get; set; }
    public IList<MaintenanceNoticeEntry> Notices { get; } = new List<MaintenanceNoticeEntry>();
}

/// <summary>Counter value pair (sparse — only emitted when at least one is non-zero).</summary>
internal sealed record CounterEntry(int? Count, int? Target);

/// <summary>One row from the ATC magazine response.</summary>
internal sealed record ToolMagazineEntry(
    int Slot,
    int ToolNumber,
    double Length,
    double Radius,
    bool IsActive,
    string? Name,
    string? Type,
    string? Life);

/// <summary>One row from the parsed (non-informational, non-maintenance) alarm list.</summary>
internal sealed record AlarmEntry(int Number, string Message);

/// <summary>One row from the maintenance-notice response.</summary>
internal sealed record MaintenanceNoticeEntry(
    string Description,
    string Condition,
    string Status,
    string Current,
    string Limit,
    string Notified,
    string State,
    int? DuePercent);
