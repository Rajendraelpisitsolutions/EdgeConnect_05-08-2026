// ============================================================================
// File: Program.cs
// Purpose: Operator commissioning aid for figuring out the right `byteOrder`
//          to put on a Modbus tag. Reads raw registers from a target PLC
//          and decodes the same bytes under every supported byte order,
//          printing a comparison table. The operator picks the row whose
//          decoded value matches what the PLC HMI shows.
//
// Usage:
//   ModbusByteOrderProbe --host 192.168.1.50 --address 10 --width 2 --datatype float32
//
// Why this tool exists:
//   Modbus byte ordering is the #1 reason a tag value comes through wrong
//   on first connect — paperwork says "ABCD big-endian" and the PLC actually
//   uses CDAB word-swap, or vice versa. Trial-and-erroring through `byteOrder`
//   in the gateway config takes minutes per cycle. This tool collapses that
//   to a single read.
// ============================================================================

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.ModbusTcp;
using ElpisEdgeConnect.Sources.ModbusTcp.Decoding;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentModbus;

namespace ElpisEdgeConnect.Tools.ModbusByteOrderProbe;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts is null)
        {
            PrintUsage();
            return 2;
        }

        Console.Error.WriteLine($"Connecting to {opts.Host}:{opts.Port} (unitId={opts.UnitId})...");

        using var client = new ModbusTcpClient
        {
            ConnectTimeout = opts.TimeoutMs,
            ReadTimeout = opts.TimeoutMs,
        };

        try
        {
            if (!IPAddress.TryParse(opts.Host, out var ip))
            {
                ip = (await Dns.GetHostAddressesAsync(opts.Host).ConfigureAwait(false))[0];
            }
            client.Connect(new IPEndPoint(ip, opts.Port), ModbusEndianness.BigEndian);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Connect failed: {ex.Message}");
            return 1;
        }

        ushort[] registers;
        try
        {
            // Read raw bytes — we'll re-interpret under every byte order locally.
            var raw = client.ReadHoldingRegisters<ushort>(opts.UnitId, opts.Address, opts.Width).ToArray();
            registers = raw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Read failed: FC03 unit={opts.UnitId} addr={opts.Address} count={opts.Width} → {ex.Message}");
            return 1;
        }
        finally
        {
            try { client.Disconnect(); } catch { /* best-effort */ }
        }

        // Header + raw bytes for context.
        Console.WriteLine();
        Console.WriteLine($"Raw registers (high-byte-first per register, as Modbus wire delivered):");
        Console.WriteLine("  index  hex     dec");
        for (var i = 0; i < registers.Length; i++)
        {
            Console.WriteLine($"   {i,3}    0x{registers[i]:X4}  {registers[i],6}");
        }
        Console.WriteLine();
        Console.WriteLine($"Decoded under each byte order ({opts.Datatype}, {opts.Width} register(s)):");
        Console.WriteLine();
        Console.WriteLine("  byteOrder    decoded value");
        Console.WriteLine("  -----------  -----------------------------");

        var spec = ModbusDatatypeParser.Parse(opts.Datatype, new ModbusDatatypeSpec(ModbusDatatype.UInt16));

        // For each enum value whose byte count matches our datatype, decode and print.
        foreach (ModbusByteOrder order in Enum.GetValues<ModbusByteOrder>())
        {
            if (order.ByteCount() != spec.ByteCount)
            {
                continue;
            }

            string rendered;
            try
            {
                var decoded = ModbusDecoder.DecodeRegisters(
                    registers,
                    offset: 0,
                    registerCount: opts.Width,
                    spec: spec,
                    byteOrder: order);
                rendered = decoded?.ToString() ?? "<null>";
            }
            catch (Exception ex)
            {
                rendered = $"<decode error: {ex.Message}>";
            }

            Console.WriteLine($"  {order,-11}  {rendered}");
        }

        Console.WriteLine();
        Console.Error.WriteLine(
            "Pick the row whose decoded value matches the PLC HMI / engineering tool. " +
            "Use that byteOrder in your gateway.json tag definition.");
        return 0;
    }

    // =========================================================================
    // PRIVATE
    // =========================================================================

    private sealed class Options
    {
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required byte UnitId { get; init; }
        public required ushort Address { get; init; }
        public required ushort Width { get; init; }
        public required string Datatype { get; init; }
        public required int TimeoutMs { get; init; }
    }

    private static Options? ParseArgs(string[] args)
    {
        string? host = null;
        var port = 502;
        byte unitId = 1;
        ushort? address = null;
        ushort width = 2;
        string? datatype = null;
        var timeout = 3000;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--host":     host     = RequireValue(args, ref i, "--host"); break;
                    case "--port":     port     = int.Parse(RequireValue(args, ref i, "--port"), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--unit":     unitId   = byte.Parse(RequireValue(args, ref i, "--unit"), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--address":  address  = ushort.Parse(RequireValue(args, ref i, "--address"), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--width":    width    = ushort.Parse(RequireValue(args, ref i, "--width"), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--datatype": datatype = RequireValue(args, ref i, "--datatype"); break;
                    case "--timeout":  timeout  = int.Parse(RequireValue(args, ref i, "--timeout"), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--help":
                    case "-h":         return null;
                    default:
                        Console.Error.WriteLine($"Unknown argument: '{args[i]}'");
                        return null;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Argument parse error: {ex.Message}");
            return null;
        }

        if (host is null || address is null || datatype is null)
        {
            Console.Error.WriteLine("Missing required argument. --host, --address, --datatype are required.");
            return null;
        }
        return new Options
        {
            Host = host,
            Port = port,
            UnitId = unitId,
            Address = address.Value,
            Width = width,
            Datatype = datatype,
            TimeoutMs = timeout,
        };
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{flag} requires a value.");
        }
        return args[++i];
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            ModbusByteOrderProbe — Modbus byte-order commissioning probe

            Reads N holding registers from a Modbus TCP target and decodes the
            same bytes under every supported byte order. Pick the row whose
            value matches the PLC HMI.

            Usage:
              ModbusByteOrderProbe --host <ip|name> [--port 502]
                                   [--unit 1] --address <regAddr>
                                   [--width 2] --datatype <name>
                                   [--timeout 3000]

            Args:
              --host        PLC IP address or hostname (required).
              --port        Modbus TCP port (default 502).
              --unit        Slave unit id (default 1).
              --address     Zero-based register address to read (required).
              --width       Number of consecutive registers (default 2).
                              uint16/int16:        1
                              uint32/int32/float32: 2
                              uint64/int64/float64: 4
              --datatype    Datatype to decode under each byte order (required).
                              Examples: uint16, int32, float32, float64
              --timeout     Connect + read timeout in ms (default 3000).
              --help, -h    Show this message.

            Example:
              ModbusByteOrderProbe --host 192.168.1.50 --address 10 \
                                   --width 2 --datatype float32

            Exit codes:
              0  success
              1  connect / read error
              2  argument error
            """);
    }
}
