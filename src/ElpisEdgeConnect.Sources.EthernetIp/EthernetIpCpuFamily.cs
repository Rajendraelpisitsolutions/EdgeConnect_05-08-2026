// ============================================================================
// File: EthernetIpCpuFamily.cs
// Purpose: The Rockwell CPU families the EtherNet/IP adapter targets, plus the
//          per-family libplctag "plc" token and the default CIP routing path.
//
//          The L8x (ControlLogix 5580) front port requires path "1,0" — baked
//          in here as the ControlLogix/GuardLogix default per the multi-protocol
//          pilot expansion plan v2.1 §3.1 + risk register (a missing path is the
//          single most common "ErrorBadData" support call).
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §3.1
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// Allen-Bradley controller families supported by the EtherNet/IP adapter.
/// Determines the libplctag <c>plc</c> token and the default CIP path.
/// </summary>
public enum EthernetIpCpuFamily
{
    /// <summary>ControlLogix 55xx / 56xx / 5570 / 5580 (L6x / L7x / L8x).</summary>
    ControlLogix = 0,

    /// <summary>CompactLogix L1x / L2x / L3x / L4x / L5x.</summary>
    CompactLogix = 1,

    /// <summary>GuardLogix 5570 / 5380 safety controllers (Logix tag model).</summary>
    GuardLogix = 2,

    /// <summary>MicroLogix 1100 / 1400.</summary>
    MicroLogix = 3,

    /// <summary>Micro800 (Micro820 / Micro850) — uses a distinct CIP variant.</summary>
    Micro800 = 4,

    /// <summary>SLC 500 family.</summary>
    Slc500 = 5,

    /// <summary>PLC-5 family.</summary>
    Plc5 = 6,
}

/// <summary>Per-family libplctag token + default-path helpers.</summary>
public static class EthernetIpCpuFamilyExtensions
{
    /// <summary>
    /// The libplctag <c>plc=</c> token for this family. Logix-class controllers
    /// all use <c>"controllogix"</c>; the smaller families have their own tokens.
    /// </summary>
    public static string LibPlcTagToken(this EthernetIpCpuFamily family) => family switch
    {
        EthernetIpCpuFamily.ControlLogix => "controllogix",
        EthernetIpCpuFamily.CompactLogix => "controllogix",
        EthernetIpCpuFamily.GuardLogix => "controllogix",
        EthernetIpCpuFamily.MicroLogix => "micrologix",
        EthernetIpCpuFamily.Micro800 => "micro800",
        EthernetIpCpuFamily.Slc500 => "slc500",
        EthernetIpCpuFamily.Plc5 => "plc5",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown CPU family."),
    };

    /// <summary>
    /// The default CIP routing path for this family. Logix controllers route to
    /// the CPU in slot 0 over the backplane (<c>"1,0"</c>); the embedded-port
    /// families take no path.
    /// </summary>
    public static string DefaultPath(this EthernetIpCpuFamily family) => family switch
    {
        EthernetIpCpuFamily.ControlLogix => "1,0",
        EthernetIpCpuFamily.CompactLogix => "1,0",
        EthernetIpCpuFamily.GuardLogix => "1,0",
        // MicroLogix / Micro800 / SLC500 / PLC5 have no backplane to route across.
        _ => string.Empty,
    };

    /// <summary>
    /// Parse an operator-facing CPU-family name (case-insensitive). Accepts the
    /// enum names; returns <see langword="null"/> on unknown input.
    /// </summary>
    public static EthernetIpCpuFamily? ParseOrNull(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }
        return Enum.TryParse<EthernetIpCpuFamily>(family.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }
}
