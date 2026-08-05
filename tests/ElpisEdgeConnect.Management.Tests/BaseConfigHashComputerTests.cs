// ============================================================================
// File: BaseConfigHashComputerTests.cs
// Purpose: Coverage for the v3.1 §6 stale-preview guard's hash function:
//          determinism, sensitivity, format, and null handling.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Api.BulkSourceMerge;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class BaseConfigHashComputerTests
{
    private static GatewayConfiguration MinimalConfig(string gatewayId = "GW-001", string gatewayName = "Gateway 1") =>
        new()
        {
            Gateway = new GatewaySettings { GatewayId = gatewayId, GatewayName = gatewayName },
        };

    [Fact]
    public void Compute_SameConfigTwice_ProducesIdenticalHash()
    {
        var hash1 = BaseConfigHashComputer.Compute(MinimalConfig());
        var hash2 = BaseConfigHashComputer.Compute(MinimalConfig());

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void Compute_DifferentGatewayIds_ProducesDifferentHash()
    {
        var hashA = BaseConfigHashComputer.Compute(MinimalConfig(gatewayId: "GW-001"));
        var hashB = BaseConfigHashComputer.Compute(MinimalConfig(gatewayId: "GW-002"));

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void Compute_DifferentGatewayNames_ProducesDifferentHash()
    {
        var hashA = BaseConfigHashComputer.Compute(MinimalConfig(gatewayName: "Gateway 1"));
        var hashB = BaseConfigHashComputer.Compute(MinimalConfig(gatewayName: "Gateway 2"));

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void Compute_AddingASource_ProducesDifferentHash()
    {
        var bare = MinimalConfig();
        var withSource = bare with
        {
            Sources = new[]
            {
                new SourceInstanceConfig
                {
                    InstanceId = "cnc-001-source",
                    ProtocolName = "focas2",
                    DeviceId = "cnc-001",
                },
            },
        };

        var hashBare = BaseConfigHashComputer.Compute(bare);
        var hashWithSource = BaseConfigHashComputer.Compute(withSource);

        hashWithSource.Should().NotBe(hashBare);
    }

    [Fact]
    public void Compute_Returns64HexLowercaseChars()
    {
        var hash = BaseConfigHashComputer.Compute(MinimalConfig());

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void Compute_NullConfig_Throws()
    {
        var act = () => BaseConfigHashComputer.Compute(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
