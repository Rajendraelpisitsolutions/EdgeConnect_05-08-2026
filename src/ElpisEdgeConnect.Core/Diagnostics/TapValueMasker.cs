// ============================================================================
// File: Diagnostics/TapValueMasker.cs
// Purpose: Capture-time value masking for the Live Data Tap (ADR-0018 Rule 6 /
//          ADR-0018A). Applied INSIDE the capture path so cleartext sensitive
//          values never enter a diagnostic ring buffer or a snapshot export —
//          masking at capture, not at render (ADR-0017 Rule 7).
// Reference: ADR-0018A, ADR-0018 Rule 6, ADR-0017 Rule 7.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Masks the value of a sensitive-tagged <see cref="CanonicalDataPoint"/> for
/// tap capture. Only the value is replaced (<see cref="MaskMarker"/>); the
/// tag, type, quality, timestamps, and metadata are preserved so the operator
/// still sees that the point flowed and when. Non-sensitive points pass through
/// unchanged (same instance — no allocation).
/// </summary>
public sealed class TapValueMasker
{
    /// <summary>The placeholder substituted for a masked value.</summary>
    public const string MaskMarker = "***";

    private readonly Func<SensitiveTagPolicy> _policy;

    /// <summary>Create a masker over a fixed policy (used by tests).</summary>
    public TapValueMasker(SensitiveTagPolicy policy)
        : this(() => policy ?? SensitiveTagPolicy.Empty)
    {
    }

    /// <summary>
    /// Create a masker over a LIVE policy. The provider is read per masked
    /// point, so a hot-reload of <c>gateway.sensitiveTags</c> takes effect
    /// immediately — a privacy control must never go stale. The read is a cheap
    /// volatile fetch and only happens while a tap is active.
    /// </summary>
    public TapValueMasker(Func<SensitiveTagPolicy> policyProvider)
    {
        _policy = policyProvider ?? (() => SensitiveTagPolicy.Empty);
    }

    /// <summary>True when the underlying policy currently has at least one rule.</summary>
    public bool HasRules => _policy().HasRules;

    /// <summary>
    /// Return <paramref name="point"/> with its value masked when its tag is
    /// sensitive; otherwise the same instance unchanged.
    /// </summary>
    public CanonicalDataPoint Mask(CanonicalDataPoint point)
    {
        if (point is null || !_policy().IsSensitive(point.TagName))
        {
            return point!;
        }
        // Value-only mask. ValueType is intentionally preserved per ADR-0018A —
        // the diagnostic render shows "***" regardless of declared type; the
        // tap capture is never re-serialised to the durable buffer, so a
        // string marker against a numeric ValueType is render-only and safe.
        return point with { Value = MaskMarker };
    }
}
