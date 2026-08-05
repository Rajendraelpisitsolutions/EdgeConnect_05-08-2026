// ============================================================================
// File: Configuration/RuntimeReloadClassifier.cs
// Purpose: Pure function that turns a ConfigurationChange list (produced
//          by ConfigurationDiffer at apply time) into a
//          ConfigurationReloadPlan the M.P2.2 RuntimeReloadCoordinator
//          can execute.
//
//          Locked rules (ADR-0009):
//
//             * Per-instance granularity. Each (Kind, EntityId) pair in
//               the diff resolves to ONE action — multiple field-level
//               Modified entries for the same entity collapse into one
//               Restart.
//
//             * Modified == Restart. Adapters today expose Initialize /
//               Start / Stop only; there is no live-reconfigure path.
//
//             * GatewaySettings changes are runtime-NoOp. The Gateway
//               record only affects identity + hosting; nothing the
//               supervisors or routing engine react to at runtime.
//               (If a future setting demands runtime effect, extend
//               here.)
//
//          The classifier never touches the running runtime. It produces
//          a plan record; the coordinator imposes the locked stop/start
//          order at execute time.
//
//          Pure: no IO, no async, no DI, no mutation of inputs. Fully
//          unit-testable.
// Reference: docs/decisions/0009-runtime-hot-reload-instance-granularity.md
//            docs/sessions/2026-05-16-mp22-kickoff.md
// Milestone: M.P2.2 phase 1
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Pure classifier mapping a configuration diff into a per-instance
/// reload plan. Consumed by the host's <c>RuntimeReloadCoordinator</c>.
/// </summary>
public static class RuntimeReloadClassifier
{
    /// <summary>
    /// Classify the given diff into a <see cref="ConfigurationReloadPlan"/>.
    /// </summary>
    /// <param name="newConfig">
    /// The configuration that was just applied — the classifier looks up
    /// the new entity definition for <see cref="ReloadOp.Add"/> and
    /// <see cref="ReloadOp.Restart"/> actions against this.
    /// </param>
    /// <param name="changes">
    /// The structured diff produced by <see cref="ConfigurationDiffer"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">When either argument is null.</exception>
    public static ConfigurationReloadPlan Classify(
        GatewayConfiguration newConfig,
        IReadOnlyList<ConfigurationChange> changes)
    {
        ArgumentNullException.ThrowIfNull(newConfig);
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return ConfigurationReloadPlan.Empty;
        }

        // Index new-config entities by id for O(1) lookup. Built only when
        // we have changes — keeps the no-op path allocation-free.
        var newSourcesById = IndexById(newConfig.Sources, s => s.InstanceId);
        var newSinksById = IndexById(newConfig.Sinks, s => s.InstanceId);
        var newRoutesById = IndexById(newConfig.Routes, r => r.RouteId);

        // Collapse multiple field-level Modified entries against the same
        // (Kind, EntityId) into one Restart. Add / Remove use the same
        // dedup so a malformed diff that contains both can't produce
        // two conflicting actions for the same key. Last-writer-wins on
        // op kind in classification order — but Add and Remove for the
        // same key in one diff is itself a contract violation and the
        // differ never emits that combination.
        var actionsByKey = new Dictionary<(ConfigurationEntityKind Kind, string EntityId), ReloadAction>(
            capacity: changes.Count);

        foreach (var change in changes)
        {
            // Per ADR-0009, GatewaySettings changes do not produce runtime
            // work. If a future setting demands runtime effect, branch
            // here and add a corresponding ReloadOp value.
            if (change.EntityKind == ConfigurationEntityKind.GatewaySettings)
            {
                continue;
            }

            var op = change.Kind switch
            {
                ConfigurationChangeKind.Added => ReloadOp.Add,
                ConfigurationChangeKind.Removed => ReloadOp.Remove,
                ConfigurationChangeKind.Modified => ReloadOp.Restart,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(changes),
                    change.Kind,
                    $"Unrecognised ConfigurationChangeKind '{change.Kind}' for entity '{change.EntityId}'."),
            };

            var newEntityConfig = op == ReloadOp.Remove
                ? null
                : LookupNewEntity(change.EntityKind, change.EntityId, newSourcesById, newSinksById, newRoutesById);

            // For Add / Restart, the entity MUST be present in the new
            // config — the differ guarantees this. A missing entity here
            // would mean either a corrupt diff or a config mutation
            // between diff and classify (impossible since both are
            // computed under the apply mutex). Be defensive: skip the
            // action rather than throw, so a partial-reconcile is still
            // possible. The coordinator's per-instance fault path will
            // surface anything that actually fails.
            if (newEntityConfig is null && op != ReloadOp.Remove)
            {
                continue;
            }

            var action = new ReloadAction
            {
                Op = op,
                Kind = change.EntityKind,
                EntityId = change.EntityId,
                NewConfig = newEntityConfig,
            };

            // Dedup: a Modified-then-Modified (different paths, same
            // entity) collapses to one Restart. An Added-then-Modified
            // (impossible from a single diff but defensive) keeps the
            // first action — Add wins because the entity wasn't there
            // before.
            actionsByKey.TryAdd((change.EntityKind, change.EntityId), action);
        }

        if (actionsByKey.Count == 0)
        {
            return ConfigurationReloadPlan.Empty;
        }

        var actions = new List<ReloadAction>(actionsByKey.Count);
        foreach (var action in actionsByKey.Values)
        {
            actions.Add(action);
        }

        return new ConfigurationReloadPlan { Actions = actions };
    }

    private static Dictionary<string, T> IndexById<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector)
    {
        var dict = new Dictionary<string, T>(items.Count, StringComparer.Ordinal);
        foreach (var item in items)
        {
            dict.TryAdd(keySelector(item), item);
        }
        return dict;
    }

    private static object? LookupNewEntity(
        ConfigurationEntityKind kind,
        string entityId,
        Dictionary<string, SourceInstanceConfig> sources,
        Dictionary<string, SinkInstanceConfig> sinks,
        Dictionary<string, RouteConfig> routes)
    {
        return kind switch
        {
            ConfigurationEntityKind.Source => sources.TryGetValue(entityId, out var s) ? s : null,
            ConfigurationEntityKind.Sink => sinks.TryGetValue(entityId, out var k) ? (object)k : null,
            ConfigurationEntityKind.Route => routes.TryGetValue(entityId, out var r) ? r : null,
            _ => null,
        };
    }
}
