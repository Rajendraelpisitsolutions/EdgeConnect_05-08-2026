// ============================================================================
// Tests: ModbusTcpBundleRedactionRules (ADR-0020 M-B B4). KnownKeys cover
//        exactly the declared constants (connection + per-tag); Modbus TCP has
//        no auth, so every key is INCLUDE.
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.ModbusTcp;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public class ModbusTcpBundleRedactionRulesTests
{
    private readonly ModbusTcpBundleRedactionRules _rules = new();

    [Fact]
    public void ProtocolName_MatchesAdapter() =>
        _rules.ProtocolName.Should().Be(ModbusTcpSourceConfiguration.ProtocolNameConstant);

    [Fact]
    public void KnownKeys_CoverExactlyTheDeclaredConstants() =>
        _rules.KnownKeys.Keys.Should().BeEquivalentTo(ModbusTcpConnectionKeys.All);

    [Fact]
    public void EveryKey_IsInclude_NoSecretsInModbusConnection()
    {
        _rules.KnownKeys.Values.Should().OnlyContain(t => t == BundleTier.Include);
        _rules.ExtraNameOverrides.Should().BeEmpty();
    }
}
