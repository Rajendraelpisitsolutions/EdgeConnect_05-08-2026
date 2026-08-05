// ============================================================================
// File: Store/SparkplugStoreIdentity.cs
// Purpose: The route-independent identity that scopes bdSeq + aliases in the
//          gateway identity store: a NORMALIZED broker endpoint + the Sparkplug
//          Edge Node (group + node). Identity is keyed by these typed columns
//          (never a lossy concatenation) so two distinct identities can never
//          collide and state survives route recreation (K0 WS5; plan v3 §5.7).
//          The broker endpoint is the shared protocol-neutral
//          Core.Configuration.BrokerEndpoint (slice-2 review r2) — the same
//          normalized type K4 route validation consumes, so the store and
//          cross-destination validation cannot diverge on normalization.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §5.5-§5.7.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Store;

/// <summary>
/// The (broker endpoint, Edge Node) key under which bdSeq and aliases are
/// persisted. Value-equal identities address the same persisted state.
/// </summary>
public sealed record SparkplugStoreIdentity
{
    private SparkplugStoreIdentity(BrokerEndpoint broker, SparkplugEdgeNodeIdentity node)
    {
        Broker = broker;
        Node = node;
    }

    /// <summary>The normalized broker endpoint (shared Core type).</summary>
    public BrokerEndpoint Broker { get; }

    /// <summary>The Sparkplug Edge Node identity (group + node), already topic-validated.</summary>
    public SparkplugEdgeNodeIdentity Node { get; }

    /// <summary>The canonical broker key persisted in the store's broker column.</summary>
    public string BrokerKey => Broker.ToString();

    /// <summary>Create a validated store identity.</summary>
    /// <param name="broker">The normalized broker endpoint.</param>
    /// <param name="node">The Edge Node identity.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the broker or node is null.</exception>
    public static SparkplugStoreIdentity Create(BrokerEndpoint broker, SparkplugEdgeNodeIdentity node)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(node);
        return new SparkplugStoreIdentity(broker, node);
    }
}
