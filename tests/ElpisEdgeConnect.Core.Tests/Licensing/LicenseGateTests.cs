// ============================================================================
// File: Licensing/LicenseGateTests.cs
// Covers: full-config evaluation against the active license.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Core.Tests.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

public sealed class LicenseGateTests
{
    private static async Task<(LicenseManager, LicenseGate)> ManagerAtAsync(
        DateTime now,
        DateTime expires,
        IReadOnlyDictionary<string, (bool, int?)>? modules = null)
    {
        var validator = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        var mgr = new LicenseManager(validator, LicenseEnforcementPolicy.Default, () => now);
        var json = B3TestFixtures.BuildSignedLicense(expires: expires, modules: modules);
        await mgr.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);
        return (mgr, new LicenseGate(mgr));
    }

    [Fact]
    public async Task NotLoaded_EmptyConfig_Allowed()
    {
        var validator = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        using var mgr = new LicenseManager(validator);
        var gate = new LicenseGate(mgr);

        var result = await gate.EvaluateAsync(B2TestFixtures.ValidMinimal() with
        {
            Sources = [],
            Sinks = [],
            Routes = [],
        }, CancellationToken.None);

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task NotLoaded_NonEmptyConfig_Blocked()
    {
        var validator = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        using var mgr = new LicenseManager(validator);
        var gate = new LicenseGate(mgr);

        var result = await gate.EvaluateAsync(B2TestFixtures.ValidMinimal(), CancellationToken.None);
        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task ValidLicense_HappyConfig_Allowed()
    {
        var (mgr, gate) = await ManagerAtAsync(
            now: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            expires: new DateTime(2027, 4, 7));
        using (mgr)
        {
            var result = await gate.EvaluateAsync(B2TestFixtures.ValidMinimal(), CancellationToken.None);
            result.Allowed.Should().BeTrue();
            result.Reasons.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task UnlicensedSourceModule_Blocked()
    {
        var (mgr, gate) = await ManagerAtAsync(
            now: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            expires: new DateTime(2027, 4, 7),
            modules: new Dictionary<string, (bool, int?)>(StringComparer.Ordinal)
            {
                ["source.modbus"] = (true, 10),
                ["sink.mqtt"] = (true, null),
            });
        using (mgr)
        {
            var config = B2TestFixtures.ValidMinimal() with
            {
                Sources =
                [
                    new SourceInstanceConfig { InstanceId = "src-1", ProtocolName = "focas2", DeviceId = "D1" },
                ],
            };
            var result = await gate.EvaluateAsync(config, CancellationToken.None);
            result.Allowed.Should().BeFalse();
            result.BlockedModules.Should().Contain("source.focas2");
        }
    }

    [Fact]
    public async Task PerModuleInstanceLimit_Exceeded_Blocked()
    {
        var (mgr, gate) = await ManagerAtAsync(
            now: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            expires: new DateTime(2027, 4, 7),
            modules: new Dictionary<string, (bool, int?)>(StringComparer.Ordinal)
            {
                ["source.focas2"] = (true, 1),
                ["sink.mqtt"] = (true, null),
            });
        using (mgr)
        {
            var config = B2TestFixtures.ValidMinimal() with
            {
                Sources =
                [
                    new SourceInstanceConfig { InstanceId = "src-1", ProtocolName = "focas2", DeviceId = "D1" },
                    new SourceInstanceConfig { InstanceId = "src-2", ProtocolName = "focas2", DeviceId = "D2" },
                ],
            };
            var result = await gate.EvaluateAsync(config, CancellationToken.None);
            result.Allowed.Should().BeFalse();
        }
    }

    [Fact]
    public async Task GraceExhausted_BlocksAllChanges()
    {
        var (mgr, gate) = await ManagerAtAsync(
            now: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            expires: new DateTime(2026, 4, 7));
        using (mgr)
        {
            // Roll forward past grace and re-tick.
            // We rebuild the manager because the clock is captured.
            var validator = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
            using var expiredMgr = new LicenseManager(
                validator,
                LicenseEnforcementPolicy.Default,
                () => new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2026, 4, 7));
            await expiredMgr.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);
            expiredMgr.Status.Should().Be(LicenseStatus.Expired);

            var expiredGate = new LicenseGate(expiredMgr);
            var result = await expiredGate.EvaluateAsync(B2TestFixtures.ValidMinimal(), CancellationToken.None);
            result.Allowed.Should().BeFalse();
        }
    }

    [Fact]
    public async Task InGracePeriod_AllowsButWarns()
    {
        var validator = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        using var mgr = new LicenseManager(
            validator,
            LicenseEnforcementPolicy.Default,
            () => new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc));
        var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2026, 4, 7));
        await mgr.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);
        mgr.Status.Should().Be(LicenseStatus.InGracePeriod);

        var gate = new LicenseGate(mgr);
        var result = await gate.EvaluateAsync(B2TestFixtures.ValidMinimal(), CancellationToken.None);
        result.Allowed.Should().BeTrue();
        result.Warnings.Should().NotBeEmpty();
    }

    /// <summary>R5 pin: unlicensed sink module must be blocked (symmetric to source).</summary>
    [Fact]
    public async Task UnlicensedSinkModule_Blocked()
    {
        var (mgr, gate) = await ManagerAtAsync(
            now: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            expires: new DateTime(2027, 4, 7),
            modules: new Dictionary<string, (bool, int?)>(StringComparer.Ordinal)
            {
                ["source.focas2"] = (true, 20),
                ["sink.http"] = (true, null),
                // sink.mqtt deliberately omitted
            });
        using (mgr)
        {
            var config = B2TestFixtures.ValidMinimal() with
            {
                Sinks =
                [
                    new SinkInstanceConfig { InstanceId = "sink-1", ProtocolName = "mqtt" },
                ],
            };
            var result = await gate.EvaluateAsync(config, CancellationToken.None);
            result.Allowed.Should().BeFalse();
            result.BlockedModules.Should().Contain("sink.mqtt");
        }
    }

    /// <summary>
    /// R5 pin: once grace is exhausted, even an empty configuration must be
    /// blocked. Matches blueprint §7.4 "After grace: config locked."
    /// </summary>
    [Fact]
    public async Task GraceExhausted_BlocksEvenEmptyConfig()
    {
        var validator = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        using var mgr = new LicenseManager(
            validator,
            LicenseEnforcementPolicy.Default,
            () => new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2026, 4, 7));
        await mgr.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);
        mgr.Status.Should().Be(LicenseStatus.Expired);

        var gate = new LicenseGate(mgr);
        var emptyConfig = B2TestFixtures.ValidMinimal() with
        {
            Sources = [],
            Sinks = [],
            Routes = [],
        };
        var result = await gate.EvaluateAsync(emptyConfig, CancellationToken.None);
        result.Allowed.Should().BeFalse();
    }

    /// <summary>
    /// R7 pin: SourceModuleKey lowercases via InvariantCulture so an uppercase
    /// ProtocolName in a config still matches the lowercase module key in the
    /// license. (Protects against a future regex relaxation.)
    /// </summary>
    [Fact]
    public void SourceModuleKey_LowercasesProtocolName()
    {
        LicenseGate.SourceModuleKey("FOCAS2").Should().Be("source.focas2");
        LicenseGate.SourceModuleKey("Modbus").Should().Be("source.modbus");
        LicenseGate.SinkModuleKey("MQTT").Should().Be("sink.mqtt");
    }

    [Fact]
    public async Task GlobalSourceLimit_Exceeded_Blocked()
    {
        var (mgr, gate) = await ManagerAtAsync(
            now: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            expires: new DateTime(2027, 4, 7),
            modules: new Dictionary<string, (bool, int?)>(StringComparer.Ordinal)
            {
                ["source.focas2"] = (true, null),
                ["sink.mqtt"] = (true, null),
            });
        using (mgr)
        {
            // ValidMinimal has 1 source; lower the global cap to 0 by rebuilding.
            var validator2 = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
            using var tightMgr = new LicenseManager(
                validator2,
                LicenseEnforcementPolicy.Default,
                () => new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
            var json = B3TestFixtures.BuildSignedLicense(
                expires: new DateTime(2027, 4, 7),
                maxSources: 0);
            await tightMgr.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);

            var tightGate = new LicenseGate(tightMgr);
            var result = await tightGate.EvaluateAsync(B2TestFixtures.ValidMinimal(), CancellationToken.None);
            result.Allowed.Should().BeFalse();
        }
    }
}
