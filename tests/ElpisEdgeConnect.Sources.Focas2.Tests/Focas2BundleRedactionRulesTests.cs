// ============================================================================
// Tests: Focas2BundleRedactionRules (ADR-0020 M-B B4). KnownKeys cover exactly
//        the declared constants (key-coverage drift guard), and FOCAS2 — which
//        has no auth in its connection block — is entirely INCLUDE.
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.Focas2;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

public class Focas2BundleRedactionRulesTests
{
    private readonly Focas2BundleRedactionRules _rules = new();

    [Fact]
    public void ProtocolName_MatchesAdapter() =>
        _rules.ProtocolName.Should().Be(Focas2SourceConfiguration.ProtocolNameConstant);

    [Fact]
    public void KnownKeys_CoverExactlyTheDeclaredConstants() =>
        _rules.KnownKeys.Keys.Should().BeEquivalentTo(Focas2ConnectionKeys.All);

    [Fact]
    public void EveryKey_IsInclude_NoSecretsInFocas2Connection()
    {
        _rules.KnownKeys.Values.Should().OnlyContain(t => t == BundleTier.Include);
        _rules.ExtraNameOverrides.Should().BeEmpty();
    }
}
