// ============================================================================
// File: Diagnostics/ModbusDiagnosticsCollector.cs
// Purpose: Adapter-scoped diagnostics collector. One collector per plan —
//          the adapter rebuilds it whenever the ScanPlan is rebuilt, so
//          per-block history follows the config lifecycle naturally.
//
// Aggregates two things:
//   * Per-block ModbusBlockMetrics (RTT, failure breakdown, timestamps)
//   * Global count of slave-exception responses keyed by (FC, code)
//
// CONCURRENCY: single-writer / multi-reader per ModbusBlockMetrics.
//   The global slave-exception dictionary is behind a small lock because
//   it's Dictionary<K,V> (reader-writer unsafe in .NET).
// Reference: docs/PHASE3_EXECUTION_PLAN.md F5
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Diagnostics;

/// <summary>
/// Collects per-block Modbus runtime metrics and returns immutable
/// snapshots on demand.
/// </summary>
/// <remarks>
/// <para>
/// The collector is built from a <see cref="ScanPlan"/>: every block in
/// the plan gets a metrics bucket pre-registered so snapshots always
/// enumerate every configured block, even ones that have never polled
/// (a silent block is useful diagnostic information).
/// </para>
/// <para>
/// When the adapter rebuilds its plan (config apply, restart), it also
/// constructs a fresh collector. Historical metrics do NOT carry over —
/// this is the deliberate choice documented in the F5 plan: history
/// lives in Core's configuration audit log, not in adapter health.
/// </para>
/// </remarks>
public sealed class ModbusDiagnosticsCollector
{
    private readonly Dictionary<ScanBlockKey, ModbusBlockMetrics> _blocks;
    private readonly Dictionary<(byte Fc, byte Code), long> _slaveExceptionsByCode = new();
    private readonly object _globalLock = new();

    /// <summary>
    /// Build a collector from a plan. Every block in the plan gets an
    /// empty metrics bucket registered up-front.
    /// </summary>
    public ModbusDiagnosticsCollector(ScanPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _blocks = new Dictionary<ScanBlockKey, ModbusBlockMetrics>(plan.TotalBlockCount);
        foreach (var group in plan.Groups)
        {
            foreach (var block in group.Blocks)
            {
                var key = ScanBlockKey.From(group, block);
                if (!_blocks.ContainsKey(key))
                {
                    _blocks[key] = new ModbusBlockMetrics(key);
                }
            }
        }
    }

    /// <summary>
    /// Record the outcome of a transaction dispatched against <paramref name="key"/>.
    /// Classifies the failure kind from the <see cref="ModbusTransactionResult"/>
    /// and bumps both the per-block counters and the global slave-exception map.
    /// </summary>
    internal void RecordTransaction(ScanBlockKey key, ModbusTransactionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!_blocks.TryGetValue(key, out var metrics))
        {
            // Block wasn't in the plan we were built from — probably a
            // test or a stale adapter after a config reload. Skip quietly.
            return;
        }

        var failureKind = TransactionFailureKind.None;
        if (!result.IsSuccess)
        {
            failureKind = result.SlaveExceptionCode != 0
                ? TransactionFailureKind.SlaveException
                : TransactionFailureKind.Transport;
        }

        metrics.RecordTransaction(
            isSuccess: result.IsSuccess,
            elapsed: result.Elapsed,
            retryCount: result.RetryCount,
            failureKind: failureKind,
            errorCode: result.Error?.Code,
            errorCategory: result.Error?.Category);

        if (failureKind == TransactionFailureKind.SlaveException)
        {
            var fc = key.RegisterClass.ToFunctionCode();
            var tallyKey = (fc, result.SlaveExceptionCode);
            lock (_globalLock)
            {
                _slaveExceptionsByCode.TryGetValue(tallyKey, out var current);
                _slaveExceptionsByCode[tallyKey] = current + 1;
            }
        }
    }

    /// <summary>
    /// Record a decode error that happened AFTER a successful executor call
    /// (e.g. the decoder threw because a datatype didn't match the payload).
    /// Counted as a per-block <see cref="ModbusBlockMetricsSnapshot.DecodeErrors"/>
    /// tally, not a transaction failure.
    /// </summary>
    internal void RecordDecodeError(ScanBlockKey key, string? errorCode, ErrorCategory? errorCategory)
    {
        if (_blocks.TryGetValue(key, out var metrics))
        {
            metrics.RecordDecodeError(errorCode, errorCategory);
        }
    }

    /// <summary>Immutable snapshot of every block + the global slave-exception tally.</summary>
    public ModbusDiagnosticsSnapshot Snapshot()
    {
        // Deterministic ordering makes diagnostics diffs readable and
        // health tests stable. Order by (unitId, regClass, addr).
        var blockSnapshots = _blocks.Values
            .OrderBy(m => m.Key.UnitId)
            .ThenBy(m => (int)m.Key.RegisterClass)
            .ThenBy(m => m.Key.StartAddress)
            .Select(m => m.Snapshot())
            .ToList();

        Dictionary<string, long> slaveMap;
        lock (_globalLock)
        {
            slaveMap = new Dictionary<string, long>(
                _slaveExceptionsByCode.Count,
                StringComparer.Ordinal);
            foreach (var kvp in _slaveExceptionsByCode.OrderBy(p => p.Key.Fc).ThenBy(p => p.Key.Code))
            {
                var label = $"0x{kvp.Key.Fc:X2}/0x{kvp.Key.Code:X2}";
                slaveMap[label] = kvp.Value;
            }
        }

        return new ModbusDiagnosticsSnapshot
        {
            Blocks = blockSnapshots,
            SlaveExceptionsByCode = slaveMap,
        };
    }
}
