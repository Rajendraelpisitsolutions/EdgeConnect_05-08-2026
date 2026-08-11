// ============================================================================
// File: Licensing/CanonicalJson.cs
// Purpose: Deterministic JSON canonicalization for license signing and
//          verification. The signer (LicenseGen) and the verifier
//          (LicenseSignatureValidator) MUST agree on this byte representation
//          or every license breaks. To prevent drift, both sides reference
//          this single implementation.
// Reference: docs/licensing/license-file-format.md
//
// Canonical form rules:
//   1. Object keys are sorted lexicographically (ordinal, case-sensitive)
//      at every depth.
//   2. The "signature" property at the top level is removed before signing
//      / verifying. Nested "signature" keys are NOT special.
//   3. No insignificant whitespace.
//   4. Strings are emitted exactly as System.Text.Json's writer does
//      (UTF-8, JSON-escaped).
//   5. Numbers are emitted exactly as they appear in the source JSON
//      (we do not normalize numeric form — license payloads only contain
//      integer day counts and similar, so this is safe).
//   6. Arrays preserve their source order.
//   7. Output is UTF-8 with no BOM.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Produces a deterministic UTF-8 byte representation of a JSON document
/// suitable for cryptographic signing.
/// </summary>
public static class CanonicalJson
{
    /// <summary>The top-level property excluded from the canonical form.</summary>
    public const string SignaturePropertyName = "signature";

    /// <summary>
    /// Build the canonical bytes from a raw JSON document. The
    /// <c>signature</c> property at the top level is stripped.
    /// </summary>
    public static byte[] Canonicalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var doc = JsonDocument.Parse(json);
        return CanonicalizeRoot(doc.RootElement);
    }

    /// <summary>
    /// Build the canonical bytes from an already-parsed JSON element. The
    /// <c>signature</c> property at the top level is stripped.
    /// </summary>
    public static byte[] CanonicalizeRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("License root must be a JSON object.");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            WriteObject(writer, root, isRoot: true);
        }
        return stream.ToArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(writer, value, isRoot: false);
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                // Use the raw text so we don't introduce numeric drift between signer and verifier.
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException($"Unsupported JSON value kind: {value.ValueKind}");
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonElement obj, bool isRoot)
    {
        writer.WriteStartObject();
        // Sort properties lexicographically for determinism. At the root,
        // skip the "signature" property.
        var props = obj.EnumerateObject()
            .Where(p => !(isRoot && string.Equals(p.Name, SignaturePropertyName, StringComparison.Ordinal)))
            .OrderBy(p => p.Name, StringComparer.Ordinal);
        foreach (var p in props)
        {
            writer.WritePropertyName(p.Name);
            WriteValue(writer, p.Value);
        }
        writer.WriteEndObject();
    }
}
