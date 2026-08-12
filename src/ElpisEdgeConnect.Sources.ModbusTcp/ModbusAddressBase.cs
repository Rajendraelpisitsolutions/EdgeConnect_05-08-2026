// ============================================================================
// File: ModbusAddressBase.cs
// Purpose: Declares which addressing notation the OPERATOR used when entering
//          tag addresses, so EdgeConnect can normalise them to the zero-based
//          logical addresses the wire protocol requires.
//
//          EdgeConnect is zero-based end-to-end internally (locked contract):
//          the scan planner, transaction executor and wire all speak zero-based
//          addresses. This type exists purely as an INPUT-NORMALISATION layer at
//          the configuration edge — conversion happens once, in
//          ModbusTcpSourceConfiguration.FromSourceInstance, and nothing
//          downstream ever sees a non-zero-based address.
//
//          Vendor documentation overwhelmingly prints the Modicon "4xxxx" form,
//          so an operator reading a PLC manual naturally types 40033 when the
//          wire address is 32. Before this type existed that was silently
//          accepted and produced Quality=Bad/Value=null reads that looked like a
//          device fault.
// Reference: docs/modbus-address-base-design.md
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// The addressing notation used for the operator-entered tag addresses of a
/// Modbus source. Converted once at config-parse time to the zero-based
/// logical address used everywhere downstream.
/// </summary>
public enum ModbusAddressBase
{
    /// <summary>
    /// Zero-based logical addressing — the value entered IS the wire address
    /// (holding register 40001 is entered as <c>0</c>). Default; matches the
    /// behaviour of every configuration written before this option existed.
    /// </summary>
    ZeroBased = 0,

    /// <summary>
    /// One-based addressing — the wire address is the entered value minus one
    /// (holding register 40001 is entered as <c>1</c>).
    /// </summary>
    OneBased = 1,

    /// <summary>
    /// Modicon / data-model notation, the "4xxxx" form printed by most PLC
    /// manuals and HMIs. The class prefix is subtracted:
    /// coils <c>00001</c>, discrete inputs <c>10001</c>, input registers
    /// <c>30001</c>, holding registers <c>40001</c> all map to wire address 0.
    /// </summary>
    Modicon = 2,
}

/// <summary>
/// Conversion + parsing helpers for <see cref="ModbusAddressBase"/>.
/// </summary>
public static class ModbusAddressBaseExtensions
{
    /// <summary>
    /// The Modicon prefix offset for a register class — the value that maps to
    /// wire address 0 (coil <c>00001</c>, discrete input <c>10001</c>, input
    /// register <c>30001</c>, holding register <c>40001</c>).
    /// </summary>
    public static int ModiconOffset(ModbusRegisterClass registerClass) => registerClass switch
    {
        ModbusRegisterClass.Coil => 1,
        ModbusRegisterClass.DiscreteInput => 10001,
        ModbusRegisterClass.InputRegister => 30001,
        ModbusRegisterClass.HoldingRegister => 40001,
        _ => throw new System.ComponentModel.InvalidEnumArgumentException(
            nameof(registerClass), (int)registerClass, typeof(ModbusRegisterClass)),
    };

    /// <summary>
    /// Convert an operator-entered address into the zero-based wire address.
    /// Returns <see langword="false"/> when the result falls outside the valid
    /// 16-bit Modbus address space (e.g. a Modicon holding-register address
    /// below 40001), leaving <paramref name="zeroBased"/> at the raw result so
    /// callers can build a precise error message.
    /// </summary>
    public static bool TryToZeroBased(
        this ModbusAddressBase addressBase,
        int enteredAddress,
        ModbusRegisterClass registerClass,
        out int zeroBased)
    {
        zeroBased = addressBase switch
        {
            ModbusAddressBase.OneBased => enteredAddress - 1,
            ModbusAddressBase.Modicon => enteredAddress - ModiconOffset(registerClass),
            _ => enteredAddress, // ZeroBased — entered value is already the wire address
        };
        return zeroBased >= 0 && zeroBased <= ushort.MaxValue;
    }

    /// <summary>
    /// Parse the <c>addressBase</c> config value. Accepts (case-insensitively)
    /// <c>zeroBased</c>/<c>zero-based</c>/<c>zero</c>, <c>oneBased</c>/<c>one-based</c>/<c>one</c>,
    /// and <c>modicon</c>/<c>4xxxx</c>/<c>dataModel</c>. Unrecognised or empty
    /// values fall back to <paramref name="defaultValue"/>.
    /// </summary>
    public static ModbusAddressBase Parse(string? raw, ModbusAddressBase defaultValue)
    {
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return defaultValue;
        }
        if (s.Equals("oneBased", StringComparison.OrdinalIgnoreCase)
            || s.Equals("one-based", StringComparison.OrdinalIgnoreCase)
            || s.Equals("one", StringComparison.OrdinalIgnoreCase))
        {
            return ModbusAddressBase.OneBased;
        }
        if (s.Equals("modicon", StringComparison.OrdinalIgnoreCase)
            || s.Equals("4xxxx", StringComparison.OrdinalIgnoreCase)
            || s.Equals("dataModel", StringComparison.OrdinalIgnoreCase)
            || s.Equals("data-model", StringComparison.OrdinalIgnoreCase))
        {
            return ModbusAddressBase.Modicon;
        }
        return ModbusAddressBase.ZeroBased;
    }

    /// <summary>
    /// True when a zero-based address looks like it was really Modicon notation
    /// the operator forgot to convert (10001–19999 discrete-input series, or
    /// 30001–49999 input/holding-register series). Used to turn a silent
    /// bad-read into a config-time validation error.
    /// </summary>
    public static bool LooksLikeModiconNotation(int zeroBasedAddress) =>
        (zeroBasedAddress >= 10001 && zeroBasedAddress <= 19999)
        || (zeroBasedAddress >= 30001 && zeroBasedAddress <= 49999);
}
