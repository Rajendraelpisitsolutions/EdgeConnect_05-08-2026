// ============================================================================
// File: Configuration/IBundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata declared beside each adapter — the
//          single source of truth for which keys a protocol's opaque
//          connection block contains and the BundleTier each one gets.
// Reference: docs/decisions/0020-diagnostic-bundle-redaction-spec.md
//            (Accepted, as amended by Amendment 1) — A1.2 mechanism 2.
//            docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md
//            (§3, Q-B4 — interface lives in Core).
//
// LOCKED (M-B plan v2 Q-B4): this interface lives in Core because it is
// adapter *metadata*, not redaction logic. It carries only BundleTier (already
// in Core) plus the protocol's own connection-key names — adapter-local
// knowledge, never cross-protocol knowledge (CLAUDE.md §9 #1, #11). Management
// owns composition / classification / execution; an adapter owns "what keys
// exist and what tier they are." An adapter module implements this WITHOUT
// referencing Management; the implementation must not reference any Management
// type (enforced by the M-B drift guard).
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Redaction metadata for a single southbound/northbound protocol's opaque
/// connection block. Declared in the protocol's own adapter module and
/// discovered by the Management-side redaction registry, which composes all
/// implementations with the shared cross-protocol baseline.
/// </summary>
/// <remarks>
/// Per ADR-0020 Amendment 1 (A1.2), an opaque connection block whose protocol
/// has an implementation of this interface is "World 2a": its
/// <see cref="KnownKeys"/> classify known keys fail-closed; keys not present
/// fall through to fail-open baseline handling ("World 2b").
/// </remarks>
public interface IBundleRedactionRules
{
    /// <summary>
    /// The protocol name this rule set applies to. MUST match the value an
    /// adapter declares as its protocol (e.g. <c>"mqtt"</c>,
    /// <c>"opcua-client"</c>, <c>"focas2"</c>) and the
    /// <c>protocolName</c> persisted in the source/sink instance config.
    /// </summary>
    string ProtocolName { get; }

    /// <summary>
    /// Exhaustive map of every connection-block key the protocol understands
    /// to the <see cref="BundleTier"/> its value receives. "Exhaustive" is
    /// enforced by the M-B drift guard: the key set must equal the protocol's
    /// declared connection-key constants, so a key the factory can parse always
    /// has a tier. Benign keys (host, port, endpoint) map to
    /// <see cref="BundleTier.Include"/>.
    /// </summary>
    IReadOnlyDictionary<string, BundleTier> KnownKeys { get; }

    /// <summary>
    /// Optional fail-open overrides for keys that are NOT part of
    /// <see cref="KnownKeys"/> (operator-authored extras the factory never
    /// reads). Empty for most protocols — add an entry only when a real
    /// overflow key needs a non-<see cref="BundleTier.Include"/> tier beyond
    /// the shared baseline. Treated as exceptional, not expected.
    /// </summary>
    IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; }
}
