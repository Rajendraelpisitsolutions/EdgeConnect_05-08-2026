// ============================================================================
// File: Identity/IGatewayIdentity.cs
// Purpose: The gateway-scoped identity that every canonical data point
//          carries via CanonicalDataPoint.GatewayId. Established at first
//          start and persisted thereafter (blueprint locked decision #19).
// Reference: ARCHITECTURE_BLUEPRINT.md §3 (Locked Decision #19 — Gateway
//            identity: per-gateway UUID + customer/site binding, established
//            at first start).
// ============================================================================

namespace ElpisEdgeConnect.Core.Identity;

/// <summary>
/// Per-host gateway identity, resolved once during startup and read thereafter.
/// Every canonical data point produced by every source adapter tags its
/// <c>GatewayId</c> from this service so downstream consumers (MQTT topics,
/// diagnostics, license enforcement) can attribute the point to its gateway.
/// </summary>
/// <remarks>
/// <para>
/// The Phase 1 host skeleton created a placeholder file without reading or
/// writing an actual UUID — the FOCAS2 adapter therefore hardcoded
/// <c>"gateway"</c> as the gateway id. This interface replaces that
/// placeholder: the host writes a persistent UUID on first start, reads it
/// on subsequent starts, and exposes the value as <see cref="GatewayId"/>.
/// </para>
/// <para>
/// Implementations are registered as DI singletons. Adapters that need the
/// gateway id should inject this interface rather than accepting the string
/// directly — that way the adapter never holds a stale copy if the identity
/// is ever rotated at runtime (a Phase 4+ management feature).
/// </para>
/// </remarks>
public interface IGatewayIdentity
{
    /// <summary>
    /// The stable gateway identifier. Never null, never empty. Shape is not
    /// enforced at the interface level — concrete implementations choose
    /// UUID, operator-assigned string, etc.
    /// </summary>
    string GatewayId { get; }
}
