// ============================================================================
// File: Diagnostics/ConfigurationFault.cs
// Purpose: One record of a runtime-observed configuration fault. Produced
//          at gateway startup by the protocol registration extensions and
//          by RouteDefinitionFactory when cross-record validation fails
//          (e.g., enabled source with no route, route referencing a
//          disabled source). Held in IConfigurationFaultRegistry and
//          surfaced via /api/v1/diagnostics/configuration-faults so the
//          operator can see and fix the bad config via the Studio rather
//          than the gateway crashing on boot.
//
//          Faults are RUNTIME state, not desired state. They are
//          in-memory only, never persisted to disk, and cleared when a
//          subsequent re-init of the same instance succeeds (M.P2.2 hot-
//          reload). The audit-chain record of the fault — written via
//          IConfigurationManager.AppendRuntimeFaultAsync with actor
//          "system" and action RuntimeConfigurationFault — is the
//          durable history; this registry is the live view.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.P2.1
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Which configuration concept a fault relates to.
/// </summary>
public enum ConfigurationFaultKind
{
    /// <summary>The fault relates to a source instance.</summary>
    Source = 0,

    /// <summary>The fault relates to a sink (destination) instance.</summary>
    Sink = 1,

    /// <summary>The fault relates to a route.</summary>
    Route = 2,

    /// <summary>
    /// A gateway-level (not instance-scoped) fault — e.g. the data root is not
    /// writable. The <see cref="ConfigurationFault.InstanceId"/> is a stable
    /// gateway-scoped token such as <c>"data-root"</c> rather than a
    /// source/sink/route id.
    /// </summary>
    Gateway = 3,
}

/// <summary>
/// One immutable record of a cross-record validation failure observed at
/// runtime (today, only at gateway startup; M.P2.2 extends to hot-reload).
/// </summary>
/// <remarks>
/// Identity is the <see cref="Kind"/>/<see cref="InstanceId"/> pair —
/// registering a second fault for the same instance replaces the first.
/// </remarks>
public sealed record ConfigurationFault
{
    /// <summary>Which kind of configuration entity is faulted.</summary>
    public required ConfigurationFaultKind Kind { get; init; }

    /// <summary>The id of the faulted instance (e.g., "modbus-line-3", "route-line-3").</summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// Structured error code (e.g., "CONFIG.SOURCE_WITHOUT_ROUTE",
    /// "CONFIG.ROUTE_REFERENCES_MISSING_SOURCE"). Stable across
    /// gateway versions for downstream tooling / alerting.
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>Operator-readable description of the fault.</summary>
    public required string Message { get; init; }

    /// <summary>UTC timestamp the fault was observed.</summary>
    public required DateTime ObservedAtUtc { get; init; }
}
