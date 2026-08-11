// ============================================================================
// File: Parity/LegacyCanonicalMapper.cs
// Purpose: Test-only mapping from the legacy CncMachineData DTO (produced
//          by BrotherHttpDataSource.CollectDataAsync) into the v3 §6
//          canonical-catalog set of CanonicalDataPoints. The output is
//          consumed by ParityTests as the "oracle" against which the new
//          BrotherHttpSourceAdapter is compared.
//
// IMPORTANT (v3 §5 + §B.5 + §C.3 — user's concerns):
//   This mapper is TEST-ONLY. The production project must NOT reference
//   the legacy ElpisEdgeConnect.Models types — only this test code does.
//   The mapper reads the legacy DTO and emits canonical points
//   INDEPENDENTLY from the new adapter's BuildPoints logic, so any drift
//   between protocol semantics and a legacy parser quirk surfaces as a
//   parity-test failure rather than being silently aligned.
//
// Documented divergences (legacy DTO is lossy on these axes):
//   * MachineInfo/StatusCode — legacy parses raw 0-5 but discards it
//     before storing in the DTO. New adapter emits it (catalog-defined,
//     new-canonical extension). Oracle SKIPS — subset-parity instead of
//     strict equality.
//   * Tools/Magazine/{slot}/* — legacy stores tool numbers in
//     ToolInfo.Offsets[] (list order) but NOT the parsed slot numbers.
//     Oracle SKIPS slot-keyed magazine paths.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Models;     // legacy DTO (test project only)

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests.Parity;

internal static class LegacyCanonicalMapper
{
    /// <summary>
    /// Map a legacy <see cref="CncMachineData"/> snapshot (produced by
    /// <c>BrotherHttpDataSource.CollectDataAsync</c>) into a canonical
    /// data-point set per v3 §6 catalog. Returns a TagPath → Value
    /// dictionary suitable for parity comparison against the new adapter.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Map(CncMachineData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);

        // ── MachineInfo (note: StatusCode omitted — legacy DTO discards it) ──
        if (data.SystemInfo?.Version is { Length: > 0 } hostname)
        {
            output[BrotherTagMap.MachineInfoHostname.TagPath] = hostname;
        }
        if (data.SystemInfo?.Series is { Length: > 0 } seriesRaw)
        {
            // Legacy stores "Brother {model}"; strip prefix to recover "model".
            var model = seriesRaw.StartsWith("Brother ", StringComparison.Ordinal)
                ? seriesRaw["Brother ".Length..]
                : seriesRaw;
            if (model.Length > 0)
            {
                output[BrotherTagMap.MachineInfoModel.TagPath] = model;
            }
        }

        // ── Status (legacy *_path1_CNC fields already reflect precedence chain) ──
        if (data.CncState_path1_CNC is { Length: > 0 } state)
        {
            output[BrotherTagMap.StatusState.TagPath] = state;
        }
        if (data.RunningStatus is { Length: > 0 } running)
        {
            output[BrotherTagMap.StatusRunning.TagPath] = running;
        }
        if (data.Mode_path1_CNC is { Length: > 0 } mode)
        {
            output[BrotherTagMap.StatusMode.TagPath] = mode;
        }
        if (data.AutoMode is { Length: > 0 } autoMode)
        {
            output[BrotherTagMap.StatusAutoMode.TagPath] = autoMode;
        }
        // Legacy stores nullable bool; new adapter always emits (defaults false).
        output[BrotherTagMap.StatusEmergencyStop.TagPath] = data.EmergencyStop ?? false;
        if (!string.IsNullOrEmpty(data.CncWarning_path1_CNC))
        {
            output[BrotherTagMap.StatusWarning.TagPath] = data.CncWarning_path1_CNC;
        }

        // ── Program ──
        if (data.MainProgram is { Length: > 0 } prog)
        {
            output[BrotherTagMap.ProgramActive.TagPath] = prog;
        }

        // ── CycleTime ──
        if (data.CycleTimeSeconds is { } cycSec)
        {
            output[BrotherTagMap.CycleTimeCycle.TagPath] = cycSec;
        }
        if (TryRead<double>(data.AdditionalData, "CuttingTimeSeconds", out var cuttingSec))
        {
            output[BrotherTagMap.CycleTimeCutting.TagPath] = cuttingSec;
        }
        if (TryRead<double>(data.AdditionalData, "OperationTimeHours", out var opHours))
        {
            output[BrotherTagMap.CycleTimeOperation.TagPath] = opHours;
        }
        if (TryRead<double>(data.AdditionalData, "PowerOnTimeHours", out var poHours))
        {
            output[BrotherTagMap.CycleTimePowerOn.TagPath] = poHours;
        }
        if (TryRead<int>(data.AdditionalData, "OperationEndCounter", out var endCounter))
        {
            output[BrotherTagMap.CycleTimeEndCounter.TagPath] = endCounter;
        }
        if (TryRead<int>(data.AdditionalData, "CuttingRatioPercent", out var ratio))
        {
            output[BrotherTagMap.CycleTimeCuttingRatioPercent.TagPath] = ratio;
        }

        // ── Production ──
        if (data.PartsCount is { } parts)
        {
            output[BrotherTagMap.ProductionPartsCount.TagPath] = parts;
        }
        for (var n = 1; n <= 4; n++)
        {
            if (TryRead<int>(data.AdditionalData, $"Counter{n}.Count", out var c))
            {
                output[BrotherTagMap.ProductionCounterCount(n).TagPath] = c;
            }
            if (TryRead<int>(data.AdditionalData, $"Counter{n}.Target", out var t))
            {
                output[BrotherTagMap.ProductionCounterTarget(n).TagPath] = t;
            }
        }

        // ── Tools — ActiveNumber + tool-number-keyed name/type/life only.
        //    Slot-keyed magazine paths are skipped (legacy DTO is slot-lossy).
        if (data.ToolInfo?.CurrentToolNumber is { } active && active > 0)
        {
            output[BrotherTagMap.ToolsActiveNumber.TagPath] = active;
        }
        if (data.ToolInfo is { } toolInfo)
        {
            output[BrotherTagMap.ToolsMagazineSize.TagPath] = toolInfo.Offsets?.Count ?? 0;
        }

        // Enumerate Tool.{N}.* keys in AdditionalData to extract per-tool
        // metadata. The tool numbers are not known a priori; we discover
        // them from the key prefix.
        foreach (var (toolNo, suffix, value) in EnumerateToolMetadata(data.AdditionalData))
        {
            BrotherTagMapEntry? entry = suffix switch
            {
                "Name" => BrotherTagMap.ToolsToolName(toolNo),
                "Type" => BrotherTagMap.ToolsToolType(toolNo),
                "Life" => BrotherTagMap.ToolsToolLife(toolNo),
                _ => null,
            };
            if (entry is not null && value is string s && s.Length > 0)
            {
                output[entry.TagPath] = s;
            }
        }

        // ── Alarms ──
        output[BrotherTagMap.AlarmsActiveCount.TagPath] = data.ActiveAlarms?.Count ?? 0;
        if (data.ActiveAlarms is { } alarms)
        {
            for (var i = 0; i < alarms.Count; i++)
            {
                var a = alarms[i];
                output[BrotherTagMap.AlarmsActiveNumber(i).TagPath] = a.AlarmNumber;
                output[BrotherTagMap.AlarmsActiveType(i).TagPath] = a.AlarmType;
                output[BrotherTagMap.AlarmsActiveMessage(i).TagPath] = a.Message;
            }
        }

        // ── Maintenance ──
        if (TryRead<string>(data.AdditionalData, "MaintenanceWarning", out var maintWarning) &&
            !string.IsNullOrEmpty(maintWarning))
        {
            output[BrotherTagMap.MaintenanceWarning.TagPath] = maintWarning;
        }
        if (TryRead<int>(data.AdditionalData, "MaintenanceWarningCount", out var maintWarnCount))
        {
            output[BrotherTagMap.MaintenanceWarningCount.TagPath] = maintWarnCount;
        }
        if (TryRead<int>(data.AdditionalData, "MaintenanceNoticeCount", out var noticeCount))
        {
            output[BrotherTagMap.MaintenanceNoticeCount.TagPath] = noticeCount;
        }
        else
        {
            // Legacy doesn't write MaintenanceNoticeCount when zero; new adapter always emits.
            // For parity strict-subset semantics, we DON'T add a 0 entry when legacy omits.
        }
        if (TryRead<string>(data.AdditionalData, "MaintenanceDueSummary", out var dueSummary) &&
            !string.IsNullOrEmpty(dueSummary))
        {
            output[BrotherTagMap.MaintenanceDueSummary.TagPath] = dueSummary;
        }

        // Enumerate Maintenance.{idx}.* keys.
        foreach (var (idx, suffix, value) in EnumerateMaintenanceNoticeFields(data.AdditionalData))
        {
            BrotherTagMapEntry? entry = suffix switch
            {
                "Description" => BrotherTagMap.MaintenanceNoticeDescription(idx),
                "Condition" => BrotherTagMap.MaintenanceNoticeCondition(idx),
                "Status" => BrotherTagMap.MaintenanceNoticeStatus(idx),
                "Current" => BrotherTagMap.MaintenanceNoticeCurrent(idx),
                "Limit" => BrotherTagMap.MaintenanceNoticeLimit(idx),
                "State" => BrotherTagMap.MaintenanceNoticeState(idx),
                "DuePercent" => BrotherTagMap.MaintenanceNoticeDuePercent(idx),
                _ => null,
            };
            if (entry is null) continue;

            if (suffix == "DuePercent")
            {
                if (value is int d) output[entry.TagPath] = d;
            }
            else if (value is string s && s.Length > 0)
            {
                output[entry.TagPath] = s;
            }
        }

        return output;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static bool TryRead<T>(IDictionary<string, object?> dict, string key, out T value)
    {
        if (dict.TryGetValue(key, out var raw) && raw is T t)
        {
            value = t;
            return true;
        }
        value = default!;
        return false;
    }

    private static readonly Regex _toolKeyRegex = new(@"^Tool\.(\d+)\.(\w+)$", RegexOptions.Compiled);

    private static IEnumerable<(int ToolNo, string Suffix, object? Value)> EnumerateToolMetadata(
        IDictionary<string, object?> dict)
    {
        foreach (var (k, v) in dict)
        {
            var m = _toolKeyRegex.Match(k);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var toolNo))
            {
                yield return (toolNo, m.Groups[2].Value, v);
            }
        }
    }

    private static readonly Regex _maintenanceKeyRegex = new(@"^Maintenance\.(\d+)\.(\w+)$", RegexOptions.Compiled);

    private static IEnumerable<(int Idx, string Suffix, object? Value)> EnumerateMaintenanceNoticeFields(
        IDictionary<string, object?> dict)
    {
        foreach (var (k, v) in dict)
        {
            var m = _maintenanceKeyRegex.Match(k);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var idx))
            {
                yield return (idx, m.Groups[2].Value, v);
            }
        }
    }
}
