// ============================================================================
// Tests: Redaction drift guard (ADR-0020 R-2, M-B B2 part 1 — boundary
//        coverage). Fails CI if any application-type typed config property
//        lacks a [BundleTier]. Without this, a newly added Core field would
//        silently resolve to STRIP (fail-closed) and quietly drop from backups
//        — the guard forces an explicit tier decision at PR time.
//
//        Opaque-boundary properties (JsonElement? connection blocks) and
//        [JsonExtensionData] overflow members are exempt: they are
//        name-classified (World 2), not typed (World 1), so they carry no tier.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Backup;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class RedactionDriftGuardTests
{
    [Fact]
    public void EveryApplicationTypedProperty_DeclaresABundleTier()
    {
        var root = ConfigSchemaModelBuilder.Build(typeof(GatewayConfiguration));

        var unattributed = new List<string>();
        Visit(root, "$", unattributed);

        unattributed.Should().BeEmpty(
            "every application-type typed property must declare a [BundleTier] " +
            "(fail-closed safety, ADR-0020 R-2); unattributed: " + string.Join(", ", unattributed));
    }

    private static void Visit(SchemaNode node, string path, List<string> unattributed)
    {
        switch (node)
        {
            case TypedObjectSchemaNode typed:
                foreach (var prop in typed.Properties.Values)
                {
                    var childPath = $"{path}.{prop.Name}";

                    // Opaque-boundary (connection) properties are name-classified
                    // (World 2) — exempt from the typed-tier requirement.
                    if (prop.Child is OpaqueBoundarySchemaNode)
                    {
                        continue;
                    }

                    if (typed.IsApplicationType && prop.Tier is null)
                    {
                        unattributed.Add(childPath);
                    }

                    Visit(prop.Child, childPath, unattributed);
                }
                break;

            case ArraySchemaNode array:
                Visit(array.Element, $"{path}[]", unattributed);
                break;
        }
    }
}
