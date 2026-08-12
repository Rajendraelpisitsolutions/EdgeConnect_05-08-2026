// ============================================================================
// File: Collectors/MaintenanceCollector.cs
// Purpose: Parse /MNTP_MAINTNOTICE into BrotherPollSession.
//          Lines starting with "None" → no notices. Other lines have eight
//          comma-separated fields:
//              condition, description, status, current, limit, notified, state, color
//          Example:
//              Z-axis travel distance),GREASING XYZ AXIS   ,Invalid,        0,   100000,Notified,Normal,#00ff00
//          DuePercent is computed as round(current * 100 / limit) when
//          limit > 0; null otherwise.
// Reference: legacy BrotherHttpDataSource.ParseMaintenanceNotices (oracle only)
// ============================================================================

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Collectors;

internal static class MaintenanceCollector
{
    public static void Collect(string? response, BrotherPollSession session)
    {
        if (response is null)
        {
            session.MaintenanceNoticesEndpointFailed = true;
            return;
        }

        foreach (var rawLine in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.StartsWith("None", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split(',');
            if (parts.Length < 7) continue;

            var condition = parts[0].Trim();
            var description = parts[1].Trim();
            var status = parts[2].Trim();
            var current = parts[3].Trim();
            var limit = parts[4].Trim();
            var notified = parts[5].Trim();
            var state = parts[6].Trim();

            int? duePercent = null;
            if (long.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentNum) &&
                long.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limitNum) &&
                limitNum > 0)
            {
                duePercent = (int)Math.Round(currentNum * 100.0 / limitNum);
            }

            session.Notices.Add(new MaintenanceNoticeEntry(
                Description: description,
                Condition: condition,
                Status: status,
                Current: current,
                Limit: limit,
                Notified: notified,
                State: state,
                DuePercent: duePercent));
        }
    }
}
