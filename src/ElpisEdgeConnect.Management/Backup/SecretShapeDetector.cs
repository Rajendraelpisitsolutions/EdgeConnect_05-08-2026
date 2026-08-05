// ============================================================================
// File: Backup/SecretShapeDetector.cs
// Purpose: The secret-shape detector (ADR-0020 R-1, M-C Phase 1). Runs over the
//          ALREADY-REDACTED JSON tree and warns when a surviving (INCLUDE'd)
//          string value LOOKS like a secret even though its key name was not
//          classified as one. This is the only runtime mitigation for the
//          fail-open World-2b path (A1.4 #1) and also catches a secret pasted
//          into a typed INCLUDE field (A1.4 #2).
//
//          IT WARNS, IT NEVER STRIPS. Value-heuristic auto-redaction was
//          rejected as a primary mechanism (BackupSecretPatterns header); the
//          detector is a deterministic advisory that runs before the human-gated
//          preview, narrowing what the operator must catch by eye.
//
//          PHASE 1 — DETERMINISTIC, near-zero false positives:
//            * PEM / PKCS key + certificate blocks  ("-----BEGIN ...")
//            * SSH keys                             ("ssh-rsa ", "ssh-ed25519 ", ...)
//            * JWTs                                 ("eyJ....x.y")
//          PHASE 2 — HEURISTIC (entropy / token-likelihood). Carries the tuning
//          burden, so it is conservative by construction: only long, high-entropy,
//          token-charset strings are flagged, with structural exclusions for the
//          common benign look-alikes (GUIDs, ULIDs, URLs, paths, versions) and a
//          digit+letter requirement to drop long identifiers / words. Still
//          warn-only — a false positive is a dismissible advisory, never data
//          loss. Thresholds are constants here; tune them if a real config proves
//          noisy.
//
//          Because it runs AFTER redaction, masked values are already "***" and
//          stripped keys are gone, so the detector only ever inspects values
//          that survived as INCLUDE — and never re-flags a redaction artifact.
//          Deterministic: document-order traversal, no timestamps / randomness.
// Reference: docs/decisions/0020-diagnostic-bundle-redaction-spec.md (A1.4 #1/#2, R-1)
//            docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md §C-5 / Q-3.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ElpisEdgeConnect.Management.Backup;

/// <summary>
/// Deterministic secret-shape detector (ADR-0020 R-1, Phase 1). Scans a redacted
/// JSON document and produces non-blocking <see cref="RedactionWarning"/>s for
/// string values that look like key material the redaction tiers did not catch.
/// </summary>
public static class SecretShapeDetector
{
    // JWT: starts with "eyJ" (base64url of {"...), three dot-separated base64url
    // segments (third may be empty for unsigned tokens). The eyJ anchor keeps
    // false positives near zero — ordinary dotted strings (versions, hostnames)
    // do not start with eyJ and have base64url segments.
    private static readonly Regex JwtPattern = new(
        @"^eyJ[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] SshKeyPrefixes =
    {
        "ssh-rsa ", "ssh-ed25519 ", "ssh-dss ", "ecdsa-sha2-",
    };

    // ---- Phase 2 (entropy heuristic) tuning constants ----

    /// <summary>Minimum length before a value is even considered a high-entropy token.</summary>
    private const int EntropyMinLength = 24;

    /// <summary>Shannon-entropy floor (bits/char). Random hex ≈ 4.0, random base64 ≈ 5.5+.</summary>
    private const double EntropyThresholdBits = 3.5;

    // Token charset: base64url + optional '=' padding. Deliberately excludes '/',
    // '+', ':', '.', whitespace etc. — that alone rules out URLs, paths, dotted
    // hostnames/versions, and human text.
    private static readonly Regex TokenCharset = new(
        @"^[A-Za-z0-9_-]+={0,2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GuidPattern = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ULID: 26 chars, Crockford base32 (excludes I, L, O, U).
    private static readonly Regex UlidPattern = new(
        @"^[0-9A-HJKMNP-TV-Z]{26}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Scan <paramref name="redactedRoot"/> (the post-redaction tree) and return
    /// a warning for every surviving string value whose shape matches a known
    /// key/secret format. Empty when nothing matches. Order follows document-order
    /// traversal (deterministic).
    /// </summary>
    public static IReadOnlyList<RedactionWarning> Scan(JsonNode? redactedRoot)
    {
        var warnings = new List<RedactionWarning>();
        if (redactedRoot is not null)
        {
            Walk(redactedRoot, parentPath: "", warnings);
        }
        return warnings;
    }

    private static void Walk(JsonNode node, string parentPath, List<RedactionWarning> warnings)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj)
                {
                    var childPath = parentPath.Length == 0 ? kvp.Key : $"{parentPath}.{kvp.Key}";
                    if (kvp.Value is JsonNode child)
                    {
                        Walk(child, childPath, warnings);
                    }
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonNode element)
                    {
                        Walk(element, $"{parentPath}[{i}]", warnings);
                    }
                }
                break;

            case JsonValue value when value.TryGetValue<string>(out var s) && s is not null:
                var kind = ClassifyShape(s);
                if (kind is not null)
                {
                    warnings.Add(new RedactionWarning
                    {
                        Kind = RedactionWarningKind.SecretShape,
                        Path = parentPath,
                        Message =
                            $"Value looks like {kind} but its key was not classified as a secret, " +
                            "so it was included verbatim. Verify before sharing this artifact.",
                    });
                }
                break;
        }
    }

    /// <summary>
    /// Returns a human-readable description of the secret shape a value matches,
    /// or <see langword="null"/> when it matches none. Deterministic detectors only.
    /// </summary>
    private static string? ClassifyShape(string value)
    {
        // PEM / PKCS armored blocks: private keys, certificates, OpenSSH keys, etc.
        if (value.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return "a PEM-armored key or certificate block";
        }

        // SSH public keys.
        foreach (var prefix in SshKeyPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return "an SSH key";
            }
        }

        // JWT (bearer / id / access token).
        if (JwtPattern.IsMatch(value))
        {
            return "a JSON Web Token (JWT)";
        }

        // Phase 2 heuristic — high-entropy opaque token (e.g. API key, bearer
        // secret) under a key name the tiers did not classify.
        if (LooksLikeHighEntropyToken(value))
        {
            return "a high-entropy token (possible API key or secret)";
        }

        return null;
    }

    /// <summary>
    /// Conservative high-entropy token heuristic (Phase 2). True only for a long,
    /// token-charset string with mixed letters+digits and high Shannon entropy,
    /// excluding GUIDs and ULIDs. The charset restriction already excludes URLs,
    /// paths, dotted hostnames/versions, and whitespace text.
    /// </summary>
    private static bool LooksLikeHighEntropyToken(string value)
    {
        if (value.Length < EntropyMinLength)
        {
            return false;
        }
        if (GuidPattern.IsMatch(value) || UlidPattern.IsMatch(value))
        {
            return false;
        }
        if (!TokenCharset.IsMatch(value))
        {
            return false;
        }

        // Require at least one letter AND one digit — drops long identifiers,
        // camelCase field names, and dictionary words (which carry no digit).
        var hasLetter = false;
        var hasDigit = false;
        foreach (var c in value)
        {
            if (char.IsLetter(c))
            {
                hasLetter = true;
            }
            else if (char.IsDigit(c))
            {
                hasDigit = true;
            }
        }
        if (!hasLetter || !hasDigit)
        {
            return false;
        }

        return ShannonEntropyBitsPerChar(value) >= EntropyThresholdBits;
    }

    /// <summary>Shannon entropy of <paramref name="value"/> in bits per character.</summary>
    private static double ShannonEntropyBitsPerChar(string value)
    {
        var counts = new Dictionary<char, int>();
        foreach (var c in value)
        {
            counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
        }

        double entropy = 0.0;
        double length = value.Length;
        foreach (var count in counts.Values)
        {
            var p = count / length;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }
}
