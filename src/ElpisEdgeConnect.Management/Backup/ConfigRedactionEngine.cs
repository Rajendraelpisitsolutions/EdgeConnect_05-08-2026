// ============================================================================
// File: Backup/ConfigRedactionEngine.cs
// Purpose: Tier-aware JSON redaction engine shared by the configuration backup
//          and (later) the diagnostic bundle. Walks a JSON document and applies
//          a BundleTier to each property: INCLUDE (verbatim), MASK (value ->
//          "***" plus a sibling byte-length annotation, key retained), or STRIP
//          (key omitted entirely). Stateless and side-effect-free; safe as a
//          singleton.
//
//          Renamed from SecretRedactor (M-A): the engine now serves both
//          backup and bundle, so the name is neutral. No alias is kept
//          (pre-launch, no external contract).
//
//          M-A classification: the shared baseline name->tier map
//          (BackupSecretPatterns). Mechanism 1 (typed attributes) and
//          mechanism 2 (per-protocol derived maps) compose in later milestones;
//          the engine takes its classification through a single resolver so
//          those compose without changing the walker.
//
//          DETERMINISM INVARIANT (LOCKED — plan v2 §1c): for identical input
//          JSON and an identical rule set, this engine MUST produce byte-for-
//          byte identical redacted output and identical provenance. No
//          timestamps, no random IDs/GUIDs, no culture-dependent formatting,
//          no non-deterministic iteration order in the output path. Provenance
//          paths are emitted in stable document-order traversal.
//
//          Path notation matches industry convention:
//             sources[0].connection.password
//             sinks[1].publishing.extras.apiKey
//          (dot-separated for object access, bracketed for array index).
// Reference: docs/decisions/0020-diagnostic-bundle-redaction-spec.md
//            (Accepted, as amended by Amendment 1) — A1.1, A1.2, §1a, §1c.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Backup;

/// <summary>The category of a <see cref="RedactionWarning"/>.</summary>
public enum RedactionWarningKind
{
    /// <summary>An opaque block's protocol had no registered rules; baseline applied (Q-B5).</summary>
    MissingProtocolRules,

    /// <summary>A surviving INCLUDE'd value looks secret-shaped (R-1 detector). Drives the bundle acknowledgement gate.</summary>
    SecretShape,
}

/// <summary>
/// A non-blocking provenance note emitted during redaction — e.g. an opaque
/// connection block whose protocol has no registered redaction rules, so only
/// the fail-open baseline was applied (ADR-0020 M-B plan v2, Q-B5). Surfacing
/// it prevents a support engineer assuming protocol rules ran when they didn't.
/// </summary>
public sealed record RedactionWarning
{
    /// <summary>JSONPath-ish path the warning applies to.</summary>
    public required string Path { get; init; }

    /// <summary>Human-readable description of what was (or wasn't) applied.</summary>
    public required string Message { get; init; }

    /// <summary>The warning category. Defaults to <see cref="RedactionWarningKind.MissingProtocolRules"/>.</summary>
    public RedactionWarningKind Kind { get; init; } = RedactionWarningKind.MissingProtocolRules;
}

/// <summary>
/// Result of a redaction pass over a JSON document.
/// </summary>
public sealed record ConfigRedactionResult
{
    /// <summary>The redacted JSON, pretty-printed.</summary>
    public required string RedactedJson { get; init; }

    /// <summary>
    /// JSONPath-ish paths of every property whose value was masked or stripped
    /// (the union of <see cref="MaskedPaths"/> and <see cref="StrippedPaths"/>).
    /// Empty list when nothing matched. Order matches the original document tree
    /// traversal (depth-first, document order).
    /// </summary>
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>Paths whose value was MASK-tiered (key retained, value blanked).</summary>
    public IReadOnlyList<string> MaskedPaths { get; init; } = System.Array.Empty<string>();

    /// <summary>Paths whose property was STRIP-tiered (key omitted entirely).</summary>
    public IReadOnlyList<string> StrippedPaths { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Non-blocking provenance warnings raised during the pass (e.g. missing
    /// protocol rules). Empty when none. The name-only <see cref="ConfigRedactionEngine.Redact(string)"/>
    /// path never raises warnings; the schema-aware overload may.
    /// </summary>
    public IReadOnlyList<RedactionWarning> Warnings { get; init; } = System.Array.Empty<RedactionWarning>();
}

/// <summary>
/// Tier-aware JSON redaction engine. Walks any JSON tree and applies a
/// <see cref="BundleTier"/> to each property — INCLUDE (verbatim),
/// MASK (value replaced with <see cref="BackupSecretPatterns.MaskMarker"/> plus
/// a sibling byte-length annotation), or STRIP (property omitted) — recording
/// each masked/stripped property's path. Deterministic (see the file header's
/// locked invariant).
/// </summary>
public sealed class ConfigRedactionEngine
{
    /// <summary>
    /// Version of the redaction engine that produced an artifact. Stamped into
    /// the backup manifest (and, later, the diagnostic bundle's
    /// <c>bundle-info.json</c>) per ADR-0020 R-3, so a support engineer can
    /// tell which engine revision did the redaction independently of the
    /// artifact's own schema version. Bump on any change to tier outcomes.
    /// </summary>
    public const int EngineVersion = 1;

    private static readonly JsonNodeOptions ParseOpts = new() { PropertyNameCaseInsensitive = false };

    // UnsafeRelaxedJsonEscaping leaves '<', '>', '&' and similar printable
    // characters as literals so operators don't have to mentally decode
    // escape sequences when inspecting an exported artifact. WriteIndented is
    // deterministic; no setting here introduces non-determinism.
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Accumulates redacted paths split by tier (and a combined document-order
    /// view) as the walkers run. Threaded through the walk so the result can
    /// report MASK vs STRIP counts for the bundle's redaction summary.
    /// </summary>
    private sealed class RedactionPathSet
    {
        public List<string> Masked { get; } = new();
        public List<string> Stripped { get; } = new();

        /// <summary>Masked ∪ stripped in document order — the historical <c>Paths</c> list.</summary>
        public List<string> All { get; } = new();

        public void AddMasked(string path)
        {
            Masked.Add(path);
            All.Add(path);
        }

        public void AddStripped(string path)
        {
            Stripped.Add(path);
            All.Add(path);
        }
    }

    /// <summary>
    /// Redact <paramref name="json"/>: parse, walk, apply the tier of every
    /// property (mask or strip secrets, include everything else), re-serialize.
    /// Returns the redacted JSON plus the paths that were masked or stripped.
    /// </summary>
    /// <exception cref="JsonException">Thrown when <paramref name="json"/> is not valid JSON.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
    public ConfigRedactionResult Redact(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = JsonNode.Parse(json, ParseOpts)
            ?? throw new JsonException("Document parsed to null root.");

        var paths = new RedactionPathSet();
        Walk(root, parentPath: "", paths);

        return new ConfigRedactionResult
        {
            RedactedJson = root.ToJsonString(WriteOpts),
            Paths = paths.All,
            MaskedPaths = paths.Masked,
            StrippedPaths = paths.Stripped,
        };
    }

    /// <summary>
    /// Schema-aware redaction (ADR-0020 M-B). Walks <paramref name="json"/> in
    /// lockstep with <paramref name="schemaRoot"/>: typed (World 1) nodes are
    /// descended structurally; opaque connection blocks (World 2) are
    /// name-classified via <paramref name="registry"/> (protocol rules then
    /// baseline). Emits a provenance warning when an opaque block names a
    /// protocol the registry doesn't know.
    /// </summary>
    /// <remarks>
    /// World 1 (typed) classification applies each property's
    /// <see cref="SchemaProperty.Tier"/> (M1): Include descends, Mask/Strip
    /// redact the value, and an unattributed typed property resolves to STRIP
    /// (fail-closed). World 2 (opaque) classification is name-based via the
    /// registry. BackupBuilder remains on the name-only path through M-B
    /// sub-milestone B2; this overload is exercised by its own tests until a
    /// later step repoints the live consumer onto it.
    /// </remarks>
    /// <exception cref="JsonException">Thrown when <paramref name="json"/> is not valid JSON.</exception>
    public ConfigRedactionResult Redact(string json, SchemaNode schemaRoot, BundleRedactionRulesRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(schemaRoot);
        ArgumentNullException.ThrowIfNull(registry);

        var root = JsonNode.Parse(json, ParseOpts)
            ?? throw new JsonException("Document parsed to null root.");

        var paths = new RedactionPathSet();
        var warnings = new List<RedactionWarning>();
        WalkSchema(root, schemaRoot, parentPath: "", registry, paths, warnings);

        // R-1 secret-shape detector (M-C Phase 1): runs over the REDACTED tree,
        // so it only inspects values that survived as INCLUDE. Warn-only — it
        // never strips. Its warnings flow through the same channel as the
        // missing-rules warnings (into the backup/bundle manifest).
        warnings.AddRange(SecretShapeDetector.Scan(root));

        return new ConfigRedactionResult
        {
            RedactedJson = root.ToJsonString(WriteOpts),
            Paths = paths.All,
            MaskedPaths = paths.Masked,
            StrippedPaths = paths.Stripped,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Lockstep walk of a JSON node against a schema node. Typed objects descend
    /// structurally; opaque boundaries switch to <see cref="WalkOpaque"/>;
    /// <c>[JsonExtensionData]</c> overflow keys are baseline-classified.
    /// </summary>
    private static void WalkSchema(
        JsonNode json,
        SchemaNode schema,
        string parentPath,
        BundleRedactionRulesRegistry registry,
        RedactionPathSet paths,
        List<RedactionWarning> warnings)
    {
        switch (schema)
        {
            case TypedObjectSchemaNode typed when json is JsonObject obj:
                var protocol = ReadProtocol(obj);

                var names = new List<string>(obj.Count);
                foreach (var kvp in obj)
                {
                    names.Add(kvp.Key);
                }

                foreach (var name in names)
                {
                    var childPath = parentPath.Length == 0 ? name : $"{parentPath}.{name}";

                    if (typed.Properties.TryGetValue(name, out var prop))
                    {
                        if (prop.Child is OpaqueBoundarySchemaNode)
                        {
                            if (!registry.HasRules(protocol))
                            {
                                warnings.Add(new RedactionWarning
                                {
                                    Path = childPath,
                                    Message = protocol is null
                                        ? "Opaque connection block has no protocolName; baseline classification applied."
                                        : $"Protocol rules unavailable for '{protocol}'; baseline classification applied.",
                                });
                            }
                            if (obj[name] is JsonNode opaque)
                            {
                                WalkOpaque(opaque, childPath, protocol, registry, paths);
                            }
                        }
                        else
                        {
                            // World 1 — apply the property's M1 tier; an
                            // unattributed typed property is STRIP (fail-closed).
                            switch (prop.Tier ?? BundleTier.Strip)
                            {
                                case BundleTier.Strip:
                                    obj.Remove(name);
                                    paths.AddStripped(childPath);
                                    break;
                                case BundleTier.Mask:
                                    MaskProperty(obj, name);
                                    paths.AddMasked(childPath);
                                    break;
                                default: // Include — descend structurally.
                                    if (obj[name] is JsonNode child)
                                    {
                                        WalkSchema(child, prop.Child, childPath, registry, paths, warnings);
                                    }
                                    break;
                            }
                        }
                    }
                    else if (typed.HasExtensionData)
                    {
                        // World 2b overflow key — baseline classify by name; recurse
                        // into Include subtrees so nested secrets are still caught.
                        if (!ApplyOpaqueTier(obj, name, childPath, protocol: null, registry, paths)
                            && obj[name] is JsonNode overflow)
                        {
                            WalkOpaque(overflow, childPath, protocol: null, registry, paths);
                        }
                    }
                    // else: unknown key on a closed typed object — B1 leaves it
                    // (Include). B2's fail-closed default handles this case.
                }
                break;

            case ArraySchemaNode array when json is JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var childPath = $"{parentPath}[{i}]";
                    if (arr[i] is JsonNode element)
                    {
                        WalkSchema(element, array.Element, childPath, registry, paths, warnings);
                    }
                }
                break;

            // Leaf / opaque-at-root / shape mismatch: nothing to descend.
            default:
                break;
        }
    }

    /// <summary>
    /// Name-based walk of an opaque connection subtree. Each key's tier is
    /// resolved via the registry (protocol rules then baseline); Mask/Strip are
    /// applied, Include recurses into nested objects/arrays so nested secrets
    /// (e.g. <c>credentials.password</c>) are still caught.
    /// </summary>
    private static void WalkOpaque(
        JsonNode node,
        string parentPath,
        string? protocol,
        BundleRedactionRulesRegistry registry,
        RedactionPathSet paths)
    {
        switch (node)
        {
            case JsonObject obj:
                var names = new List<string>(obj.Count);
                foreach (var kvp in obj)
                {
                    names.Add(kvp.Key);
                }

                foreach (var name in names)
                {
                    var childPath = $"{parentPath}.{name}";
                    if (ApplyOpaqueTier(obj, name, childPath, protocol, registry, paths))
                    {
                        continue; // masked or stripped — do not recurse into it
                    }
                    if (obj[name] is JsonNode child)
                    {
                        WalkOpaque(child, childPath, protocol, registry, paths);
                    }
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonNode element)
                    {
                        WalkOpaque(element, $"{parentPath}[{i}]", protocol, registry, paths);
                    }
                }
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Resolve and apply the tier for one key on an opaque object. Returns
    /// <see langword="true"/> when the key was masked or stripped (caller must
    /// not recurse into it), <see langword="false"/> when it is Include.
    /// </summary>
    private static bool ApplyOpaqueTier(
        JsonObject obj,
        string name,
        string path,
        string? protocol,
        BundleRedactionRulesRegistry registry,
        RedactionPathSet paths)
    {
        switch (registry.ResolveOpaqueKeyTier(protocol, name))
        {
            case BundleTier.Strip:
                obj.Remove(name);
                paths.AddStripped(path);
                return true;
            case BundleTier.Mask:
                MaskProperty(obj, name);
                paths.AddMasked(path);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Read the <c>protocolName</c> sibling (case-insensitive) from an object, or null.</summary>
    private static string? ReadProtocol(JsonObject obj)
    {
        foreach (var kvp in obj)
        {
            if (string.Equals(kvp.Key, "protocolName", StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            }
        }
        return null;
    }

    /// <summary>
    /// Recursive walk. Mutates the tree in-place; appends the path of every
    /// masked or stripped property in stable document order. Arrays emit
    /// element indices in bracketed form.
    /// </summary>
    private static void Walk(JsonNode node, string parentPath, RedactionPathSet paths)
    {
        switch (node)
        {
            case JsonObject obj:
                // Capture property names first so we can mutate (replace values,
                // strip keys, append sibling metadata) without enumerating the
                // live object. Iterating this stable snapshot keeps traversal —
                // and therefore output and provenance — deterministic.
                var propertyNames = new List<string>(obj.Count);
                foreach (var kvp in obj)
                {
                    propertyNames.Add(kvp.Key);
                }

                foreach (var name in propertyNames)
                {
                    var childPath = parentPath.Length == 0 ? name : $"{parentPath}.{name}";

                    if (BackupSecretPatterns.TryGetTier(name, out var tier))
                    {
                        switch (tier)
                        {
                            case BundleTier.Strip:
                                // Omit the property entirely — key must not appear.
                                obj.Remove(name);
                                paths.AddStripped(childPath);
                                continue;

                            case BundleTier.Mask:
                                MaskProperty(obj, name);
                                paths.AddMasked(childPath);
                                continue;

                            case BundleTier.Include:
                            default:
                                break; // fall through to recurse
                        }
                    }

                    // Not a secret (or Include) — recurse into nested
                    // object/array; leave primitives alone.
                    if (obj[name] is JsonNode child)
                    {
                        Walk(child, childPath, paths);
                    }
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var childPath = $"{parentPath}[{i}]";
                    if (arr[i] is JsonNode element)
                    {
                        Walk(element, childPath, paths);
                    }
                }
                break;

            default:
                // JsonValue (primitives) — leaf, nothing to walk into.
                break;
        }
    }

    /// <summary>
    /// Mask the value of <paramref name="name"/> on <paramref name="obj"/>:
    /// replace it with the mask marker (a string, regardless of the original
    /// value's type) and append a sibling <c>"&lt;name&gt;__redacted"</c>
    /// object carrying <c>masked</c> + <c>originalByteLength</c>. The key is
    /// retained so a restore round-trip can detect the redaction and prompt.
    /// </summary>
    private static void MaskProperty(JsonObject obj, string name)
    {
        var originalByteLength = MeasureByteLength(obj[name]);

        obj[name] = JsonValue.Create(BackupSecretPatterns.MaskMarker);

        var metadataKey = name + BackupSecretPatterns.RedactedMetadataSuffix;
        obj[metadataKey] = new JsonObject
        {
            ["masked"] = JsonValue.Create(true),
            ["originalByteLength"] = JsonValue.Create(originalByteLength),
        };
    }

    /// <summary>
    /// UTF-8 byte length of the original value: the string content for a JSON
    /// string, or the compact JSON serialization for any non-string value
    /// (number, object, array, bool). Null values measure as 0.
    /// </summary>
    private static int MeasureByteLength(JsonNode? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value is JsonValue jv && jv.TryGetValue<string>(out var s))
        {
            return Encoding.UTF8.GetByteCount(s);
        }

        return Encoding.UTF8.GetByteCount(value.ToJsonString());
    }
}
