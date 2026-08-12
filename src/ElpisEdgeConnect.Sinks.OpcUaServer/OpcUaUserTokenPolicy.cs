// ============================================================================
// File: OpcUaUserTokenPolicy.cs
// Purpose: User-authentication token policy enum for the OPC UA endpoint.
//          MVP-implemented values: Anonymous. UserName and Certificate are
//          present from day one for forward-compatible config authoring.
//          See Milestone K for the implementation switch.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H, K
// ============================================================================

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// User-token policy presented by the OPC UA endpoint. Multiple policies
/// can be enabled simultaneously per the spec.
/// </summary>
public enum OpcUaUserTokenPolicy
{
    /// <summary>
    /// Anonymous access. MVP-implemented. No identity verification —
    /// acceptable on a trusted OT VLAN.
    /// </summary>
    Anonymous,

    /// <summary>
    /// Username + password. Forward-compat — MVP rejects with
    /// OPCUA.SECURITY_NOT_YET_IMPLEMENTED. Implemented in Milestone K
    /// alongside Sign / SignAndEncrypt modes.
    /// </summary>
    UserName,

    /// <summary>
    /// X.509 certificate authentication. Forward-compat — MVP rejects.
    /// Implemented in Milestone K.
    /// </summary>
    Certificate,
}
