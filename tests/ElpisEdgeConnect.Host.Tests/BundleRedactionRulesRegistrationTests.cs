// ============================================================================
// Tests: BundleRedactionRulesRegistration (ADR-0020 M-B B5). Pins that every
//        adapter's IBundleRedactionRules is wired into DI unconditionally, and
//        the cross-protocol drift guard: no two rule sets claim the same
//        ProtocolName (which would make the registry's last-wins composition
//        silently drop one protocol's rules).
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sinks.Mqtt;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using ElpisEdgeConnect.Sources.BrotherHttp;
using ElpisEdgeConnect.Sources.EthernetIp;
using ElpisEdgeConnect.Sources.Focas2;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.ModbusTcp;
using ElpisEdgeConnect.Sources.MTConnect;
using ElpisEdgeConnect.Sources.OpcUaClient;
using ElpisEdgeConnect.Sources.S7;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public class BundleRedactionRulesRegistrationTests
{
    private static System.Collections.Generic.IReadOnlyList<IBundleRedactionRules> Resolve()
    {
        var services = new ServiceCollection();
        services.AddBundleRedactionRules();
        using var sp = services.BuildServiceProvider();
        return sp.GetServices<IBundleRedactionRules>().ToList();
    }

    [Fact]
    public void AddBundleRedactionRules_RegistersEveryAdapter()
    {
        var rules = Resolve();

        rules.Select(r => r.ProtocolName).Should().BeEquivalentTo(new[]
        {
            MqttSinkConfiguration.ProtocolNameConstant,
            OpcUaServerConfiguration.ProtocolNameConstant,
            OpcUaClientSourceConfiguration.ProtocolNameConstant,
            Focas2SourceConfiguration.ProtocolNameConstant,
            MTConnectSourceConfiguration.ProtocolNameConstant,
            BrotherHttpSourceConfiguration.ProtocolNameConstant,
            ModbusTcpSourceConfiguration.ProtocolNameConstant,
            S7SourceConfiguration.ProtocolNameConstant,
            EthernetIpSourceConfiguration.ProtocolNameConstant,
            MelsecSourceConfiguration.ProtocolNameConstant,
        });
    }

    [Fact]
    public void EveryProtocolName_IsUnique_CrossProtocolDriftGuard()
    {
        var protocolNames = Resolve().Select(r => r.ProtocolName).ToList();

        protocolNames.Should().OnlyHaveUniqueItems(
            "two rule sets sharing a ProtocolName would make the registry's last-wins " +
            "composition silently drop one protocol's redaction rules");
    }

    [Fact]
    public void EveryRuleSet_HasNonEmptyKnownKeys()
    {
        Resolve().Should().OnlyContain(r => r.KnownKeys.Count > 0,
            "a rule set with no known keys would classify nothing and is almost certainly a mistake");
    }
}
