// ============================================================================
// File: EthernetIpConnectionManager.cs
// Purpose: Owns the EtherNet/IP CIP session lifecycle — connect, disconnect,
//          exponential-backoff retry, and a circuit breaker that suspends
//          connect attempts after N consecutive failures. Serializes on-wire
//          reads through a SemaphoreSlim. Mirrors ModbusConnectionManager.
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §3.1
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>Circuit-breaker state for <see cref="EthernetIpConnectionManager"/>.</summary>
internal enum EthernetIpBreakerState
{
    /// <summary>Normal operation — connect attempts proceed on demand.</summary>
    Closed = 0,

    /// <summary>Too many consecutive failures — connect attempts are suppressed.</summary>
    Open = 1,

    /// <summary>Cool-down elapsed — the next connect attempt is a single probe.</summary>
    HalfOpen = 2,
}

/// <summary>
/// Manages the EtherNet/IP CIP session lifecycle: connect / reconnect,
/// exponential backoff, circuit breaker, and a single-in-flight wire lock.
/// </summary>
internal sealed class EthernetIpConnectionManager : IAsyncDisposable
{
    private readonly IEthernetIpClient _client;
    private readonly EthernetIpSourceConfiguration _config;
    private readonly string _instanceId;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _wireLock = new(1, 1);
    private readonly EthernetIpConnectionParameters _parameters;

    private int _consecutiveFailures;
    private DateTimeOffset _nextRetryAt = DateTimeOffset.MinValue;
    private EthernetIpBreakerState _breakerState = EthernetIpBreakerState.Closed;
    private DateTimeOffset _breakerOpenedAt = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastSuccessAt;
    private bool _disposed;

    public EthernetIpConnectionManager(
        IEthernetIpClient client,
        EthernetIpSourceConfiguration config,
        string instanceId,
        ILogger logger,
        TimeProvider? time = null)
    {
        _client = client;
        _config = config;
        _instanceId = instanceId;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _parameters = new EthernetIpConnectionParameters
        {
            Host = config.Host,
            Path = config.Path,
            CpuFamily = config.CpuFamily,
            ConnectTimeout = TimeSpan.FromMilliseconds(config.ConnectTimeoutMs),
            RequestTimeout = TimeSpan.FromMilliseconds(config.RequestTimeoutMs),
        };
    }

    /// <summary>True if the underlying client reports a usable CIP session.</summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>Number of consecutive connect failures (cleared on success).</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>Current circuit-breaker state.</summary>
    internal EthernetIpBreakerState BreakerState => _breakerState;

    /// <summary>UTC timestamp of the most recent successful connect, if any.</summary>
    public DateTimeOffset? LastSuccessAt => _lastSuccessAt;

    /// <summary>
    /// Ensure a live CIP session exists. Returns true if one is open, false if
    /// the call was suppressed by backoff or the circuit breaker. Fatal connect
    /// errors increment the failure counter and may trip the breaker; they do
    /// not throw out of this method.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_client.IsConnected)
        {
            return true;
        }

        var now = _time.GetUtcNow();

        if (_breakerState == EthernetIpBreakerState.Open)
        {
            var resetAt = _breakerOpenedAt.AddMilliseconds(_config.CircuitBreakerResetMs);
            if (now < resetAt)
            {
                _logger.LogDebug(
                    "EtherNet/IP source {InstanceId}: circuit breaker OPEN, connect suppressed (resets at {ResetAt:O}).",
                    _instanceId, resetAt);
                return false;
            }
            _breakerState = EthernetIpBreakerState.HalfOpen;
            _logger.LogDebug(
                "EtherNet/IP source {InstanceId}: circuit breaker HALF-OPEN — permitting probe connect.",
                _instanceId);
        }

        if (_breakerState != EthernetIpBreakerState.HalfOpen && now < _nextRetryAt)
        {
            return false;
        }

        try
        {
            await _client.ConnectAsync(_parameters, ct).ConfigureAwait(false);
            OnConnectSuccess(now);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EthernetIpFatalException ex)
        {
            OnConnectFailure(ex, now);
            return false;
        }
    }

    /// <summary>Disconnect the underlying client. Idempotent.</summary>
    public void Disconnect()
    {
        try
        {
            _client.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "EtherNet/IP source {InstanceId}: exception during disconnect (ignored).",
                _instanceId);
        }
    }

    /// <summary>
    /// Called by the poll loop when a read fails with a fatal transport error.
    /// Disconnects the client and starts backoff so the next poll re-opens.
    /// </summary>
    public void HandleFatalTransportError(EthernetIpFatalException error)
    {
        var now = _time.GetUtcNow();
        _logger.LogWarning(
            "EtherNet/IP source {InstanceId}: fatal transport error ({Code}) — dropping session.",
            _instanceId, error.ErrorCode);
        Disconnect();
        OnConnectFailure(error, now);
    }

    /// <summary>
    /// Acquire the single-in-flight wire lock. Use with <c>using</c> to
    /// guarantee release. Callers MUST hold this for the entire read.
    /// </summary>
    public async Task<IDisposable> AcquireWireLockAsync(CancellationToken ct)
    {
        await _wireLock.WaitAsync(ct).ConfigureAwait(false);
        return new WireLockReleaser(_wireLock);
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
        _breakerState = EthernetIpBreakerState.Closed;
        _lastSuccessAt = now;
        _logger.LogInformation(
            "EtherNet/IP source {InstanceId}: session ready for {Host} (path '{Path}', {Family}).",
            _instanceId, _config.Host, _config.Path, _config.CpuFamily);
    }

    private void OnConnectFailure(EthernetIpFatalException ex, DateTimeOffset now)
    {
        _consecutiveFailures++;

        var cappedMs = (int)Math.Min(
            _config.InitialBackoffMs *
                Math.Pow(_config.BackoffMultiplier, Math.Min(_consecutiveFailures - 1, 10)),
            _config.MaxBackoffMs);

        // Equal jitter (AWS "Exponential Backoff And Jitter"), same shape as
        // Focas2ConnectionManager.IncrementBackoff: keep at least half the
        // computed delay, then spread the upper half randomly.
        //
        // WHY (do not remove as noise): EtherNet/IP sources rarely fail one at
        // a time — a switch blip, a rack power cycle or a plant-wide network
        // hiccup drops every CIP session at the same instant. Without jitter
        // each source walks the identical deterministic ramp from that shared
        // instant, so all the re-registrations arrive together. A Logix
        // controller has a hard, small cap on concurrent CIP connections;
        // a simultaneous burst exhausts it, the sessions are refused, and the
        // shared failure re-synchronises the ramp — so the recovery attempt is
        // what causes the next outage. De-correlating the retries avoids the
        // reconnect storm.
        //
        // Spread: [50%, 100%) of the capped delay. It never exceeds
        // MaxBackoffMs (so no configured cap is overshot) and never drops
        // below half the intended wait (so the ramp still means something).
        // Math.Max(1, …) guards the degenerate case where a very small
        // configured backoff would integer-divide to a zero delay.
        var half = cappedMs / 2;
        var backoffMs = Math.Max(1, half + (int)(Random.Shared.NextDouble() * half));
        _nextRetryAt = now.AddMilliseconds(backoffMs);

        _logger.LogWarning(
            "EtherNet/IP source {InstanceId}: session to {Host} failed ({Code}) — consecutive failures {N}, next retry in {BackoffMs}ms (jittered).",
            _instanceId, _config.Host, ex.ErrorCode, _consecutiveFailures, backoffMs);

        if (_consecutiveFailures >= _config.CircuitBreakerThreshold)
        {
            _breakerState = EthernetIpBreakerState.Open;
            _breakerOpenedAt = now;
            _logger.LogWarning(
                "EtherNet/IP source {InstanceId}: circuit breaker OPEN after {N} consecutive failures (cool-down {ResetMs}ms).",
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
