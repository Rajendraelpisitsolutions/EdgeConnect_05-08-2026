// ============================================================================
// File: Collectors/AlarmCollector.cs
// Purpose: Parse /ALARM_CURALMLIST into BrotherPollSession with the three-way
//          filtering legacy applies:
//            * Alarm number 501 (Standby mode) → InformationalAlarmMessage
//              (NOT added to ActiveAlarms; the adapter's precedence chain
//              uses it to force Status/State=STOP, Status/Running=Idle).
//            * Messages matching maintenance keywords (GREASING, GREASE,
//              MAINTENANCE, FILTER, LUBRICATION, LUBRICANT) → MaintenanceAlarmMessages
//              (NOT added to ActiveAlarms; surfaces as Maintenance/Warning).
//            * Everything else → ActiveAlarms.
// Reference: legacy BrotherHttpDataSource.ParseAlarms (oracle only)
// ============================================================================

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Collectors;

internal static class AlarmCollector
{
    private const int InformationalStandbyAlarmNumber = 501;

    private static readonly string[] MaintenanceKeywords =
    {
        "GREASING", "GREASE", "MAINTENANCE", "FILTER", "LUBRICATION", "LUBRICANT",
    };

    public static void Collect(string? response, BrotherPollSession session)
    {
        if (response is null)
        {
            session.AlarmsEndpointFailed = true;
            return;
        }

        // Empty body = no alarms (the happy path). Just return.
        var trimmedTotal = response.Trim();
        if (trimmedTotal.Length == 0)
        {
            return;
        }

        foreach (var rawLine in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;

            // Format: "<num>, <message>, <program>, <?>, <statusCode>"
            var parts = line.Split(',');
            var alarmNumber = 0;
            var message = line;
            if (parts.Length >= 2)
            {
                int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out alarmNumber);
                message = parts[1].Trim();
            }

            // Informational (e.g., 501 Standby) → surface separately.
            if (alarmNumber == InformationalStandbyAlarmNumber)
            {
                session.InformationalAlarmMessage = message;
                continue;
            }

            // Maintenance keyword match → surface as maintenance, not alarm.
            var isMaintenance = false;
            foreach (var keyword in MaintenanceKeywords)
            {
                if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    isMaintenance = true;
                    break;
                }
            }
            if (isMaintenance)
            {
                session.MaintenanceAlarmMessages.Add(message);
                continue;
            }

            session.ActiveAlarms.Add(new AlarmEntry(alarmNumber, message));
        }
    }
}
