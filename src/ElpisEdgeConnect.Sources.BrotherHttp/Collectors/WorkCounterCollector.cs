// ============================================================================
// File: Collectors/WorkCounterCollector.cs
// Purpose: Parse /MNTP_WKCNTR into BrotherPollSession.
//          Four lines, one per counter slot. Format per line:
//              count, currentValue, targetValue, endSignalValue, ?, ?
//          Example:
//              999,     0,     0,     0,,0
//                0,     0,     0,     0,,0
//          The first line's count is the primary PartsCount alias.
//          Sparse: only non-zero values are recorded.
// Reference: legacy BrotherHttpDataSource.ParseWorkCounters (oracle only)
// ============================================================================

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Collectors;

internal static class WorkCounterCollector
{
    public static void Collect(string? response, BrotherPollSession session)
    {
        if (response is null)
        {
            session.WorkCountersEndpointFailed = true;
            return;
        }

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < Math.Min(lines.Length, 4); i++)
        {
            var line = lines[i].Trim().TrimEnd('\r');
            if (line.Length == 0) continue;

            var parts = line.Split(',');
            if (parts.Length == 0) continue;

            // Primary counter — write PartsCount alias for the first row only.
            if (i == 0 &&
                long.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var primary))
            {
                session.PartsCount = primary;
            }

            int? count = null;
            int? target = null;

            if (parts.Length >= 1 &&
                int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) &&
                c != 0)
            {
                count = c;
            }
            if (parts.Length >= 3 &&
                int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) &&
                t != 0)
            {
                target = t;
            }

            if (count is not null || target is not null)
            {
                session.Counters[i + 1] = new CounterEntry(count, target);
            }
        }
    }
}
