// ============================================================================
// File: Wizards/PortAvailabilityProbe.cs
// Purpose: Cheap, local pre-flight check for "is this TCP port already
//          bound on this host?" — used by the Add OPC UA Server
//          Destination wizard to catch the very common port-4840
//          conflict (Kepware, Matrikon, Prosys, vendor PLC tooling) at
//          wizard time, instead of surfacing it as an opaque
//          "Failed to establish tcp listener sockets" stack trace after
//          Apply.
//
//          The probe briefly opens a TcpListener on the requested port
//          (bound to IPAddress.Any to match how OpcUaServerSinkAdapter
//          binds) and immediately releases it. Bind+release is sub-ms;
//          the small race window during which a client could fail to
//          connect to our (not-yet-started) server is benign — the
//          server is not yet running anyway.
//
//          ADR-0015 Rule 6 carve-out note: this is the wizard-time
//          version of the "can we bind?" verification the comment in
//          AddOpcUaServerDestination.razor pointed at as a future
//          milestone. It is intentionally NOT marketed as a full
//          "Test Connection" — that label is reserved for sources that
//          can actually verify end-to-end protocol handshake.
// Reference: Operator feedback 2026-05-28 (PR #44 follow-up) —
//            QA hit "Failed to establish tcp listener sockets" with
//            another OPC UA stack already holding 4840 on their box.
// ============================================================================

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Result of a single <see cref="PortAvailabilityProbe.ProbeAsync"/> call.
/// </summary>
public sealed record PortProbeResult(PortProbeStatus Status, int Port, string Message)
{
    /// <summary>Endpoint URL was missing or did not parse as opc.tcp://host:port.</summary>
    public static PortProbeResult Invalid(string message) =>
        new(PortProbeStatus.Invalid, 0, message);

    /// <summary>Port bound + released successfully — nothing else holds it.</summary>
    public static PortProbeResult Available(int port) =>
        new(PortProbeStatus.Available, port,
            $"Port {port} is free on this host — the sink will be able to bind.");

    /// <summary>Port is already bound by another process on this host.</summary>
    public static PortProbeResult InUse(int port) =>
        new(PortProbeStatus.InUse, port,
            $"Port {port} is already in use by another process on this host. " +
            $"Stop the conflicting service (run 'netstat -ano | findstr :{port}' to identify it) " +
            "or change the endpoint port below.");

    /// <summary>Socket-layer failure other than AddressAlreadyInUse.</summary>
    public static PortProbeResult Error(int port, string message) =>
        new(PortProbeStatus.Error, port, message);
}

/// <summary>State of a probe result.</summary>
public enum PortProbeStatus
{
    /// <summary>Endpoint URL could not be parsed; no probe was performed.</summary>
    Invalid,

    /// <summary>Probe bound + released successfully.</summary>
    Available,

    /// <summary>Probe failed with AddressAlreadyInUse — another process owns the port.</summary>
    InUse,

    /// <summary>Other socket-layer error (permission denied, address family unsupported, etc.).</summary>
    Error,
}

/// <summary>
/// Cheap pre-flight check for TCP port availability. Local-only: the
/// probe binds on the Management server's host, which is the same host
/// the sink adapter will later bind on, so the result is meaningful
/// even when the configured endpoint host is 0.0.0.0 or a NIC IP.
/// </summary>
public static class PortAvailabilityProbe
{
    /// <summary>
    /// Test whether the port in <paramref name="endpointUrl"/> is
    /// currently free on this host. Returns within ~1ms in the
    /// happy path; the cancellation token is honored before the bind
    /// attempt so the UI can drop a stale probe.
    /// </summary>
    public static async Task<PortProbeResult> ProbeAsync(string? endpointUrl, CancellationToken ct = default)
    {
        if (!TryParseOpcTcpPort(endpointUrl, out var port))
        {
            return PortProbeResult.Invalid(
                "Endpoint URL must be 'opc.tcp://host:port/path' before the port can be checked.");
        }

        ct.ThrowIfCancellationRequested();

        // Yield to the synchronization context so callers observe truly
        // async behavior even though the bind itself is synchronous.
        await Task.Yield();

        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            try
            {
                listener.Start();
                return PortProbeResult.Available(port);
            }
            finally
            {
                listener.Stop();
            }
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return PortProbeResult.InUse(port);
        }
        catch (SocketException ex)
        {
            return PortProbeResult.Error(port,
                $"Port {port} probe failed: {ex.SocketErrorCode} ({ex.Message}).");
        }
    }

    /// <summary>
    /// Extract the TCP port from an <c>opc.tcp://host:port/path</c>
    /// URL. Returns false (and port = 0) when the URL is missing,
    /// not absolute, or not in the opc.tcp scheme. Default port 4840
    /// is applied when the URL omits an explicit port.
    /// </summary>
    public static bool TryParseOpcTcpPort(string? endpointUrl, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(endpointUrl)) return false;
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, "opc.tcp", StringComparison.OrdinalIgnoreCase)) return false;

        // Uri.Port returns -1 when scheme is unknown (opc.tcp falls in
        // that bucket on .NET 8 for some inputs); 4840 is the IANA
        // default for OPC UA.
        port = uri.Port > 0 ? uri.Port : 4840;
        return port is > 0 and < 65_536;
    }
}
