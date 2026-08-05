// ============================================================================
// File: Contracts/EnableDisableResponseDto.cs
// Purpose: Response envelope for the Enable/Disable verb endpoints.
//          Discriminated by `outcome` per Locked F (NoOp), Locked G
//          (Conflict / StaleView), and Locked D (Applied).
//
//          Single record carries all three shapes via nullable fields.
//          Clients dispatch on `outcome`:
//             * Applied  → present: draftId, validationOutcome, appliedAt, auditRecordId
//             * NoOp     → present: reason, entity, currentEnabled
//             * Conflict → present: error (with code-specific extra fields)
//
//          JSON serialization uses default null-omission semantics so
//          each envelope only carries the fields relevant to its
//          outcome. The discriminator is always present.
// Reference: docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md §1, §2
//            docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan.md §3 Locked D
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Outcome discriminator. Drives both the HTTP status code (at the API
/// layer) and the client-side branch dispatch (in the confirm drawer
/// model).
/// </summary>
public enum EnableDisableOutcome
{
    /// <summary>Draft created + validated + applied. HTTP 200.</summary>
    Applied,

    /// <summary>Current state already equals desired state. HTTP 200. (Locked F.)</summary>
    NoOp,

    /// <summary>
    /// Stale view OR cross-record refusal OR other conflict. HTTP 409.
    /// The <see cref="EnableDisableResponseDto.Error"/> field carries
    /// the structured error code that lets the client branch further.
    /// </summary>
    Conflict,
}

/// <summary>
/// Reason an <see cref="EnableDisableOutcome.NoOp"/> result was produced.
/// Reserved for future extension (e.g. "EntityQuiescing"); MVP carries one.
/// </summary>
public enum EnableDisableNoOpReason
{
    /// <summary>Target entity's current Enabled value already equals desired.</summary>
    AlreadyInDesiredState,
}

/// <summary>
/// Compact reference to an entity, used in NoOp and dependent-list
/// payloads. Mirrors <see cref="ElpisEdgeConnect.Management.Wizards.DependencyRef"/>
/// shape but lives in Contracts so the wire surface doesn't drag in the
/// Wizards namespace.
/// </summary>
public sealed record EnableDisableEntityRef
{
    /// <summary>Lower-case kind tag: <c>"source"</c> / <c>"sink"</c> / <c>"route"</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Instance id.</summary>
    public required string Id { get; init; }

    /// <summary>Operator-readable name when available; falls back to <see cref="Id"/> in UI.</summary>
    public string? Name { get; init; }
}

/// <summary>
/// Error envelope carried by <see cref="EnableDisableOutcome.Conflict"/>
/// responses. The <see cref="Code"/> drives the client branch:
/// <list type="bullet">
/// <item><c>CONFIG.STALE_VIEW</c> — <see cref="ExpectedVersion"/> + <see cref="CurrentVersion"/> populated</item>
/// <item><c>CONFIG.CROSS_RECORD_REFUSED</c> — <see cref="Dependents"/> populated</item>
/// </list>
/// </summary>
public sealed record EnableDisableErrorDto
{
    /// <summary>Structured error code (e.g. <c>CONFIG.STALE_VIEW</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Operator-facing message in operational English (Locked N).</summary>
    public required string Message { get; init; }

    // ── Stale-view fields ──────────────────────────────────────────────

    /// <summary>The version the request claimed; populated for <c>CONFIG.STALE_VIEW</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedVersion { get; init; }

    /// <summary>The server's current version; populated for <c>CONFIG.STALE_VIEW</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentVersion { get; init; }

    // ── Cross-record fields ────────────────────────────────────────────

    /// <summary>Dependent entities blocking the request; populated for <c>CONFIG.CROSS_RECORD_REFUSED</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EnableDisableEntityRef>? Dependents { get; init; }
}

/// <summary>
/// Response envelope. Outcome-discriminated; clients branch on
/// <see cref="Outcome"/>.
/// </summary>
public sealed record EnableDisableResponseDto
{
    /// <summary>Discriminator. Always present.</summary>
    public required EnableDisableOutcome Outcome { get; init; }

    // ── Applied (200) ──────────────────────────────────────────────────

    /// <summary>The created draft's id; populated for <see cref="EnableDisableOutcome.Applied"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DraftId { get; init; }

    /// <summary>Validation outcome string (typically <c>"Passed"</c>); populated for Applied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ValidationOutcome { get; init; }

    /// <summary>UTC apply timestamp; populated for Applied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? AppliedAt { get; init; }

    /// <summary>Audit-record id for the apply; populated for Applied. Surfaces in the snackbar for traceability.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuditRecordId { get; init; }

    // ── NoOp (200) ─────────────────────────────────────────────────────

    /// <summary>Reason classification; populated for <see cref="EnableDisableOutcome.NoOp"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EnableDisableNoOpReason? Reason { get; init; }

    /// <summary>The entity the request targeted; populated for NoOp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EnableDisableEntityRef? Entity { get; init; }

    /// <summary>The entity's current Enabled value; populated for NoOp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CurrentEnabled { get; init; }

    // ── Conflict (409) ─────────────────────────────────────────────────

    /// <summary>Structured error envelope; populated for <see cref="EnableDisableOutcome.Conflict"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EnableDisableErrorDto? Error { get; init; }
}
