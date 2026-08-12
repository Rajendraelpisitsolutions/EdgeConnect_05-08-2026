// ============================================================================
// File: OpcUaNodeIdCanonicalizer.cs
// Purpose: Shared semantic-NodeId equality utility. Promoted out of the
//          PR 6b reconfigure diff (where it lived as a private helper)
//          per user lock 2026-05-29: NodeId normalisation is consumed by
//          the diff, the reconfigure executor, the wizard's browse step,
//          manual tag entry, and future tag import/export. It deserves
//          its own home rather than being exposed as diff internals.
//
// LOCKED behaviour:
//
//   1. Canonicalise once at lookup-build time, not in inner comparison
//      loops — at 30K+ items the OPC stack's NodeId.Parse cost adds up
//      even though reconfigure isn't on the hot data path
//   2. Defensive on bad input: empty / whitespace → empty string;
//      malformed NodeId string → echo the raw input unchanged so
//      operator hand-entry errors surface as Add/Remove churn rather
//      than silent equality
//   3. Equality is SEMANTIC, not raw string — "ns=2;i=1" and
//      "ns=2;i=00000001" are the same NodeId; "ns=2;s=Foo" and
//      "ns=2;s=foo" are NOT (server-side string identifier equality
//      is case-sensitive per OPC UA Part 4)
//
// Reference: PR 6b plan + amendments (user lock 2026-05-29)
//            PR 7c plan + amendments (user lock 2026-05-29)
//            OPC UA Part 4 §7.6 NodeId
// ============================================================================

using Opc.Ua;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Normalises OPC UA NodeId strings to a canonical form so semantically-
/// equal identifiers ("ns=2;i=1" vs "ns=2;i=00000001") compare equal.
/// Used by the reconfigure diff, the reconfigure executor, the wizard's
/// browse step + manual tag entry, and any future tag import/export.
/// </summary>
public static class OpcUaNodeIdCanonicalizer
{
    /// <summary>
    /// Canonicalise a NodeId expressed as an operator-facing string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty / whitespace input returns <see cref="string.Empty"/> so
    /// dictionary keying remains stable when an operator hasn't yet
    /// supplied a NodeId.
    /// </para>
    /// <para>
    /// Malformed input — anything the OPC stack's
    /// <see cref="NodeId.Parse(string)"/> rejects — falls back to the
    /// raw string. This guarantees the canonicalizer never throws and
    /// keeps obviously-broken NodeIds visible as Add/Remove churn (so
    /// the operator notices), rather than swallowing them silently.
    /// </para>
    /// </remarks>
    public static string Canonicalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            return NodeId.Parse(raw).ToString();
        }
        catch
        {
            return raw;
        }
    }

    /// <summary>
    /// Canonicalise a parsed <see cref="NodeId"/> (typically the
    /// <c>StartNodeId</c> off a live <see cref="Opc.Ua.Client.MonitoredItem"/>).
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when <paramref name="nodeId"/> is
    /// <see langword="null"/>; otherwise the stack's <see cref="object.ToString"/>
    /// form which already canonicalises numeric / string / Guid / opaque
    /// identifier types.
    /// </returns>
    public static string? Canonicalize(NodeId? nodeId) =>
        nodeId?.ToString();
}
