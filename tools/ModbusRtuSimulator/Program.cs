// ============================================================================
// File: Program.cs
// Purpose: CLI entry for the Modbus RTU-over-TCP simulator. Listens on a TCP
//          port and answers RTU read frames via ModbusRtuSlave until Ctrl+C.
//
// Usage:
//   ModbusRtuSimulator [--port 5020] [--bind 127.0.0.1] [--verbose]
//
//   Holding/input register[addr] = 1000 + addr; coils/discrete alternate by
//   address (even = true). Read FC01-FC04 only (the adapter never writes).
// ============================================================================

using System.Net;
using System.Net.Sockets;
using ElpisEdgeConnect.Tools.ModbusRtuSimulator;

var port = 5020;
var bind = IPAddress.Loopback;
var verbose = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length:
            if (!int.TryParse(args[++i], out port) || port is < 1 or > 65535)
            {
                Console.Error.WriteLine("--port must be 1..65535");
                return 1;
            }
            break;
        case "--bind" when i + 1 < args.Length:
            if (!IPAddress.TryParse(args[++i], out var parsed))
            {
                Console.Error.WriteLine($"--bind '{args[i]}' is not a valid IP address");
                return 1;
            }
            bind = parsed;
            break;
        case "--verbose":
            verbose = true;
            break;
        case "--help" or "-h":
            Console.WriteLine("ModbusRtuSimulator [--port 5020] [--bind 127.0.0.1] [--verbose]");
            return 0;
        default:
            Console.Error.WriteLine($"unknown argument '{args[i]}' (try --help)");
            return 1;
    }
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var listener = new TcpListener(bind, port);
listener.Start();
Console.WriteLine($"Modbus RTU-over-TCP simulator listening on {bind}:{port}. Press Ctrl+C to stop.");

try
{
    while (!cts.IsCancellationRequested)
    {
        var client = await listener.AcceptTcpClientAsync(cts.Token);
        _ = HandleClientAsync(client, verbose, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // graceful shutdown
}
finally
{
    listener.Stop();
}

Console.WriteLine("simulator stopped.");
return 0;

static async Task HandleClientAsync(TcpClient client, bool verbose, CancellationToken ct)
{
    var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
    Console.WriteLine($"[+] client connected: {remote}");
    try
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            // RTU read requests are a fixed 8 bytes: unit + fc + addr(2) + qty(2) + crc(2).
            var req = new byte[8];
            while (!ct.IsCancellationRequested)
            {
                if (!await ReadExactAsync(stream, req, 8, ct).ConfigureAwait(false))
                {
                    break; // clean EOF
                }

                var response = ModbusRtuSlave.ProcessReadRequest(req, 8);
                if (response is null)
                {
                    if (verbose) { Console.WriteLine("    bad CRC / silent"); }
                    continue;
                }

                if (verbose)
                {
                    Console.WriteLine($"    unit={req[0]} fc={req[1]} addr={(req[2] << 8) | req[3]} qty={(req[4] << 8) | req[5]} -> {response.Length} bytes");
                }
                await stream.WriteAsync(response, ct).ConfigureAwait(false);
            }
        }
    }
    catch (Exception ex)
    {
        if (verbose) { Console.WriteLine($"[!] client {remote} error: {ex.Message}"); }
    }
    finally
    {
        Console.WriteLine($"[-] client disconnected: {remote}");
    }
}

static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
{
    var read = 0;
    while (read < count)
    {
        var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct).ConfigureAwait(false);
        if (n == 0)
        {
            return false;
        }
        read += n;
    }
    return true;
}
