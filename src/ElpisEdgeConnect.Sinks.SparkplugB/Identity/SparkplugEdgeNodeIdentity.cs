// ============================================================================
// File: Identity/SparkplugEdgeNodeIdentity.cs
// Purpose: The Sparkplug namespace identity (group_id + edge_node_id) used to
//          build topics (ADR-0035 Rule 4). Both elements appear in MQTT topic
//          levels, so they must be non-empty and free of the topic-reserved
//          characters '/', '+', '#' (and NUL). Validation rejects with a typed
//          SPARKPLUG.* error. The broker+group+edge_node single-owner
//          cardinality rule is K4 config validation, not this type.
// Reference: ADR-0035 Rule 4; plan v2 §1.7.
// ============================================================================

using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Identity;

/// <summary>
/// A validated Sparkplug Edge Node namespace identity. Built via
/// <see cref="Create"/>; instances are always topic-safe.
/// </summary>
public sealed record SparkplugEdgeNodeIdentity
{
    private static readonly char[] ForbiddenChars = ['/', '+', '#', '\0'];

    private SparkplugEdgeNodeIdentity(string groupId, string edgeNodeId)
    {
        GroupId = groupId;
        EdgeNodeId = edgeNodeId;
    }

    /// <summary>The Sparkplug group id (topic level 2).</summary>
    public string GroupId { get; }

    /// <summary>The Sparkplug edge node id (topic level 4).</summary>
    public string EdgeNodeId { get; }

    /// <summary>Create a validated identity.</summary>
    /// <param name="groupId">The group id (non-empty, no '/', '+', '#').</param>
    /// <param name="edgeNodeId">The edge node id (non-empty, no '/', '+', '#').</param>
    /// <returns>The identity.</returns>
    /// <exception cref="AdapterException">
    /// Thrown with <see cref="SparkplugErrors.IdentityInvalid"/> when an element
    /// is empty or contains a topic-reserved character.
    /// </exception>
    public static SparkplugEdgeNodeIdentity Create(string groupId, string edgeNodeId)
    {
        Validate(groupId, nameof(groupId));
        Validate(edgeNodeId, nameof(edgeNodeId));
        return new SparkplugEdgeNodeIdentity(groupId, edgeNodeId);
    }

    private static void Validate(string element, string name)
    {
        if (string.IsNullOrEmpty(element) || element.IndexOfAny(ForbiddenChars) >= 0)
        {
            throw new AdapterException(new AdapterError
            {
                Code = SparkplugErrors.IdentityInvalid,
                Category = ErrorCategory.Configuration,
                Message = $"Sparkplug identity element '{name}' must be non-empty and must not contain '/', '+', '#', or NUL (was '{element}').",
                Retryable = false,
            });
        }
    }
}
