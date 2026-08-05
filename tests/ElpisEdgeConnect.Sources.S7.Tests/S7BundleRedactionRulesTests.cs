// ============================================================================
// Tests: S7BundleRedactionRules (ADR-0020 M-B B4). KnownKeys cover exactly the
//        declared constants (connection + per-tag); S7comm has no credential
//        exchange in this connection block, so every key is INCLUDE.
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.S7;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests;

public class S7BundleRedactionRulesTests
{
    private readonly S7BundleRedactionRules _rules = new();

    [Fact]
    public void ProtocolName_MatchesAdapter() =>
        _rules.ProtocolName.Should().Be(S7SourceConfiguration.ProtocolNameConstant);

    [Fact]
    public void KnownKeys_CoverExactlyTheDeclaredConstants() =>
        _rules.KnownKeys.Keys.Should().BeEquivalentTo(S7ConnectionKeys.All);

    [Fact]
    public void EveryKey_IsInclude_NoSecretsInS7Connection()
    {
        _rules.KnownKeys.Values.Should().OnlyContain(t => t == BundleTier.Include);
        _rules.ExtraNameOverrides.Should().BeEmpty();
    }
}
