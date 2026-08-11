// ============================================================================
// Test fixture: in-proc loopback SLMP server (TcpListener on 127.0.0.1) that
// reads one 3E request per connection and runs a configurable per-connection
// script to shape the response (success / end code / malformed / chunked /
// no-reply / close / late-reply). No real PLC, no external dependency.
// ============================================================================

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Integration.Tests.Melsec;

/// <summary>A per-connection response script (the request has already been consumed).</summary>
internal delegate Task ConnectionScript(NetworkStream stream, CancellationToken ct);

/// <summary>In-proc loopback SLMP TcpListener for SlmpClient tests.</summary>
internal sealed class LoopbackSlmpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<int, ConnectionScript> _scriptFor;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _connectionIndex;

    public LoopbackSlmpServer(Func<int, ConnectionScript> scriptFor)
    {
        _scriptFor = scriptFor;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    /// <summary>Ephemeral port the server is listening on.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                var index = _connectionIndex++;
                _ = HandleAsync(client, _scriptFor(index));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task HandleAsync(TcpClient client, ConnectionScript script)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();

                // Consume one 3E request: fixed 9-byte header, then declared body.
                var header = new byte[9];
                await stream.ReadExactlyAsync(header, 0, 9, _cts.Token).ConfigureAwait(false);
                int requestDataLength = header[7] | (header[8] << 8);
                var body = new byte[requestDataLength];
                await stream.ReadExactlyAsync(body, 0, requestDataLength, _cts.Token).ConfigureAwait(false);

                await script(stream, _cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // connection closed / cancelled / client dropped — expected in several tests
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { await _acceptLoop.ConfigureAwait(false); } catch { /* shutting down */ }
        _cts.Dispose();
    }
}

/// <summary>Response frame builders + connection-script helpers for the loopback server.</summary>
internal static class SlmpResponses
{
    /// <summary>Build a well-formed 3E success response for the given LE words.</summary>
    public static byte[] SuccessFrame(params ushort[] words)
    {
        var payload = new byte[words.Length * 2];
        for (var i = 0; i < words.Length; i++)
        {
            payload[(i * 2) + 0] = (byte)(words[i] & 0xFF);
            payload[(i * 2) + 1] = (byte)(words[i] >> 8);
        }
        return Frame(0x0000, payload);
    }

    /// <summary>Build a 3E response carrying a non-zero end code (no payload).</summary>
    public static byte[] EndCodeFrame(ushort endCode) => Frame(endCode, Array.Empty<byte>());

    /// <summary>Compose a 3E response frame (subheader D000, echoed route, length, end code, data).</summary>
    public static byte[] Frame(ushort endCode, byte[] afterEndCode)
    {
        var responseDataLength = (ushort)(2 + afterEndCode.Length);
        var f = new byte[9 + responseDataLength];
        f[0] = 0xD0; f[1] = 0x00;
        f[2] = 0x00; f[3] = 0xFF; f[4] = 0xFF; f[5] = 0x03; f[6] = 0x00;
        f[7] = (byte)(responseDataLength & 0xFF);
        f[8] = (byte)(responseDataLength >> 8);
        f[9] = (byte)(endCode & 0xFF);
        f[10] = (byte)(endCode >> 8);
        Array.Copy(afterEndCode, 0, f, 11, afterEndCode.Length);
        return f;
    }

    /// <summary>Send the given bytes once.</summary>
    public static ConnectionScript Send(byte[] bytes) =>
        async (stream, ct) => await stream.WriteAsync(bytes, ct).ConfigureAwait(false);

    /// <summary>Send bytes in small chunks with a delay between them (exercises partial reads).</summary>
    public static ConnectionScript SendChunked(byte[] bytes, int chunkSize, int delayMs) =>
        async (stream, ct) =>
        {
            for (var pos = 0; pos < bytes.Length; pos += chunkSize)
            {
                var n = Math.Min(chunkSize, bytes.Length - pos);
                await stream.WriteAsync(bytes.AsMemory(pos, n), ct).ConfigureAwait(false);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        };

    /// <summary>Never reply (client should time out).</summary>
    public static ConnectionScript NoReply() =>
        async (_, ct) => await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);

    /// <summary>Return without writing — the connection closes (server disconnect).</summary>
    public static ConnectionScript CloseImmediately() => (_, _) => Task.CompletedTask;

    /// <summary>Write only a partial body then close (truncated response).</summary>
    public static ConnectionScript SendThenClose(byte[] bytes, int count) =>
        async (stream, ct) => await stream.WriteAsync(bytes.AsMemory(0, count), ct).ConfigureAwait(false);

    /// <summary>Delay past the client timeout, then send (a late reply on a socket the client dropped).</summary>
    public static ConnectionScript DelayThenSend(byte[] bytes, int delayMs) =>
        async (stream, ct) =>
        {
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        };
}
