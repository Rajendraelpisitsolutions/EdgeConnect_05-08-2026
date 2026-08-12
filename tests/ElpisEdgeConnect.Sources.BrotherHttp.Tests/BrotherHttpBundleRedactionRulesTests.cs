// ============================================================================
// Tests: BrotherHttpBundleRedactionRules (ADR-0020 M-B B4). KnownKeys cover
//        exactly the declared constants; Brother HTTP has no credentials in its
//        connection block, so every key is INCLUDE.
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.BrotherHttp;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

public class BrotherHttpBundleRedactionRulesTests
{
    private readonly BrotherHttpBundleRedactionRules _rules = new();

    [Fact]
    public void ProtocolName_MatchesAdapter() =>
        _rules.ProtocolName.Should().Be(BrotherHttpSourceConfiguration.ProtocolNameConstant);

    [Fact]
    public void KnownKeys_CoverExactlyTheDeclaredConstants() =>
        _rules.KnownKeys.Keys.Should().BeEquivalentTo(BrotherHttpConnectionKeys.All);

    [Fact]
    public void EveryKey_IsInclude_NoSecretsInBrotherHttpConnection()
    {
        _rules.KnownKeys.Values.Should().OnlyContain(t => t == BundleTier.Include);
        _rules.ExtraNameOverrides.Should().BeEmpty();
    }
}
