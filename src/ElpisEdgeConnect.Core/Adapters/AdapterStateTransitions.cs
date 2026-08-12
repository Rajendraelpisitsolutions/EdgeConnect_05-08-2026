// ============================================================================
// File: Adapters/AdapterStateTransitions.cs
// Purpose: Declarative table of legal adapter state transitions. Used by
//          adapter base classes and the runtime to enforce valid lifecycle.
// Reference: PHASE1_EXECUTION_PLAN.md A2 "state machine transitions"
// ============================================================================

using System.Collections.Frozen;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Declares which <see cref="AdapterState"/> transitions are legal. The
/// runtime consults this before applying a state change so that bugs in
/// adapter implementations cannot corrupt lifecycle state.
/// </summary>
/// <remarks>
/// The transition table is stored in <see cref="FrozenDictionary{TKey, TValue}"/>
/// of <see cref="FrozenSet{T}"/> so that callers cannot cast a returned set
/// to a mutable type and corrupt the global table.
/// </remarks>
public static class AdapterStateTransitions
{
    private static readonly FrozenSet<AdapterState> Empty = FrozenSet<AdapterState>.Empty;

    private static readonly FrozenDictionary<AdapterState, FrozenSet<AdapterState>> Allowed =
        new Dictionary<AdapterState, FrozenSet<AdapterState>>
        {
            [AdapterState.Created] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Initializing,
                AdapterState.Blocked,
                AdapterState.Failed,
            }),
            [AdapterState.Initializing] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Initialized,
                AdapterState.Failed,
            }),
            [AdapterState.Initialized] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Starting,
                AdapterState.Stopped,
                AdapterState.Failed,
            }),
            [AdapterState.Starting] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Running,
                AdapterState.Failed,
            }),
            [AdapterState.Running] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Degraded,
                AdapterState.Stopping,
                AdapterState.Failed,
            }),
            [AdapterState.Degraded] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Running,
                AdapterState.Stopping,
                AdapterState.Failed,
            }),
            [AdapterState.Stopping] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Stopped,
                AdapterState.Failed,
            }),
            [AdapterState.Stopped] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Starting,
                AdapterState.Initializing,
            }),
            [AdapterState.Failed] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Initializing, // recovery via re-init
                AdapterState.Stopped,
            }),
            [AdapterState.Blocked] = FrozenSet.ToFrozenSet(new[]
            {
                AdapterState.Initializing, // license reinstated
                AdapterState.Stopped,
            }),
        }.ToFrozenDictionary();

    /// <summary>
    /// True if <paramref name="to"/> is a legal transition from
    /// <paramref name="from"/>.
    /// </summary>
    public static bool IsAllowed(AdapterState from, AdapterState to)
    {
        return Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>
    /// Enumerate the legal next states from a given state. Returns a frozen
    /// set that cannot be cast and mutated.
    /// </summary>
    public static IReadOnlySet<AdapterState> GetAllowedTransitions(AdapterState from)
    {
        return Allowed.TryGetValue(from, out var targets) ? targets : Empty;
    }
}
