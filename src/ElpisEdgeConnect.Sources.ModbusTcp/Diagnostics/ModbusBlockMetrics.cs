// ============================================================================
// File: Diagnostics/ModbusBlockMetrics.cs
// Purpose: Mutable, adapter-scoped metrics for one scan block.
//          Records transactions, failure categories, and successful-
//          transaction RTT. Snapshot() returns an immutable copy.
//
// CONCURRENCY MODEL:
//   - Single writer: the adapter's poll loop is serialized per instance
//     by SourceSupervisor.
//   - Concurrent readers: CheckHealthAsync can be called from the
//     management API while polling is in flight.
//   - Counters use Interlocked. The RTT ring buffer + Welford state lives
//     behind a short-lived lock (copy of ≤100 doubles) — a SpinLock
//     equivalent would save nothing measurable at < 1 health-check / sec.
//   - Snapshots are NOT transactionally atomic across fields. Callers
//     must tolerate minor counter skew — documented on
//     ModbusBlockMetricsSnapshot.
// Reference: docs/PHASE3_EXECUTION_PLAN.md F5
// ============================================================================

using System;
using System.Threading;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Diagnostics;

/// <summary>Classification of a failed Modbus transaction for per-block counters.</summary>
internal enum TransactionFailureKind
{
    /// <summary>Success — no counter increment.</summary>
    None = 0,

    /// <summary>Transport-level fault: timeout, socket error, connect failure.</summary>
    Transport = 1,

    /// <summary>Slave returned an exception code (Illegal Function / Illegal Data Address / etc.).</summary>
    SlaveException = 2,

    /// <summary>Executor succeeded but the adapter's decoder rejected the payload.</summary>
    Decode = 3,
}

/// <summary>
/// Mutable metrics for a single <see cref="ScanBlockKey"/>.
/// One instance lives inside <see cref="ModbusDiagnosticsCollector"/>.
/// </summary>
internal sealed class ModbusBlockMetrics
{
    /// <summary>Ring-buffer size for the p95 RTT sample window.</summary>
    public const int RttWindowSize = 100;

    private readonly object _rttLock = new();
    private readonly double[] _rttRing = new double[RttWindowSize];
    private int _rttRingCount;
    private int _rttRingNextIndex;

    // Counters — Interlocked accessible.
    private long _transactions;
    private long _successes;
    private long _failures;
    private long _retries;
    private long _transportErrors;
    private long _slaveExceptions;
    private long _decodeErrors;

    // Welford running mean + min/max/latest. Only written under _rttLock
    // so reads within Snapshot() are consistent with the ring contents.
    private double _mean;
    private long _meanSampleCount;
    private double _min = double.MaxValue;
    private double _max = double.MinValue;
    private double _latest;

    // Timestamps stored as ticks. DateTime.Ticks is a 64-bit value;
    // writes on 64-bit .NET are atomic, but Interlocked.Exchange keeps
    // the semantics explicit and portable if we ever see a 32-bit runtime.
    private long _lastSuccessTicks;  // 0 = never
    private long _lastFailureTicks;  // 0 = never

    // Last-error identity — only stable code + category, no message. Guarded
    // by a cheap lock so Snapshot() sees a consistent pair.
    private readonly object _lastErrorLock = new();
    private string? _lastErrorCode;
    private ErrorCategory? _lastErrorCategory;

    public ModbusBlockMetrics(ScanBlockKey key)
    {
        Key = key;
    }

    public ScanBlockKey Key { get; }

    /// <summary>
    /// Record one transaction. Called by the collector from the adapter's
    /// poll loop — single writer per adapter instance.
    /// </summary>
    public void RecordTransaction(
        bool isSuccess,
        TimeSpan elapsed,
        int retryCount,
        TransactionFailureKind failureKind,
        string? errorCode,
        ErrorCategory? errorCategory)
    {
        Interlocked.Increment(ref _transactions);
        if (retryCount > 0)
        {
            Interlocked.Add(ref _retries, retryCount);
        }

        if (isSuccess)
        {
            Interlocked.Increment(ref _successes);
            Interlocked.Exchange(ref _lastSuccessTicks, DateTime.UtcNow.Ticks);

            // Only successful transactions feed the RTT stats. Failed
            // transactions carry the full retry budget in their elapsed time
            // and would skew latency metrics that operators care about.
            var rttMs = elapsed.TotalMilliseconds;
            lock (_rttLock)
            {
                _rttRing[_rttRingNextIndex % RttWindowSize] = rttMs;
                _rttRingNextIndex++;
                if (_rttRingCount < RttWindowSize)
                {
                    _rttRingCount++;
                }

                // Welford's online mean — numerically stable vs naive sum/count
                // across millions of samples.
                _meanSampleCount++;
                _mean += (rttMs - _mean) / _meanSampleCount;

                if (rttMs < _min) _min = rttMs;
                if (rttMs > _max) _max = rttMs;
                _latest = rttMs;
            }
            return;
        }

        // Failure path — classify and tally.
        Interlocked.Increment(ref _failures);
        Interlocked.Exchange(ref _lastFailureTicks, DateTime.UtcNow.Ticks);

        switch (failureKind)
        {
            case TransactionFailureKind.Transport:
                Interlocked.Increment(ref _transportErrors);
                break;
            case TransactionFailureKind.SlaveException:
                Interlocked.Increment(ref _slaveExceptions);
                break;
            case TransactionFailureKind.Decode:
                Interlocked.Increment(ref _decodeErrors);
                break;
            case TransactionFailureKind.None:
            default:
                // A failure with no classification is a bug in the caller —
                // still tally it in _failures so it's visible, but don't
                // increment any specific counter.
                break;
        }

        if (errorCode is not null || errorCategory is not null)
        {
            lock (_lastErrorLock)
            {
                _lastErrorCode = errorCode;
                _lastErrorCategory = errorCategory;
            }
        }
    }

    /// <summary>
    /// Record a decode-time failure that occurred AFTER a successful
    /// executor round-trip (bad byte order, truncated payload, …). Called
    /// by the adapter, not the executor — decode errors don't surface
    /// through <see cref="ModbusTransactionResult"/>.
    /// </summary>
    public void RecordDecodeError(string? errorCode, ErrorCategory? errorCategory)
    {
        Interlocked.Increment(ref _decodeErrors);
        // A decode error means one tag within an otherwise-successful block
        // was lost. The block-level Failures counter does not increment —
        // the executor result was a success — but operators still want to
        // see it.
        Interlocked.Exchange(ref _lastFailureTicks, DateTime.UtcNow.Ticks);
        if (errorCode is not null || errorCategory is not null)
        {
            lock (_lastErrorLock)
            {
                _lastErrorCode = errorCode;
                _lastErrorCategory = errorCategory;
            }
        }
    }

    /// <summary>Immutable view of the metrics at the moment of the call.</summary>
    public ModbusBlockMetricsSnapshot Snapshot()
    {
        // Copy the ring under the lock so the read side doesn't see torn
        // writes and the sort for p95 can run outside the critical section.
        double[] rttCopy;
        int rttCount;
        double mean, min, max, latest;
        long meanSamples;

        lock (_rttLock)
        {
            rttCount = _rttRingCount;
            rttCopy = new double[rttCount];
            if (rttCount > 0)
            {
                // The ring may have wrapped; the exact order does not matter
                // for percentile or min/max computation, so a straight copy
                // of the first `count` slots is sufficient.
                if (rttCount < RttWindowSize)
                {
                    Array.Copy(_rttRing, rttCopy, rttCount);
                }
                else
                {
                    // Full ring — the physical start index is wherever the
                    // next write would go, but again order doesn't matter.
                    Array.Copy(_rttRing, rttCopy, RttWindowSize);
                }
            }
            mean = _mean;
            meanSamples = _meanSampleCount;
            min = _min;
            max = _max;
            latest = _latest;
        }

        string? errorCode;
        ErrorCategory? errorCategory;
        lock (_lastErrorLock)
        {
            errorCode = _lastErrorCode;
            errorCategory = _lastErrorCategory;
        }

        return new ModbusBlockMetricsSnapshot
        {
            Key = Key,
            Transactions = Interlocked.Read(ref _transactions),
            Successes = Interlocked.Read(ref _successes),
            Failures = Interlocked.Read(ref _failures),
            Retries = Interlocked.Read(ref _retries),
            TransportErrors = Interlocked.Read(ref _transportErrors),
            SlaveExceptions = Interlocked.Read(ref _slaveExceptions),
            DecodeErrors = Interlocked.Read(ref _decodeErrors),
            RttMeanMs = meanSamples > 0 ? mean : null,
            RttMinMs = meanSamples > 0 ? min : null,
            RttMaxMs = meanSamples > 0 ? max : null,
            RttLatestMs = meanSamples > 0 ? latest : null,
            RttP95Ms = ComputeP95(rttCopy),
            LastSuccessAt = TicksToUtc(Interlocked.Read(ref _lastSuccessTicks)),
            LastFailureAt = TicksToUtc(Interlocked.Read(ref _lastFailureTicks)),
            LastErrorCode = errorCode,
            LastErrorCategory = errorCategory?.ToString(),
        };
    }

    private static double? ComputeP95(double[] samples)
    {
        if (samples.Length == 0)
        {
            return null;
        }
        // Sort a copy so the order-dependent ring semantics don't matter.
        Array.Sort(samples);
        // Nearest-rank method: ceil(0.95 * N) - 1 index, clamped.
        var idx = (int)Math.Ceiling(0.95 * samples.Length) - 1;
        if (idx < 0) idx = 0;
        if (idx >= samples.Length) idx = samples.Length - 1;
        return samples[idx];
    }

    private static DateTime? TicksToUtc(long ticks)
        => ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
}
