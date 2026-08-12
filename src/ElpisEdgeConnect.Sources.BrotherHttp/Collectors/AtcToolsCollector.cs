// ============================================================================
// File: Collectors/AtcToolsCollector.cs
// Purpose: Parse /ATC_TOOLS into BrotherPollSession.
//          First line: "Program(0926)" header (skipped — already in CycleTime).
//          Subsequent lines: magazine slot entries with this column layout:
//          slot, color1, color2, toolNo, ?, ?, name, ?, ?, lengthXradius,
//          ?, ?, ?, ?, ?, lifeDisplay, ?, ?, toolType, ?, ?, ?, color3,
//          color4, ?
//
//          Active tool = slot with color1 + color2 both starting with '#'
//          (legacy convention: #000000 background, #ff8000 foreground).
// Reference: legacy BrotherHttpDataSource.ParseAtcTools (oracle only)
// ============================================================================

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Collectors;

internal static class AtcToolsCollector
{
    public static void Collect(string? response, BrotherPollSession session)
    {
        if (response is null)
        {
            session.AtcToolsEndpointFailed = true;
            return;
        }

        foreach (var rawLine in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line.StartsWith("Program(", StringComparison.OrdinalIgnoreCase))
            {
                continue;   // Header — already parsed elsewhere.
            }

            var parts = line.Split(',');
            if (parts.Length < 10) continue;

            // Slot number (col 0)
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
            {
                continue;
            }

            // Active-tool detection — both color columns start with '#'.
            var color1 = parts[1].Trim();
            var color2 = parts[2].Trim();
            var isActive = color1.StartsWith('#') && color2.StartsWith('#');

            // Tool number (col 3)
            int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var toolNo);

            if (isActive)
            {
                session.ActiveToolNumber = toolNo;
            }

            // Tool name (col 6), trimmed; null if empty/whitespace.
            var nameRaw = parts[6].Trim();
            var name = nameRaw.Length > 0 ? nameRaw : null;

            // Geometry length × radius (col 9) — "448.254x0.000"
            double length = 0;
            double radius = 0;
            if (parts[9].Trim() is { } dimStr && dimStr.Length > 0)
            {
                var dims = dimStr.Split('x', StringSplitOptions.RemoveEmptyEntries);
                if (dims.Length >= 1)
                {
                    double.TryParse(dims[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out length);
                }
                if (dims.Length >= 2)
                {
                    double.TryParse(dims[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out radius);
                }
            }

            // Tool life display (col 15) — "********" means unlimited (skip).
            string? life = null;
            if (parts.Length > 15)
            {
                var lifeRaw = parts[15].Trim();
                if (lifeRaw.Length > 0 && lifeRaw != "********")
                {
                    life = lifeRaw;
                }
            }

            // Tool type (col 18)
            string? type = null;
            if (parts.Length > 18)
            {
                var typeRaw = parts[18].Trim();
                if (typeRaw.Length > 0)
                {
                    type = typeRaw;
                }
            }

            session.Magazine.Add(new ToolMagazineEntry(
                Slot: slot,
                ToolNumber: toolNo,
                Length: length,
                Radius: radius,
                IsActive: isActive,
                Name: name,
                Type: type,
                Life: life));
        }
    }
}
