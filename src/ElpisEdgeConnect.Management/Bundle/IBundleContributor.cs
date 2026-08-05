// ============================================================================
// File: Bundle/IBundleContributor.cs
// Purpose: The contributor model for the diagnostic bundle (ADR-0020 Rules
//          3/4/5; bundle plan v2 §1). A bundle is COMPOSED from independent
//          contributors over a shared ArchiveComposer — adding a future surface
//          (diagnostics, Flight Recorder, capability matrix, Route Timeline) is
//          a new contributor, never a BundleBuilder edit.
//
//          LOCKED design rules (plan v2 §1):
//            * Contributors contribute CONTENT ONLY. They never know about
//              INCLUDE/MASK/STRIP/EXCLUDE grouping — the composer derives that
//              from the redaction result (Q-7).
//            * Fail-closed: any contributor throwing fails the whole bundle. No
//              silent partial bundles (the bundle is a trust artifact).
// Reference: docs/sessions/2026-05-31-adr0020-bundle-generation-plan-v2.md §1.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Management.Bundle;

/// <summary>
/// Coarse capability metadata for a contributor, surfaced in
/// <c>bundle-info.json</c> so a support engineer (and a future loader) sees
/// exactly which kinds of surface a bundle contains.
/// </summary>
public enum BundleCapability
{
    /// <summary>Gateway identity (id, name, site/area).</summary>
    Identity,

    /// <summary>Redacted active configuration (<c>gateway.json</c>).</summary>
    Config,

    /// <summary>Redacted configuration history.</summary>
    History,

    /// <summary>Audit log.</summary>
    Audit,

    /// <summary>Topology / route inventory summary.</summary>
    Inventory,

    /// <summary>Diagnostic health snapshots (v1.1+).</summary>
    Diagnostics,

    /// <summary>Route Flight Recorder events (future, ADR-0021).</summary>
    FlightRecorder,
}

/// <summary>
/// Contributor-facing surface of the shared <c>ArchiveComposer</c>. Exposes only
/// content verbs — the composer applies redaction, computes checksums, and
/// derives the manifest tier grouping. A contributor never sees a tier.
/// </summary>
public interface IBundleWriter
{
    /// <summary>
    /// Add a JSON document, redacted through the configuration redaction engine
    /// (config/history/diagnostic-snapshot content). The composer records the
    /// redaction paths, detector warnings, and checksum.
    /// </summary>
    Task AddRedactedJsonAsync(string entryName, string rawJson, CancellationToken ct);

    /// <summary>
    /// Add content verbatim (no redaction) — for surfaces that are non-secret by
    /// construction (e.g. the route-inventory summary, the audit log). The
    /// composer records the checksum.
    /// </summary>
    Task AddVerbatimAsync(string entryName, string content, CancellationToken ct);

    /// <summary>
    /// Record that a surface was deliberately excluded from the bundle (Rule 1
    /// EXCLUDE), with an operator-readable reason. No file is written; the note
    /// surfaces in the manifest's EXCLUDE group and the preview.
    /// </summary>
    void AddExcludedNote(string surface, string reason);
}

/// <summary>
/// Read-only context handed to every contributor: the gateway identity and the
/// query surfaces a contributor may need. Contributors pull what they need
/// rather than receiving bespoke constructor wiring.
/// </summary>
public sealed class BundleContext
{
    /// <summary>The gateway's stable id.</summary>
    public required string GatewayId { get; init; }

    /// <summary>The active configuration (already loaded) for inventory / identity contributors.</summary>
    public required Core.Configuration.GatewayConfiguration Configuration { get; init; }

    /// <summary>The configuration manager, for history + audit streaming.</summary>
    public required Core.Configuration.IConfigurationManager ConfigManager { get; init; }

    /// <summary>The on-disk layout (config/history paths) for raw-file contributors.</summary>
    public required Core.Configuration.ConfigurationStorageLayout Layout { get; init; }
}

/// <summary>
/// One source of bundle content. Implementations decide WHAT goes in and supply
/// raw content through the writer; the composer handles redaction, checksums,
/// and manifest grouping.
/// </summary>
public interface IBundleContributor
{
    /// <summary>Stable id recorded in <c>bundle-info.json</c>'s contributor list (e.g. <c>"config"</c>).</summary>
    string Name { get; }

    /// <summary>Coarse capability category for <c>bundle-info.json</c>.</summary>
    BundleCapability Capability { get; }

    /// <summary>
    /// Contribute this surface's content. Throwing fails the entire bundle
    /// (fail-closed — no silent partial bundles).
    /// </summary>
    Task ContributeAsync(IBundleWriter writer, BundleContext context, CancellationToken ct);
}
