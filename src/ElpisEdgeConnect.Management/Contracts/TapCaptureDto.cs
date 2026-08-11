// ============================================================================
// File: Contracts/TapCaptureDto.cs
// Purpose: Wire shape for one Live Data Tap capture streamed over SSE
//          (ADR-0018). The value is already privacy-masked at capture
//          (ADR-0018A) — this DTO never carries cleartext sensitive data.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Diagnostics;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>One captured point, as streamed to a tap subscriber.</summary>
public sealed record TapCaptureDto
{
    /// <summary>The route this capture belongs to.</summary>
    public required string RouteId { get; init; }

    /// <summary><c>"Source"</c> or <c>"Sink"</c>.</summary>
    public required string Side { get; init; }

    /// <summary>Sink instance id for a sink-side capture; null for source.</summary>
    public string? SinkInstanceId { get; init; }

    /// <summary>Canonical tag name.</summary>
    public required string TagName { get; init; }

    /// <summary>The value as a string — already masked to <c>***</c> when sensitive.</summary>
    public string? Value { get; init; }

    /// <summary>Declared canonical value type.</summary>
    public required string ValueType { get; init; }

    /// <summary>Data quality.</summary>
    public required string Quality { get; init; }

    /// <summary>Device timestamp (UTC, ISO 8601).</summary>
    public required string DeviceTimestampUtc { get; init; }

    /// <summary>Upstream per-source sequence number.</summary>
    public required long SequenceNumber { get; init; }

    /// <summary>Stable Compare join key (source/sink share it).</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Monotonic per-route capture sequence (SSE cursor).</summary>
    public required long CaptureSequence { get; init; }

    /// <summary>Gateway time the capture was taken (UTC, ISO 8601).</summary>
    public required string CapturedAtUtc { get; init; }

    /// <summary>True when the value was masked by the sensitive-tag policy.</summary>
    public bool Redacted { get; init; }

    /// <summary>
    /// Quality reason / error detail carried on the point (e.g. the Modbus read
    /// failure that produced a <c>Bad</c>/<c>null</c> point). Null when the point
    /// carries no reason (typical for good-quality values).
    /// </summary>
    public string? QualityReason { get; init; }

    /// <summary>Project a core <see cref="TapCapture"/> into its wire shape.</summary>
    public static TapCaptureDto From(TapCapture c)
    {
        var value = c.Point.Value;
        var redacted = value is string s && s == TapValueMasker.MaskMarker;
        return new TapCaptureDto
        {
            RouteId = c.RouteId,
            Side = c.Side.ToString(),
            SinkInstanceId = c.SinkInstanceId,
            TagName = c.Point.TagName,
            Value = value?.ToString(),
            ValueType = c.Point.ValueType.ToString(),
            Quality = c.Point.Quality.ToString(),
            DeviceTimestampUtc = c.Point.DeviceTimestamp.ToString("O"),
            SequenceNumber = c.Point.SequenceNumber,
            CorrelationId = c.CorrelationId,
            CaptureSequence = c.CaptureSequence,
            CapturedAtUtc = c.CapturedAtUtc.ToString("O"),
            Redacted = redacted,
            QualityReason = c.Point.QualityReason,
        };
    }
}

/// <summary>Rolling status pushed to the tap subscriber for the banner.</summary>
public sealed record TapStatusDto
{
    /// <summary>True while capture is active.</summary>
    public required bool Active { get; init; }

    /// <summary>Lifetime source-side captures since activation.</summary>
    public required long SourceCaptured { get; init; }

    /// <summary>Lifetime sink-side captures since activation.</summary>
    public required long SinkCaptured { get; init; }

    /// <summary>True when a ring evicted — the operator is seeing a recent sample.</summary>
    public required bool Truncated { get; init; }

    /// <summary>Project a core <see cref="RouteTapStatus"/> into its wire shape.</summary>
    public static TapStatusDto From(RouteTapStatus s) => new()
    {
        Active = s.Active,
        SourceCaptured = s.SourceCaptured,
        SinkCaptured = s.SinkCaptured,
        Truncated = s.Truncated,
    };
}
