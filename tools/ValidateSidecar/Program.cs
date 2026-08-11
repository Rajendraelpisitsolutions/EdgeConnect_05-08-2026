// ============================================================================
// File: tools/ValidateSidecar/Program.cs
// Purpose: CLI validator for a bulk-provision sidecar (YAML or JSON) against
//          tools/bulk-provision/sidecar-schema.json. Pipeline per v3 §6:
//          YAML/JSON sidecar -> .NET object -> JSON string -> NJsonSchema.
//
//          Single-purpose CLI per the tools/ pattern. Intentionally diverges
//          from tools/ValidateConfig in two ways:
//            * Generic on the schema path (passed at the command line).
//            * Returns exit 2 on argument errors (vs ValidateConfig's quirky
//              exit 0 with usage text). See v3 §1.4.
//
// Reference: docs/sessions/2026-06-14-chip3-impl-session2.5-plan-v3-lock-final.md
//            §3 (Kind -> message mapping)
//            §4 (display-path projection)
//            §5 (CLI argument-error rule)
//            §6 (YAML normalization helper)
// ============================================================================

using System.Globalization;
using System.Text.Json;
using NJsonSchema;
using NJsonSchema.Validation;
using YamlDotNet.RepresentationModel;

namespace ElpisEdgeConnect.Tools.ValidateSidecar;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // ── 1. Argument parsing (v3 §5) ──────────────────────────────────
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            WriteUsage(Console.Error);
            return 0;
        }

        string? schemaPath = null;
        string? sidecarPath = null;
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--schema":
                    if (i + 1 >= args.Length) return ArgError($"missing value for {args[i]}");
                    if (schemaPath is not null) return ArgError($"duplicate argument: {args[i]}");
                    schemaPath = args[++i];
                    break;
                case "--sidecar":
                    if (i + 1 >= args.Length) return ArgError($"missing value for {args[i]}");
                    if (sidecarPath is not null) return ArgError($"duplicate argument: {args[i]}");
                    sidecarPath = args[++i];
                    break;
                case "--verbose":
                    if (verbose) return ArgError("duplicate argument: --verbose");
                    verbose = true;
                    break;
                default:
                    return ArgError($"unknown argument: {args[i]}");
            }
        }

        if (schemaPath is null) return ArgError("missing required argument: --schema");
        if (sidecarPath is null) return ArgError("missing required argument: --sidecar");

        // ── 2. File existence (exit 2) ───────────────────────────────────
        if (!File.Exists(schemaPath))
        {
            Console.Error.WriteLine($"ValidateSidecar: schema file not found: {schemaPath}");
            return 2;
        }
        if (!File.Exists(sidecarPath))
        {
            Console.Error.WriteLine($"ValidateSidecar: sidecar file not found: {sidecarPath}");
            return 2;
        }

        // ── 3. Parse sidecar by extension (v3 §6, exit 3 on parse failure) ─
        string sidecarJson;
        try
        {
            sidecarJson = await ParseSidecarToJsonAsync(sidecarPath).ConfigureAwait(false);
        }
        catch (SidecarParseException ex)
        {
            Console.Error.WriteLine($"ValidateSidecar: sidecar parse failed: {ex.Message}");
            return 3;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"ValidateSidecar: unexpected error reading sidecar: {ex.GetType().Name}: {ex.Message}");
            return 4;
        }

        // ── 4. Load schema + validate (exit 1 on errors) ─────────────────
        try
        {
            var schema = await JsonSchema.FromFileAsync(schemaPath).ConfigureAwait(false);
            var errors = schema.Validate(sidecarJson);

            if (errors.Count == 0)
            {
                Console.Out.WriteLine($"ValidateSidecar: {sidecarPath} is valid.");
                return 0;
            }

            Console.Error.WriteLine("Sidecar validation failed:");
            foreach (var error in errors)
            {
                var displayPath = ToDisplayPath(error.Path, error.Property);
                var message = WrapMessage(error.Kind);
                Console.Error.WriteLine($"  {displayPath}: {message}");
                if (verbose)
                {
                    Console.Error.WriteLine($"    [raw] {error.Kind}: {error.Path ?? string.Empty}");
                }
            }
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ValidateSidecar: unexpected error during schema validation: {ex.GetType().Name}: {ex.Message}");
            if (verbose)
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            return 4;
        }
    }

    // ── Argument-error helper (v3 §5) ────────────────────────────────────
    private static int ArgError(string message)
    {
        Console.Error.WriteLine($"ValidateSidecar: {message}");
        WriteUsage(Console.Error);
        return 2;
    }

    private static void WriteUsage(TextWriter writer) =>
        writer.WriteLine(
            "Usage: ValidateSidecar --schema <schema.json> --sidecar <sidecar.{yml,yaml,json}> [--verbose]\n" +
            "\n" +
            "Validates a bulk-provision sidecar (YAML or JSON) against the supplied JSON Schema.\n" +
            "\n" +
            "Exit codes:\n" +
            "  0  Sidecar is well-formed AND validates against the schema\n" +
            "  1  Schema-validation failure (printed to stderr)\n" +
            "  2  Argument problem OR file not found / unreadable\n" +
            "  3  Sidecar parse failure (malformed YAML / JSON)\n" +
            "  4  Unexpected internal error");

    // ── Sidecar parse (v3 §6) ────────────────────────────────────────────
    private static async Task<string> ParseSidecarToJsonAsync(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".yml":
            case ".yaml":
                return await ParseYamlToJsonAsync(path).ConfigureAwait(false);
            case ".json":
                return await ParseJsonToJsonAsync(path).ConfigureAwait(false);
            default:
                throw new SidecarParseException(
                    $"unsupported sidecar extension '{ext}' for {path}; expected .yml/.yaml/.json");
        }
    }

    private static async Task<string> ParseJsonToJsonAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.GetRawText();
        }
        catch (JsonException ex)
        {
            throw new SidecarParseException($"invalid JSON: {ex.Message}");
        }
    }

    private static async Task<string> ParseYamlToJsonAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count == 0)
            {
                throw new SidecarParseException("YAML file contains no documents");
            }
            var normalized = NormalizeYaml(stream.Documents[0].RootNode);
            return JsonSerializer.Serialize(normalized);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new SidecarParseException($"invalid YAML at line {ex.Start.Line}, col {ex.Start.Column}: {ex.Message}");
        }
    }

    // Recursive YAML -> .NET object normalization (v3 §6).
    private static object? NormalizeYaml(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                var dict = new Dictionary<string, object?>();
                foreach (var kv in mapping.Children)
                {
                    if (kv.Key is not YamlScalarNode keyScalar)
                    {
                        throw new SidecarParseException("YAML mapping has a non-string key (only string keys are supported)");
                    }
                    dict[keyScalar.Value ?? string.Empty] = NormalizeYaml(kv.Value);
                }
                return dict;
            case YamlSequenceNode sequence:
                var list = new List<object?>();
                foreach (var child in sequence.Children) list.Add(NormalizeYaml(child));
                return list;
            case YamlScalarNode scalar:
                return NormalizeScalar(scalar.Value, scalar.Style);
            default:
                throw new SidecarParseException($"unexpected YAML node kind: {node.GetType().Name}");
        }
    }

    private static object? NormalizeScalar(string? value, YamlDotNet.Core.ScalarStyle style)
    {
        if (value is null) return null;

        // YAML distinguishes plain (unquoted) scalars from quoted ones.
        // Quoted scalars are ALWAYS strings -- their author asked for that
        // explicitly. Only plain scalars get bool/long/double coercion per
        // YAML's resolution rules. Without this, `mqttPort: "1883"` would
        // be coerced back to an integer 1883 in the JSON output and pass
        // the integer-typed schema even though the operator wrote a string.
        var isQuoted = style is YamlDotNet.Core.ScalarStyle.SingleQuoted
                              or YamlDotNet.Core.ScalarStyle.DoubleQuoted
                              or YamlDotNet.Core.ScalarStyle.Literal
                              or YamlDotNet.Core.ScalarStyle.Folded;
        if (isQuoted) return value;

        // Mirror YamlDotNet's default plain-scalar interpretation conservatively.
        // Empty / `null` / `~` -> null.
        if (string.IsNullOrEmpty(value) || value == "~" || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            return null;
        if (bool.TryParse(value, out var b)) return b;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return value;
    }

    // ── Display-path projection (v3 §4) ──────────────────────────────────
    internal static string ToDisplayPath(string? rawPath, string? property)
    {
        var p = rawPath ?? string.Empty;

        if (p.StartsWith("#/", StringComparison.Ordinal)) p = p.Substring(2);
        else if (p == "#") p = string.Empty;

        if (string.IsNullOrEmpty(p))
        {
            return string.IsNullOrEmpty(property) ? "<root>" : property!;
        }

        return p;
    }

    // ── Kind -> wrapped message (v3 §3) ──────────────────────────────────
    internal static string WrapMessage(ValidationErrorKind kind) => kind switch
    {
        ValidationErrorKind.NoAdditionalPropertiesAllowed => "unknown field — schema does not permit additional properties",
        ValidationErrorKind.PropertyRequired => "required field is missing",
        ValidationErrorKind.StringExpected => "wrong type — expected string",
        ValidationErrorKind.IntegerExpected => "wrong type — expected integer",
        ValidationErrorKind.NumberExpected => "wrong type — expected number",
        ValidationErrorKind.BooleanExpected => "wrong type — expected boolean",
        ValidationErrorKind.ObjectExpected => "wrong type — expected object",
        ValidationErrorKind.ArrayExpected => "wrong type — expected array",
        ValidationErrorKind.PatternMismatch => "value does not match the required pattern",
        ValidationErrorKind.NumberTooSmall => "value is out of range",
        ValidationErrorKind.NumberTooBig => "value is out of range",
        ValidationErrorKind.IntegerTooBig => "value is out of range",
        ValidationErrorKind.StringTooShort => "value cannot be empty",
        ValidationErrorKind.NotInEnumeration => "value is not one of the allowed values",
        ValidationErrorKind.GuidExpected => "value must be a valid UUID",
        ValidationErrorKind.UuidExpected => "value must be a valid UUID",
        _ => "schema rule violation",
    };
}

internal sealed class SidecarParseException : Exception
{
    public SidecarParseException(string message) : base(message) { }
}
