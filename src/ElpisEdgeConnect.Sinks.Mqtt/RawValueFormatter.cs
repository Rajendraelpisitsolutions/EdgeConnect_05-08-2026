// ============================================================================
// File: RawValueFormatter.cs
// Purpose: Deterministic raw-scalar formatting for PerTag publish mode.
//          EREMOS V2 consumes these values directly as unquoted strings on
//          per-tag topics. Formatting rules are locked per the plan.
// Reference: PHASE2_ENTRY.md — MQTT sink adapter plan
// ============================================================================

using System;
using System.Globalization;
using System.Text.Json;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sinks.Mqtt;

/// <summary>
/// Formats a <see cref="CanonicalDataPoint"/> value as a raw scalar string
/// suitable for MQTT PerTag payload. Each <see cref="CanonicalValueType"/>
/// has explicit, locked formatting rules — no ad-hoc <c>ToString()</c>.
/// </summary>
public static class RawValueFormatter
{
    /// <summary>
    /// Format the raw value. Returns <c>null</c> for unsupported types
    /// (e.g., <see cref="CanonicalValueType.ByteArray"/>); the caller is
    /// responsible for rejecting those and incrementing
    /// <c>RejectedCount</c>.
    /// </summary>
    /// <param name="value">The boxed value from the canonical point.</param>
    /// <param name="valueType">The declared value type.</param>
    /// <returns>
    /// The formatted string, or <c>null</c> when the type is unsupported
    /// in PerTag mode.
    /// </returns>
    public static string? Format(object? value, CanonicalValueType valueType)
    {
        if (value is null || valueType == CanonicalValueType.Null)
        {
            return string.Empty;
        }

        return valueType switch
        {
            CanonicalValueType.Boolean => value is bool b
                ? (b ? "true" : "false")
                : value.ToString()?.ToLowerInvariant() ?? string.Empty,

            CanonicalValueType.Integer => value is int i
                ? i.ToString(CultureInfo.InvariantCulture)
                : Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture),

            CanonicalValueType.Long => value is long l
                ? l.ToString(CultureInfo.InvariantCulture)
                : Convert.ToInt64(value, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture),

            CanonicalValueType.Float => value is float f
                ? f.ToString("G", CultureInfo.InvariantCulture)
                : Convert.ToSingle(value, CultureInfo.InvariantCulture)
                    .ToString("G", CultureInfo.InvariantCulture),

            CanonicalValueType.Double => value is double d
                ? d.ToString("G", CultureInfo.InvariantCulture)
                : Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    .ToString("G", CultureInfo.InvariantCulture),

            CanonicalValueType.String => value.ToString() ?? string.Empty,

            CanonicalValueType.DateTime => value is DateTime dt
                ? NormalizeToUtc(dt).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
                : value.ToString() ?? string.Empty,

            // ByteArray is NOT supported in PerTag mode — return null to
            // signal rejection.
            CanonicalValueType.ByteArray => null,

            // Array / Object: JSON serialize as a fallback.
            CanonicalValueType.Array or CanonicalValueType.Object =>
                JsonSerializer.Serialize(value),

            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Normalize a DateTime to UTC. Locked rules:
    /// <list type="bullet">
    ///   <item><c>Utc</c> → used directly</item>
    ///   <item><c>Local</c> → converted to UTC</item>
    ///   <item><c>Unspecified</c> → <b>assumed UTC</b> (no conversion)</item>
    /// </list>
    /// Changing this later would silently shift timestamps.
    /// </summary>
    internal static DateTime NormalizeToUtc(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc), // Unspecified → assume UTC
    };
}
