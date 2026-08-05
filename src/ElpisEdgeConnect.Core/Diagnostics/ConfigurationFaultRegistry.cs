// ============================================================================
// File: Diagnostics/ConfigurationFaultRegistry.cs
// Purpose: Default thread-safe in-memory implementation of
//          IConfigurationFaultRegistry. ConcurrentDictionary-backed so
//          M.P2.2's reload coordinator can register/clear faults from
//          multiple supervisor tasks concurrently without external
//          locking.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.P2.1
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Thread-safe in-memory implementation of
/// <see cref="IConfigurationFaultRegistry"/>. Singleton-scoped in DI.
/// </summary>
public sealed class ConfigurationFaultRegistry : IConfigurationFaultRegistry
{
    // Key is (Kind, InstanceId) — instance id strings are case-sensitive
    // per the Core regex (^[A-Za-z0-9][A-Za-z0-9._-]*$), so Ordinal compares.
    private readonly ConcurrentDictionary<(ConfigurationFaultKind Kind, string InstanceId), ConfigurationFault> _faults
        = new();

    /// <inheritdoc/>
    public void Register(ConfigurationFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        ArgumentException.ThrowIfNullOrEmpty(fault.InstanceId);
        ArgumentException.ThrowIfNullOrEmpty(fault.ErrorCode);
        ArgumentException.ThrowIfNullOrEmpty(fault.Message);

        _faults[(fault.Kind, fault.InstanceId)] = fault;
    }

    /// <inheritdoc/>
    public bool ClearFor(ConfigurationFaultKind kind, string instanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        return _faults.TryRemove((kind, instanceId), out _);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ConfigurationFault> GetFaults()
    {
        // Snapshot the values, sort by ObservedAtUtc ascending so the
        // Studio's Configuration-faults panel renders them in observation
        // order without re-sorting on the client.
        return _faults.Values
            .OrderBy(f => f.ObservedAtUtc)
            .ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ConfigurationFault> GetFaultsFor(ConfigurationFaultKind kind)
    {
        return _faults.Values
            .Where(f => f.Kind == kind)
            .OrderBy(f => f.ObservedAtUtc)
            .ToList();
    }

    /// <inheritdoc/>
    public bool IsFaulted(ConfigurationFaultKind kind, string instanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        return _faults.ContainsKey((kind, instanceId));
    }
}
