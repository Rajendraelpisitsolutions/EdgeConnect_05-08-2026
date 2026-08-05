// ============================================================================
// Tests: BundleRedactionRulesRegistry (ADR-0020 M-B B3). Pins the M2/M3
//        composition: a protocol's KnownKeys / ExtraNameOverrides win over the
//        baseline, unknown keys fall to the baseline, and an unknown protocol
//        uses the baseline alone (fail-open Include for non-secrets).
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Backup;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class BundleRedactionRulesRegistryTests
{
    private sealed class FakeRules : IBundleRedactionRules
    {
        public string ProtocolName => "fakeproto";

        public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } = new Dictionary<string, BundleTier>
        {
            ["host"] = BundleTier.Include,
            ["password"] = BundleTier.Mask,
            ["sessionKey"] = BundleTier.Strip, // a protocol-specific secret not in the baseline
        };

        public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } = new Dictionary<string, BundleTier>
        {
            ["legacyToken"] = BundleTier.Strip,
        };
    }

    private readonly BundleRedactionRulesRegistry _registry = new(new IBundleRedactionRules[] { new FakeRules() });

    [Fact]
    public void HasRules_TrueForRegistered_FalseForUnknown()
    {
        _registry.HasRules("fakeproto").Should().BeTrue();
        _registry.HasRules("mqtt").Should().BeFalse();
        _registry.HasRules(null).Should().BeFalse();
    }

    [Fact]
    public void ResolveOpaqueKeyTier_ProtocolKnownKeys_WinAndAreUsed()
    {
        _registry.ResolveOpaqueKeyTier("fakeproto", "password").Should().Be(BundleTier.Mask);
        _registry.ResolveOpaqueKeyTier("fakeproto", "host").Should().Be(BundleTier.Include);
        _registry.ResolveOpaqueKeyTier("fakeproto", "sessionKey").Should().Be(BundleTier.Strip,
            "a protocol can declare a secret the shared baseline doesn't know about");
    }

    [Fact]
    public void ResolveOpaqueKeyTier_ExtraNameOverrides_Apply()
    {
        _registry.ResolveOpaqueKeyTier("fakeproto", "legacyToken").Should().Be(BundleTier.Strip);
    }

    [Fact]
    public void ResolveOpaqueKeyTier_UnknownKeyForKnownProtocol_FallsToBaselineThenInclude()
    {
        // 'apiKey' is not in the protocol's KnownKeys but IS in the baseline.
        _registry.ResolveOpaqueKeyTier("fakeproto", "apiKey").Should().Be(BundleTier.Mask,
            "an unknown protocol key still gets the shared baseline tier");
        // 'endpointUrl' is neither — fail-open Include.
        _registry.ResolveOpaqueKeyTier("fakeproto", "endpointUrl").Should().Be(BundleTier.Include);
    }

    [Fact]
    public void ResolveOpaqueKeyTier_UnknownProtocol_UsesBaselineOnly()
    {
        _registry.ResolveOpaqueKeyTier("unregistered", "password").Should().Be(BundleTier.Mask,
            "baseline still classifies known secret names even without protocol rules");
        _registry.ResolveOpaqueKeyTier(null, "privateKey").Should().Be(BundleTier.Strip);
        _registry.ResolveOpaqueKeyTier("unregistered", "host").Should().Be(BundleTier.Include);
    }
}
