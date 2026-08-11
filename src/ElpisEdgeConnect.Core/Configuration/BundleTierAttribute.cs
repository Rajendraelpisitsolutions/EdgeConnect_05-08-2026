// ============================================================================
// File: Configuration/BundleTierAttribute.cs
// Purpose: Marks a configuration field with its redaction tier so the
//          redaction engine can classify it without value heuristics.
// Reference: docs/decisions/0020-diagnostic-bundle-redaction-spec.md
//            (Accepted, as amended by Amendment 1) — Rule 1 / A1.2 mechanism 1.
//
// LOCKED (ADR-0020): this attribute is the authoring surface for "mechanism 1"
// (typed-field classification). A typed configuration field with no
// [BundleTier] resolves to STRIP (fail-closed) when classified through the
// typed-field path; that fail-closed default is enforced by the redaction
// drift guard (ADR-0020 R-2), not by silent runtime behaviour. The attribute
// is protocol-agnostic and lives in Core; protocol-specific *placement* of the
// attribute happens in each adapter module (which references Core).
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Declares the <see cref="BundleTier"/> of a configuration field for the
/// redaction engine. Apply to a property on a configuration record to state
/// whether its value is included verbatim, masked, or stripped when the
/// configuration is emitted into a diagnostic bundle or a backup.
/// </summary>
/// <remarks>
/// Per ADR-0020 Amendment 1, a typed field without this attribute is treated
/// as <see cref="BundleTier.Strip"/> (fail-closed) by the typed-field
/// classifier. Place the attribute explicitly on every typed configuration
/// field so the redaction tier is a reviewed decision, not a default.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class BundleTierAttribute : Attribute
{
    /// <summary>
    /// Initialises the attribute with the redaction tier for the decorated
    /// field.
    /// </summary>
    /// <param name="tier">The redaction tier applied to the field's value.</param>
    public BundleTierAttribute(BundleTier tier) => Tier = tier;

    /// <summary>The redaction tier applied to the decorated field's value.</summary>
    public BundleTier Tier { get; }
}
