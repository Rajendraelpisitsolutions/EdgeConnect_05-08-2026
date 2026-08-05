// ============================================================================
// File: Collectors/CycleTimeCollector.cs
// Purpose: Parse /MNTP_CYCLETIME into BrotherPollSession.
//          Multi-line, key,value format. Example:
//              Program,(,0926,)
//              Cycle time,0000:04:56.2
//              Cutting time,0000:04:43.5
//              Operation time,3462:45:28
//              Power on time,2496:17:52
//              Operation end counter,0994
//              Cutting time / cycle time, 95 %
// Reference: legacy BrotherHttpDataSource.ParseCycleTime (oracle only)
// ============================================================================

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Collectors;

internal static class CycleTimeCollector
{
    public static void Collect(string? response, BrotherPollSession session)
    {
        if (response is null)
        {
            session.CycleTimeEndpointFailed = true;
            return;
        }

        foreach (var rawLine in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;

            if (TryConsumePrefix(line, "Program,", out var afterProgram))
            {
                // "(,0926,)" → strip parens/commas → "0926" → prefix with 'O'
                var prog = afterProgram.Replace("(", "").Replace(")", "").Replace(",", "").Trim();
                if (prog.Length > 0)
                {
                    session.Program = $"O{prog}";
                }
                continue;
            }

            if (TryConsumePrefix(line, "Cycle time,", out var cycleTimeText))
            {
                session.CycleSeconds = ParseBrotherTimeToSeconds(cycleTimeText);
                continue;
            }

            // "Cutting time," but NOT "Cutting time / cycle time," (next branch).
            if (TryConsumePrefix(line, "Cutting time,", out var cuttingText) &&
                !line.StartsWith("Cutting time /", StringComparison.OrdinalIgnoreCase))
            {
                session.CuttingSeconds = ParseBrotherTimeToSeconds(cuttingText);
                continue;
            }

            if (TryConsumePrefix(line, "Operation time,", out var opText))
            {
                var seconds = ParseBrotherTimeToSeconds(opText);
                if (seconds is { } s)
                {
                    session.OperationHours = Math.Round(s / 3600.0, 1);
                }
                continue;
            }

            if (TryConsumePrefix(line, "Power on time,", out var poText))
            {
                var seconds = ParseBrotherTimeToSeconds(poText);
                if (seconds is { } s)
                {
                    session.PowerOnHours = Math.Round(s / 3600.0, 1);
                }
                continue;
            }

            if (TryConsumePrefix(line, "Operation end counter,", out var endCounterText) &&
                int.TryParse(endCounterText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var endCount))
            {
                session.EndCounter = endCount;
                continue;
            }

            if (line.StartsWith("Cutting time / cycle time", StringComparison.OrdinalIgnoreCase))
            {
                // "Cutting time / cycle time, 95 %"
                var afterComma = line.Split(',', 2);
                if (afterComma.Length == 2)
                {
                    var pct = afterComma[1].Replace("%", "").Trim();
                    if (int.TryParse(pct, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ratio))
                    {
                        session.CuttingRatioPercent = ratio;
                    }
                }
            }
        }
    }

    private static bool TryConsumePrefix(string line, string prefix, out string remainder)
    {
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            remainder = line[prefix.Length..].Trim();
            return true;
        }
        remainder = string.Empty;
        return false;
    }

    /// <summary>
    /// Brother time format is "HHHH:MM:SS.D" (hours can exceed 24).
    /// </summary>
    private static double? ParseBrotherTimeToSeconds(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(':');
        if (parts.Length < 2) return null;

        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours);
        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes);
        double seconds = 0;
        if (parts.Length >= 3)
        {
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        }
        return hours * 3600 + minutes * 60 + seconds;
    }
}
