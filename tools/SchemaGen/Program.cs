// ============================================================================
// File: tools/SchemaGen/Program.cs
// Purpose: Generate JSON Schema files for the ElpisEdgeConnect.Core.Configuration
//          records and write them to docs/config-schemas/.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B1 Definition of Done
//
// Usage:
//   dotnet run --project tools/SchemaGen -c Release
//
// The tool walks the type graph rooted at GatewayConfiguration via NJsonSchema
// and emits one schema file per top-level record. Output paths are computed
// relative to the repository root by walking up from the current working
// directory until a directory containing ElpisEdgeConnect.sln is found.
// ============================================================================

using System;
using System.IO;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.SchemaValidation;
using NJsonSchema.Generation;

namespace ElpisEdgeConnect.Tools.SchemaGen;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var repoRoot = FindRepositoryRoot();
            var outputDir = Path.Combine(repoRoot, "docs", "config-schemas");
            Directory.CreateDirectory(outputDir);

            Console.WriteLine($"SchemaGen → output directory: {outputDir}");

            // Settings come from ConfigurationSchemaFactory so the CLI and
            // the round-trip schema validation tests in Core.Tests share a
            // single source of truth and cannot drift.
            var generator = ConfigurationSchemaFactory.CreateGenerator();

            // Root schema — every other record is reachable from here.
            await EmitSchemaAsync<GatewayConfiguration>(generator, outputDir, "gateway-configuration.schema.json");

            // Standalone schemas for editor experience and partial validation.
            await EmitSchemaAsync<GatewaySettings>(generator, outputDir, "gateway-settings.schema.json");
            await EmitSchemaAsync<SourceInstanceConfig>(generator, outputDir, "source-instance.schema.json");
            await EmitSchemaAsync<SinkInstanceConfig>(generator, outputDir, "sink-instance.schema.json");
            await EmitSchemaAsync<RouteConfig>(generator, outputDir, "route.schema.json");

            Console.WriteLine("SchemaGen completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SchemaGen failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static async Task EmitSchemaAsync<T>(JsonSchemaGenerator generator, string outputDir, string fileName)
    {
        var schema = generator.Generate(typeof(T));
        var json = schema.ToJson();
        var path = Path.Combine(outputDir, fileName);
        await File.WriteAllTextAsync(path, json);
        Console.WriteLine($"  wrote {fileName} ({json.Length} chars)");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ElpisEdgeConnect.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate ElpisEdgeConnect.sln in any parent directory of " +
            Directory.GetCurrentDirectory());
    }
}
