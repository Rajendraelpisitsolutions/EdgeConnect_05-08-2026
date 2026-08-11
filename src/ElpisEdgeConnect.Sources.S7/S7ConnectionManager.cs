// ============================================================================
// File: S7ConnectionManager.cs
// Purpose: Owns the IS7Client connection lifecycle, exponential
//          backoff after connect failures, and a 3-state circuit
//          breaker (Closed / Open / HalfOpen). Mirrors
//          ModbusConnectionManager's contract — the adapter calls
//          EnsureConnectedAsync at the top of every poll and trusts
//          the manager's verdict.
//
//          The breaker exists to suppress the storm of failed
//          connects that would otherwise hammer the PLC whenever it
//          (or the OT VLAN) goes away. Once tripped Open, it waits
//          CircuitBreakerResetMs before probing once (HalfOpen) and
//          either resuming (Closed) or re-tripping.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I (mirrors
//            ModbusConnectionManager)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Manages the S7 connection: connect, disconnect, backoff after
/// failure, circuit breaker.
/// </summary>
public sealed class S7ConnectionManager : IAsyncDisposable
{
    private readonly IS7Client _client;
    private readonly S7SourceConfiguration _config;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _wireLock = new(1, 1);

    private int _consecutiveFailures;
    private DateTimeOffset _nextRetryAt = DateTimeOffset.MinValue;
    private CircuitBreakerState _breakerState = CircuitBreakerState.Closed;
    private DateTimeOffset _breakerOpenedAt;
    private DateTimeOffset? _lastSuccessAt;

    /// <summary>Construct a manager around an injected transport.</summary>
    public S7ConnectionManager(
        IS7Client client,
        S7SourceConfiguration config,
        ILogger logger,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _config = config;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True when the underlying client is connected.</summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>Current breaker state (Closed / Open / HalfOpen).</summary>
    public CircuitBreakerState BreakerState => _breakerState;

    /// <summary>Count of consecutive connect failures since last success.</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>UTC timestamp of the last successful connect, if any.</summary>
    public DateTimeOffset? LastSuccessAt => _lastSuccessAt;

    /// <summary>
    /// Ensure the connection is live. Returns true when the client is
    /// connected by the end of the call. Returns false (with no thrown
    /// exception) when the breaker suppresses the attempt or backoff
    /// is still pending.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
        {
            return true;
        }

        // Per-instance serialization — Sharp7 is single-threaded per S7Client.
        await _wireLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                return true;
            }

            var now = _time.GetUtcNow();

            // Breaker gate.
            switch (_breakerState)
            {
                case CircuitBreakerState.Open:
                    if (now < _breakerOpenedAt + TimeSpan.FromMilliseconds(_config.CircuitBreakerResetMs))
                    {
                        return false;
                    }
                    _breakerState = CircuitBreakerState.HalfOpen;
                    _logger.LogInformation(
                        "S7 connection {Host}: breaker reset elapsed → HalfOpen, probing.",
                        _config.Host);
                    break;
                case CircuitBreakerState.HalfOpen:
                    // Probe permitted regardless of backoff timing.
                    break;
                case CircuitBreakerState.Closed:
                    if (now < _nextRetryAt)
                    {
                        return false;
                    }
                    break;
            }

            // Attempt connect.
            var result = await _client.ConnectAsync(
                _config.Host,
                _config.Port,
                _config.Rack,
                _config.Slot,
                _config.ConnectionType,
                TimeSpan.FromMilliseconds(_config.ConnectTimeoutMs),
                ct).ConfigureAwait(false);

            if (result.Success)
            {
                OnConnectSuccess();
                return true;
            }

            OnConnectFailure(result.ErrorCode, result.ErrorText);
            return false;
        }
        finally
        {
            _wireLock.Release();
        }
    }

    /// <summary>
    /// Read raw bytes via the held connection. Acquires the wire-lock
    /// so concurrent callers serialize. On a fatal transport error the
    /// connection is dropped and the breaker bookkeeping advanced; the
    /// caller can re-try via <see cref="EnsureConnectedAsync"/>.
    /// </summary>
    public async Task<S7OperationResult> ReadAreaAsync(
        S7MemoryArea area,
        int dbNumber,
        int startByte,
        int byteCount,
        byte[] buffer,
        CancellationToken ct)
    {
        await _wireLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_client.IsConnected)
            {
                return S7OperationResult.Fail(-2, "Not connected.");
            }
            var result = await _client.ReadAreaAsync(area, dbNumber, startByte, byteCount, buffer, ct).ConfigureAwait(false);
            if (!result.Success && IsFatalTransportError(result.ErrorCode))
            {
                _logger.LogWarning(
                    "S7 read {Area} DB={Db} start={Start} count={Count} hit fatal transport error {Code} ({Text}); dropping connection.",
                    area, dbNumber, startByte, byteCount, result.ErrorCode, result.ErrorText);
                _client.Disconnect();
                OnConnectFailure(result.ErrorCode, result.ErrorText);
            }
            return result;
        }
        finally
        {
            _wireLock.Release();
        }
    }

    /// <summary>Drop the underlying connection.</summary>
    public void Disconnect()
    {
        try
        {
            _client.Disconnect();
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>Acquire the single-in-flight wire lock. Use with <c>await using</c>.</summary>
    public async Task<IDisposable> AcquireWireLockAsync(CancellationToken ct = default)
    {
        await _wireLock.WaitAsync(ct).ConfigureAwait(false);
        return new WireLockReleaser(_wireLock);
    }

    /// <summary>
    /// Completes once no transaction is in flight (the single-in-flight lock is
    /// momentarily acquirable). Used by retirement as an <b>in-flight indicator
    /// only</b>; it must NEVER gate the transport close — <see cref="Disconnect"/>
    /// is lock-free so a wedged read holding this lock can be interrupted by
    /// closing the socket without first acquiring the lock (avoids deadlock).
    /// </summary>
    public async Task WaitForWireIdleAsync(CancellationToken ct = default)
    {
        await _wireLock.WaitAsync(ct).ConfigureAwait(false);
        _wireLock.Release();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Disconnect();
        _wireLock.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class WireLockReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }

    private void OnConnectSuccess()
    {
        _consecutiveFailures = 0;
        _nextRetryAt = DateTimeOffset.MinValue;
        _breakerState = CircuitBreakerState.Closed;
        _lastSuccessAt = _time.GetUtcNow();
    }

    private void OnConnectFailure(int errorCode, string errorText)
    {
        _consecutiveFailures++;
        var backoffMs = ComputeBackoffMs(_consecutiveFailures);
        _nextRetryAt = _time.GetUtcNow() + TimeSpan.FromMilliseconds(backoffMs);

        if (_breakerState == CircuitBreakerState.HalfOpen
            || _consecutiveFailures >= _config.CircuitBreakerThreshold)
        {
            if (_breakerState != CircuitBreakerState.Open)
            {
                _logger.LogWarning(
                    "S7 connection {Host} breaker → Open after {Failures} consecutive failures (last code {Code}: {Text}).",
                    _config.Host, _consecutiveFailures, errorCode, errorText);
            }
            _breakerState = CircuitBreakerState.Open;
            _breakerOpenedAt = _time.GetUtcNow();
        }
    }

    private int ComputeBackoffMs(int failureCount)
    {
        // Exponential: initial * multiplier^(failures-1), clamped.
        var attempt = Math.Max(1, failureCount);
        var raw = _config.InitialBackoffMs * Math.Pow(_config.BackoffMultiplier, attempt - 1);
        var capped = (int)Math.Min(raw, _config.MaxBackoffMs);

        // Equal jitter (AWS "Exponential Backoff And Jitter"), same shape as
        // Focas2ConnectionManager.IncrementBackoff: keep at least half the
        // computed delay, then spread the upper half randomly.
        //
        // WHY (do not remove as noise): several S7 sources typically fail at
        // the SAME instant, because they share one cause — a switch blip, a
        // rack power cycle, an OT VLAN hiccup. Without jitter they all count
        // failures on an identical deterministic ramp, so they retry in
        // lockstep and every reconnect lands in the same millisecond. An S7
        // CPU has a small fixed pool of PG/OP connection resources; a
        // simultaneous burst exhausts it, the connects fail, and the failure
        // re-synchronises the ramp — the recovery attempt becomes the next
        // outage. De-correlating the retries lets them arrive spread out.
        //
        // Spread: [50%, 100%) of the capped delay. It never exceeds
        // MaxBackoffMs (so no configured cap is overshot) and never drops
        // below half the intended wait (so the ramp still means something).
        // Math.Max(1, …) guards the degenerate case where a very small
        // configured backoff would integer-divide to a zero delay.
        var half = capped / 2;
        return Math.Max(1, half + (int)(Random.Shared.NextDouble() * half));
    }

    private static bool IsFatalTransportError(int code)
    {
        // Sharp7 returns negative codes for transport-level failures
        // (TCP closed, etc.) and high-bit-set codes for ISO/PDU
        // errors. We treat anything that isn't 0 OR a documented
        // "harmless" code as fatal. The conservative default at MVP
        // is "any non-zero is fatal" — the caller can still recover
        // via re-connect.
        return code != 0;
    }
}

/// <summary>Three-state circuit-breaker view.</summary>
public enum CircuitBreakerState
{
    /// <summary>Closed — normal operation, attempts proceed under backoff schedule.</summary>
    Closed = 0,
    /// <summary>Open — attempts suppressed entirely until reset window elapses.</summary>
    Open = 1,
    /// <summary>HalfOpen — one probe permitted; success closes, failure re-opens.</summary>
    HalfOpen = 2,
}
