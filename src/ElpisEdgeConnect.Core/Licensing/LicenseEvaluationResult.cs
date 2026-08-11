// ============================================================================
// File: Licensing/LicenseEvaluationResult.cs
// Purpose: Result returned by ILicenseManager fast-path checks
//          (IsModuleEnabled, CheckInstanceLimit). NOT the same as
//          LicenseGateResult, which describes a full-config evaluation.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B3
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Single-call evaluation result for a fast-path license check (e.g.
/// <see cref="ILicenseManager.CheckInstanceLimit"/>).
/// </summary>
public sealed record LicenseEvaluationResult
{
    /// <summary>True if the operation is permitted.</summary>
    public required bool Allowed { get; init; }

    /// <summary>
    /// Stable error code from <see cref="Errors.CoreErrors"/> when
    /// <see cref="Allowed"/> is false; <c>null</c> when allowed.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>Human-readable reason; empty when allowed.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Remaining grace period when the license is in
    /// <see cref="LicenseStatus.InGracePeriod"/>; <c>null</c> otherwise.
    /// </summary>
    public TimeSpan? RemainingGrace { get; init; }

    /// <summary>Cached "allowed" instance for the common happy path.</summary>
    public static LicenseEvaluationResult Allow { get; } = new() { Allowed = true };

    /// <summary>Build a denial result.</summary>
    public static LicenseEvaluationResult Deny(string code, string reason) => new()
    {
        Allowed = false,
        Code = code,
        Reason = reason,
    };
}
