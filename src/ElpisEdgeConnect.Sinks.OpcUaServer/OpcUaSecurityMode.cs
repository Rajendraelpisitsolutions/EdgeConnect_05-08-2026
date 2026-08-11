// ============================================================================
// File: OpcUaSecurityMode.cs
// Purpose: OPC UA security-mode enum mirrored from the OPC UA spec.
//          MVP-implemented values: None. Sign and SignAndEncrypt are
//          present in the enum from day one so customer configs can be
//          authored forward-compatibly without later schema migration —
//          the validator throws OPCUA.SECURITY_NOT_YET_IMPLEMENTED when
//          a non-None value is selected in MVP, and MVP-3 (Milestone K)
//          flips the implementation switch without touching the schema.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H, K
//            shared-knowledge/contracts/opcua-namespace-policy.md
// ============================================================================

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// OPC UA endpoint security mode. Mirrors the OPC UA spec.
/// </summary>
public enum OpcUaSecurityMode
{
    /// <summary>
    /// No security. Messages are not signed or encrypted. MVP-implemented;
    /// acceptable for commissioning on a trusted OT VLAN.
    /// </summary>
    None,

    /// <summary>
    /// Messages are signed with the server's application certificate.
    /// Forward-compat field — MVP rejects with OPCUA.SECURITY_NOT_YET_IMPLEMENTED.
    /// Implemented in Milestone K.
    /// </summary>
    Sign,

    /// <summary>
    /// Messages are signed AND encrypted. Forward-compat field —
    /// MVP rejects with OPCUA.SECURITY_NOT_YET_IMPLEMENTED.
    /// Implemented in Milestone K.
    /// </summary>
    SignAndEncrypt,
}
