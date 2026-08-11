// ============================================================================
// Tests: BrotherErrors — code stability + naming convention.
//        Catalog is append-only; renaming a code is a breaking change for
//        any caller that pattern-matches on the string.
// ============================================================================

using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

public sealed class BrotherErrorsTests
{
    [Fact]
    public void AllCodes_StartWith_BrotherPrefix()
    {
        foreach (var field in EnumerateCodes())
        {
            var code = (string)field.GetValue(null)!;
            code.Should().StartWith("BROTHER.",
                $"{field.Name} must follow the MODULE.CATEGORY_SUBCATEGORY convention per CLAUDE.md §7");
        }
    }

    [Fact]
    public void AllCodes_ContainOnly_UppercaseAndUnderscores()
    {
        foreach (var field in EnumerateCodes())
        {
            var code = (string)field.GetValue(null)!;
            var afterPrefix = code["BROTHER.".Length..];

            afterPrefix.Should().NotBeEmpty();
            afterPrefix.Should().MatchRegex("^[A-Z_]+$",
                $"{field.Name} suffix must be uppercase letters or underscores");
        }
    }

    [Fact]
    public void AllCodes_AreUnique()
    {
        var codes = EnumerateCodes()
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void RequiredCodes_AreInTheCatalog()
    {
        // Pins the codes the adapter + collectors + validator reference.
        // Renaming any of these breaks downstream pattern matchers.
        var codes = EnumerateCodes().Select(f => (string)f.GetValue(null)!).ToHashSet();

        codes.Should().Contain("BROTHER.HTTP_UNREACHABLE");
        codes.Should().Contain("BROTHER.POLL_TOO_FAST");
        codes.Should().Contain("BROTHER.UNKNOWN_DATA_POINT");
        codes.Should().Contain("BROTHER.CONFIG_MISSING_FIELD");
    }

    private static System.Collections.Generic.IEnumerable<FieldInfo> EnumerateCodes() =>
        typeof(BrotherErrors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string));
}
