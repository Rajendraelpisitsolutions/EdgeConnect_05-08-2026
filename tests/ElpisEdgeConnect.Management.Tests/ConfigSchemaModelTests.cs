// ============================================================================
// Tests: ConfigSchemaModel — the reflection-derived World boundary (M-B B1).
//        Pins that exactly the expected World-2 seams (Opaque + extensionData)
//        are detected, that the schema-model dump is stable (a structural
//        change surfaces as a diff), and that the model build is deterministic.
//
//        Current expected seam set:
//          Opaque (JsonElement?)       Sources[*].Connection, Sinks[*].Connection
//          ExtensionData               GatewayConfiguration.ExtensionData (ADR-0030),
//                                       PublishingSettings.Extras
// ============================================================================

using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Backup;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class ConfigSchemaModelTests
{
    [Fact]
    public void Build_GatewayConfiguration_DetectsExpectedWorldTwoSeams()
    {
        var node = ConfigSchemaModelBuilder.Build(typeof(GatewayConfiguration));
        var dump = ConfigSchemaModelBuilder.Dump(node);

        // Two JsonElement? connection blocks render as Opaque nodes...
        CountOccurrences(dump, "Opaque").Should().Be(2,
            "sources[*].connection and sinks[*].connection are the only JsonElement? seams");
        // ...and two [JsonExtensionData] overflows render as extensionData flags:
        // PublishingSettings.Extras (original) + GatewayConfiguration.ExtensionData (ADR-0030).
        CountOccurrences(dump, "[+extensionData]").Should().Be(2,
            "PublishingSettings.Extras + GatewayConfiguration.ExtensionData are the [JsonExtensionData] seams");
    }

    [Fact]
    public void Build_GatewayConfiguration_TopLevelIsTypedNotOpaque()
    {
        var node = ConfigSchemaModelBuilder.Build(typeof(GatewayConfiguration));

        node.Should().BeOfType<TypedObjectSchemaNode>();
        var root = (TypedObjectSchemaNode)node;
        root.Properties.Keys.Should().Contain(new[] { "Gateway", "Sources", "Sinks", "Routes" });
        root.Properties["Sources"].Child.Should().BeOfType<ArraySchemaNode>();
    }

    [Fact]
    public void Build_SourceConnection_IsOpaqueBoundary()
    {
        var node = ConfigSchemaModelBuilder.Build(typeof(GatewayConfiguration));
        var root = (TypedObjectSchemaNode)node;

        var sourceElement = (TypedObjectSchemaNode)((ArraySchemaNode)root.Properties["Sources"].Child).Element;
        sourceElement.Properties["Connection"].Child.Should().BeOfType<OpaqueBoundarySchemaNode>(
            "the connection block is the World-2 boundary");
        sourceElement.Properties["DeviceId"].Child.Should().BeOfType<LeafSchemaNode>();
    }

    [Fact]
    public void Build_B2_EveryTypedFieldIsInclude()
    {
        // B2 placed [BundleTier(Include)] on every typed Core property (no
        // secrets at the typed level). Opaque-boundary properties (Connection)
        // carry no tier and are excluded from this check by AllTiers (which only
        // yields tiers for non-opaque properties).
        var node = ConfigSchemaModelBuilder.Build(typeof(GatewayConfiguration));
        AllTiers(node).Should().OnlyContain(t => t == BundleTier.Include,
            "all typed Core fields are Include; secrets live only in opaque connection blocks");
    }

    [Fact]
    public void SchemaModel_Dump_IsStable()
    {
        // Golden-file snapshot: a structural change (e.g. a field moving from
        // JsonElement? to a typed dictionary) surfaces as a diff in PR review.
        var dump = ConfigSchemaModelBuilder.Dump(
            ConfigSchemaModelBuilder.Build(typeof(GatewayConfiguration)));

        var path = SnapshotPath("GatewayConfigurationSchema.txt");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, dump);
        }

        var expected = File.ReadAllText(path);
        Normalize(dump).Should().Be(Normalize(expected),
            "schema model structure changed; review the diff and update the snapshot if intended");
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var a = ConfigSchemaModelBuilder.Dump(ConfigSchemaModelBuilder.Build(typeof(GatewayConfiguration)));
        var b = ConfigSchemaModelBuilder.Dump(ConfigSchemaModelBuilder.Build(typeof(GatewayConfiguration)));
        a.Should().Be(b);
    }

    // ---- helpers ----

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    private static System.Collections.Generic.IEnumerable<BundleTier?> AllTiers(SchemaNode node)
    {
        switch (node)
        {
            case TypedObjectSchemaNode t:
                foreach (var p in t.Properties.Values)
                {
                    // Opaque-boundary properties (e.g. Connection) intentionally
                    // carry no tier — they are name-classified, not typed.
                    if (p.Child is not OpaqueBoundarySchemaNode)
                    {
                        yield return p.Tier;
                    }
                    foreach (var inner in AllTiers(p.Child))
                    {
                        yield return inner;
                    }
                }
                break;
            case ArraySchemaNode a:
                foreach (var inner in AllTiers(a.Element))
                {
                    yield return inner;
                }
                break;
        }
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    private static string SnapshotPath(string name, [CallerFilePath] string thisFile = "")
        => Path.Combine(Path.GetDirectoryName(thisFile)!, "Snapshots", name);
}
