// ============================================================================
// File: OpcUaQualityMapping.cs
// Purpose: Map between EdgeConnect's protocol-agnostic DataQuality
//          enum and OPC UA's wire-level StatusCode. The mapping is
//          part of the consumer-facing compatibility contract
//          (shared-knowledge/contracts/opcua-namespace-policy.md
//          § Quality propagation) — any change here is a contract
//          change.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.1
//            docs/core/canonical-data-model.md (Quality state machine)
//            shared-knowledge/contracts/opcua-namespace-policy.md
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Opc.Ua;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Maps <see cref="DataQuality"/> to OPC UA <see cref="StatusCode"/>.
/// The mapping is locked by the namespace policy contract; changing it
/// is a v2 namespace bump.
/// </summary>
public static class OpcUaQualityMapping
{
    /// <summary>
    /// Translate an EdgeConnect <see cref="DataQuality"/> into the
    /// OPC UA <see cref="StatusCode"/> a client sees on the wire.
    /// </summary>
    public static StatusCode ToStatusCode(DataQuality quality) => quality switch
    {
        DataQuality.Good => StatusCodes.Good,
        DataQuality.Uncertain => StatusCodes.Uncertain,
        DataQuality.Bad => StatusCodes.Bad,
        DataQuality.Stale => StatusCodes.UncertainSubstituteValue,

        // Unknown is the default-constructed value and should never
        // reach the wire. If it does, surface as Bad so clients don't
        // silently accept a default-constructed point as good.
        DataQuality.Unknown => StatusCodes.Bad,
        _ => StatusCodes.Bad,
    };
}
