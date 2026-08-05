// ============================================================================
// File: Diagnostics/GatewayStartupEvent.cs
// Purpose: Process-lifetime boot-time signal record. Captures observations
//          made at gateway startup that need to be visible on the Studio's
//          Diagnostics page WITHOUT abusing the audit chain (which describes
//          configuration CHANGES per ADR-0006) or the per-route event ring
//          buffers (which require a route scope).
//
//          First consumer: M.2b.3.1 demo-mode activation. Future consumers
//          may include license-state alerts, native-library-load warnings,
//          and manifest-mismatch signals.
//
//          LOCKED behaviour:
//            * Append-only for the process lifetime (no Clear, no Remove).
//            * In-memory only; not persisted, not chained, not signed.
//            * Gateway-scoped — no RouteId / SourceId coupling.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md §1
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// A single boot-time observation surfaced to the Studio's Diagnostics page.
/// Frozen at construction.
/// </summary>
public sealed record GatewayStartupEvent
{
    /// <summary>
    /// Stable machine-readable code, dotted lowercase
    /// (e.g. <c>"focas2.fake-mode.activated"</c>). Used by UI filtering
    /// and ops runbooks.
    /// </summary>
    public required string EventCode { get; init; }

    /// <summary>Human-readable message rendered in the Diagnostics row.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Severity tag matching the Studio's existing event severity vocabulary.
    /// Use <c>"Info"</c>, <c>"Warning"</c>, or <c>"Critical"</c>.
    /// </summary>
    public required string Severity { get; init; }

    /// <summary>When the event was observed (UTC).</summary>
    public required DateTime EmittedAtUtc { get; init; }
}
