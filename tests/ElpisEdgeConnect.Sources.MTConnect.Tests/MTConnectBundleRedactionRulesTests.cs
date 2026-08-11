// ============================================================================
// Tests: MTConnectBundleRedactionRules (ADR-0020 M-B B4). KnownKeys cover
//        exactly the declared constants; MTConnect has no credentials in its
//        connection block, so every key is INCLUDE.
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.MTConnect;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.MTConnect.Tests;

public class MTConnectBundleRedactionRulesTests
{
    private readonly MTConnectBundleRedactionRules _rules = new();

    [Fact]
    public void ProtocolName_MatchesAdapter() =>
        _rules.ProtocolName.Should().Be(MTConnectSourceConfiguration.ProtocolNameConstant);

    [Fact]
    public void KnownKeys_CoverExactlyTheDeclaredConstants() =>
        _rules.KnownKeys.Keys.Should().BeEquivalentTo(MTConnectConnectionKeys.All);

    [Fact]
    public void EveryKey_IsInclude_NoSecretsInMTConnectConnection()
    {
        _rules.KnownKeys.Values.Should().OnlyContain(t => t == BundleTier.Include);
        _rules.ExtraNameOverrides.Should().BeEmpty();
    }
}
