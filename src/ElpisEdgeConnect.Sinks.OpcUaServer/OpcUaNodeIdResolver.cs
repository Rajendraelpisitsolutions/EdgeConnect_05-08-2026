// ============================================================================
// File: OpcUaNodeIdResolver.cs
// Purpose: Pure template-substitution helper that resolves the NodeId
//          string for a given (gatewayId, sourceInstanceId, stableTagId)
//          triple by substituting the placeholders in
//          OpcUaNamespaceConfig.NodeIdTemplate.
//
//          This is the IDENTITY contract for OPC UA clients. Keep this
//          deliberately tiny — the resolver has no library dependency
//          so it can be unit-tested in isolation, and so a future
//          refactor of the OPC UA library never touches the NodeId
//          derivation rules. See
//          shared-knowledge/contracts/opcua-namespace-policy.md.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.1
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Resolves OPC UA NodeId strings from a template + per-tag inputs.
/// The default template is
/// <c>ns=2;s={gatewayId}/{sourceId}/{stableTagId}</c>; placeholders are
/// substituted at adapter Initialize time. The resolver also produces
/// the BrowsePath segments by applying
/// <see cref="OpcUaNamespaceConfig.BrowsePathTemplate"/>.
/// </summary>
public sealed class OpcUaNodeIdResolver
{
    private readonly string _nodeIdTemplate;
    private readonly string _browsePathTemplate;

    /// <summary>
    /// Construct a resolver around the locked namespace shape.
    /// </summary>
    /// <param name="namespaceConfig">The namespace configuration to resolve against. Must be non-null.</param>
    public OpcUaNodeIdResolver(OpcUaNamespaceConfig namespaceConfig)
    {
        ArgumentNullException.ThrowIfNull(namespaceConfig);
        _nodeIdTemplate = namespaceConfig.NodeIdTemplate;
        _browsePathTemplate = namespaceConfig.BrowsePathTemplate;
    }

    /// <summary>
    /// Resolve the NodeId string for a (gatewayId, sourceInstanceId,
    /// stableTagId) triple. Returns e.g.
    /// <c>ns=2;s=gw-line1-edge/modbus-s7-line1/spindle_rpm</c>.
    /// </summary>
    public string ResolveNodeId(string gatewayId, string sourceInstanceId, string stableTagId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableTagId);

        return _nodeIdTemplate
            .Replace("{gatewayId}", gatewayId, StringComparison.Ordinal)
            .Replace("{sourceId}", sourceInstanceId, StringComparison.Ordinal)
            .Replace("{stableTagId}", stableTagId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Build the BrowsePath segment list from the configured template,
    /// substituting every available placeholder. Placeholders whose
    /// value is null or empty are silently dropped from the resulting
    /// segment list (per
    /// <c>shared-knowledge/contracts/opcua-namespace-policy.md</c>).
    /// </summary>
    /// <param name="placeholders">
    /// Map of placeholder name (without braces) to its value.
    /// Unknown placeholders in the template are left intact (for
    /// forward-compatibility — adding a placeholder later is additive).
    /// </param>
    public IReadOnlyList<string> ResolveBrowsePathSegments(IReadOnlyDictionary<string, string?> placeholders)
    {
        ArgumentNullException.ThrowIfNull(placeholders);

        var rawSegments = _browsePathTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(rawSegments.Length);

        foreach (var segment in rawSegments)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // Single-placeholder segments: drop the whole segment when the
            // placeholder resolves to null/empty. Multi-token segments are
            // not currently supported in the template grammar (KISS).
            if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            {
                var key = trimmed[1..^1];
                if (placeholders.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value);
                }
                // else: silently drop
            }
            else
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
