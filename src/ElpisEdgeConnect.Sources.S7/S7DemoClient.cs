// ============================================================================
// File: S7DemoClient.cs
// Purpose: Synthetic IS7Client for S7 demo mode — emits deterministic,
//          time-varying bytes so a gateway with EDGECONNECT_S7_FAKE_MODE set
//          shows live-looking data with no real PLC. Mirrors the FOCAS2
//          Focas2DemoApi role on the S7 transport seam.
//
//          Pure managed C# — NO Sharp7, no native calls, no sockets. Connect
//          always succeeds; reads fill the buffer with a big-endian uint16
//          ramp driven by a sine over an injectable clock, so Word/Int tags
//          oscillate smoothly and Bool tags toggle. The clock is injectable
//          (default DateTime.UtcNow) for deterministic tests.
// Reference: docs/decisions/0029-s7-demo-mode.md (mirrors ADR-0012)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Synthetic <see cref="IS7Client"/> used when S7 demo mode is active. Returns
/// deterministic, time-varying values; never touches the network.
/// </summary>
public sealed class S7DemoClient : IS7Client
{
    private readonly Func<DateTime> _now;
    private DateTime? _epoch;
    private bool _disposed;

    /// <summary>Production constructor — wall-clock driven.</summary>
    public S7DemoClient() : this(() => DateTime.UtcNow) { }

    /// <summary>Test constructor — injectable clock for deterministic output.</summary>
    internal S7DemoClient(Func<DateTime> now)
    {
        _now = now ?? throw new ArgumentNullException(nameof(now));
    }

    /// <inheritdoc/>
    public bool IsConnected { get; private set; }

    /// <inheritdoc/>
    public Task<S7OperationResult> ConnectAsync(
        string host, int port, int rack, int slot,
        S7ConnectionType connectionType, TimeSpan timeout, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        _epoch ??= _now();
        IsConnected = true;
        return Task.FromResult(S7OperationResult.Ok);
    }

    /// <inheritdoc/>
    public void Disconnect() => IsConnected = false;

    /// <inheritdoc/>
    public Task<S7OperationResult> ReadAreaAsync(
        S7MemoryArea area, int dbNumber, int startByte, int byteCount,
        byte[] buffer, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ct.ThrowIfCancellationRequested();

        var epoch = _epoch ??= _now();
        var elapsed = (_now() - epoch).TotalSeconds;
        var count = Math.Min(byteCount, buffer.Length);

        // Phase varies by area + DB so distinct tags show distinct curves.
        var areaPhase = dbNumber * 0.1 + (int)area * 0.05;

        for (var j = 0; j + 1 < count; j += 2)
        {
            // 100..900 sine over a 30s period — looks like a live process value.
            var phase = 2.0 * Math.PI * (elapsed / 30.0) + (startByte + j) * 0.3 + areaPhase;
            var value = (int)Math.Round(500 + 400 * Math.Sin(phase));
            var v = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
            buffer[j] = (byte)(v >> 8);     // big-endian high byte
            buffer[j + 1] = (byte)(v & 0xFF);
        }
        if ((count & 1) == 1)
        {
            // Trailing odd byte (e.g. a single-byte read): a slow ramp.
            buffer[count - 1] = (byte)((int)(elapsed) & 0xFF);
        }

        return Task.FromResult(S7OperationResult.Ok);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        IsConnected = false;
    }
}
