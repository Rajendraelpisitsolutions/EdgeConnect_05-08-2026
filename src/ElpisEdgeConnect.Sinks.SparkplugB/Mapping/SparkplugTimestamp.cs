// ============================================================================
// File: Mapping/SparkplugTimestamp.cs
// Purpose: The frozen timestamp conversion (plan v2 §3.3): normalize to UTC,
//          REJECT pre-epoch instants with a typed error (an unsigned cast
//          would fabricate a huge future time), floor to whole Unix
//          milliseconds (sub-millisecond precision discarded), and reject
//          upper-range overflow rather than wrap. Applies to metric/payload
//          timestamps AND to DateTime metric values (both are unsigned 64-bit
//          milliseconds on the wire).
// Reference: ADR-0035 Rule 5 (as amended 2026-07-19); plan v3 (frozen) §1.
// ============================================================================

using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Mapping;

/// <summary>
/// Converts canonical UTC instants to Sparkplug unsigned 64-bit Unix
/// milliseconds under the frozen range rules (plan v2 §3.3).
/// </summary>
public static class SparkplugTimestamp
{
    /// <summary>Convert an instant to whole Unix milliseconds (UTC, floored).</summary>
    /// <param name="instant">The instant; any offset is normalized to UTC.</param>
    /// <returns>Whole milliseconds since 1970-01-01T00:00:00Z.</returns>
    /// <exception cref="AdapterException">
    /// Thrown with <see cref="SparkplugErrors.EncodeTimestampPreEpoch"/> when
    /// the instant precedes the Unix epoch.
    /// </exception>
    public static ulong ToUnixMilliseconds(DateTimeOffset instant)
    {
        // ToUnixTimeMilliseconds is offset-aware (normalizes to UTC) and floors
        // toward negative infinity — for the accepted (post-epoch) range that is
        // exactly the frozen sub-millisecond truncation. DateTimeOffset's range
        // (year 1..9999) cannot exceed ulong milliseconds, so overflow is
        // structurally unreachable here; the overflow rejection code exists for
        // any future non-DateTimeOffset time source.
        var milliseconds = instant.ToUnixTimeMilliseconds();
        if (milliseconds < 0)
        {
            throw new AdapterException(new AdapterError
            {
                Code = SparkplugErrors.EncodeTimestampPreEpoch,
                Category = ErrorCategory.DeviceState,
                Message = $"Timestamp '{instant:O}' precedes the Unix epoch and cannot be represented as an unsigned Sparkplug millisecond timestamp.",
                Retryable = false,
            });
        }

        return (ulong)milliseconds;
    }

    /// <summary>
    /// Convert a canonical <see cref="DateTime"/> metric VALUE to wire
    /// milliseconds. Per the canonical UTC policy: <see cref="DateTimeKind.Utc"/>
    /// is used as-is, <see cref="DateTimeKind.Unspecified"/> is treated as UTC,
    /// and <see cref="DateTimeKind.Local"/> is normalized to UTC.
    /// </summary>
    /// <param name="value">The DateTime value.</param>
    /// <returns>Whole milliseconds since the Unix epoch.</returns>
    public static ulong ValueToUnixMilliseconds(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime(),
        };

        return ToUnixMilliseconds(new DateTimeOffset(utc));
    }
}
