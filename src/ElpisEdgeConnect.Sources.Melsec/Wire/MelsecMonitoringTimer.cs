// ============================================================================
// File: Wire/MelsecMonitoringTimer.cs
// Purpose: Encode the MC monitoring timer (250 ms units on the wire; 0 = wait
//          indefinitely) using the ADR-0033 Rule 6 CEIL-UP policy — a supplied
//          timeout is never silently shortened — and validate that the client
//          socket timeout is coherent with it.
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.Melsec.Wire;

/// <summary>Result of encoding a monitoring-timer millisecond value into 250 ms units.</summary>
/// <param name="Units">Encoded value in 250 ms units (0 = wait indefinitely).</param>
/// <param name="EffectiveMs">The effective wait in ms after ceil-up (<c>Units * 250</c>).</param>
/// <param name="Rounded">True when the input was ceiled up to the next 250 ms boundary.</param>
public readonly record struct MonitoringTimerEncoding(ushort Units, int EffectiveMs, bool Rounded);

/// <summary>
/// Encodes and validates the MC monitoring timer per ADR-0033 Rule 6.
/// </summary>
public static class MelsecMonitoringTimer
{
    /// <summary>Wire unit size in milliseconds.</summary>
    public const int UnitMs = 250;

    /// <summary>Maximum encodable value (16-bit units field).</summary>
    public const int MaxUnits = 0xFFFF;

    /// <summary>
    /// Encode <paramref name="monitoringTimerMs"/> to 250 ms units, ceiling up so
    /// a caller-supplied timeout is never shortened. <c>0</c> encodes to <c>0</c>
    /// (wait indefinitely).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If negative, or if the ceiled value exceeds <see cref="MaxUnits"/>.
    /// </exception>
    public static MonitoringTimerEncoding Encode(int monitoringTimerMs)
    {
        if (monitoringTimerMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monitoringTimerMs),
                "Monitoring timer must be >= 0 (0 = wait indefinitely).");
        }
        if (monitoringTimerMs == 0)
        {
            return new MonitoringTimerEncoding(0, 0, Rounded: false);
        }

        var units = (monitoringTimerMs + UnitMs - 1) / UnitMs; // ceil-up
        if (units > MaxUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(monitoringTimerMs),
                $"Monitoring timer {monitoringTimerMs} ms exceeds the maximum {MaxUnits * UnitMs} ms.");
        }

        var effectiveMs = units * UnitMs;
        return new MonitoringTimerEncoding((ushort)units, effectiveMs, Rounded: effectiveMs != monitoringTimerMs);
    }

    /// <summary>
    /// Validate that the client socket timeout is not shorter than the device's
    /// (ceil-encoded) monitoring timer — else the client would abandon a read
    /// before the CPU could answer (ADR-0033 Rule 6). When the monitoring timer
    /// is 0 (device waits indefinitely) the client timeout is the only bound and
    /// is always considered coherent.
    /// </summary>
    /// <returns><c>true</c> when coherent; otherwise <c>false</c> with a reason in <paramref name="error"/>.</returns>
    public static bool TryValidateCoherence(int monitoringTimerMs, int requestTimeoutMs, out string? error)
    {
        var encoding = Encode(monitoringTimerMs);
        if (encoding.Units != 0 && requestTimeoutMs < encoding.EffectiveMs)
        {
            error =
                $"RequestTimeoutMs ({requestTimeoutMs} ms) is shorter than the encoded monitoring timer " +
                $"({encoding.EffectiveMs} ms); the client would abandon reads before the CPU responds.";
            return false;
        }
        error = null;
        return true;
    }
}
