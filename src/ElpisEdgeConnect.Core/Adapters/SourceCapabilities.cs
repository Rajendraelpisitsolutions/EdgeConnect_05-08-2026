// ============================================================================
// File: Adapters/SourceCapabilities.cs
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Capabilities declared by a source adapter. The runtime checks these before
/// invoking optional methods such as <c>BrowseTagsAsync</c> or subscription
/// methods to avoid calling unsupported operations.
/// </summary>
[Flags]
public enum SourceCapabilities
{
    /// <summary>No capabilities declared. A misconfigured adapter; the runtime will reject it.</summary>
    None = 0,

    /// <summary>Adapter supports periodic polling via <c>PollAsync</c>.</summary>
    Polling = 1 << 0,

    /// <summary>Adapter supports subscription via <c>SubscribeAsync</c>.</summary>
    Subscription = 1 << 1,

    /// <summary>Adapter can enumerate available tags via <c>BrowseTagsAsync</c>.</summary>
    Browse = 1 << 2,

    /// <summary>Adapter supports writing values back to the device.</summary>
    WriteBack = 1 << 3,

    /// <summary>Adapter can perform a connectivity test without starting.</summary>
    TestConnect = 1 << 4,

    /// <summary>
    /// Adapter can auto-discover tags from the upstream device beyond what
    /// is configured (e.g. read an S7 PLC symbol table, parse a TIA Portal
    /// export, list FOCAS2 collector groups). Distinct from
    /// <see cref="Browse"/> which only returns already-configured tags.
    /// </summary>
    Discovery = 1 << 5,

    /// <summary>
    /// Adapter populates <c>CanonicalDataPoint.Quality</c> from a real
    /// upstream signal (vs. always emitting Good). Sinks that depend on
    /// quality information (OPC UA Server, alarms) gate features on this
    /// capability. Every Phase-4 adapter declares this; future protocols
    /// without quality semantics (rare) can leave it off so downstream
    /// consumers know not to trust the field.
    /// </summary>
    Quality = 1 << 6,
}
