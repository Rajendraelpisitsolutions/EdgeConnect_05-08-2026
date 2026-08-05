// ============================================================================
// File: ModbusConnectionManager.cs
// Purpose: Owns the Modbus TCP connection lifecycle — connect, disconnect,
//          exponential-backoff retry, and a circuit breaker that suspends
//          connect attempts after N consecutive failures.
//
//          Unlike FOCAS2 the Modbus library is thread-safe per-instance, so
//          there is no dedicated worker thread. The manager serializes
//          transactions through a SemaphoreSlim to keep the wire
//          single-in-flight (matches the IModbusClient contract).
// Reference: PHASE3_EXECUTION_PLAN.md §5 (Connection Manager)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Circuit-breaker state for <see cref="ModbusConnectionManager"/>.
/// </summary>
internal enum CircuitBreakerState
{
    /// <summary>Normal operation — connect attempts proceed on demand.</summary>
    Closed = 0,

    /// <summary>
    /// Too many consecutive failures — connect attempts are suppressed
    /// until the breaker's cool-down window elapses.
    /// </summary>
    Open = 1,

    /// <summary>
    /// Cool-down elapsed — the next connect attempt is a single probe.
    /// Success closes the breaker; another failure re-opens it.
    /// </summary>
    HalfOpen = 2,
}

/// <summary>
/// Manages the Modbus TCP connection's lifecycle: connect / reconnect,
/// exponential backoff, circuit breaker, and a single-in-flight wire lock.
/// </summary>
internal sealed class ModbusConnectionManager : IAsyncDisposable
{
    private readonly IModbusClient _client;
    private readonly ModbusTcpSourceConfiguration _config;
    private readonly string _instanceId;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _wireLock = new(1, 1);

    private int _consecutiveFailures;
    private DateTimeOffset _nextRetryAt = DateTimeOffset.MinValue;
    private CircuitBreakerState _breakerState = CircuitBreakerState.Closed;
    private DateTimeOffset _breakerOpenedAt = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastSuccessAt;
    private bool _disposed;

    public ModbusConnectionManager(
        IModbusClient client,
        ModbusTcpSourceConfiguration config,
        string instanceId,
        ILogger logger,
        TimeProvider? time = null)
    {
        _client = client;
        _config = config;
        _instanceId = instanceId;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True if the underlying client reports an open TCP connection.</summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>Number of consecutive connect failures (cleared on success).</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>Current circuit-breaker state.</summary>
    internal CircuitBreakerState BreakerState => _breakerState;

    /// <summary>UTC timestamp of the most recent successful connect, if any.</summary>
    public DateTimeOffset? LastSuccessAt => _lastSuccessAt;

    /// <summary>
    /// Ensure a live connection exists. Returns true if one is open (now or
    /// was already open), false if the call was suppressed by backoff or the
    /// circuit breaker. Fatal connect errors increment the failure counter
    /// and may trip the breaker; they do not throw out of this method.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_client.IsConnected)
        {
            return true;
        }

        var now = _time.GetUtcNow();

        // Circuit breaker gate
        if (_breakerState == CircuitBreakerState.Open)
        {
            var resetAt = _breakerOpenedAt.AddMilliseconds(_config.CircuitBreakerResetMs);
            if (now < resetAt)
            {
                _logger.LogDebug(
                    "Modbus source {InstanceId}: circuit breaker OPEN, connect suppressed (resets at {ResetAt:O}).",
                    _instanceId, resetAt);
                return false;
            }
            _breakerState = CircuitBreakerState.HalfOpen;
            _logger.LogDebug(
                "Modbus source {InstanceId}: circuit breaker HALF-OPEN — permitting probe connect.",
                _instanceId);
        }

        // Backoff gate (ignored when breaker is HALF-OPEN — the probe runs
        // immediately so we can observe whether the peer has recovered).
        if (_breakerState != CircuitBreakerState.HalfOpen && now < _nextRetryAt)
        {
            return false;
        }

        try
        {
            await _client.ConnectAsync(
                _config.Host,
                _config.Port,
                _config.Encapsulation,
                TimeSpan.FromMilliseconds(_config.ConnectTimeoutMs),
                TimeSpan.FromMilliseconds(_config.RequestTimeoutMs),
                ct).ConfigureAwait(false);

            OnConnectSuccess(now);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ModbusFatalException ex)
        {
            OnConnectFailure(ex, now);
            return false;
        }
    }

    /// <summary>
    /// Disconnect the underlying client. Idempotent.
    /// </summary>
    public void Disconnect()
    {
        try
        {
            _client.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Modbus source {InstanceId}: exception during disconnect (ignored).",
                _instanceId);
        }
    }

    /// <summary>
    /// Called by the transaction executor when a request fails with a fatal
    /// transport error (socket reset, timeout past the retry budget).
    /// Disconnects the client and starts backoff so the next poll re-opens.
    /// </summary>
    public void HandleFatalTransportError(ModbusFatalException error)
    {
        var now = _time.GetUtcNow();
        _logger.LogWarning(
            "Modbus source {InstanceId}: fatal transport error ({Code}) — dropping connection.",
            _instanceId, error.ErrorCode);
        Disconnect();
        OnConnectFailure(error, now);
    }

    /// <summary>
    /// Acquire the single-in-flight wire lock. Use with <c>await using</c>
    /// to guarantee release even if the transaction throws. Callers MUST
    /// hold this lock for the entire duration of a Modbus read call.
    /// </summary>
    public async Task<IDisposable> AcquireWireLockAsync(CancellationToken ct)
    {
        await _wireLock.WaitAsync(ct).ConfigureAwait(false);
        return new WireLockReleaser(_wireLock);
    }

    /// <summary>
    /// Completes once no transaction is in flight on the wire (the single-in-flight
    /// lock is momentarily acquirable). Used by retirement as an <b>in-flight
    /// indicator only</b>. It must NEVER gate the transport close — <see cref="Disconnect"/>
    /// is lock-free precisely so a wedged read holding this lock can be interrupted
    /// by closing the socket without first acquiring the lock (avoids deadlock).
    /// </summary>
    public async Task WaitForWireIdleAsync(CancellationToken ct = default)
    {
        await _wireLock.WaitAsync(ct).ConfigureAwait(false);
        _wireLock.Release();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Disconnect();
        await _client.DisposeAsync().ConfigureAwait(false);
        _wireLock.Dispose();
    }

    // =========================================================================
    // PRIVATE
    // =========================================================================

    private void OnConnectSuccess(DateTimeOffset now)
    {
        _consecutiveFailures = 0;
        _nextRetryAt = DateTimeOffset.MinValue;
        _breakerState = CircuitBreakerState.Closed;
        _lastSuccessAt = now;
        _logger.LogInformation(
            "Modbus source {InstanceId}: connected to {Host}:{Port} ({Encapsulation}).",
            _instanceId, _config.Host, _config.Port, _config.Encapsulation);
    }

    private void OnConnectFailure(ModbusFatalException ex, DateTimeOffset now)
    {
        _consecutiveFailures++;

        var backoffMs = (int)Math.Min(
            _config.InitialBackoffMs *
                Math.Pow(_config.BackoffMultiplier, Math.Min(_consecutiveFailures - 1, 10)),
            _config.MaxBackoffMs);
        _nextRetryAt = now.AddMilliseconds(backoffMs);

        _logger.LogWarning(
            "Modbus source {InstanceId}: connect to {Host}:{Port} failed ({Code}) — consecutive failures {N}, next retry in {BackoffMs}ms.",
            _instanceId, _config.Host, _config.Port, ex.ErrorCode, _consecutiveFailures, backoffMs);

        if (_consecutiveFailures >= _config.CircuitBreakerThreshold)
        {
            _breakerState = CircuitBreakerState.Open;
            _breakerOpenedAt = now;
            _logger.LogWarning(
                "Modbus source {InstanceId}: circuit breaker OPEN after {N} consecutive failures (cool-down {ResetMs}ms).",
                _instanceId, _consecutiveFailures, _config.CircuitBreakerResetMs);
        }
    }

    private sealed class WireLockReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
