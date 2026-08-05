// ============================================================================
// Tests: OpcUaServerBundleRedactionRules (ADR-0020 M-B B4). KnownKeys cover
//        exactly the declared constants (key-coverage drift guard). The only
//        secret is the per-user credential password (MASK); certificate-store
//        PATHS and everything else are INCLUDE.
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.OpcUaServer.Tests;

public class OpcUaServerBundleRedactionRulesTests
{
    private readonly OpcUaServerBundleRedactionRules _rules = new();

    [Fact]
    public void ProtocolName_MatchesAdapter() =>
        _rules.ProtocolName.Should().Be(OpcUaServerConfiguration.ProtocolNameConstant);

    [Fact]
    public void KnownKeys_CoverExactlyTheDeclaredConstants() =>
        _rules.KnownKeys.Keys.Should().BeEquivalentTo(OpcUaServerConnectionKeys.All);

    [Fact]
    public void CredentialPassword_IsMask()
    {
        _rules.KnownKeys[OpcUaServerConnectionKeys.Password].Should().Be(BundleTier.Mask);
    }

    [Fact]
    public void CertificateStorePaths_AreInclude_NotKeyMaterial()
    {
        _rules.KnownKeys[OpcUaServerConnectionKeys.ApplicationCertificatePath].Should().Be(BundleTier.Include);
        _rules.KnownKeys[OpcUaServerConnectionKeys.TrustedClientsPath].Should().Be(BundleTier.Include);
        _rules.KnownKeys[OpcUaServerConnectionKeys.RejectedClientsPath].Should().Be(BundleTier.Include);
    }

    [Fact]
    public void OnlyPassword_IsSecret()
    {
        foreach (var (key, tier) in _rules.KnownKeys)
        {
            if (key == OpcUaServerConnectionKeys.Password)
            {
                tier.Should().Be(BundleTier.Mask);
            }
            else
            {
                tier.Should().Be(BundleTier.Include, $"'{key}' is not a secret");
            }
        }

        _rules.ExtraNameOverrides.Should().BeEmpty();
    }
}
