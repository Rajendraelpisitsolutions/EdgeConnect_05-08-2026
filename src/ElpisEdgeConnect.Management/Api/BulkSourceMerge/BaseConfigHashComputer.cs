// ============================================================================
// File: Api/BulkSourceMerge/BaseConfigHashComputer.cs
// Purpose: Compute the v3.1 §6 base config hash — SHA-256 over the
//          canonical-JSON byte representation of a GatewayConfiguration.
//
//          PreviewAsync returns this hash; SubmitAsync receives it back and
//          rejects if the current applied config has drifted (someone else
//          applied a draft mid-flight). This prevents the wizard from
//          shipping a stale merge silently.
//
//          Canonicalization reuses Core.Licensing.CanonicalJson — the same
//          deterministic form used for offline license signing — so the
//          hash is stable across hosts and across re-serializations.
//
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md §6
//            src/ElpisEdgeConnect.Core/Licensing/CanonicalJson.cs
// ============================================================================

using System;
using System.Security.Cryptography;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Licensing;

namespace ElpisEdgeConnect.Management.Api.BulkSourceMerge;

/// <summary>
/// Compute a deterministic SHA-256 hash over a <see cref="GatewayConfiguration"/>
/// for the v3.1 §6 stale-preview guard.
/// </summary>
public static class BaseConfigHashComputer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Compute the hex-encoded SHA-256 hash over the canonical JSON form of
    /// <paramref name="config"/>. Returns lowercase hex (64 chars).
    /// </summary>
    public static string Compute(GatewayConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var canonical = CanonicalJson.Canonicalize(json);
        var hash = SHA256.HashData(canonical);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
