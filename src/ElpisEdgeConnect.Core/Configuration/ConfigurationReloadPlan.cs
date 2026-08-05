// ============================================================================
// File: Configuration/ConfigurationReloadPlan.cs
// Purpose: Output shape of RuntimeReloadClassifier. Describes the per-
//          instance operations the M.P2.2 RuntimeReloadCoordinator must
//          execute to converge the running supervisors + routing engine
//          on a freshly-applied GatewayConfiguration.
//
//          The plan is pure data: a list of immutable ReloadAction
//          records. The classifier produces it from
//
//              (newConfig, IReadOnlyList<ConfigurationChange>)
//
//          and the coordinator consumes it in the order locked by
//          ADR-0009 Decision 2 — routes first on teardown, routes last
//          on bring-up.
//
//          A Modified change on any entity resolves to a single Restart
//          action (per ADR-0009 Decision 3 — no in-place reconfigure
//          path exists for adapters in v1; a future ITryReconfigureLive
//          opt-in may add one).
//
//          Gateway-settings-only changes produce an empty plan: the
//          runtime does not currently react to GatewaySettings deltas
//          (a future milestone may extend this if a setting demands
//          runtime effect).
// Reference: docs/decisions/0009-runtime-hot-reload-instance-granularity.md
//            docs/sessions/2026-05-16-mp22-kickoff.md
// Milestone: M.P2.2 phase 1
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Which lifecycle operation a <see cref="ReloadAction"/> requests against
/// one (<see cref="ConfigurationEntityKind"/>, instance id) pair.
/// </summary>
public enum ReloadOp
{
    /// <summary>The instance is new in the applied config — start it.</summary>
    Add = 0,

    /// <summary>The instance is gone from the applied config — stop + dispose it.</summary>
    Remove = 1,

    /// <summary>
    /// The instance exists in both old + new configs but its definition
    /// changed. Resolved as <c>Remove(old)</c> + <c>Add(new)</c> at the
    /// supervisor layer per ADR-0009 Decision 3.
    /// </summary>
    Restart = 2,
}

/// <summary>
/// One operation the coordinator must execute. Carries the new entity
/// definition (for Add / Restart) so the coordinator does not need to
/// re-lookup against the configuration. <see cref="NewConfig"/> is null
/// for Remove; the supervisor only needs the entity id to tear down.
/// </summary>
public sealed record ReloadAction
{
    /// <summary>Which lifecycle operation to perform.</summary>
    public required ReloadOp Op { get; init; }

    /// <summary>Which kind of entity (Source / Sink / Route).</summary>
    public required ConfigurationEntityKind Kind { get; init; }

    /// <summary>The instance id (or route id) the operation targets.</summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// The new entity definition, populated for <see cref="ReloadOp.Add"/>
    /// and <see cref="ReloadOp.Restart"/>; null for <see cref="ReloadOp.Remove"/>.
    /// Typed as <c>object</c> to keep the action shape protocol-agnostic;
    /// the coordinator casts to the appropriate Core record
    /// (<see cref="SourceInstanceConfig"/>, <see cref="SinkInstanceConfig"/>,
    /// or <see cref="RouteConfig"/>) using <see cref="Kind"/>.
    /// </summary>
    public object? NewConfig { get; init; }
}

/// <summary>
/// The full reconciliation plan emitted by <see cref="RuntimeReloadClassifier"/>.
/// Actions are listed in classification order (not execution order — the
/// coordinator imposes the locked stop/start ordering at execute time).
/// </summary>
public sealed record ConfigurationReloadPlan
{
    /// <summary>An empty plan (no runtime work to do).</summary>
    public static ConfigurationReloadPlan Empty { get; } = new()
    {
        Actions = Array.Empty<ReloadAction>(),
    };

    /// <summary>The actions to execute, keyed by (Kind, EntityId).</summary>
    public required IReadOnlyList<ReloadAction> Actions { get; init; }

    /// <summary>
    /// True when there is no runtime work to do (e.g., the only changes
    /// were against <see cref="ConfigurationEntityKind.GatewaySettings"/>).
    /// </summary>
    public bool IsNoOp => Actions.Count == 0;
}
