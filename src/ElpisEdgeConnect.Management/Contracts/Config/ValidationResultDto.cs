// ============================================================================
// File: Contracts/Config/ValidationResultDto.cs
// Purpose: Wire-shape mirror of Core's ValidationResult for the
//          /validate endpoint and the 409 body on /apply when
//          revalidation fails. 1:1 with the Core type — keeps the wire
//          contract stable even if Core's ValidationResult evolves.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2a
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts.Config;

/// <summary>
/// Result of a configuration validation call. Pass-through shape of
/// <c>Core.Adapters.ValidationResult</c> with one wire addition
/// (<see cref="ValidatedAtUtc"/>) so operators see how fresh the result is.
/// </summary>
public sealed record ValidationResultDto
{
    /// <summary>True when the configuration is valid and safe to apply.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Fatal validation errors. Non-empty means <see cref="IsValid"/> is false.</summary>
    public required IReadOnlyList<ValidationProblemDto> Errors { get; init; }

    /// <summary>Non-fatal warnings that should be surfaced but do not block apply.</summary>
    public required IReadOnlyList<ValidationProblemDto> Warnings { get; init; }

    /// <summary>UTC timestamp the validation pass ran.</summary>
    public required DateTime ValidatedAtUtc { get; init; }
}

/// <summary>
/// One validation issue, used for both errors and warnings. Pass-through
/// shape of <c>Core.Adapters.ValidationIssue</c>.
/// </summary>
public sealed record ValidationProblemDto
{
    /// <summary>Machine-readable code (e.g. <c>"CORE.CONFIG_MISSING_FIELD"</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable message.</summary>
    public required string Message { get; init; }

    /// <summary>JSON path or field name this issue applies to, if known.</summary>
    public string? Path { get; init; }
}
