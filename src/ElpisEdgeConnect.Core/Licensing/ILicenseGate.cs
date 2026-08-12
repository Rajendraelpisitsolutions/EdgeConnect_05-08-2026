// ============================================================================
// File: Licensing/ILicenseGate.cs
// Purpose: Abstraction over license enforcement for B2's configuration
//          validator. The real implementation lands in B3 (LicenseManager);
//          B2 ships only the contract plus a default permissive
//          implementation so the validator pipeline shape is locked now.
// Reference: ARCHITECTURE_BLUEPRINT.md §7 (3-layer licensing), B2 hand-off
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Validates a configuration against the active license. Called by the
/// configuration validator as the final stage of the apply pipeline.
/// </summary>
/// <remarks>
/// <para>
/// In B2 the only implementation is <see cref="AllowAllLicenseGate"/>,
/// which always permits everything. This keeps the pipeline shape stable
/// for B3 to drop in a real signed-license-validating implementation
/// without modifying <c>ConfigurationValidator</c>.
/// </para>
/// <para>
/// The gate is the third stage of the validator pipeline (after
/// DataAnnotations and cross-record). It receives the typed configuration
/// after structural validation has passed and is responsible only for
/// license-related rejections (module not licensed, instance limit
/// exceeded, license expired and grace period exhausted).
/// </para>
/// </remarks>
public interface ILicenseGate
{
    /// <summary>
    /// Evaluate the configuration against the active license. Returns a
    /// result describing whether the configuration is permitted and, if
    /// not, why.
    /// </summary>
    /// <param name="configuration">The configuration to evaluate. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<LicenseGateResult> EvaluateAsync(
        GatewayConfiguration configuration,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result returned by <see cref="ILicenseGate.EvaluateAsync"/>. When
/// <see cref="Allowed"/> is false, <see cref="BlockedModules"/> lists the
/// protocol module names that are not licensed and <see cref="Reasons"/>
/// carries human-readable explanations suitable for the validation result.
/// </summary>
public sealed record LicenseGateResult
{
    /// <summary>True if the configuration is permitted by the active license.</summary>
    public required bool Allowed { get; init; }

    /// <summary>
    /// Protocol module identifiers that the configuration uses but the
    /// license does not permit. Empty when <see cref="Allowed"/> is true.
    /// </summary>
    public IReadOnlyList<string> BlockedModules { get; init; } = [];

    /// <summary>Human-readable reasons. Empty when allowed.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>
    /// Non-blocking warnings (e.g., "license in grace period, 12 days
    /// remaining"). Empty by default. Allowed configurations may still carry
    /// warnings; the validator surfaces them through the audit log.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The result returned by an unconditionally permissive gate.</summary>
    public static LicenseGateResult AllowAll { get; } = new() { Allowed = true };
}
