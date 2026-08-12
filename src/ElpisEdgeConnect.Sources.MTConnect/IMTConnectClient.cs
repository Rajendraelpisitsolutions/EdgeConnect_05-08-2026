// ============================================================================
// File: IMTConnectClient.cs
// Purpose: HTTP seam for the MTConnect adapter. Separating the HTTP call
//          from the XML parsing lets unit tests inject fixed XML strings
//          via FakeMTConnectClient without spinning up a real Agent.
// Reference: PHASE2_ENTRY.md Phase 2 adapter list
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>
/// Minimal HTTP façade over an MTConnect Agent. The adapter only needs the
/// two endpoints required to run — <c>/current</c> for per-poll values and
/// <c>/probe</c> for one-time device discovery.
/// </summary>
internal interface IMTConnectClient
{
    /// <summary>
    /// GET <c>{agentBaseUrl}{deviceName?}/probe</c>. Returns the raw XML body.
    /// </summary>
    Task<string> GetProbeAsync(CancellationToken ct);

    /// <summary>
    /// GET <c>{agentBaseUrl}{deviceName?}/current</c>. Returns the raw XML body.
    /// </summary>
    Task<string> GetCurrentAsync(CancellationToken ct);
}
