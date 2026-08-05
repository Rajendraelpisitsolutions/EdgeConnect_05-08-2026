// ============================================================================
// File: S7MemoryArea.cs
// Purpose: The six addressable memory areas of a Siemens S7 PLC. Used by
//          S7Address and S7ScanPlanner to decide which Sharp7 read call
//          to issue and how to group tags into contiguous reads.
//
//          The integer values match the S7 protocol's area codes — the
//          same constants used by Snap7 and Sharp7 (S7Area enum). Keeping
//          them aligned means a wrapper-level cast suffices when calling
//          into the library.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
// ============================================================================

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Memory areas exposed by the S7 protocol. Numeric values match the
/// Snap7/Sharp7 <c>S7Area</c> codes so a direct cast is valid in the
/// transport layer.
/// </summary>
public enum S7MemoryArea
{
    /// <summary>Process inputs (also addressed as <c>I</c> / <c>E</c>).</summary>
    Input = 0x81,

    /// <summary>Process outputs (also addressed as <c>Q</c> / <c>A</c>).</summary>
    Output = 0x82,

    /// <summary>Bit memory / markers / flags (<c>M</c>).</summary>
    Marker = 0x83,

    /// <summary>User data block (<c>DB</c>). Requires a non-zero DbNumber.</summary>
    DataBlock = 0x84,

    /// <summary>Counter area (<c>C</c>).</summary>
    Counter = 0x1C,

    /// <summary>Timer area (<c>T</c>).</summary>
    Timer = 0x1D,
}
