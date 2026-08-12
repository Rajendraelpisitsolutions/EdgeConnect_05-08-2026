// ============================================================================
// File: Decoding/ModbusScaleOffset.cs
// Purpose: Apply optional linear scale + offset to a decoded numeric value.
//          Always returns a double so integer raw values that get scaled
//          (e.g. temperature 42 * 0.1 = 4.2) promote correctly.
//
// Rejected for bool and stringN datatypes — ValidateConfigAsync catches
// these at config apply time; this helper stays simple.
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Decoding;

/// <summary>
/// Apply optional linear scale + offset to a decoded numeric value.
/// Formula: <c>result = (raw * scale) + offset</c>. No-op when both
/// <c>scale</c> and <c>offset</c> are null.
/// </summary>
public static class ModbusScaleOffset
{
    /// <summary>
    /// Apply scale/offset to <paramref name="rawValue"/>. When either factor
    /// is non-null the return type promotes to <see cref="double"/>. When
    /// both are null the raw value is returned unchanged, preserving its
    /// original numeric type so the canonical data point carries the
    /// narrowest correct representation.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="rawValue"/> is not a numeric type — the
    /// adapter's config validator should have rejected such tags already,
    /// this is a defensive check for bugs.
    /// </exception>
    public static object Apply(object rawValue, double? scale, double? offset)
    {
        if (scale is null && offset is null)
        {
            return rawValue;
        }

        var raw = rawValue switch
        {
            int i => (double)i,
            long l => (double)l,
            float f => (double)f,
            double d => d,
            short s => (double)s,
            ushort us => (double)us,
            uint u => (double)u,
            ulong ul => (double)ul,
            _ => throw new ArgumentException(
                $"Scale/offset cannot be applied to value of type {rawValue.GetType().Name}. " +
                "Only numeric datatypes support Scale/Offset.", nameof(rawValue)),
        };

        var result = raw * (scale ?? 1.0) + (offset ?? 0.0);
        return result;
    }
}
