// ============================================================================
// File: Model/TagDefinition.cs
// Purpose: Describes a discovered tag on a source device. Returned by
//          ISourceAdapter.BrowseTagsAsync for protocols that support discovery.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

namespace ElpisEdgeConnect.Core.Model;

/// <summary>
/// Metadata describing a tag available on a source device, returned by
/// discovery/browse operations on adapters that support it.
/// </summary>
/// <remarks>
/// Not every adapter supports browse. Adapters that cannot enumerate tags
/// (e.g., free-form HTTP endpoints) return an empty list and declare the
/// <c>Browse</c> capability as absent.
/// </remarks>
public sealed record TagDefinition
{
    /// <summary>The source-native tag name as returned by the device.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Hierarchical path of the tag on the device (e.g., "Spindle/Speed").
    /// Equal to <see cref="Name"/> for flat namespaces.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>Declared value type reported by the device.</summary>
    public required CanonicalValueType ValueType { get; init; }

    /// <summary>Engineering unit string, if reported by the device.</summary>
    public string? Unit { get; init; }

    /// <summary>Human-readable description, if reported by the device.</summary>
    public string? Description { get; init; }

    /// <summary>True if the adapter supports writing back to this tag.</summary>
    public bool Writable { get; init; }

    /// <summary>
    /// Free-form protocol-specific metadata (e.g., Modbus register address,
    /// FOCAS2 parameter number). Informational only; transforms do not use this.
    /// </summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, string>? ProtocolMetadata { get; init; }
}
