// ============================================================================
// File: Mapping/SparkplugAcquisitionTimestamp.cs
// Purpose: The single, shared, fail-loud conversion of a canonical acquisition
//          System.DateTime to a UTC DateTimeOffset (slice-3 review r2). Both the
//          birth-plan comparator (SparkplugMetricState.FromDataPoint) and the
//          slice-5 CanonicalDataPoint -> SparkplugMetricSample path use this, so a
//          non-UTC instant can never be silently reinterpreted with the machine's
//          local timezone (which would make the encoded milliseconds — and thus the
//          comparator — differ across gateways). Local/Unspecified are rejected, not
//          converted, matching the Core route store's tracked-route timestamp policy.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §5.4, §9.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Mapping;

/// <summary>Converts a canonical acquisition timestamp to a UTC offset, fail-loud on non-UTC.</summary>
internal static class SparkplugAcquisitionTimestamp
{
    /// <summary>
    /// Require a canonical acquisition <see cref="DateTime"/> to be UTC and return it as a
    /// zero-offset <see cref="DateTimeOffset"/>. Never applies the machine's local timezone.
    /// </summary>
    /// <param name="value">The canonical acquisition timestamp.</param>
    /// <returns>The instant as a UTC <see cref="DateTimeOffset"/>.</returns>
    /// <exception cref="AdapterException">
    /// Thrown with <see cref="SparkplugErrors.EncodeTimestampNotUtc"/> when the value's
    /// <see cref="DateTime.Kind"/> is not <see cref="DateTimeKind.Utc"/>.
    /// </exception>
    public static DateTimeOffset RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new AdapterException(new AdapterError
            {
                Code = SparkplugErrors.EncodeTimestampNotUtc,
                Category = ErrorCategory.Internal,
                Message = $"Sparkplug acquisition timestamps must be UTC; received DateTimeKind.{value.Kind}.",
                Retryable = false,
            });
        }

        return new DateTimeOffset(value, TimeSpan.Zero);
    }
}
