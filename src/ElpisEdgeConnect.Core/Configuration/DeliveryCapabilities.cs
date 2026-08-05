// ============================================================================
// File: Configuration/DeliveryCapabilities.cs
// Purpose: A sink's advertised delivery capabilities and the protocol-neutral
//          rules that (a) compare a route's required acknowledgment boundary
//          against what the sink can offer and (b) validate a delivery policy
//          against a sink, returning a typed result. Undefined enum values fail
//          CLOSED. Broker-acknowledged AtLeastOnce is derived from the ordered
//          boundary (no second stored flag that could drift).
// Reference: ARCHITECTURE_BLUEPRINT.md §19.7; docs/decisions/0036-*.md Rule 1;
//            PR #180 review. (Full draft->validate->apply integration + error
//            surfacing is K4; this file provides the primitive + typed result.)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// The delivery guarantees a sink advertises. The ordered
/// <see cref="AcknowledgementBoundary"/> is the single authoritative capability;
/// <see cref="SupportsBrokerAcknowledgedAtLeastOnce"/> is derived from it and fails
/// closed for an undefined boundary.
/// </summary>
public sealed record DeliveryCapabilities
{
    /// <summary>Whether the sink supports durable store-and-forward buffering.</summary>
    public required bool SupportsStoreAndForward { get; init; }

    /// <summary>The strongest acknowledgment the sink can provide for a publish.</summary>
    public required DeliveryAcknowledgementBoundary AcknowledgementBoundary { get; init; }

    /// <summary>
    /// Broker-acknowledged AtLeastOnce is possible only when the boundary is a
    /// <em>defined</em> value at <see cref="DeliveryAcknowledgementBoundary.Broker"/>
    /// or stronger. An undefined boundary fails closed (returns <c>false</c>).
    /// </summary>
    public bool SupportsBrokerAcknowledgedAtLeastOnce =>
        Enum.IsDefined(AcknowledgementBoundary) &&
        AcknowledgementBoundary >= DeliveryAcknowledgementBoundary.Broker;
}

/// <summary>The reason a delivery policy failed validation against a sink.</summary>
public enum DeliveryPolicyErrorCode
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>A boundary value was not a defined enum member.</summary>
    UnknownAcknowledgementBoundary = 1,

    /// <summary>The route requires a stronger boundary than the sink advertises.</summary>
    RequirementExceedsSinkBoundary = 2,

    /// <summary><c>AtMostOnce</c> was paired with a positive acknowledgment requirement.</summary>
    AtMostOnceRequiresNoBoundary = 3,

    /// <summary>The delivery mode was not a defined enum member.</summary>
    UnknownDeliveryMode = 4,
}

/// <summary>A typed delivery-policy validation result.</summary>
/// <param name="IsValid">True when the policy is valid against the sink.</param>
/// <param name="Error">The error code (<see cref="DeliveryPolicyErrorCode.None"/> when valid).</param>
/// <param name="Message">A human-readable message (null when valid).</param>
public readonly record struct DeliveryValidationResult(bool IsValid, DeliveryPolicyErrorCode Error, string? Message)
{
    /// <summary>The valid result.</summary>
    public static DeliveryValidationResult Valid { get; } = new(true, DeliveryPolicyErrorCode.None, null);

    /// <summary>Create a failed result.</summary>
    /// <param name="error">The error code.</param>
    /// <param name="message">The message.</param>
    /// <returns>A failed result.</returns>
    public static DeliveryValidationResult Fail(DeliveryPolicyErrorCode error, string message) =>
        new(false, error, message);
}

/// <summary>
/// Protocol-neutral rules for delivery acknowledgment boundaries: the raw
/// comparison primitive and a typed policy-against-sink validation.
/// </summary>
public static class DeliveryBoundaryRules
{
    /// <summary>
    /// True when <paramref name="required"/> does not exceed <paramref name="available"/>.
    /// Both values must be defined enum members; an unknown value is rejected (throws).
    /// </summary>
    /// <param name="required">The boundary a route requires.</param>
    /// <param name="available">The boundary a sink advertises.</param>
    /// <returns><c>true</c> when the requirement is met.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either value is not a defined enum member.</exception>
    public static bool RequirementIsMet(
        DeliveryAcknowledgementBoundary required,
        DeliveryAcknowledgementBoundary available)
    {
        EnsureDefined(required, nameof(required));
        EnsureDefined(available, nameof(available));
        return required <= available;
    }

    /// <summary>
    /// Validate a route's delivery policy against a sink's capabilities, returning a
    /// typed result: rejects unknown boundary values, an <c>AtMostOnce</c> mode with a
    /// positive required boundary, and a required boundary that exceeds the sink's.
    /// </summary>
    /// <param name="policy">The route delivery policy.</param>
    /// <param name="sink">The sink's advertised capabilities.</param>
    /// <returns>The validation result.</returns>
    public static DeliveryValidationResult ValidateAgainstSink(
        DeliveryPolicyConfig policy, DeliveryCapabilities sink)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(sink);

        if (!Enum.IsDefined(policy.Mode))
        {
            return DeliveryValidationResult.Fail(
                DeliveryPolicyErrorCode.UnknownDeliveryMode,
                $"Unknown delivery mode '{(int)policy.Mode}'.");
        }

        if (!Enum.IsDefined(policy.RequiredAcknowledgementBoundary))
        {
            return DeliveryValidationResult.Fail(
                DeliveryPolicyErrorCode.UnknownAcknowledgementBoundary,
                $"Unknown required acknowledgment boundary '{(int)policy.RequiredAcknowledgementBoundary}'.");
        }

        if (!Enum.IsDefined(sink.AcknowledgementBoundary))
        {
            return DeliveryValidationResult.Fail(
                DeliveryPolicyErrorCode.UnknownAcknowledgementBoundary,
                $"Unknown sink acknowledgment boundary '{(int)sink.AcknowledgementBoundary}'.");
        }

        if (policy.Mode == DeliveryMode.AtMostOnce &&
            policy.RequiredAcknowledgementBoundary != DeliveryAcknowledgementBoundary.None)
        {
            return DeliveryValidationResult.Fail(
                DeliveryPolicyErrorCode.AtMostOnceRequiresNoBoundary,
                "AtMostOnce requires RequiredAcknowledgementBoundary = None; a positive requirement is only meaningful for AtLeastOnce.");
        }

        if (policy.RequiredAcknowledgementBoundary > sink.AcknowledgementBoundary)
        {
            return DeliveryValidationResult.Fail(
                DeliveryPolicyErrorCode.RequirementExceedsSinkBoundary,
                $"Route requires {policy.RequiredAcknowledgementBoundary} but the sink advertises only {sink.AcknowledgementBoundary}.");
        }

        return DeliveryValidationResult.Valid;
    }

    private static void EnsureDefined(DeliveryAcknowledgementBoundary value, string paramName)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Unknown DeliveryAcknowledgementBoundary value.");
        }
    }
}
