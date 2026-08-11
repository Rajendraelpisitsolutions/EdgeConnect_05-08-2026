// ============================================================================
// File: SlmpClient.cs
// Purpose: Real TCP transport for MELSEC SLMP / MC 3E binary, built on the pure
//          SlmpFrameCodec. Read-only batch-read path only (ADR-0033): no writes,
//          UDP, 4E, 1E, or ASCII. Single-flight by contract (the connection
//          manager serializes; this client adds no concurrency of its own).
//
//          Wire discipline: write the request, read exactly the 9-byte 3E
//          header, then exactly the declared response body. On timeout, a bad
//          declared length, a partial/EOF read, a socket error, or a parse
//          failure, the socket is DROPPED so a late reply can never desync the
//          next request (3E has no request/response serial). A non-zero MELSEC
//          end code is a valid response (protocol result) and keeps the socket.
//
//          GOLDEN/REQUEST BYTES ARE SPEC-DERIVED (SH(NA)-080008) and remain
//          FIELD-UNVERIFIED until the customer Part B capture confirms them.
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
// ============================================================================

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.Melsec.Wire;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>TCP SLMP / MC 3E binary client (read-only batch read).</summary>
public sealed class SlmpClient : IMelsecClient
{
    private const int HeaderLength = 9;
    private const int MaxResponseDataLength = 2 + (2 * SlmpFrameCodec.MaxWordPoints) + 64; // end code + payload + slack

    private readonly MelsecSourceConfiguration _config;
    private readonly Slmp3ERoute _route;
    private readonly ushort _monitoringTimerUnits;

    private TcpClient? _tcp;
    private NetworkStream? _stream;

    /// <summary>Build a client from configuration (route header, monitoring timer, timeouts).</summary>
    public SlmpClient(MelsecSourceConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _route = new Slmp3ERoute(config.NetworkNo, config.PcNo, config.RequestDestModuleIoNo, config.RequestDestModuleStationNo);
        _monitoringTimerUnits = MelsecMonitoringTimer.Encode(config.MonitoringTimerMs).Units;
    }

    /// <inheritdoc/>
    public bool IsConnected => _tcp is { Connected: true } && _stream is not null;

    /// <inheritdoc/>
    public async Task<MelsecClientResult> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        Disconnect();
        var tcp = new TcpClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            tcp.NoDelay = true;
            _tcp = tcp;
            _stream = tcp.GetStream();
            return MelsecClientResult.Connected;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            tcp.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            tcp.Dispose();
            return MelsecClientResult.Transport($"connect failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        try { _stream?.Dispose(); } catch { /* best-effort */ }
        try { _tcp?.Dispose(); } catch { /* best-effort */ }
        _stream = null;
        _tcp = null;
    }

    /// <inheritdoc/>
    public async Task<MelsecClientResult> ReadWordsAsync(MelsecDeviceCode device, int headDeviceNumber, int points, CancellationToken ct)
    {
        var stream = _stream;
        if (stream is null || _tcp is not { Connected: true })
        {
            return MelsecClientResult.Transport("not connected");
        }

        try
        {
            var request = SlmpFrameCodec.BuildBatchReadWordRequest(_route, _monitoringTimerUnits, device, headDeviceNumber, points);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(_config.RequestTimeoutMs));
            var token = cts.Token;

            await stream.WriteAsync(request, token).ConfigureAwait(false);

            // Exactly the 3E header, then exactly the declared response body.
            var header = new byte[HeaderLength];
            await stream.ReadExactlyAsync(header, 0, HeaderLength, token).ConfigureAwait(false);

            int responseDataLength = header[7] | (header[8] << 8);
            if (responseDataLength is < 2 or > MaxResponseDataLength)
            {
                Disconnect();
                return MelsecClientResult.Malformed($"declared response data length {responseDataLength} is out of range");
            }

            var full = new byte[HeaderLength + responseDataLength];
            Array.Copy(header, full, HeaderLength);
            await stream.ReadExactlyAsync(full, HeaderLength, responseDataLength, token).ConfigureAwait(false);

            var parsed = SlmpFrameCodec.ParseBatchReadWordResponse(full, points);
            switch (parsed.Status)
            {
                case MelsecReadStatus.Success:
                    return MelsecClientResult.Ok(parsed.WordData);
                case MelsecReadStatus.ProtocolError:
                    return MelsecClientResult.Protocol(parsed.EndCode); // valid response — keep the socket
                default:
                    Disconnect();
                    return MelsecClientResult.Malformed(parsed.Message ?? "malformed response");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Disconnect();
            throw; // external cancel / stop — propagate
        }
        catch (OperationCanceledException)
        {
            Disconnect(); // request timeout — drop so a late reply can't desync the next read
            return MelsecClientResult.Transport($"request timed out after {_config.RequestTimeoutMs} ms");
        }
        catch (Exception ex) // socket close, partial read (EndOfStream), etc.
        {
            Disconnect();
            return MelsecClientResult.Transport($"socket error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Disconnect();
}
