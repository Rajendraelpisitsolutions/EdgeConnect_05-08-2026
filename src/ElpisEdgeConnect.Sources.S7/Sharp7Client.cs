// ============================================================================
// File: Sharp7Client.cs
// Purpose: Production IS7Client implementation backed by Sharp7's
//          S7Client. Sharp7 is fully synchronous — we marshal calls
//          to Task.Run so the adapter's async poll loop stays
//          unblocked. The single-instance-per-S7Client constraint is
//          honored by the adapter (one IS7Client per S7SourceAdapter).
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
//            https://github.com/fbarresi/Sharp7 (MIT-licensed)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Sharp7-backed implementation of <see cref="IS7Client"/>.
/// </summary>
public sealed class Sharp7Client : IS7Client
{
    private readonly Sharp7.S7Client _inner = new();
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsConnected => !_disposed && _inner.Connected;

    /// <inheritdoc/>
    public async Task<S7OperationResult> ConnectAsync(
        string host,
        int port,
        int rack,
        int slot,
        S7ConnectionType connectionType,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return await Task.Run(() =>
        {
            try
            {
                _inner.SetConnectionType((ushort)(byte)connectionType);

                // Sharp7 exposes timeouts via SetParam with numeric param ids.
                // From its source: PingTimeout=3, SendTimeout=4, RecvTimeout=5.
                // We treat the configured value as a per-operation budget for
                // send/recv; PingTimeout governs the initial connect handshake.
                var timeoutMs = (int)timeout.TotalMilliseconds;
                _ = _inner.SetParam(3, ref timeoutMs); // PingTimeout
                _ = _inner.SetParam(4, ref timeoutMs); // SendTimeout
                _ = _inner.SetParam(5, ref timeoutMs); // RecvTimeout

                // Sharp7 uses port 102 implicitly (ISO-on-TCP standard).
                // We accept the configured port for forward-compat with
                // non-standard PLC gateways but call the standard
                // ConnectTo overload, which forces 102. When customers
                // need a different port they'll use SetConnectionParams
                // — track as a follow-up if requested.
                _ = port; // accepted but Sharp7 hard-codes ISO-on-TCP 102

                var code = _inner.ConnectTo(host, rack, slot);
                if (code != 0)
                {
                    return S7OperationResult.Fail(code, _inner.ErrorText(code));
                }
                return S7OperationResult.Ok;
            }
            catch (Exception ex)
            {
                return S7OperationResult.Fail(-1, ex.Message);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        if (_disposed) return;
        try
        {
            _inner.Disconnect();
        }
        catch
        {
            // best-effort
        }
    }

    /// <inheritdoc/>
    public async Task<S7OperationResult> ReadAreaAsync(
        S7MemoryArea area,
        int dbNumber,
        int startByte,
        int byteCount,
        byte[] buffer,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(buffer);

        return await Task.Run(() =>
        {
            try
            {
                // Sharp7's ReadArea: (int area, int dbNumber, int start,
                // int amount, int wordLen, byte[] buf).
                // wordLen=0x02 (Byte) — we always read raw bytes and
                // decode in C#. This is the simplest, most portable
                // pattern and matches the byte-granular planning.
                const int WordLenByte = 0x02;
                var code = _inner.ReadArea((int)area, dbNumber, startByte, byteCount, WordLenByte, buffer);
                if (code != 0)
                {
                    return S7OperationResult.Fail(code, _inner.ErrorText(code));
                }
                return S7OperationResult.Ok;
            }
            catch (Exception ex)
            {
                return S7OperationResult.Fail(-1, ex.Message);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _inner.Disconnect(); } catch { /* best-effort */ }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
