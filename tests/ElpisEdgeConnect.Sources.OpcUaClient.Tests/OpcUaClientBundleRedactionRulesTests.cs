// ============================================================================
// Tests: OpcUaClientBundleRedactionRules (ADR-0020 M-B B3). Pins the per-
//        protocol redaction contract for OPC UA Client: KnownKeys cover exactly
//        the declared constants (key-coverage drift guard, Q-B3), the
//        user-identity password is MASK, certificate/credential PATHS are
//        INCLUDE (a path is not key material), and the factory-unread
//        certificatePassword is masked via ExtraNameOverrides.
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public class OpcUaClientBundleRedactionRulesTests
{
    private readonly OpcUaClientBundleRedactionRules _rules = new();

    [Fact]
    public void ProtocolName_MatchesAdapter()
    {
        _rules.ProtocolName.Should().Be(OpcUaClientSourceConfiguration.ProtocolNameConstant);
    }

    [Fact]
    public void KnownKeys_CoverExactlyTheDeclaredConstants()
    {
        // Drift guard (Q-B3): KnownKeys == OpcUaClientConnectionKeys.All.
        _rules.KnownKeys.Keys.Should().BeEquivalentTo(OpcUaClientConnectionKeys.All,
            "every connection key the factory reads must have a declared tier, and no extra tiers");
    }

    [Fact]
    public void Password_IsMask()
    {
        _rules.KnownKeys[OpcUaClientConnectionKeys.Password].Should().Be(BundleTier.Mask);
    }

    [Fact]
    public void CertificateAndCredentialPaths_AreInclude_NotKeyMaterial()
    {
        _rules.KnownKeys[OpcUaClientConnectionKeys.CertificatePath].Should().Be(BundleTier.Include);
        _rules.KnownKeys[OpcUaClientConnectionKeys.ApplicationCertificateStorePath].Should().Be(BundleTier.Include);
    }

    [Fact]
    public void CredentialsAndMonitoredItems_AreInclude_SoWalkerRecurses()
    {
        _rules.KnownKeys[OpcUaClientConnectionKeys.Credentials].Should().Be(BundleTier.Include);
        _rules.KnownKeys[OpcUaClientConnectionKeys.MonitoredItems].Should().Be(BundleTier.Include);
    }

    [Fact]
    public void CertificatePassword_IsMaskedViaExtraNameOverrides_NotKnownKeys()
    {
        // The factory does not parse certificatePassword, so it is NOT a known
        // key — but it is secret-shaped, so it is masked via the overrides.
        _rules.KnownKeys.Should().NotContainKey("certificatePassword");
        _rules.ExtraNameOverrides["certificatePassword"].Should().Be(BundleTier.Mask);
    }

    [Fact]
    public void OnlyPassword_IsSecretAmongKnownKeys()
    {
        foreach (var (key, tier) in _rules.KnownKeys)
        {
            if (key == OpcUaClientConnectionKeys.Password)
            {
                tier.Should().Be(BundleTier.Mask);
            }
            else
            {
                tier.Should().Be(BundleTier.Include, $"'{key}' is not a secret (paths and tuning are Include)");
            }
        }
    }
}
