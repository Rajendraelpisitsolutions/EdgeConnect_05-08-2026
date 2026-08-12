// ============================================================================
// File: Program.cs
// Purpose: CLI entry point for the EtherNet/IP simulator. Parses options,
//          builds the tag table, and runs the server until Ctrl+C.
//
// Usage:
//   EthernetIpSimulator [--port 44818] [--bind 127.0.0.1] [--verbose]
//                       [--tag name:TYPE]...
//
//   --port      TCP port to listen on (default 44818, the EtherNet/IP port).
//   --bind      Local address to bind (default 127.0.0.1).
//   --verbose   Log every CIP request/response.
//   --tag       Add a tag as name:TYPE (TYPE = BOOL|SINT|INT|DINT|LINT|REAL|
//               LREAL). May be repeated. If none given, a default demo set is
//               used: spindle_speed:DINT, temperature:REAL, running:BOOL.
//
// Example:
//   EthernetIpSimulator --verbose --tag Speed:DINT --tag Temp:REAL
// ============================================================================

using System.Net;
using ElpisEdgeConnect.Tools.EthernetIpSimulator;

var port = 44818;
var bind = IPAddress.Loopback;
var verbose = false;
var tags = new TagTable();

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

        case "--tag" when i + 1 < args.Length:
            if (!TryParseTag(args[++i], out var tag, out var error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }
            tags.Add(tag);
            break;

        case "--help" or "-h":
            PrintUsage();
            return 0;

        default:
            Console.Error.WriteLine($"unknown argument '{args[i]}' (try --help)");
            return 1;
    }
}

if (tags.Count == 0)
{
    tags.Add(new SimulatedTag("spindle_speed", SimCipType.Dint));
    tags.Add(new SimulatedTag("temperature", SimCipType.Real));
    tags.Add(new SimulatedTag("running", SimCipType.Bool));
}

Console.WriteLine($"EtherNet/IP simulator — {tags.Count} tag(s):");
foreach (var t in tags.All)
{
    Console.WriteLine($"    {t.Name} : {t.Type}");
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new EthernetIpSimulatorServer(bind, port, tags, verbose);
try
{
    await server.RunAsync(cts.Token);
}
catch (System.Net.Sockets.SocketException ex)
{
    Console.Error.WriteLine($"failed to listen on {bind}:{port} — {ex.Message}");
    return 1;
}

Console.WriteLine("simulator stopped.");
return 0;

static bool TryParseTag(string spec, out SimulatedTag tag, out string error)
{
    tag = null!;
    error = string.Empty;
    var parts = spec.Split(':', 2);
    if (parts.Length != 2 || parts[0].Length == 0)
    {
        error = $"--tag '{spec}' must be name:TYPE (e.g. Speed:DINT)";
        return false;
    }
    var type = SimCipTypeExtensions.ParseOrNull(parts[1]);
    if (type is null)
    {
        error = $"--tag '{spec}': unknown type '{parts[1]}' (BOOL|SINT|INT|DINT|LINT|REAL|LREAL)";
        return false;
    }
    tag = new SimulatedTag(parts[0], type.Value);
    return true;
}

static void PrintUsage()
{
    Console.WriteLine(
        "EtherNet/IP simulator\n\n" +
        "  --port N        TCP port (default 44818)\n" +
        "  --bind IP       bind address (default 127.0.0.1)\n" +
        "  --verbose       log every CIP request/response\n" +
        "  --tag name:TYPE add a tag (TYPE = BOOL|SINT|INT|DINT|LINT|REAL|LREAL), repeatable\n" +
        "  --help          show this help\n\n" +
        "Default tags: spindle_speed:DINT, temperature:REAL, running:BOOL");
}
