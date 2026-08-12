// ============================================================================
// File: Configuration/SourceEndpointIdentity.cs
// Purpose: Resolve the physical device endpoint a source instance connects to,
//          from its protocol-specific opaque Connection block, so that
//          CrossRecordValidator can warn when two enabled sources point at the
//          same device (rule 13).
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4 (cross-record invariants)
//
// ---------------------------------------------------------------------------
// NOTE ON THE PROTOCOL-AGNOSTIC CORE LOCK (CLAUDE.md §3 lock 1, §9 anti-pattern 1)
//
// Core must never *reference* a protocol module assembly, and this file does
// not. It holds only literal JSON key names transcribed from each adapter's
// public `*ConnectionKeys` class, as data. That follows the existing precedent
// in Core/Licensing/LicenseEditionCatalog.cs, which likewise hard-codes
// protocol module keys as string data with no assembly reference.
//
// It is still protocol-specific knowledge sitting in Core, which is a real (if
// mild) tension with the lock. The clean long-term shape is for each adapter to
// declare its own endpoint identity through a Core-owned contract (e.g. an
// ISourceEndpointDescriptor registered by the Host), reducing this table to a
// registry lookup. Until that contract exists, this table is deliberately:
//   (a) data only — no protocol behaviour, no parsing beyond field reads;
//   (b) advisory only — it can only ever raise a WARNING, never block an apply;
//   (c) fail-open — any protocol or connection block it cannot read with
//       confidence is skipped silently rather than guessed at.
//
// Field reads mirror the adapters' own readers exactly (case-sensitive
// TryGetProperty; JSON Number kind only for numeric fields; the adapters'
// documented defaults) so the validator models what the runtime will really do.
// ---------------------------------------------------------------------------
//
// Endpoint shape per protocol family:
//
//   modbus (modbustcp / modbus-tcp / modbus / modbusrtu)
//       tcp | rtuOverTcp : host + port (default 502) — the TCP socket identity
//       serialRtu        : serialPort (COM3, /dev/ttyUSB0) — the serial line
//   s7                   : host + rack (default 0) + slot (default 1).
//                          Port is deliberately NOT part of the key: the S7
//                          driver ignores it, and two CPUs can sit behind one
//                          host on different rack/slot pairs.
//   melsec               : host + port (adapter default 0 = unset)
//   focas2               : ipAddress + port (default 8193)
//   ethernetip           : host + explicit CIP path (a chassis gateway routes
//                          to several CPUs by path)
//   opcua-client         : endpointUrl, normalised (trim, lowercase, no
//                          trailing slash)
//
// Deliberately EXCLUDED (see the report / class docs below):
//   mtconnect, brother-http — HTTP-polled agents, no low concurrent-connection
//   cap, and one MTConnect agent base URL is legitimately shared by many
//   devices. Flagging them would be noise, not a finding.
// ============================================================================

using System;
using System.Text.Json;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Resolves a comparable device-endpoint identity for a configured source.
/// </summary>
/// <remarks>
/// <para>
/// Used only by <see cref="CrossRecordValidator"/> to raise the advisory
/// duplicate-endpoint warning. Resolution is best-effort and fail-open: an
/// unrecognised protocol, a missing connection block, or a missing required
/// endpoint field all yield <c>false</c>, and the source is simply left out of
/// the duplicate check. A missed warning is a far better outcome than a false
/// one raised against a configuration that works today.
/// </para>
/// <para>
/// The rule is scoped to protocols whose peer is a device or server with a
/// bounded number of concurrent connections — the failure this warning exists
/// to pre-empt. HTTP-polled sources (MTConnect, Brother HTTP) are excluded on
/// purpose.
/// </para>
/// </remarks>
internal static class SourceEndpointIdentity
{
    // ---- Protocol family keys (also the leading segment of the endpoint key) ----
    private const string FamilyModbus = "modbus";
    private const string FamilyS7 = "s7";
    private const string FamilyMelsec = "melsec";
    private const string FamilyFocas2 = "focas2";
    private const string FamilyEthernetIp = "ethernetip";
    private const string FamilyOpcUaClient = "opcua-client";

    /// <summary>
    /// Try to resolve the device endpoint <paramref name="source"/> connects to.
    /// </summary>
    /// <param name="source">The configured source instance.</param>
    /// <param name="key">
    /// On success, an opaque comparison key. Two sources sharing this key target
    /// the same endpoint. Includes the protocol family, so two different
    /// protocols that happen to share a host never collide.
    /// </param>
    /// <param name="display">
    /// On success, an operator-readable rendering of the endpoint for the
    /// warning message (e.g. <c>"Modbus TCP 192.168.1.10:502"</c>).
    /// </param>
    /// <returns><c>true</c> if the endpoint could be resolved with confidence.</returns>
    public static bool TryResolve(SourceInstanceConfig? source, out string key, out string display)
    {
        key = string.Empty;
        display = string.Empty;

        if (source?.Connection is not { } connection || connection.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var family = NormaliseProtocolName(source.ProtocolName);

        return family switch
        {
            "modbustcp" or "modbusrtu" or "modbus" =>
                TryResolveModbus(connection, family, out key, out display),
            FamilyS7 => TryResolveS7(connection, out key, out display),
            FamilyMelsec => TryResolveMelsec(connection, out key, out display),
            FamilyFocas2 => TryResolveFocas2(connection, out key, out display),
            FamilyEthernetIp => TryResolveEthernetIp(connection, out key, out display),
            "opcuaclient" => TryResolveOpcUaClient(connection, out key, out display),
            _ => false,
        };
    }

    // ------------------------------------------------------------------------
    // Modbus — ModbusTcpConnectionKeys: host, port, encapsulation, serialPort
    // ------------------------------------------------------------------------
    private static bool TryResolveModbus(JsonElement connection, string family, out string key, out string display)
    {
        key = string.Empty;
        display = string.Empty;

        // The adapter defaults an "modbusrtu" instance to serial RTU and every
        // other Modbus instance to native TCP; an explicit `encapsulation`
        // value wins either way (ModbusTcpSourceConfiguration.ReadEncapsulation).
        var serialByDefault = string.Equals(family, "modbusrtu", StringComparison.Ordinal);
        var serial = IsSerialEncapsulation(ReadString(connection, "encapsulation"), serialByDefault);

        if (serial)
        {
            var serialPort = ReadString(connection, "serialPort")?.Trim();
            if (string.IsNullOrEmpty(serialPort))
            {
                return false;
            }

            // COM port names are case-insensitive on Windows; upper-casing also
            // collapses trivial /dev/tty casing differences, which in practice
            // always denote the same line.
            key = $"{FamilyModbus}|serial|{serialPort.ToUpperInvariant()}";
            display = $"Modbus serial port {serialPort}";
            return true;
        }

        var host = NormaliseHost(ReadString(connection, "host"));
        if (host is null)
        {
            return false;
        }

        // Native TCP and RTU-over-TCP share one socket identity, which is what
        // the connection budget is actually spent on.
        var port = ReadInt(connection, "port", defaultValue: 502);
        key = $"{FamilyModbus}|tcp|{host}|{port}";
        display = $"Modbus TCP {host}:{port}";
        return true;
    }

    // ------------------------------------------------------------------------
    // Siemens S7 — S7ConnectionKeys: host, rack, slot (port intentionally omitted)
    // ------------------------------------------------------------------------
    private static bool TryResolveS7(JsonElement connection, out string key, out string display)
    {
        key = string.Empty;
        display = string.Empty;

        var host = NormaliseHost(ReadString(connection, "host"));
        if (host is null)
        {
            return false;
        }

        var rack = ReadInt(connection, "rack", defaultValue: 0);
        var slot = ReadInt(connection, "slot", defaultValue: 1);

        key = $"{FamilyS7}|{host}|{rack}|{slot}";
        display = $"S7 {host} rack {rack} slot {slot}";
        return true;
    }

    // ------------------------------------------------------------------------
    // Mitsubishi MELSEC — MelsecConnectionKeys: host, port
    // ------------------------------------------------------------------------
    private static bool TryResolveMelsec(JsonElement connection, out string key, out string display)
    {
        key = string.Empty;
        display = string.Empty;

        var host = NormaliseHost(ReadString(connection, "host"));
        if (host is null)
        {
            return false;
        }

        // The MELSEC adapter has no meaningful port default (0 = unset), because
        // the port is whatever the Ethernet module was configured with.
        var port = ReadInt(connection, "port", defaultValue: 0);

        key = $"{FamilyMelsec}|{host}|{port}";
        display = port > 0
            ? $"MELSEC {host}:{port}"
            : $"MELSEC {host}";
        return true;
    }

    // ------------------------------------------------------------------------
    // FANUC FOCAS2 — Focas2ConnectionKeys: ipAddress, port
    // ------------------------------------------------------------------------
    private static bool TryResolveFocas2(JsonElement connection, out string key, out string display)
    {
        key = string.Empty;
        display = string.Empty;

        var host = NormaliseHost(ReadString(connection, "ipAddress"));
        if (host is null)
        {
            return false;
        }

        var port = ReadInt(connection, "port", defaultValue: 8193);

        key = $"{FamilyFocas2}|{host}|{port}";
        display = $"FOCAS2 {host}:{port}";
        return true;
    }

    // ------------------------------------------------------------------------
    // EtherNet/IP — EthernetIpConnectionKeys: host, path
    // ------------------------------------------------------------------------
    private static bool TryResolveEthernetIp(JsonElement connection, out string key, out string display)
    {
        key = string.Empty;
        display = string.Empty;

        var host = NormaliseHost(ReadString(connection, "host"));
        if (host is null)
        {
            return false;
        }

        // Only an explicit CIP path participates. The adapter's per-CPU-family
        // default path is not replicated here, so two sources that differ only
        // by an omitted path are under-reported rather than wrongly reported.
        var path = ReadString(connection, "path")?.Trim() ?? string.Empty;

        key = $"{FamilyEthernetIp}|{host}|{path}";
        display = path.Length == 0
            ? $"EtherNet/IP {host}"
            : $"EtherNet/IP {host} path {path}";
        return true;
    }

    // ------------------------------------------------------------------------
    // OPC UA Client — OpcUaClientConnectionKeys: endpointUrl
    // ------------------------------------------------------------------------
    private static bool TryResolveOpcUaClient(JsonElement connection, out string key, out string display)
    {
        key = string.Empty;
        display = string.Empty;

        var endpointUrl = ReadString(connection, "endpointUrl")?.Trim();
        if (string.IsNullOrEmpty(endpointUrl))
        {
            return false;
        }

        var normalised = endpointUrl.TrimEnd('/').ToLowerInvariant();

        key = $"{FamilyOpcUaClient}|{normalised}";
        display = $"OPC UA {endpointUrl}";
        return true;
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// Fold a configured protocol name to a comparison form. Hyphens are
    /// stripped so the historical <c>"modbus-tcp"</c> spelling and the current
    /// <c>"modbustcp"</c> spelling resolve to the same family.
    /// </summary>
    private static string NormaliseProtocolName(string? protocolName)
    {
        if (string.IsNullOrWhiteSpace(protocolName))
        {
            return string.Empty;
        }

        return protocolName.Trim().ToLowerInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Trim and lower-case a host; returns null when absent or blank.</summary>
    private static string? NormaliseHost(string? host)
    {
        var trimmed = host?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Mirrors <c>ModbusTcpSourceConfiguration.ReadEncapsulation</c>: an absent
    /// or blank value takes the protocol-name-derived default; an explicit
    /// value selects serial only for the serial spellings, and anything else
    /// falls back to TCP.
    /// </summary>
    private static bool IsSerialEncapsulation(string? encapsulation, bool serialByDefault)
    {
        var value = encapsulation?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return serialByDefault;
        }

        return value.Equals("serialRtu", StringComparison.OrdinalIgnoreCase)
            || value.Equals("serial-rtu", StringComparison.OrdinalIgnoreCase)
            || value.Equals("serial", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Read a JSON string property; mirrors the adapters' own reader.</summary>
    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// Read a JSON integer property; mirrors the adapters' own reader, which
    /// accepts only the Number kind and otherwise falls back to the default.
    /// </summary>
    private static int ReadInt(JsonElement obj, string name, int defaultValue) =>
        obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out var parsed)
            ? parsed
            : defaultValue;
}
