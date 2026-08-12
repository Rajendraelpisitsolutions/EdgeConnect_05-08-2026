// ============================================================================
// File: Session/SparkplugNodeCommand.cs
// Purpose: The pure, fail-safe NCMD classifier (plan v3 §1.6, ADR-0036 Rule 4).
//          Decodes an inbound NCMD payload into a REDACTED classification: a valid
//          Node Control/Rebirth = true command (optionally accompanied by unknown
//          extra metrics), or one of the ignored kinds (malformed, missing rebirth
//          metric, explicit null, wrong value type, false). Every non-actionable
//          kind is a NO-OP so a bad or hostile NCMD can never cause a side effect,
//          but each kind is now DISTINGUISHABLE so the actor can tally it and
//          surface a sanitized diagnostic code (slice-7 review B1). The classifier
//          never publishes, mutates protocol counters, touches the store, or exposes
//          a raw metric name or payload byte.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.6, §9, §11.
// ============================================================================

using System;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
using Org.Eclipse.Tahu.Protobuf;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;

/// <summary>The redacted classification of an inbound NCMD payload (plan v3 §1.6, §11 acceptance matrix).</summary>
internal enum SparkplugNodeCommandKind
{
    /// <summary>A well-formed Node Control/Rebirth = true, with no other metrics present.</summary>
    RebirthRequested,

    /// <summary>A well-formed Node Control/Rebirth = true, accompanied by unknown extra metrics.</summary>
    RebirthRequestedWithUnknownExtras,

    /// <summary>The payload did not parse as a Sparkplug NCMD.</summary>
    IgnoredMalformed,

    /// <summary>No Node Control/Rebirth metric was present (includes an unknown-only command).</summary>
    IgnoredMissing,

    /// <summary>The Rebirth metric was present but explicitly null.</summary>
    IgnoredNull,

    /// <summary>The Rebirth metric was present but not a boolean value.</summary>
    IgnoredWrongType,

    /// <summary>The Rebirth metric was present, boolean, but false.</summary>
    IgnoredFalse,

    /// <summary>
    /// More than one <c>Node Control/Rebirth</c> metric was present — an ambiguous command whose meaning would
    /// depend on metric ordering. Fail-safe: ignored regardless of order (slice-7 review r2, focused hardening).
    /// </summary>
    IgnoredAmbiguous,
}

/// <summary>Classifies an inbound NCMD payload (rebirth-command detection with redacted diagnostics).</summary>
internal static class SparkplugNodeCommand
{
    /// <summary>
    /// Classify <paramref name="payload"/> into a redacted <see cref="SparkplugNodeCommandKind"/>. A valid
    /// <c>Node Control/Rebirth = true</c> is actionable (optionally flagged as carrying unknown extras); every
    /// other kind is a no-op with a distinguishable diagnostic. Never throws, never has a side effect.
    /// </summary>
    /// <param name="payload">The raw inbound NCMD payload bytes.</param>
    /// <returns>The classification.</returns>
    public static SparkplugNodeCommandKind Classify(ReadOnlyMemory<byte> payload)
    {
        Payload parsed;
        try
        {
            parsed = Payload.Parser.ParseFrom(payload.Span);
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            return SparkplugNodeCommandKind.IgnoredMalformed;
        }

        Payload.Types.Metric? rebirth = null;
        var rebirthCount = 0;
        var hasOtherMetrics = false;
        foreach (var metric in parsed.Metrics)
        {
            if (string.Equals(metric.Name, SparkplugPayloadEncoder.NodeControlRebirthMetricName, StringComparison.Ordinal))
            {
                rebirth ??= metric;
                rebirthCount++;
            }
            else
            {
                hasOtherMetrics = true;
            }
        }

        if (rebirthCount > 1)
        {
            // Ambiguous: multiple Rebirth metrics — do NOT action one representation (order-dependence).
            return SparkplugNodeCommandKind.IgnoredAmbiguous;
        }

        if (rebirth is null)
        {
            return SparkplugNodeCommandKind.IgnoredMissing; // no rebirth metric (includes unknown-only commands)
        }

        if (rebirth.IsNull)
        {
            return SparkplugNodeCommandKind.IgnoredNull;
        }

        if (rebirth.ValueCase != Payload.Types.Metric.ValueOneofCase.BooleanValue)
        {
            return SparkplugNodeCommandKind.IgnoredWrongType;
        }

        if (!rebirth.BooleanValue)
        {
            return SparkplugNodeCommandKind.IgnoredFalse;
        }

        return hasOtherMetrics
            ? SparkplugNodeCommandKind.RebirthRequestedWithUnknownExtras
            : SparkplugNodeCommandKind.RebirthRequested;
    }

    /// <summary>True when the classification is an actionable rebirth request (with or without extras).</summary>
    public static bool IsActionableRebirth(this SparkplugNodeCommandKind kind) =>
        kind is SparkplugNodeCommandKind.RebirthRequested or SparkplugNodeCommandKind.RebirthRequestedWithUnknownExtras;

    /// <summary>A short, stable, secret-free diagnostic code for the classification (no metric names/bytes).</summary>
    public static string DiagnosticCode(this SparkplugNodeCommandKind kind) => kind switch
    {
        SparkplugNodeCommandKind.RebirthRequested => "rebirth",
        SparkplugNodeCommandKind.RebirthRequestedWithUnknownExtras => "rebirth+unknown-extras",
        SparkplugNodeCommandKind.IgnoredMalformed => "ignored:malformed",
        SparkplugNodeCommandKind.IgnoredMissing => "ignored:missing",
        SparkplugNodeCommandKind.IgnoredNull => "ignored:null",
        SparkplugNodeCommandKind.IgnoredWrongType => "ignored:wrong-type",
        SparkplugNodeCommandKind.IgnoredFalse => "ignored:false",
        SparkplugNodeCommandKind.IgnoredAmbiguous => "ignored:ambiguous",
        _ => "ignored:unknown",
    };
}
