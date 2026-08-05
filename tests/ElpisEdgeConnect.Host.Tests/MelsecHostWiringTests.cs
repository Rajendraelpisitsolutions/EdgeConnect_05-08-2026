// ============================================================================
// Tests: Host DI wiring for MELSEC — RegistrationFactory dispatch, Premium
// license gating, connection-key redaction consistency, and retirement
// discoverability. MELSEC only CONSUMES ISourceRetirement (no generation logic).
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Text.Json;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.Melsec;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class MelsecHostWiringTests
{
    private const string MelsecConnJson = """{ "host": "127.0.0.1", "port": 6000 }""";

    private static RegistrationFactory MakeFactory() => new(NullLogger<RegistrationFactory>.Instance);

    private static IServiceProvider ServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        return services.BuildServiceProvider();
    }

    private static GatewaySettings Gateway() => new() { GatewayId = "gw-test", GatewayName = "Test Gateway" };

    private static SourceInstanceConfig MelsecSource(string protocolName = "melsec", bool enabled = true) => new()
    {
        InstanceId = "melsec-1",
        ProtocolName = protocolName,
        DeviceId = "dev-melsec-1",
        Enabled = enabled,
        Connection = JsonDocument.Parse(MelsecConnJson).RootElement,
    };

    private static ILicenseManager LicenseWith(bool melsecEnabled)
    {
        var license = Substitute.For<ILicenseManager>();
        license.Current.Returns(new LicenseInfo
        {
            LicenseId = "TEST",
            Customer = "Test",
            GatewayId = "gw-test",
            Edition = LicenseEdition.Professional,
            IssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Limits = new LicenseLimits { MaxSourceInstances = 100, MaxSinkInstances = 100, MaxRoutes = 100 },
            Modules = FrozenDictionary<string, LicenseModule>.Empty,
        });
        license.IsModuleEnabled(MelsecSourceConfiguration.LicenseModuleKey).Returns(melsecEnabled);
        return license;
    }

    [Fact]
    public void RegistrationFactory_builds_melsec_source_from_source_instance()
    {
        var reg = MakeFactory().BuildSource(
            MelsecSource(), Gateway(), _ => "route-melsec", license: null, faultRegistry: null, ServiceProvider());

        reg.Should().NotBeNull();
        reg!.Adapter.InstanceId.Should().Be("melsec-1");
        reg.Adapter.ProtocolName.Should().Be(MelsecSourceConfiguration.ProtocolNameConstant);
        reg.Config.Should().BeOfType<MelsecSourceConfiguration>();
        reg.RouteId.Should().Be("route-melsec");
    }

    [Fact]
    public void License_disabled_source_melsec_is_not_registered()
    {
        var reg = MakeFactory().BuildSource(
            MelsecSource(), Gateway(), _ => "r", LicenseWith(melsecEnabled: false), faultRegistry: null, ServiceProvider());

        reg.Should().BeNull("source-melsec is Premium-gated and the module is disabled");
    }

    [Fact]
    public void License_enabled_source_melsec_is_registered()
    {
        var reg = MakeFactory().BuildSource(
            MelsecSource(), Gateway(), _ => "r", LicenseWith(melsecEnabled: true), faultRegistry: null, ServiceProvider());

        reg.Should().NotBeNull();
    }

    [Fact]
    public void Built_adapter_is_discoverable_as_ISourceRetirement()
    {
        var reg = MakeFactory().BuildSource(
            MelsecSource(), Gateway(), _ => "r", null, null, ServiceProvider());

        // MELSEC consumes the shared retirement lease (ISourceRetirement) and adds
        // no generation logic of its own.
        reg!.Adapter.Should().BeAssignableTo<ISourceRetirement>();
    }

    [Fact]
    public async Task Built_adapter_still_rejects_unsupported_mode()
    {
        var reg = MakeFactory().BuildSource(
            MelsecSource(), Gateway(), _ => "r", null, null, ServiceProvider());

        var udpConfig = ((MelsecSourceConfiguration)reg!.Config) with { TransportProtocol = MelsecTransportProtocol.Udp };
        var result = await reg.Adapter.ValidateConfigAsync(udpConfig, default);

        result.IsValid.Should().BeFalse();
        result.Errors[0].Code.Should().Be("MELSEC.CONFIG_MODE_NOT_IMPLEMENTED");
    }

    [Fact]
    public void Redaction_rules_cover_every_connection_key_as_include()
    {
        var rules = new MelsecBundleRedactionRules();

        rules.ProtocolName.Should().Be(MelsecSourceConfiguration.ProtocolNameConstant);
        rules.KnownKeys.Keys.Should().Contain(new[]
        {
            MelsecConnectionKeys.Host,
            MelsecConnectionKeys.Port,
            MelsecConnectionKeys.NetworkNo,
            MelsecConnectionKeys.PcNo,
            MelsecConnectionKeys.RequestDestModuleIoNo,
        });
        rules.KnownKeys.Count.Should().Be(MelsecConnectionKeys.All.Count);
        rules.KnownKeys.Values.Should().OnlyContain(tier => tier == BundleTier.Include);
    }
}
