// ============================================================================
// File: OpcUaSecurityMode.cs
// Purpose: Endpoint security mode for an OPC UA Client connection. Mirrors
//          the same enum on the Sinks.OpcUaServer side (per v2.1 §1.1 —
//          the client supports the full surface the server side exposes).
//
//          Engineering baseline per v2.1 §6 Q7: SignAndEncrypt + Anonymous
//          for lab benchmark runs. All three modes are operator-selectable
//          per v2.1 §6 Q7 user lock 2026-05-29 — adapter commits to
//          support the FULL endpoint matrix the customer site presents.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §6 Q7
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>OPC UA endpoint security mode locked at the connection level.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OpcUaSecurityMode>))]
public enum OpcUaSecurityMode
{
    /// <summary>
    /// No signing or encryption. Acceptable on a trusted OT VLAN; rejected
    /// by most production policies because messages are in cleartext.
    /// </summary>
    None = 0,

    /// <summary>Messages signed with the configured policy (Basic256Sha256).</summary>
    Sign = 1,

    /// <summary>
    /// Messages signed AND encrypted with the configured policy. Recommended
    /// for any production deployment, especially when AuthMode is UserName
    /// (which transmits credentials and MUST NOT be paired with None).
    /// </summary>
    SignAndEncrypt = 2,
}
