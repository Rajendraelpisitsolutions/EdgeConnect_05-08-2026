// ============================================================================
// File: MelsecConnectionManager.cs
// Purpose: Owns the IMelsecClient lifecycle: connect, exponential backoff, a
//          3-state circuit breaker, and single-in-flight read serialization.
//          Mirrors S7ConnectionManager. Transport/malformed read failures drop
//          the connection and advance the breaker (so a wedged/desynced socket
//          is torn down and reconnected — ADR-0033 Rule 5); protocol end-code
//          responses keep the connection. WaitForWireIdleAsync is an in-flight
//          indicator only and never gates the lock-free Disconnect.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>Three-state circuit-breaker view.</summary>
public enum MelsecCircuitBreakerState
{
    /// <summary>Normal operation; attempts proceed under the backoff schedule.</summary>
    Closed = 0,

    /// <summary>Attempts suppressed until the reset window elapses.</summary>
    Open = 1,

    /// <summary>One probe permitted; success closes, failure re-opens.</summary>
    HalfOpen = 2,
}

/// <summary>Manages the MELSEC connection: connect, backoff, breaker, single-in-flight reads.</summary>
public sealed class MelsecConnectionManager : IAsyncDisposable
{
    private readonly IMelsecClient _client;
    private readonly MelsecSourceConfiguration _config;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _wireLock = new(1, 1);

    private int _consecutiveFailures;
    private DateTimeOffset _nextRetryAt = DateTimeOffset.MinValue;
    private MelsecCircuitBreakerState _breakerState = MelsecCircuitBreakerState.Closed;
    private DateTimeOffset _breakerOpenedAt;
    private DateTimeOffset? _lastSuccessAt;

    /// <summary>Construct a manager around an injected transport.</summary>
    public MelsecConnectionManager(
        IMelsecClient client,
        MelsecSourceConfiguration config,
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

    /// <summary>Current breaker state.</summary>
    public MelsecCircuitBreakerState BreakerState => _breakerState;

    /// <summary>Consecutive connect/transport failures since the last success.</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>UTC of the last successful connect, if any.</summary>
    public DateTimeOffset? LastSuccessAt => _lastSuccessAt;

    /// <summary>
    /// Ensure the connection is live. Returns true when connected by the end of
    /// the call; false (no exception) when the breaker or backoff suppresses the
    /// attempt.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
        {
            return true;
        }

        await _wireLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                return true;
            }

            var now = _time.GetUtcNow();
            switch (_breakerState)
            {
                case MelsecCircuitBreakerState.Open:
                    if (now < _breakerOpenedAt + TimeSpan.FromMilliseconds(_config.CircuitBreakerResetMs))
                    {
                        return false;
                    }
                    _breakerState = MelsecCircuitBreakerState.HalfOpen;
                    _logger.LogInformation("MELSEC connection {Host}: breaker reset elapsed → HalfOpen, probing.", _config.Host);
                    break;
                case MelsecCircuitBreakerState.HalfOpen:
                    break;
                case MelsecCircuitBreakerState.Closed:
                    if (now < _nextRetryAt)
                    {
                        return false;
                    }
                    break;
            }

            var result = await _client.ConnectAsync(
                _config.Host, _config.Port, TimeSpan.FromMilliseconds(_config.ConnectTimeoutMs), ct).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                OnConnectSuccess();
                return true;
            }

            OnConnectFailure(result.Message);
            return false;
        }
        finally
        {
            _wireLock.Release();
        }
    }

    /// <summary>
    /// Read <paramref name="points"/> words via the held connection (single
    /// in-flight). A transport or malformed-frame failure drops the connection
    /// and advances the breaker; a protocol end-code response leaves the
    /// connection intact.
    /// </summary>
    public async Task<MelsecClientResult> ReadWordsAsync(
        Wire.MelsecDeviceCode device, int headDeviceNumber, int points, CancellationToken ct)
    {
        await _wireLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_client.IsConnected)
            {
                return MelsecClientResult.Transport("not connected");
            }

            var result = await _client.ReadWordsAsync(device, headDeviceNumber, points, ct).ConfigureAwait(false);

            if (result.Status is MelsecClientStatus.TransportError or MelsecClientStatus.MalformedResponse)
            {
                _logger.LogWarning(
                    "MELSEC read {Device}{Head} count={Count} failed ({Status}: {Message}); dropping connection.",
                    device, headDeviceNumber, points, result.Status, result.Message);
                _client.Disconnect();
                OnConnectFailure(result.Message);
            }

            return result;
        }
        finally
        {
            _wireLock.Release();
        }
    }

    /// <summary>Drop the underlying connection. Lock-free.</summary>
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

    /// <summary>
    /// Completes once no read is in flight (the single-in-flight lock is briefly
    /// acquirable). In-flight indicator ONLY — it must never gate the lock-free
    /// <see cref="Disconnect"/>, so a wedged read is interrupted by closing the
    /// socket without first acquiring the lock (avoids deadlock).
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

    private void OnConnectSuccess()
    {
        _consecutiveFailures = 0;
        _nextRetryAt = DateTimeOffset.MinValue;
        _breakerState = MelsecCircuitBreakerState.Closed;
        _lastSuccessAt = _time.GetUtcNow();
    }

    private void OnConnectFailure(string? reason)
    {
        _consecutiveFailures++;
        var backoffMs = ComputeBackoffMs(_consecutiveFailures);
        _nextRetryAt = _time.GetUtcNow() + TimeSpan.FromMilliseconds(backoffMs);

        if (_breakerState == MelsecCircuitBreakerState.HalfOpen
            || _consecutiveFailures >= _config.CircuitBreakerThreshold)
        {
            if (_breakerState != MelsecCircuitBreakerState.Open)
            {
                _logger.LogWarning(
                    "MELSEC connection {Host} breaker → Open after {Failures} consecutive failures ({Reason}).",
                    _config.Host, _consecutiveFailures, reason);
            }
            _breakerState = MelsecCircuitBreakerState.Open;
            _breakerOpenedAt = _time.GetUtcNow();
        }
    }

    private int ComputeBackoffMs(int failureCount)
    {
        var attempt = Math.Max(1, failureCount);
        var raw = _config.InitialBackoffMs * Math.Pow(_config.BackoffMultiplier, attempt - 1);
        var capped = (int)Math.Min(raw, _config.MaxBackoffMs);

        // Equal jitter (AWS "Exponential Backoff And Jitter"), same shape as
        // Focas2ConnectionManager.IncrementBackoff: keep at least half the
        // computed delay, then spread the upper half randomly.
        //
        // WHY (do not remove as noise): MELSEC sources on one line usually
        // lose their sockets together, because they lose them for one reason —
        // a switch blip, a power cycle, a plant-wide network hiccup. Without
        // jitter every source walks the identical deterministic ramp from the
        // identical failure instant, so their SLMP reconnects all land in the
        // same millisecond. An Ethernet/built-in-Ethernet module accepts only
        // a handful of concurrent connections; a simultaneous burst exceeds
        // that limit, the connects are refused, and the shared failure
        // re-synchronises the ramp — the recovery itself causes the next
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
}
