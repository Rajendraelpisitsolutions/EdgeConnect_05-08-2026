// ============================================================================
// File: Configuration/GatewayConfiguration.cs
// Purpose: Root aggregate of the gateway JSON configuration.
// Reference: ARCHITECTURE_BLUEPRINT.md §8.1 (sample)
// Milestone: B1
//
// LOCKED: This is the top of the JSON tree the configuration manager (B2)
// will load, validate, and apply. Adding optional sections is acceptable.
// Renaming or removing existing sections is a breaking change requiring
// blueprint revision and a config file migration plan.
// ============================================================================

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Root aggregate of the gateway configuration. Loaded from
/// <c>config/current.json</c> by the configuration manager (B2). Holds the
/// gateway-level settings plus the source, sink, and route definitions that
/// describe the data flow.
/// </summary>
public sealed record GatewayConfiguration
{
    /// <summary>Gateway-level identity and host settings.</summary>
    [Required]
    [BundleTier(BundleTier.Include)]
    public required GatewaySettings Gateway { get; init; }

    /// <summary>
    /// Configured source connector instances. Default empty — a gateway
    /// with no sources is technically valid (and is the starting state of
    /// a freshly provisioned gateway).
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyList<SourceInstanceConfig> Sources { get; init; } = [];

    /// <summary>
    /// Configured sink connector instances. Default empty.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyList<SinkInstanceConfig> Sinks { get; init; } = [];

    /// <summary>
    /// Configured routes. Default empty. A route references one source by
    /// <see cref="RouteConfig.SourceInstanceId"/> and one or more sinks by
    /// <see cref="RouteConfig.SinkInstanceIds"/>; cross-record validation
    /// (verifying those ids resolve) happens in B2.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyList<RouteConfig> Routes { get; init; } = [];

    /// <summary>
    /// Captures unknown root-level JSON members so they survive
    /// Deserialize → Serialize round-trips through
    /// <see cref="ConfigurationManager.CreateDraftAsync"/> / ApplyDraftAsync.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Governed by <strong>ADR-0030 — reserved underscore-prefix namespace</strong>:
    /// keys beginning with <c>_</c> (e.g. <c>_provisioning</c>, <c>_diagnostics</c>)
    /// are <em>reserved metadata</em> attached by tooling that produces
    /// the configuration; their semantics are defined by the producer, not
    /// by the gateway runtime, which never consults them.
    /// </para>
    /// <para>
    /// Non-<c>_</c>-prefixed unknown roots are also captured here for forward
    /// compatibility (a future EdgeConnect version may add a new canonical
    /// root that older code would otherwise drop), but downstream validators
    /// SHOULD warn on them — they likely indicate a typo on a canonical key
    /// like <c>Sources</c> / <c>Sinks</c> / <c>Routes</c>.
    /// </para>
    /// <para>
    /// Null when no unknown roots are present, so round-tripping a
    /// canonical-only configuration produces no <c>"ExtensionData": {}</c>
    /// noise.
    /// </para>
    /// </remarks>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}
