// ============================================================================
// File: Routing/ReplayRouteConfigurationException.cs
// Purpose: Registration-time failures specific to a replay-aware route (K1.3 slice 1).
//          Thrown from RoutingEngine.RegisterRouteAsync BEFORE the route is published /
//          any worker starts, so a misconfigured replay route (or a legacy route over an
//          already-enabled replay store) fails closed rather than failing later at runtime.
//          Derives from InvalidOperationException so existing registration catch sites still
//          handle it; carries a CoreErrors Code for precise assertions/diagnostics.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.2-amendment.md
//            §B1 / §B2.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// A replay-route registration failed a fail-closed validation. Each factory carries the
/// matching <see cref="CoreErrors"/> code in <see cref="Code"/> (and as a <c>[CODE]</c> prefix
/// on the message, matching the engine's existing convention).
/// </summary>
public sealed class ReplayRouteConfigurationException : InvalidOperationException
{
    private ReplayRouteConfigurationException(string code, string routeId, string message)
        : base($"[{code}] {message}")
    {
        Code = code;
        RouteId = routeId;
    }

    /// <summary>The <see cref="CoreErrors"/> code for this failure.</summary>
    public string Code { get; }

    /// <summary>The route id that failed to register.</summary>
    public string RouteId { get; }

    /// <summary>A replay-aware sink was configured alongside another sink (a replay route is exactly one sink).</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>The exception.</returns>
    public static ReplayRouteConfigurationException RequiresSingleSink(string routeId) =>
        new(CoreErrors.ReplayRouteRequiresSingleSink, routeId,
            $"Route '{routeId}' has a replay-aware sink and therefore must have exactly one sink.");

    /// <summary>A replay route uses a non-store-and-forward buffer mode.</summary>
    /// <param name="routeId">The route id.</param>
    /// <param name="mode">The offending buffer mode.</param>
    /// <returns>The exception.</returns>
    public static ReplayRouteConfigurationException RequiresStoreAndForward(string routeId, BufferMode mode) =>
        new(CoreErrors.ReplayRouteRequiresStoreAndForward, routeId,
            $"Replay route '{routeId}' requires BufferMode.StoreAndForward but was configured with '{mode}'.");

    /// <summary>A replay route resolved a buffer that does not implement the replay capability.</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>The exception.</returns>
    public static ReplayRouteConfigurationException BufferNotReplayCapable(string routeId) =>
        new(CoreErrors.ReplayRouteBufferNotCapable, routeId,
            $"Replay route '{routeId}' resolved a buffer that does not implement the replay capability.");

    /// <summary>An ordinary route resolved a buffer whose store is already replay-tracking-enabled (no silent downgrade).</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>The exception.</returns>
    public static ReplayRouteConfigurationException AutomaticDowngradeNotAllowed(string routeId) =>
        new(CoreErrors.ReplayRouteDowngradeNotAllowed, routeId,
            $"Route '{routeId}' resolved an already replay-enabled store but has no replay-aware sink; " +
            "automatic downgrade to the legacy enqueue path is not allowed.");

    /// <summary>Activation succeeded but returned an incomplete capability set (an internal invariant failure).</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>The exception.</returns>
    public static ReplayRouteConfigurationException IncompleteActivation(string routeId) =>
        new(CoreErrors.ReplayRouteIncompleteActivation, routeId,
            $"Replay route '{routeId}' activation returned an incomplete capability set (a null provider).");
}
