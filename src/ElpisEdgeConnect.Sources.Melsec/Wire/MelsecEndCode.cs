// ============================================================================
// File: Wire/MelsecEndCode.cs
// Purpose: Descriptions for common MELSEC / SLMP end codes (the 16-bit
//          completion code in a 3E response). End code 0 = success; non-zero
//          codes become typed protocol errors (never generic exceptions).
//          Descriptions are spec-derived (SH(NA)-080008) and field-unverified.
// ============================================================================

namespace ElpisEdgeConnect.Sources.Melsec.Wire;

/// <summary>
/// Known MELSEC / SLMP end codes and human-readable descriptions for diagnostics.
/// </summary>
public static class MelsecEndCode
{
    /// <summary>Successful completion (end code 0x0000).</summary>
    public const ushort Success = 0x0000;

    /// <summary>
    /// Describe a MELSEC end code for diagnostics. Unknown codes return a
    /// hex-formatted fallback so no code is silently swallowed.
    /// </summary>
    public static string Describe(ushort endCode) => endCode switch
    {
        0x0000 => "success",
        0x0055 => "request cannot be processed in the current CPU state",
        0x4031 => "device number out of range",
        0xC050 => "ASCII communication configured but binary data received",
        0xC051 => "read/write points exceed the allowable range",
        0xC052 => "request data length exceeds the allowable range",
        0xC053 => "random read/write points exceed the allowable range",
        0xC056 => "read address + points exceed the device range",
        0xC059 => "command / subcommand is wrong or unsupported",
        0xC05B => "the CPU cannot read from the specified device",
        0xC05C => "the request data content is wrong (e.g. bad device code)",
        0xC061 => "request data length does not match the number of data items",
        _ => $"unrecognized end code 0x{endCode:X4}",
    };
}
