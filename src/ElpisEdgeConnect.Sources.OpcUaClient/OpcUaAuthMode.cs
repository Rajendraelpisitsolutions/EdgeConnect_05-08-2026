// ============================================================================
// File: OpcUaAuthMode.cs
// Purpose: User-identity / authentication mode for an OPC UA Client
//          connection. Locked at the connection level — the adapter
//          connects with ONE auth mode (vs the Server side which can
//          advertise multiple UserTokenPolicies and clients pick one).
//
//          Per v2.1 §6 Q7 user lock 2026-05-29: adapter supports ALL
//          three modes. Wizard exposes the full schema.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §6 Q7
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>OPC UA Client authentication mode.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OpcUaAuthMode>))]
public enum OpcUaAuthMode
{
    /// <summary>
    /// No user-identity credential exchanged. Acceptable on a trusted OT
    /// VLAN and the v2.1 §6 Q7 lab-benchmark baseline.
    /// </summary>
    Anonymous = 0,

    /// <summary>
    /// Username + password user-identity token. MUST NOT be paired with
    /// <see cref="OpcUaSecurityMode.None"/> — credentials would travel on
    /// the wire in cleartext. The adapter validates this combination at
    /// <c>ValidateConfigAsync</c> time and rejects it loudly.
    /// </summary>
    UserName = 1,

    /// <summary>
    /// X.509 client-certificate user-identity token. Pairs with any
    /// security mode. Certificate path + optional cert password supplied
    /// via the credentials block.
    /// </summary>
    Certificate = 2,
}
