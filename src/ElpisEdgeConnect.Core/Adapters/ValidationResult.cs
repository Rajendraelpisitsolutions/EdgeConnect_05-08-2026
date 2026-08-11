// ============================================================================
// File: Adapters/ValidationResult.cs
// Purpose: Result of a configuration validation call.
// ============================================================================

using System.Collections.Generic;
using System.Linq;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Result of a configuration validation call such as
/// <see cref="ISourceAdapter.ValidateConfigAsync"/>.
/// </summary>
public sealed record ValidationResult
{
    /// <summary>True if the configuration is valid and safe to apply.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Fatal validation errors. Non-empty means <see cref="IsValid"/> is false.</summary>
    public IReadOnlyList<ValidationIssue> Errors { get; init; } = [];

    /// <summary>Non-fatal warnings that should be surfaced but do not block apply.</summary>
    public IReadOnlyList<ValidationIssue> Warnings { get; init; } = [];

    /// <summary>Convenience: a successful validation with no issues.</summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>Convenience: a failed validation with a single error.</summary>
    public static ValidationResult Failure(string code, string message, string? path = null) =>
        new()
        {
            IsValid = false,
            Errors = [new ValidationIssue { Code = code, Message = message, Path = path }],
        };

    /// <summary>Convenience: a successful validation with one or more warnings.</summary>
    public static ValidationResult SuccessWithWarnings(params ValidationIssue[] warnings) =>
        new()
        {
            IsValid = true,
            Warnings = warnings.ToList(),
        };
}

/// <summary>
/// A single validation issue. Used for both errors and warnings.
/// </summary>
public sealed record ValidationIssue
{
    /// <summary>Machine-readable code (e.g., <c>CORE.CONFIG_MISSING_FIELD</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable message.</summary>
    public required string Message { get; init; }

    /// <summary>JSON path or field name this issue applies to, if known.</summary>
    public string? Path { get; init; }
}
