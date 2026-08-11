// ============================================================================
// File: tools/ValidateConfig/Program.cs
// Purpose: CLI wrapper around NJsonSchemaConfigurationValidator. Validates a
//          gateway.json against the canonical schema and (per ADR-0030) warns
//          on non-`_`-prefixed unknown roots that likely indicate typos on
//          canonical fields (Sources / Sinks / Routes / Gateway).
//
//          Primary consumer: tools/bulk-provision/lib/Validate-AgainstSchema.ps1
//          (Chip 3 generator).
//
//          Single-purpose, single-file CLI per the existing tools/ pattern
//          (tools/SchemaGen, tools/ModbusCsvImport).
// Reference: docs/sessions/2026-05-21-chip3-provisioning-subsystem-v3-reality-check.md §1.2
//            docs/decisions/0030-reserved-underscore-namespace.md
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.SchemaValidation;

namespace ElpisEdgeConnect.Tools.ValidateConfig;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || args[0] is "--help" or "-h")
        {
            Console.Error.WriteLine(
                "Usage: dotnet run --project tools/ValidateConfig -- <path-to-gateway.json>\n" +
                "\n" +
                "Validates the JSON file against the canonical GatewayConfiguration schema.\n" +
                "Warns on non-`_`-prefixed unknown root keys (likely typos on canonical fields).\n" +
                "\n" +
                "Exit codes:\n" +
                "  0  Valid — no schema errors, no suspect roots\n" +
                "  1  Schema-validation errors (printed to stderr)\n" +
                "  2  File not found / unreadable\n" +
                "  3  Unexpected internal error");
            return 0;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"ValidateConfig: file not found: {path}");
            return 2;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ValidateConfig: unable to read {path}: {ex.Message}");
            return 2;
        }

        try
        {
            // ── Schema validation (canonical-field correctness) ────────
            var validator = new NJsonSchemaConfigurationValidator();
            var result = await validator.ValidateAsync(json, CancellationToken.None).ConfigureAwait(false);

            if (!result.IsValid)
            {
                Console.Error.WriteLine($"ValidateConfig: schema validation FAILED for {path}");
                foreach (var issue in result.Errors)
                {
                    var pathSegment = string.IsNullOrEmpty(issue.Path) ? "<root>" : issue.Path;
                    Console.Error.WriteLine($"  [{issue.Code}] {pathSegment}: {issue.Message}");
                }
                return 1;
            }

            // ── ADR-0030 suspect-roots warning ─────────────────────────
            // The canonical-field schema is clean — but the parser
            // captures unknown roots in ExtensionData. Per ADR-0030,
            // non-`_`-prefixed unknown roots SHOULD be warned about; they
            // typically indicate a typo on a canonical field name.
            // `_`-prefixed roots are reserved metadata (e.g. `_provisioning`)
            // and pass through silently.
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            var config = JsonSerializer.Deserialize<GatewayConfiguration>(json, jsonOptions);

            var suspectRoots = config?.ExtensionData?
                .Where(kv => !kv.Key.StartsWith('_'))
                .Select(kv => kv.Key)
                .ToList()
                ?? new List<string>();

            if (suspectRoots.Count > 0)
            {
                Console.Error.WriteLine(
                    $"ValidateConfig: WARNING — {suspectRoots.Count} suspect root key(s) in {path}");
                Console.Error.WriteLine(
                    "Per ADR-0030, non-`_`-prefixed unknown roots are preserved for forward compatibility");
                Console.Error.WriteLine(
                    "but likely indicate typos on canonical fields (Sources / Sinks / Routes / Gateway / Schemas).");
                foreach (var key in suspectRoots)
                {
                    var suggestion = SuggestCanonicalRoot(key);
                    if (suggestion is not null)
                    {
                        Console.Error.WriteLine($"  - \"{key}\" — did you mean \"{suggestion}\"?");
                    }
                    else
                    {
                        Console.Error.WriteLine($"  - \"{key}\"");
                    }
                }
                // Warnings do not fail the build — operators may legitimately
                // attach metadata under non-`_` roots while a future canonical
                // field is being designed. Exit 0; caller decides whether to
                // promote to an error.
            }

            Console.Out.WriteLine($"ValidateConfig: {path} is valid.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ValidateConfig: unexpected error: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    /// <summary>
    /// Returns the canonical root name closest to <paramref name="suspect"/>
    /// (case-insensitive Levenshtein distance ≤ 2), or null if nothing is
    /// close enough to suggest. Keeps the suggestion list short — false
    /// suggestions are worse than no suggestion.
    /// </summary>
    private static string? SuggestCanonicalRoot(string suspect)
    {
        ReadOnlySpan<string> canonical = ["Gateway", "Sources", "Sinks", "Routes", "Schemas"];
        var best = (Name: (string?)null, Distance: int.MaxValue);
        foreach (var name in canonical)
        {
            var d = LevenshteinDistance(suspect, name);
            if (d < best.Distance)
            {
                best = (name, d);
            }
        }
        return best.Distance <= 2 ? best.Name : null;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        // Simple iterative DP. Enough for short root-key names.
        var n = a.Length;
        var m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[m];
    }
}
