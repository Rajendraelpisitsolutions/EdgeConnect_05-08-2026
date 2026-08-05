// ============================================================================
// File: ModbusRegisterClass.cs
// Purpose: The four classical Modbus address spaces, each served by a distinct
//          read function code.
// Reference: PHASE3_EXECUTION_PLAN.md §5 (Per-tag configuration)
// ============================================================================

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// The four Modbus address spaces readable by the adapter. Each class maps to
/// a single function code; write-back function codes (FC05/06/15/16) are
/// explicitly out of scope for Phase 3 and deferred to Phase 5.
/// </summary>
public enum ModbusRegisterClass
{
    /// <summary>
    /// Discrete writable bit — read with FC01 (Read Coils).
    /// Max 2000 bits per request.
    /// </summary>
    Coil = 0,

    /// <summary>
    /// Discrete read-only bit — read with FC02 (Read Discrete Inputs).
    /// Max 2000 bits per request.
    /// </summary>
    DiscreteInput = 1,

    /// <summary>
    /// 16-bit writable register — read with FC03 (Read Holding Registers).
    /// Max 125 registers per request.
    /// </summary>
    HoldingRegister = 2,

    /// <summary>
    /// 16-bit read-only register — read with FC04 (Read Input Registers).
    /// Max 125 registers per request.
    /// </summary>
    InputRegister = 3,
}

/// <summary>
/// Function-code and limit helpers for <see cref="ModbusRegisterClass"/>.
/// Centralized here so the connection manager, transaction executor, and
/// future scan-group planner all reason about the same numbers.
/// </summary>
internal static class ModbusRegisterClassExtensions
{
    /// <summary>
    /// Modbus function code that reads this register class.
    /// </summary>
    public static byte ToFunctionCode(this ModbusRegisterClass registerClass) =>
        registerClass switch
        {
            ModbusRegisterClass.Coil => 0x01,
            ModbusRegisterClass.DiscreteInput => 0x02,
            ModbusRegisterClass.HoldingRegister => 0x03,
            ModbusRegisterClass.InputRegister => 0x04,
            _ => throw new System.ComponentModel.InvalidEnumArgumentException(
                nameof(registerClass), (int)registerClass, typeof(ModbusRegisterClass)),
        };

    /// <summary>
    /// Protocol-hard maximum quantity per read: 2000 bits for FC01/FC02,
    /// 125 registers for FC03/FC04.
    /// </summary>
    public static int MaxQuantity(this ModbusRegisterClass registerClass) =>
        registerClass switch
        {
            ModbusRegisterClass.Coil or ModbusRegisterClass.DiscreteInput => 2000,
            ModbusRegisterClass.HoldingRegister or ModbusRegisterClass.InputRegister => 125,
            _ => throw new System.ComponentModel.InvalidEnumArgumentException(
                nameof(registerClass), (int)registerClass, typeof(ModbusRegisterClass)),
        };

    /// <summary>True for bit-oriented reads (FC01/FC02).</summary>
    public static bool IsBitRead(this ModbusRegisterClass registerClass) =>
        registerClass is ModbusRegisterClass.Coil or ModbusRegisterClass.DiscreteInput;
}
