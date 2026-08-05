// ============================================================================
// File: Licensing/CanonicalJsonTests.cs
// Covers: CanonicalJson.Canonicalize — key ordering at every depth, top-level
//         signature stripping, nested signature preservation, array order,
//         primitives.
// R2 pin (Mutation 2, 3 from the B3 close-out review).
// ============================================================================

using System.Text;
using ElpisEdgeConnect.Core.Licensing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void DifferentTopLevelKeyOrder_ProducesIdenticalCanonicalBytes()
    {
        // Pin (Mutation 2): if someone removes the OrderBy in WriteObject,
        // these two inputs will produce different canonical bytes.
        var a = """{"b":2,"a":1}""";
        var b = """{"a":1,"b":2}""";

        var ca = CanonicalJson.Canonicalize(a);
        var cb = CanonicalJson.Canonicalize(b);

        ca.Should().Equal(cb);
    }

    [Fact]
    public void DifferentNestedKeyOrder_ProducesIdenticalCanonicalBytes()
    {
        // Pin (Mutation 2 — nested case): sorting must happen at EVERY depth,
        // not just the root.
        var a = """{"outer":{"z":1,"a":2}}""";
        var b = """{"outer":{"a":2,"z":1}}""";

        CanonicalJson.Canonicalize(a).Should().Equal(CanonicalJson.Canonicalize(b));
    }

    [Fact]
    public void TopLevelSignature_IsStripped()
    {
        var withSig = """{"a":1,"signature":"abc"}""";
        var withoutSig = """{"a":1}""";

        CanonicalJson.Canonicalize(withSig).Should().Equal(CanonicalJson.Canonicalize(withoutSig));
    }

    [Fact]
    public void NestedSignatureKey_IsPreserved()
    {
        // Pin (Mutation 3): stripping signature at all depths would break this.
        var withNested = """{"outer":{"signature":"keep-me"}}""";
        var withoutNested = """{"outer":{}}""";

        CanonicalJson.Canonicalize(withNested).Should().NotEqual(CanonicalJson.Canonicalize(withoutNested));

        var canonical = Encoding.UTF8.GetString(CanonicalJson.Canonicalize(withNested));
        canonical.Should().Contain("signature").And.Contain("keep-me");
    }

    [Fact]
    public void ArrayOrder_IsPreserved()
    {
        var forward = """{"xs":[1,2,3]}""";
        var reversed = """{"xs":[3,2,1]}""";

        CanonicalJson.Canonicalize(forward).Should().NotEqual(CanonicalJson.Canonicalize(reversed));
    }

    [Fact]
    public void Primitives_RoundTrip()
    {
        var json = """{"b":true,"f":false,"n":null,"num":42,"s":"hello"}""";
        var canonical = Encoding.UTF8.GetString(CanonicalJson.Canonicalize(json));

        // Keys sorted ordinal: b, f, n, num, s
        canonical.Should().Be("""{"b":true,"f":false,"n":null,"num":42,"s":"hello"}""");
    }
}
