// ============================================================================
// File: Backup/BundleRedactionRulesRegistry.cs
// Purpose: Composes all per-protocol IBundleRedactionRules (discovered via DI)
//          with the shared cross-protocol baseline, and resolves the BundleTier
//          for a key inside an opaque connection block. This is the Management-
//          side composition point of ADR-0020 Amendment 1 A1.2: World 2a
//          (protocol KnownKeys) is tried first, then World 2b (baseline).
//
//          "Management coordinates; protocols declare." The registry holds no
//          protocol knowledge of its own — it only composes what each adapter
//          declares plus the baseline.
// Reference: docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md §1a, §3.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Backup;

/// <summary>
/// Resolves redaction tiers for keys inside opaque connection blocks by
/// composing per-protocol <see cref="IBundleRedactionRules"/> (keyed by
/// protocol name) over the shared <see cref="BackupSecretPatterns"/> baseline.
/// </summary>
public sealed class BundleRedactionRulesRegistry
{
    private readonly Dictionary<string, IBundleRedactionRules> _byProtocol;

    /// <summary>
    /// Compose the registry from the DI-discovered set of protocol rules. In
    /// M-B sub-milestone B1 no protocol rules are registered yet, so every
    /// opaque key resolves via the baseline; B3 adds the first implementations
    /// and the same code path picks them up.
    /// </summary>
    public BundleRedactionRulesRegistry(IEnumerable<IBundleRedactionRules> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var map = new Dictionary<string, IBundleRedactionRules>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules)
        {
            // Last registration wins; the M-B drift guard asserts there are no
            // duplicate ProtocolName declarations across adapter modules.
            map[r.ProtocolName] = r;
        }
        _byProtocol = map;
    }

    /// <summary>
    /// True when a protocol rule set is registered for <paramref name="protocolName"/>.
    /// Used to emit the "protocol rules unavailable" provenance warning
    /// (ADR-0020 M-B plan v2, Q-B5) when an opaque block names a protocol the
    /// registry doesn't know.
    /// </summary>
    public bool HasRules(string? protocolName) =>
        protocolName is not null && _byProtocol.ContainsKey(protocolName);

    /// <summary>
    /// Resolve the tier for <paramref name="key"/> inside an opaque connection
    /// block belonging to <paramref name="protocolName"/>. Tries the protocol's
    /// <see cref="IBundleRedactionRules.KnownKeys"/> then
    /// <see cref="IBundleRedactionRules.ExtraNameOverrides"/> (World 2a), then
    /// the shared baseline (World 2b). Returns <see cref="BundleTier.Include"/>
    /// when nothing matches (fail-open).
    /// </summary>
    public BundleTier ResolveOpaqueKeyTier(string? protocolName, string key)
    {
        if (protocolName is not null && _byProtocol.TryGetValue(protocolName, out var rules))
        {
            if (rules.KnownKeys.TryGetValue(key, out var known))
            {
                return known;
            }
            if (rules.ExtraNameOverrides.TryGetValue(key, out var overridden))
            {
                return overridden;
            }
        }

        return BackupSecretPatterns.TryGetTier(key, out var baseline)
            ? baseline
            : BundleTier.Include;
    }
}
