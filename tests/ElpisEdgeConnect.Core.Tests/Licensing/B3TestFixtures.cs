// ============================================================================
// File: Licensing/B3TestFixtures.cs
// Purpose: Build and sign in-memory license payloads for B3 tests using
//          Core's CanonicalJson + LicenseSignatureValidator. Eliminates the
//          risk of test fixtures drifting from production canonicalization.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ElpisEdgeConnect.Core.Licensing;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

internal static class B3TestFixtures
{
    public static string DefaultLicenseId => "LIC-TEST-0001";
    public static string DefaultCustomer => "Test Customer";
    public static string DefaultGatewayId => "GW-TEST-001";

    /// <summary>
    /// Build a license payload as a sorted dictionary, sign it with the
    /// dev private key, and return the resulting indented JSON string.
    /// </summary>
    public static string BuildSignedLicense(
        DateTime expires,
        DateTime? issued = null,
        LicenseEdition edition = LicenseEdition.Professional,
        IReadOnlyDictionary<string, (bool Enabled, int? MaxInstances)>? modules = null,
        int maxSources = 50,
        int maxSinks = 5,
        int maxRoutes = 100,
        IReadOnlyDictionary<string, bool>? features = null,
        string? privateKeyPemOverride = null,
        string licenseId = "LIC-TEST-0001",
        string customer = "Test Customer",
        string gatewayId = "GW-TEST-001",
        string? editionStringOverride = null)
    {
        modules ??= new Dictionary<string, (bool, int?)>(StringComparer.Ordinal)
        {
            ["source.focas2"] = (true, 20),
            ["source.modbus"] = (true, 10),
            ["sink.mqtt"] = (true, null),
            ["sink.http"] = (true, null),
        };

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["customer"] = customer,
            // The License Generator writes the edition as its DISPLAY string
            // (which may contain spaces, e.g. "CNC Pro"). editionStringOverride
            // lets a test reproduce that raw wire value; otherwise use the
            // space-free enum name.
            ["edition"] = editionStringOverride ?? edition.ToString(),
            ["expiresAt"] = expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["gatewayId"] = gatewayId,
            // Default issuedAt to 30 days before expiresAt. Using DateTime.UtcNow
            // here would tie the fixture to wall-clock time — a test that builds
            // a license with expiresAt = 2026-04-07 and issuedAt = "yesterday"
            // breaks the moment the dev box's clock passes 2026-04-07 + 1 day,
            // because LicenseManager enforces expiresAt >= issuedAt. Anchoring
            // issuedAt to expiresAt keeps the fixture deterministic.
            ["issuedAt"] = (issued ?? expires.AddDays(-30)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["licenseId"] = licenseId,
            ["limits"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["maxRoutes"] = maxRoutes,
                ["maxSinkInstances"] = maxSinks,
                ["maxSourceInstances"] = maxSources,
            },
        };

        var modulesDict = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, val) in modules)
        {
            var entry = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["enabled"] = val.Enabled,
            };
            if (val.MaxInstances is { } m)
            {
                entry["maxInstances"] = m;
            }
            modulesDict[key] = entry;
        }
        payload["modules"] = modulesDict;

        if (features is not null)
        {
            var featuresDict = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in features)
            {
                featuresDict[k] = v;
            }
            payload["features"] = featuresDict;
        }

        var unsignedJson = JsonSerializer.Serialize(payload);
        var canonical = CanonicalJson.Canonicalize(unsignedJson);
        var signature = LicenseSignatureValidator.SignWithPrivateKey(
            canonical,
            privateKeyPemOverride ?? TestRsaKeys.PrivatePem);

        // Re-serialize WITH the signature added.
        var withSig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(unsignedJson)!;
        var final = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in withSig)
        {
            final[k] = JsonSerializer.Deserialize<object?>(v.GetRawText());
        }
        final["signature"] = signature;

        return JsonSerializer.Serialize(final, new JsonSerializerOptions { WriteIndented = true });
    }

    public static Stream ToStream(string json) =>
        new MemoryStream(Encoding.UTF8.GetBytes(json));
}
