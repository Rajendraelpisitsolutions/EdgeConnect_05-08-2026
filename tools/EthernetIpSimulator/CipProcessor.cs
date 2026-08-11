// ============================================================================
// File: CipProcessor.cs
// Purpose: The CIP (Common Industrial Protocol) application layer. Given a CIP
//          request message (already unwrapped from the EtherNet/IP
//          encapsulation + CPF by EthernetIpSimulatorServer), it produces the
//          CIP response. Handles the subset libplctag uses for ControlLogix
//          symbolic atomic reads:
//
//            0x54 / 0x5B  ForwardOpen / Large ForwardOpen  -> open a connection
//            0x4E         ForwardClose                     -> close it
//            0x4C         Read Tag Service                 -> return tag value
//            0x52         Unconnected Send (wrapper)       -> unwrap + dispatch
//            0x0A         Multiple Service Packet          -> dispatch each sub
//
//          Out of scope: writes, fragmented reads, UDT/array/STRING structures.
// ============================================================================

namespace ElpisEdgeConnect.Tools.EthernetIpSimulator;

/// <summary>Per-TCP-connection CIP/EtherNet-IP session state.</summary>
internal sealed class SimSession
{
    public uint SessionHandle { get; set; }

    /// <summary>Originator→Target connection id (originator sends with this).</summary>
    public uint OtConnId { get; set; }

    /// <summary>Target→Originator connection id (we reply with this).</summary>
    public uint ToConnId { get; set; }
}

internal sealed class CipProcessor
{
    // CIP services
    private const byte SvcForwardOpen = 0x54;
    private const byte SvcLargeForwardOpen = 0x5B;
    private const byte SvcForwardClose = 0x4E;
    private const byte SvcReadTag = 0x4C;
    // 0x52 is overloaded: "Read Tag Fragmented" when addressed to a symbolic
    // tag (path starts 0x91), "Unconnected Send" when addressed to the
    // Connection Manager (path starts 0x20 0x06). libplctag uses the fragmented
    // read by default for Logix controllers.
    private const byte SvcReadTagFragmentedOrUnconnectedSend = 0x52;
    private const byte SvcMultipleService = 0x0A;
    private const byte ReplyMask = 0x80;
    private const byte SymbolicSegment = 0x91;

    // CIP general status codes
    private const byte StatusOk = 0x00;
    private const byte StatusPathDestUnknown = 0x05;
    private const byte StatusServiceNotSupported = 0x08;

    private readonly TagTable _tags;
    private readonly Func<double> _elapsedSeconds;
    private readonly Action<string> _log;

    public CipProcessor(TagTable tags, Func<double> elapsedSeconds, Action<string> log)
    {
        _tags = tags;
        _elapsedSeconds = elapsedSeconds;
        _log = log;
    }

    /// <summary>Process one CIP request and return the CIP response bytes.</summary>
    public byte[] Process(ReadOnlySpan<byte> cip, SimSession session)
    {
        if (cip.Length < 2)
        {
            return Error(0x00, StatusServiceNotSupported);
        }

        _log($"CIP>  {Hex(cip)}");

        var service = cip[0];
        var response = service switch
        {
            SvcForwardOpen or SvcLargeForwardOpen => ForwardOpen(cip, session, service),
            SvcForwardClose => ForwardClose(cip),
            SvcReadTag => ReadTag(cip, service),
            SvcReadTagFragmentedOrUnconnectedSend when IsSymbolicPath(cip) => ReadTag(cip, service),
            SvcReadTagFragmentedOrUnconnectedSend => UnconnectedSend(cip, session),
            SvcMultipleService => MultipleService(cip, session),
            _ => Error(service, StatusServiceNotSupported),
        };

        _log($"CIP<  {Hex(response)}");
        return response;
    }

    private static string Hex(ReadOnlySpan<byte> b)
    {
        var sb = new System.Text.StringBuilder(b.Length * 3);
        foreach (var x in b)
        {
            sb.Append(x.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
        }
        return sb.ToString().TrimEnd();
    }

    private byte[] ForwardOpen(ReadOnlySpan<byte> cip, SimSession session, byte service)
    {
        var bodyStart = 2 + (cip[1] * 2);
        if (cip.Length < bodyStart + 18)
        {
            return Error(service, StatusServiceNotSupported);
        }
        var body = cip[bodyStart..];

        var otId = Wire.ReadU32(body, 2);
        var toId = Wire.ReadU32(body, 6);
        var connSerial = Wire.ReadU16(body, 10);
        var vendorId = Wire.ReadU16(body, 12);
        var origSerial = Wire.ReadU32(body, 14);

        // Accept the originator's connection ids when provided; otherwise assign.
        session.OtConnId = otId != 0 ? otId : RandId();
        session.ToConnId = toId != 0 ? toId : RandId();

        _log($"ForwardOpen  O->T=0x{session.OtConnId:X8} T->O=0x{session.ToConnId:X8} serial=0x{connSerial:X4}");

        const uint api = 0x000F_4240; // 1,000,000 us actual packet interval
        var respBody = new Wire()
            .U32(session.OtConnId)
            .U32(session.ToConnId)
            .U16(connSerial)
            .U16(vendorId)
            .U32(origSerial)
            .U32(api)
            .U32(api)
            .U8(0)  // application reply size (words)
            .U8(0)  // reserved
            .ToArray();

        return new Wire()
            .U8((byte)(service | ReplyMask))
            .U8(0)
            .U8(StatusOk)
            .U8(0)
            .Bytes(respBody)
            .ToArray();
    }

    private byte[] ForwardClose(ReadOnlySpan<byte> cip)
    {
        var bodyStart = 2 + (cip[1] * 2);
        ushort connSerial = 0;
        ushort vendorId = 0;
        uint origSerial = 0;
        if (cip.Length >= bodyStart + 10)
        {
            var body = cip[bodyStart..];
            connSerial = Wire.ReadU16(body, 2);
            vendorId = Wire.ReadU16(body, 4);
            origSerial = Wire.ReadU32(body, 6);
        }
        _log($"ForwardClose serial=0x{connSerial:X4}");

        var respBody = new Wire()
            .U16(connSerial)
            .U16(vendorId)
            .U32(origSerial)
            .U8(0)
            .U8(0)
            .ToArray();

        return new Wire()
            .U8(SvcForwardClose | ReplyMask)
            .U8(0)
            .U8(StatusOk)
            .U8(0)
            .Bytes(respBody)
            .ToArray();
    }

    // Handles both Read Tag (0x4C) and Read Tag Fragmented (0x52). The reply
    // echoes the request service with the reply bit set (0xCC / 0xD2). The
    // trailing request data (element count, plus a 4-byte byte-offset for the
    // fragmented variant) is ignored — atomics fit in a single reply.
    private byte[] ReadTag(ReadOnlySpan<byte> cip, byte service)
    {
        var pathWords = cip[1];
        var pathStart = 2;
        var path = cip.Slice(pathStart, Math.Min(pathWords * 2, cip.Length - pathStart));

        var name = ParseSymbolicName(path);
        if (name is null)
        {
            _log("ReadTag      (unparseable path)");
            return Error(service, StatusPathDestUnknown);
        }

        if (!_tags.TryGet(name, out var tag))
        {
            _log($"ReadTag      '{name}' -> NOT FOUND");
            return Error(service, StatusPathDestUnknown);
        }

        var (value, display) = tag.Encode(_elapsedSeconds());
        _log($"ReadTag      '{name}' -> {tag.Type} {display}");

        return new Wire()
            .U8((byte)(service | ReplyMask))
            .U8(0)
            .U8(StatusOk)
            .U8(0)
            .U16(tag.Type.TypeCode())
            .Bytes(value)
            .ToArray();
    }

    /// <summary>True if the CIP request path is a symbolic (tag) segment.</summary>
    private static bool IsSymbolicPath(ReadOnlySpan<byte> cip) =>
        cip.Length > 2 && cip[2] == SymbolicSegment;

    private byte[] UnconnectedSend(ReadOnlySpan<byte> cip, SimSession session)
    {
        // service, pathSize, path, then: priority(1), timeoutTicks(1),
        // embeddedLen(2), embedded[embeddedLen], pad?, routePath...
        var bodyStart = 2 + (cip[1] * 2);
        if (cip.Length < bodyStart + 4)
        {
            return Error(SvcReadTagFragmentedOrUnconnectedSend, StatusServiceNotSupported);
        }
        var body = cip[bodyStart..];
        var embeddedLen = Wire.ReadU16(body, 2);
        if (body.Length < 4 + embeddedLen)
        {
            return Error(SvcReadTagFragmentedOrUnconnectedSend, StatusServiceNotSupported);
        }
        var embedded = body.Slice(4, embeddedLen);
        // The reply to an Unconnected Send IS the embedded service's reply.
        return Process(embedded, session);
    }

    private byte[] MultipleService(ReadOnlySpan<byte> cip, SimSession session)
    {
        var dataStart = 2 + (cip[1] * 2);
        if (cip.Length < dataStart + 2)
        {
            return Error(SvcMultipleService, StatusServiceNotSupported);
        }
        var data = cip[dataStart..];
        int count = Wire.ReadU16(data, 0);
        if (count <= 0 || data.Length < 2 + (count * 2))
        {
            return Error(SvcMultipleService, StatusServiceNotSupported);
        }

        var subResponses = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            int off = Wire.ReadU16(data, 2 + (i * 2));
            int end = i + 1 < count ? Wire.ReadU16(data, 2 + ((i + 1) * 2)) : data.Length;
            if (off < 0 || end > data.Length || off >= end)
            {
                subResponses.Add(Error(0x00, StatusServiceNotSupported));
                continue;
            }
            subResponses.Add(Process(data.Slice(off, end - off), session));
        }

        // Reassemble: header, then count, then offsets (relative to the count
        // field), then the concatenated sub-responses.
        var offsetTableBytes = 2 + (subResponses.Count * 2);
        var w = new Wire()
            .U8(SvcMultipleService | ReplyMask)
            .U8(0)
            .U8(StatusOk)
            .U8(0)
            .U16((ushort)subResponses.Count);

        var running = offsetTableBytes;
        foreach (var sub in subResponses)
        {
            w.U16((ushort)running);
            running += sub.Length;
        }
        foreach (var sub in subResponses)
        {
            w.Bytes(sub);
        }
        return w.ToArray();
    }

    /// <summary>Extract the tag name from an ANSI extended symbolic segment (0x91).</summary>
    private static string? ParseSymbolicName(ReadOnlySpan<byte> path)
    {
        if (path.Length < 2 || path[0] != 0x91)
        {
            return null;
        }
        int nameLen = path[1];
        if (path.Length < 2 + nameLen)
        {
            return null;
        }
        return System.Text.Encoding.ASCII.GetString(path.Slice(2, nameLen));
    }

    private static byte[] Error(byte service, byte status) => new Wire()
        .U8((byte)(service | ReplyMask))
        .U8(0)
        .U8(status)
        .U8(0)
        .ToArray();

    private static uint RandId() => (uint)Random.Shared.Next(1, int.MaxValue);
}
