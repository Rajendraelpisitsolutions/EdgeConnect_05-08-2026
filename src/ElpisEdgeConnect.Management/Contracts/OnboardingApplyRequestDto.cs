// ============================================================================
// File: Contracts/OnboardingApplyRequestDto.cs
// Purpose: Request body for POST /api/v1/onboarding/apply (ADR-0016 Rule 6).
//          Carries the three buildable entities collected by the
//          OnboardingFlow meta-wizard plus an optional gateway-identity
//          override from the Welcome step.
// Reference: docs/decisions/0016-onboarding-meta-wizard.md
//            docs/sessions/2026-05-27-connect-a-device-plan-v2.1.md §3.6
// ============================================================================

using System.ComponentModel.DataAnnotations;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Request body for the bundled onboarding apply. All three entities
/// (source, sink, route) are required; identity overrides are optional
/// and only consumed when they differ from the current config.
/// </summary>
public sealed record OnboardingApplyRequestDto
{
    /// <summary>The new source the operator configured in Step 3.</summary>
    [Required]
    public required SourceInstanceConfig Source { get; init; }

    /// <summary>The new sink the operator configured in Step 4.</summary>
    [Required]
    public required SinkInstanceConfig Sink { get; init; }

    /// <summary>The new route the operator configured in Step 5.</summary>
    [Required]
    public required RouteConfig Route { get; init; }

    /// <summary>
    /// Optional GatewayId override from the Welcome step. Applied only
    /// when present and different from the current gateway identity.
    /// </summary>
    public string? GatewayIdOverride { get; init; }

    /// <summary>
    /// Optional GatewayName override from the Welcome step.
    /// </summary>
    public string? GatewayNameOverride { get; init; }

    /// <summary>
    /// Free-form actor stamped on the audit entry. Defaults to
    /// <c>"system"</c> when null/empty.
    /// </summary>
    public string? Actor { get; init; }
}
