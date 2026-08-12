// ============================================================================
// Tests: MqttBundleRedactionRules (ADR-0020 M-B B3). Pins the per-protocol
//        redaction contract for MQTT: the rules cover exactly the declared
//        connection-key constants (the "key coverage" drift guard, Q-B3), the
//        broker password is MASK, and every other key is INCLUDE.
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sinks.Mqtt;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.Mqtt.Tests;

public class MqttBundleRedactionRulesTests
{
    private readonly MqttBundleRedactionRules _rules = new();

    [Fact]
    public void ProtocolName_MatchesAdapter()
    {
        _rules.ProtocolName.Should().Be(MqttSinkConfiguration.ProtocolNameConstant);
    }

    [Fact]
    public void KnownKeys_CoverExactlyTheDeclaredConstants()
    {
        // Drift guard (Q-B3): KnownKeys == MqttConnectionKeys.All. A new key the
        // factory parses adds a constant, which fails this until a tier is set.
        _rules.KnownKeys.Keys.Should().BeEquivalentTo(MqttConnectionKeys.All,
            "every connection key must have a declared tier, and no extra tiers");
    }

    [Fact]
    public void Password_IsMask()
    {
        _rules.KnownKeys[MqttConnectionKeys.Password].Should().Be(BundleTier.Mask,
            "the broker password is re-enterable on restore — MASK keeps the key visible");
    }

    [Fact]
    public void NonSecretKeys_AreInclude()
    {
        _rules.KnownKeys[MqttConnectionKeys.BrokerHost].Should().Be(BundleTier.Include);
        _rules.KnownKeys[MqttConnectionKeys.BrokerPort].Should().Be(BundleTier.Include);
        _rules.KnownKeys[MqttConnectionKeys.ClientId].Should().Be(BundleTier.Include);
        _rules.KnownKeys[MqttConnectionKeys.Username].Should().Be(BundleTier.Include);
    }

    [Fact]
    public void OnlySecretsAreMaskedOrStripped()
    {
        foreach (var (key, tier) in _rules.KnownKeys)
        {
            if (key == MqttConnectionKeys.Password)
            {
                tier.Should().Be(BundleTier.Mask);
            }
            else
            {
                tier.Should().Be(BundleTier.Include, $"'{key}' is not a secret");
            }
        }
    }
}
