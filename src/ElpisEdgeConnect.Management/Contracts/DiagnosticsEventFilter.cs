// ============================================================================
// File: Contracts/DiagnosticsEventFilter.cs
// Purpose: Server-side filter shape consumed by
//          /api/v1/diagnostics/events. The API parses query-string
//          parameters into this record; the aggregator branches on
//          which fields are set.
//
//          EventCodes is a list rather than a single string so the API
//          supports both single-code filtering ("just SINK.DEGRADED")
//          and grouped filtering ("all SINK.*" → 3 explicit codes).
//          Single-element lists cover today's UI; the list shape
//          future-proofs the contract.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.2
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Server-side filter for the diagnostics-events query. All fields are
/// optional except <see cref="Limit"/> (defaulted). Aggregators apply
/// fields with AND semantics — an event must match every set field
/// to appear in the result.
/// </summary>
public sealed record DiagnosticsEventFilter
{
    /// <summary>Default cap when the caller doesn't pin one (or asks for &lt;=0).</summary>
    public const int DefaultLimit = 100;

    /// <summary>Hard server-side cap regardless of caller's <see cref="Limit"/>.</summary>
    public const int MaxLimit = 500;

    /// <summary>Max number of <c>EventCodes</c> to accept per query (DoS guard).</summary>
    public const int MaxEventCodes = 20;

    /// <summary>Filter to one route id (exact match). Null = all routes + gateway events.</summary>
    public string? RouteId { get; init; }

    /// <summary>
    /// Filter to a set of event codes. Empty/null = no filtering. Each code
    /// is matched exactly — no prefix/wildcard support yet (intentional,
    /// avoids glob-parsing on untrusted input). To select an entire prefix
    /// like SINK.*, callers pass the explicit list of constants.
    /// </summary>
    public IReadOnlyList<string>? EventCodes { get; init; }

    /// <summary>Filter to a single severity bucket. Null = all severities.</summary>
    public DiagnosticsSeverity? Severity { get; init; }

    /// <summary>Filter to events newer than this UTC timestamp. Null = no time floor.</summary>
    public DateTime? SinceUtc { get; init; }

    /// <summary>
    /// Maximum events to return. Clamped server-side to <see cref="MaxLimit"/>.
    /// Falls back to <see cref="DefaultLimit"/> when not specified or non-positive.
    /// </summary>
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Clamp <see cref="Limit"/> into the legal range.</summary>
    public int EffectiveLimit
    {
        get
        {
            if (Limit <= 0) return DefaultLimit;
            if (Limit > MaxLimit) return MaxLimit;
            return Limit;
        }
    }
}
