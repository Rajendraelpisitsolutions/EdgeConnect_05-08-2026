// ============================================================================
// File: Configuration/BundleTier.cs
// Purpose: The redaction tier of a single configuration field when it is
//          emitted into a diagnostic bundle or a configuration backup.
// Reference: docs/decisions/0020-diagnostic-bundle-redaction-spec.md
//            (Accepted, as amended by Amendment 1) — Rule 1 tier semantics.
//
// LOCKED (ADR-0020): the tier vocabulary is INCLUDE / MASK / STRIP. EXCLUDE
// is deliberately NOT a value here — it is a file-level decision made by the
// bundling consumer, not a per-field classification. The enum is intentionally
// protocol-agnostic so it may live in Core without violating CLAUDE.md §9 #1:
// it carries a redaction *policy*, never protocol knowledge.
// ============================================================================

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// The redaction outcome applied to a single configuration field's value when
/// it is emitted into a diagnostic bundle or a configuration backup. A field's
/// tier is a property of the field, decided once, and applied identically by
/// every consumer (no per-consumer semantics — see ADR-0020 Amendment 1 §1a).
/// </summary>
public enum BundleTier
{
    /// <summary>
    /// Value is emitted verbatim, unmodified. Used for benign data a support
    /// engineer needs (endpoint, host, port, identifiers, status).
    /// </summary>
    Include = 0,

    /// <summary>
    /// Key is retained; value is replaced with the mask marker and a sibling
    /// byte-length annotation. Used for re-enterable secrets (passwords,
    /// tokens, auth material) — the key staying visible is what lets a restore
    /// round-trip detect the redaction and prompt the operator to re-enter it.
    /// </summary>
    Mask = 1,

    /// <summary>
    /// Property is omitted entirely — the key itself does not appear in the
    /// output. Reserved for material that must never ship in any artifact, not
    /// even its key name (private-key bytes, certificate PEM bodies, license
    /// content).
    /// </summary>
    Strip = 2,
}
