// ============================================================================
// File: EthernetIpDemoClient.cs
// Purpose: Synthetic IEthernetIpClient for EtherNet/IP demo mode — emits
//          deterministic, time-varying values per element type so a gateway
//          with EDGECONNECT_ETHERNETIP_FAKE_MODE set shows live-looking
//          Allen-Bradley data with no real PLC. Mirrors S7DemoClient on the
//          EtherNet/IP transport seam.
//
//          Pure managed C# — NO libplctag, no native calls, no sockets.
//          Connect always succeeds; each read returns a value driven by a sine
//          over an injectable clock, with a stable per-address phase so distinct
//          tags trace distinct curves. The CLR types returned match
//          LibPlcTagClient.DecodeTag exactly so downstream typing is identical.
//          The clock is injectable (default DateTime.UtcNow) for deterministic
//          tests.
// Reference: docs/decisions/0029-s7-demo-mode.md (mirrors ADR-0012)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// Synthetic <see cref="IEthernetIpClient"/> used when EtherNet/IP demo mode is
/// active. Returns deterministic, time-varying values; never touches the
/// network.
/// </summary>
public sealed class EthernetIpDemoClient : IEthernetIpClient
{
    private readonly Func<DateTime> _now;
    private DateTime? _epoch;
    private bool _connected;
    private bool _disposed;

    /// <summary>Production constructor — wall-clock driven.</summary>
    public EthernetIpDemoClient() : this(() => DateTime.UtcNow) { }

    /// <summary>Test constructor — injectable clock for deterministic output.</summary>
    internal EthernetIpDemoClient(Func<DateTime> now)
    {
        _now = now ?? throw new ArgumentNullException(nameof(now));
    }

    /// <inheritdoc/>
    public bool IsConnected => _connected;

    /// <inheritdoc/>
    public Task ConnectAsync(EthernetIpConnectionParameters parameters, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        _epoch ??= _now();
        _connected = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Disconnect() => _connected = false;

    /// <inheritdoc/>
    public Task<EthernetIpReadResult> ReadTagAsync(
        string address, EthernetIpElementType elementType, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        if (!_connected)
        {
            throw new EthernetIpFatalException(
                EthernetIpErrors.ConnectionLost, "EtherNet/IP demo client is not connected.");
        }

        var epoch = _epoch ??= _now();
        var elapsed = (_now() - epoch).TotalSeconds;

        // 30-second sine, offset by a stable per-address phase so distinct tags
        // trace distinct curves consistently across process restarts (string
        // hashing is randomized, so derive the offset from the address chars).
        var sine = Math.Sin((2.0 * Math.PI * elapsed / 30.0) + StablePhase(address));

        // CLR types mirror LibPlcTagClient.DecodeTag so the canonical pipeline
        // sees identical typing in demo and production.
        var result = elementType switch
        {
            EthernetIpElementType.Bool => EthernetIpReadResult.Ok(sine >= 0.0, CanonicalValueType.Boolean),
            EthernetIpElementType.Sint => EthernetIpReadResult.Ok((int)Math.Round(50 + 40 * sine), CanonicalValueType.Integer),
            EthernetIpElementType.Int => EthernetIpReadResult.Ok((int)Math.Round(500 + 400 * sine), CanonicalValueType.Integer),
            EthernetIpElementType.Dint => EthernetIpReadResult.Ok((int)Math.Round(5000 + 4000 * sine), CanonicalValueType.Integer),
            EthernetIpElementType.Lint => EthernetIpReadResult.Ok((long)Math.Round(50_000 + 40_000 * sine), CanonicalValueType.Long),
            EthernetIpElementType.Real => EthernetIpReadResult.Ok((float)(100.0 + 50.0 * sine), CanonicalValueType.Float),
            EthernetIpElementType.Lreal => EthernetIpReadResult.Ok(100.0 + 50.0 * sine, CanonicalValueType.Double),
            EthernetIpElementType.String => EthernetIpReadResult.Ok($"DEMO-{(int)elapsed}", CanonicalValueType.String),
            _ => EthernetIpReadResult.Fail(
                EthernetIpErrors.DecodeFailed, $"Demo client cannot synthesize element type {elementType}."),
        };
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _connected = false;
        return ValueTask.CompletedTask;
    }

    /// <summary>Deterministic phase in <c>[0, 2π)</c> derived from the address chars.</summary>
    private static double StablePhase(string address)
    {
        var acc = 0;
        foreach (var c in address)
        {
            acc = unchecked((acc * 31) + c);
        }
        return ((acc & 0x7FFFFFFF) % 360) * Math.PI / 180.0;
    }
}
