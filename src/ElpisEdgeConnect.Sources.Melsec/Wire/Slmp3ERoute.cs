// ============================================================================
// File: Wire/Slmp3ERoute.cs
// Purpose: The 3E frame route header fields (network / PC / destination module
//          / station). Defaults address the local CPU.
// ============================================================================

namespace ElpisEdgeConnect.Sources.Melsec.Wire;

/// <summary>
/// 3E-frame route header fields. All are echoed back by the CPU in the response.
/// </summary>
/// <param name="NetworkNo">Network number (default 0x00 = local).</param>
/// <param name="PcNo">PC / station number (default 0xFF = local CPU).</param>
/// <param name="RequestDestModuleIoNo">Request destination module I/O number (default 0x03FF).</param>
/// <param name="RequestDestModuleStationNo">Request destination module station number (default 0x00).</param>
public readonly record struct Slmp3ERoute(
    byte NetworkNo,
    byte PcNo,
    ushort RequestDestModuleIoNo,
    byte RequestDestModuleStationNo)
{
    /// <summary>Local-CPU defaults: network 0x00, PC 0xFF, module I/O 0x03FF, station 0x00.</summary>
    public static Slmp3ERoute LocalCpu => new(0x00, 0xFF, 0x03FF, 0x00);
}
