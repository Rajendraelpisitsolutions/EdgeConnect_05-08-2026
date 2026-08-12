// ============================================================================
// File: EthernetIpSimulatorServer.cs
// Purpose: The EtherNet/IP encapsulation layer + TCP accept loop. Decodes the
//          24-byte encapsulation header and the Common Packet Format (CPF),
//          hands the inner CIP message to CipProcessor, and frames the reply.
//
//          Handled encapsulation commands:
//            0x0065 RegisterSession    -> assign a session handle
//            0x0066 UnRegisterSession  -> (client is closing)
//            0x006F SendRRData         -> unconnected CIP (ForwardOpen/Close)
//            0x0070 SendUnitData       -> connected CIP (Read Tag)
//          Unknown commands get an "unsupported" status reply rather than a
//          dropped connection, so a probing client can continue.
// ============================================================================

using System.Net;
using System.Net.Sockets;

namespace ElpisEdgeConnect.Tools.EthernetIpSimulator;

internal sealed class EthernetIpSimulatorServer
{
    // Encapsulation commands
    private const ushort CmdRegisterSession = 0x0065;
    private const ushort CmdUnRegisterSession = 0x0066;
    private const ushort CmdSendRRData = 0x006F;
    private const ushort CmdSendUnitData = 0x0070;

    // Encapsulation status
    private const uint EncapOk = 0x0000_0000;
    private const uint EncapUnsupported = 0x0000_0001;

    // CPF item type ids
    private const ushort ItemNullAddress = 0x0000;
    private const ushort ItemConnectedAddress = 0x00A1;
    private const ushort ItemConnectedData = 0x00B1;
    private const ushort ItemUnconnectedData = 0x00B2;

    private const int HeaderSize = 24;

    private readonly IPAddress _bind;
    private readonly int _port;
    private readonly CipProcessor _cip;
    private readonly bool _verbose;
    private readonly DateTime _startUtc = DateTime.UtcNow;

    public EthernetIpSimulatorServer(IPAddress bind, int port, TagTable tags, bool verbose)
    {
        _bind = bind;
        _port = port;
        _verbose = verbose;
        _cip = new CipProcessor(tags, ElapsedSeconds, Log);
    }

    private double ElapsedSeconds() => (DateTime.UtcNow - _startUtc).TotalSeconds;

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(_bind, _port);
        listener.Start();
        Console.WriteLine($"EtherNet/IP simulator listening on {_bind}:{_port} (CIP/TCP). Press Ctrl+C to stop.");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
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
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Console.WriteLine($"[+] client connected: {remote}");
        var session = new SimSession();
        try
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                var header = new byte[HeaderSize];
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, header, HeaderSize, ct).ConfigureAwait(false))
                    {
                        break; // clean EOF
                    }

                    var command = Wire.ReadU16(header, 0);
                    int dataLen = Wire.ReadU16(header, 2);
                    var senderContext = header.AsMemory(12, 8);

                    var data = Array.Empty<byte>();
                    if (dataLen > 0)
                    {
                        data = new byte[dataLen];
                        if (!await ReadExactAsync(stream, data, dataLen, ct).ConfigureAwait(false))
                        {
                            break;
                        }
                    }

                    var (status, respData) = Dispatch(command, data, session);

                    var respHeader = BuildHeader(command, respData.Length, session.SessionHandle, status, senderContext.Span);
                    await stream.WriteAsync(respHeader, ct).ConfigureAwait(false);
                    if (respData.Length > 0)
                    {
                        await stream.WriteAsync(respData, ct).ConfigureAwait(false);
                    }

                    if (command == CmdUnRegisterSession)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
            {
                Console.WriteLine($"[!] client {remote} error: {ex.Message}");
            }
        }
        finally
        {
            Console.WriteLine($"[-] client disconnected: {remote}");
        }
    }

    private (uint Status, byte[] Data) Dispatch(ushort command, byte[] data, SimSession session)
    {
        switch (command)
        {
            case CmdRegisterSession:
                if (session.SessionHandle == 0)
                {
                    session.SessionHandle = (uint)Random.Shared.Next(1, int.MaxValue);
                }
                Log($"RegisterSession -> handle 0x{session.SessionHandle:X8}");
                // Echo the 4-byte request body (protocol version + options).
                return (EncapOk, data.Length >= 4 ? data[..4] : new byte[] { 0x01, 0x00, 0x00, 0x00 });

            case CmdUnRegisterSession:
                Log("UnRegisterSession");
                return (EncapOk, Array.Empty<byte>());

            case CmdSendRRData:
                return (EncapOk, HandleSendRRData(data, session));

            case CmdSendUnitData:
                return (EncapOk, HandleSendUnitData(data, session));

            default:
                Log($"unsupported encapsulation command 0x{command:X4}");
                return (EncapUnsupported, Array.Empty<byte>());
        }
    }

    // SendRRData: [interfaceHandle(4)][timeout(2)][CPF]. Unconnected CIP lives
    // in the 0x00B2 data item; the reply mirrors the same envelope.
    private byte[] HandleSendRRData(byte[] data, SimSession session)
    {
        if (!TryFindCpfItem(data, ItemUnconnectedData, out var cip))
        {
            return BuildRRDataEnvelope(Array.Empty<byte>());
        }
        var cipResp = _cip.Process(cip, session);
        return BuildRRDataEnvelope(cipResp);
    }

    // SendUnitData: connected CIP. Address item 0x00A1 carries the connection
    // id; data item 0x00B1 is [sequence(2)][CIP]. The reply echoes the sequence
    // and uses our Target->Originator connection id.
    private byte[] HandleSendUnitData(byte[] data, SimSession session)
    {
        if (!TryFindCpfItem(data, ItemConnectedData, out var connData) || connData.Length < 2)
        {
            return BuildUnitDataEnvelope(session.ToConnId, 0, Array.Empty<byte>());
        }
        var sequence = Wire.ReadU16(connData, 0);
        var cip = connData[2..];
        var cipResp = _cip.Process(cip, session);
        return BuildUnitDataEnvelope(session.ToConnId, sequence, cipResp);
    }

    private static byte[] BuildRRDataEnvelope(byte[] cipResp) =>
        new Wire()
            .U32(0)             // interface handle
            .U16(0)             // timeout
            .U16(2)             // CPF item count
            .U16(ItemNullAddress).U16(0)
            .U16(ItemUnconnectedData).U16((ushort)cipResp.Length).Bytes(cipResp)
            .ToArray();

    private static byte[] BuildUnitDataEnvelope(uint toConnId, ushort sequence, byte[] cipResp)
    {
        var connData = new Wire().U16(sequence).Bytes(cipResp).ToArray();
        return new Wire()
            .U32(0)             // interface handle
            .U16(0)             // timeout
            .U16(2)             // CPF item count
            .U16(ItemConnectedAddress).U16(4).U32(toConnId)
            .U16(ItemConnectedData).U16((ushort)connData.Length).Bytes(connData)
            .ToArray();
    }

    // Walk the CPF item list (starts at offset 6: after interfaceHandle+timeout)
    // and return the data bytes of the first item matching wantType.
    private static bool TryFindCpfItem(byte[] data, ushort wantType, out byte[] itemData)
    {
        itemData = Array.Empty<byte>();
        if (data.Length < 8)
        {
            return false;
        }
        int itemCount = Wire.ReadU16(data, 6);
        var offset = 8;
        for (var i = 0; i < itemCount; i++)
        {
            if (offset + 4 > data.Length)
            {
                return false;
            }
            var type = Wire.ReadU16(data, offset);
            int len = Wire.ReadU16(data, offset + 2);
            var dataOffset = offset + 4;
            if (dataOffset + len > data.Length)
            {
                return false;
            }
            if (type == wantType)
            {
                itemData = data[dataOffset..(dataOffset + len)];
                return true;
            }
            offset = dataOffset + len;
        }
        return false;
    }

    private static byte[] BuildHeader(
        ushort command, int dataLen, uint sessionHandle, uint status, ReadOnlySpan<byte> senderContext)
    {
        return new Wire()
            .U16(command)
            .U16((ushort)dataLen)
            .U32(sessionHandle)
            .U32(status)
            .Bytes(senderContext.Length == 8 ? senderContext : new byte[8])
            .U32(0)             // options
            .ToArray();
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                return false; // peer closed
            }
            read += n;
        }
        return true;
    }

    private void Log(string message)
    {
        if (_verbose)
        {
            Console.WriteLine($"    {message}");
        }
    }
}
