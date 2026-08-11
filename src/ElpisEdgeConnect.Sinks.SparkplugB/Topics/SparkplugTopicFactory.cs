// ============================================================================
// File: Topics/SparkplugTopicFactory.cs
// Purpose: The spBv1.0 topic grammar for node-only v1 (ADR-0035 Rules 3/4):
//          NBIRTH/NDATA/NDEATH publish topics and the EXACT NCMD subscribe
//          topic (a conforming Edge Node MUST subscribe its own NCMD topic —
//          the mandatory receive-only Node Control/Rebirth carve-out). No
//          device-level topics in v1.
// Reference: ADR-0035 Rules 3/4; plan v2 §1.7.
// ============================================================================

using ElpisEdgeConnect.Sinks.SparkplugB.Identity;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Topics;

/// <summary>
/// Builds the Sparkplug topic strings for a validated Edge Node identity.
/// Inputs are topic-safe by construction (<see cref="SparkplugEdgeNodeIdentity"/>).
/// </summary>
public static class SparkplugTopicFactory
{
    /// <summary>The NBIRTH publish topic: <c>spBv1.0/{group}/NBIRTH/{edge_node}</c>.</summary>
    /// <param name="identity">The Edge Node identity.</param>
    /// <returns>The topic string.</returns>
    public static string NBirth(SparkplugEdgeNodeIdentity identity) => Topic(identity, "NBIRTH");

    /// <summary>The NDATA publish topic: <c>spBv1.0/{group}/NDATA/{edge_node}</c>.</summary>
    /// <param name="identity">The Edge Node identity.</param>
    /// <returns>The topic string.</returns>
    public static string NData(SparkplugEdgeNodeIdentity identity) => Topic(identity, "NDATA");

    /// <summary>The NDEATH topic (used as the MQTT Will topic and for intentional-disconnect publish): <c>spBv1.0/{group}/NDEATH/{edge_node}</c>.</summary>
    /// <param name="identity">The Edge Node identity.</param>
    /// <returns>The topic string.</returns>
    public static string NDeath(SparkplugEdgeNodeIdentity identity) => Topic(identity, "NDEATH");

    /// <summary>
    /// The exact NCMD subscribe topic: <c>spBv1.0/{group}/NCMD/{edge_node}</c>.
    /// No wildcards — the conformance requirement is the Edge Node's own topic.
    /// </summary>
    /// <param name="identity">The Edge Node identity.</param>
    /// <returns>The topic string.</returns>
    public static string NCmdSubscribe(SparkplugEdgeNodeIdentity identity) => Topic(identity, "NCMD");

    private static string Topic(SparkplugEdgeNodeIdentity identity, string messageType)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return $"{SparkplugBProtocol.TopicNamespace}/{identity.GroupId}/{messageType}/{identity.EdgeNodeId}";
    }
}
