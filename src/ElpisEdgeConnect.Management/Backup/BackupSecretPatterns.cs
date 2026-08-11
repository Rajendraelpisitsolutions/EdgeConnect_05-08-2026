// ============================================================================
// File: Backup/BackupSecretPatterns.cs
// Purpose: The shared cross-protocol baseline for redaction. Maps JSON
//          property NAMES to a BundleTier (Mask / Strip). This is the
//          "mechanism 3 baseline" of ADR-0020 Amendment 1 A1.2 — the
//          fail-open name allowlist that classifies secrets living in opaque
//          JSON blocks with no typed counterpart.
//
//          Design rules:
//             * Property-NAME based (not value-heuristic). Names are
//               operator-controlled; values aren't.
//             * Case-insensitive matching.
//             * Conservative: false-positive redaction is harmless
//               (operator re-enters value on restore), false-negative
//               leakage is a CVE.
//             * Tier per name (ADR-0020 §1a universal tiers):
//                 - re-enterable auth secrets -> MASK (key retained so a
//                   restore round-trip can prompt the operator)
//                 - never-ship key material   -> STRIP (key omitted entirely)
// Reference: docs/decisions/0020-diagnostic-bundle-redaction-spec.md
//            (Accepted, as amended by Amendment 1) — A1.1, A1.2 mechanism 3, §1a.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Backup;

/// <summary>
/// Shared cross-protocol redaction baseline: a case-insensitive map of JSON
/// property names to the <see cref="BundleTier"/> applied to their values.
/// This is the fail-open name allowlist (ADR-0020 Amendment 1, A1.2 mechanism
/// 3) — names not present here default to <see cref="BundleTier.Include"/>.
/// </summary>
/// <remarks>
/// <para>Industrial gateway configs frequently embed credentials and key
/// material in opaque JSON connection blocks. This baseline covers the common
/// names across MQTT, OPC UA, HTTPS, Modbus over TLS, S7 with credentials, and
/// cloud-relay configurations. Per-protocol rules declared beside each adapter
/// (M-B) extend this baseline; they do not replace it.</para>
/// <para>Tiering follows ADR-0020 §1a: re-enterable auth secrets are
/// <see cref="BundleTier.Mask"/> (the key stays visible so a restore can prompt
/// for re-entry); cryptographic key material that must never ship in any
/// artifact is <see cref="BundleTier.Strip"/>.</para>
/// </remarks>
public static class BackupSecretPatterns
{
    /// <summary>
    /// The marker placed in a JSON value that is masked. The value stays a
    /// string; a sibling <c>"&lt;name&gt;__redacted"</c> object carries the
    /// metadata. Deliberately not a valid string for any typed config so an
    /// importer applying a masked file without secret-replay fails validation
    /// rather than silently committing the literal marker.
    /// </summary>
    public const string MaskMarker = "***";

    /// <summary>
    /// Suffix appended to a masked field's key to name its sibling metadata
    /// object (e.g. <c>password</c> -&gt; <c>password__redacted</c>).
    /// </summary>
    public const string RedactedMetadataSuffix = "__redacted";

    /// <summary>
    /// Case-insensitive map of property name to the redaction tier applied to
    /// its value. Only <see cref="BundleTier.Mask"/> and
    /// <see cref="BundleTier.Strip"/> appear here; an absent name is
    /// <see cref="BundleTier.Include"/> (fail-open).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, BundleTier> Tiers =
        new Dictionary<string, BundleTier>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- MASK: re-enterable authentication secrets ----
            // Key retained so a restore round-trip can detect + prompt.
            ["password"] = BundleTier.Mask,
            ["passphrase"] = BundleTier.Mask,
            ["userToken"] = BundleTier.Mask,
            ["bearerToken"] = BundleTier.Mask,
            ["accessToken"] = BundleTier.Mask,
            ["refreshToken"] = BundleTier.Mask,
            ["token"] = BundleTier.Mask,          // generic — false-positive friendly
            ["secret"] = BundleTier.Mask,         // generic — false-positive friendly
            ["clientSecret"] = BundleTier.Mask,
            ["apiKey"] = BundleTier.Mask,
            ["sasKey"] = BundleTier.Mask,
            ["sasToken"] = BundleTier.Mask,
            ["connectionString"] = BundleTier.Mask, // embeds shared keys; re-entered on restore

            // ---- STRIP: cryptographic key material that must never ship ----
            ["privateKey"] = BundleTier.Strip,
            ["tlsKey"] = BundleTier.Strip,
            ["x509"] = BundleTier.Strip,
            ["certificate"] = BundleTier.Strip,   // PEM-embedded certs in gateway.json
            ["p12"] = BundleTier.Strip,
            ["pfx"] = BundleTier.Strip,
        };

    /// <summary>
    /// Resolves the baseline tier for <paramref name="propertyName"/>. Returns
    /// <see langword="true"/> and the tier when the name is a known secret;
    /// <see langword="false"/> (an Include / fail-open name) otherwise.
    /// </summary>
    public static bool TryGetTier(string propertyName, out BundleTier tier)
    {
        if (propertyName is not null)
        {
            return Tiers.TryGetValue(propertyName, out tier);
        }

        tier = BundleTier.Include;
        return false;
    }
}
