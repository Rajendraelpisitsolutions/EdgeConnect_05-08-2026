// ============================================================================
// File: Collectors/MachineInfoCollector.cs
// Purpose: Parse /HTTPD_MCNINFO into BrotherPollSession.
//          Format: hostname,model,statusCode,?,?,language
//          Example: "BRN68E74A6608EA,SXd1,3,01,0,1"
//          Status codes: 0=Off, 1=Idle, 2=Hold, 3=Running, 4=Notice, 5=Standby
//          (The Status/State derivation lives in the adapter's precedence
//          chain per v3 §6.2 — this collector just emits the raw signal.)
// Reference: legacy BrotherHttpDataSource.ParseMachineInfo (oracle only)
// ============================================================================

namespace ElpisEdgeConnect.Sources.BrotherHttp.Collectors;

internal static class MachineInfoCollector
{
    public static void Collect(string? response, BrotherPollSession session)
    {
        if (response is null)
        {
            session.MachineInfoEndpointFailed = true;
            return;
        }

        var trimmed = response.Trim();
        if (trimmed.Length == 0)
        {
            // Empty response is not a hard failure — endpoint responded but
            // the body is empty. Adapter treats this as "no data this cycle".
            return;
        }

        var parts = trimmed.Split(',');
        if (parts.Length < 3) return;

        session.Hostname = parts[0].Trim();
        session.Model = parts[1].Trim();
        if (int.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var status))
        {
            session.StatusCode = status;
        }
    }
}
