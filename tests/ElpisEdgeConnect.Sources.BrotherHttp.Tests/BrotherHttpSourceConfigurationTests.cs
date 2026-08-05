// ============================================================================
// Tests: BrotherHttpSourceConfiguration — FromSourceInstance round-trip,
//        DataPoints normalization per v3.1 §B.6, polling-cadence
//        classification per Q10.
// Reference: src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpSourceConfiguration.cs
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

public sealed class BrotherHttpSourceConfigurationTests
{
    // ── Constants ─────────────────────────────────────────────────────────

    [Fact]
    public void ProtocolNameConstant_IsBrotherHttp()
    {
        BrotherHttpSourceConfiguration.ProtocolNameConstant.Should().Be("brother-http");
    }

    [Fact]
    public void LicenseModuleKey_MatchesCatalogConvention()
    {
        BrotherHttpSourceConfiguration.LicenseModuleKey.Should().Be("source-brother-http");
    }

    // ── FromSourceInstance round-trip ─────────────────────────────────────

    [Fact]
    public void FromSourceInstance_HappyPath_ProducesExpectedConfig()
    {
        var instance = BuildSourceInstance(connectionJson: """
        {
            "baseUrl": "http://192.168.2.110",
            "timeoutSeconds": 7,
            "faultThresholdConsecutiveFailures": 5,
            "initialBackoffMs": 1000,
            "maxBackoffMs": 60000,
            "backoffMultiplier": 1.5,
            "dataPoints": ["MachineInfo/", "Status/State"]
        }
        """);

        var config = BrotherHttpSourceConfiguration.FromSourceInstance(instance);

        config.BaseUrl.Should().Be("http://192.168.2.110");
        config.TimeoutSeconds.Should().Be(7);
        config.FaultThresholdConsecutiveFailures.Should().Be(5);
        config.InitialBackoffMs.Should().Be(1000);
        config.MaxBackoffMs.Should().Be(60000);
        config.BackoffMultiplier.Should().Be(1.5);
        config.DataPoints.Should().BeEquivalentTo(new[] { "MachineInfo/", "Status/State" });

        // Base SourceConfiguration fields propagated
        config.InstanceId.Should().Be("brother-test-01");
        config.ProtocolName.Should().Be("brother-http");
        config.PollIntervalMs.Should().Be(3000);
    }

    [Fact]
    public void FromSourceInstance_MissingBaseUrl_Throws()
    {
        var instance = BuildSourceInstance(connectionJson: """
        {
            "timeoutSeconds": 7
        }
        """);

        Action act = () => BrotherHttpSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*baseUrl*");
    }

    [Fact]
    public void FromSourceInstance_WrongProtocol_Throws()
    {
        var instance = BuildSourceInstance(
            connectionJson: """{ "baseUrl": "http://x" }""",
            protocolOverride: "focas2");

        Action act = () => BrotherHttpSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*'brother-http'*");
    }

    [Fact]
    public void FromSourceInstance_DefaultsApplied_WhenFieldsMissing()
    {
        var instance = BuildSourceInstance(connectionJson: """
        { "baseUrl": "http://x" }
        """);

        var config = BrotherHttpSourceConfiguration.FromSourceInstance(instance);

        config.TimeoutSeconds.Should().Be(10);
        config.FaultThresholdConsecutiveFailures.Should().Be(3);
        config.InitialBackoffMs.Should().Be(5000);
        config.MaxBackoffMs.Should().Be(120_000);
        config.BackoffMultiplier.Should().Be(2.0);
        config.DataPoints.Should().BeEmpty();
    }

    // ── v3.1 §B.6 DataPoints normalization tests (the three locked) ──────

    [Fact]
    public void Normalize_PrefixAndLeafBoth_KeepsPrefix()
    {
        // v3.1 §B.6 rule: prefix wins over leaf.
        var input = new[] { "Tools/", "Tools/ActiveNumber", "Tools/Magazine/01/Number" };

        var result = BrotherHttpSourceConfiguration.NormalizeDataPoints(input);

        // "Tools/" → after trailing-slash strip becomes "Tools". The two
        // longer entries start with "Tools/", so they're dropped.
        result.Should().BeEquivalentTo(new[] { "Tools" });
    }

    [Fact]
    public void Normalize_TrailingSlashVariants_Deduplicate()
    {
        var input = new[] { "Status/", "Status", "Status/" };

        var result = BrotherHttpSourceConfiguration.NormalizeDataPoints(input);

        result.Should().ContainSingle().Which.Should().Be("Status");
    }

    [Fact]
    public void Normalize_UnknownEntry_PassesThrough_ButFailsCatalogCheck()
    {
        // Normalize itself is mechanical (trim/dedup/strip/collapse); it does
        // NOT validate catalog membership. The CALLER runs IsCatalogMember
        // and emits issues for unknowns. Pin both behaviours.
        var input = new[] { "Status/State", "TotallyMadeUp/Path" };

        var result = BrotherHttpSourceConfiguration.NormalizeDataPoints(input);
        result.Should().BeEquivalentTo(new[] { "Status/State", "TotallyMadeUp/Path" });

        BrotherHttpSourceConfiguration.IsCatalogMember("TotallyMadeUp/Path").Should().BeFalse();
        BrotherHttpSourceConfiguration.IsCatalogMember("Status/State").Should().BeTrue();
    }

    [Fact]
    public void Normalize_EmptyAndWhitespaceEntries_AreDropped()
    {
        var input = new[] { "", "   ", "Status/State", " " };

        var result = BrotherHttpSourceConfiguration.NormalizeDataPoints(input);

        result.Should().ContainSingle().Which.Should().Be("Status/State");
    }

    [Fact]
    public void Normalize_CaseInsensitiveDedup_PreservesFirstOccurrenceCasing()
    {
        var input = new[] { "Status/State", "status/state", "STATUS/STATE" };

        var result = BrotherHttpSourceConfiguration.NormalizeDataPoints(input);

        result.Should().ContainSingle().Which.Should().Be("Status/State",
            "first occurrence casing is preserved");
    }

    // ── Q10 polling-cadence classification ────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(499)]
    public void ClassifyPollInterval_BelowHardMinimum_ReturnsTooFast(int ms)
    {
        BrotherHttpSourceConfiguration.ClassifyPollInterval(ms)
            .Should().Be(BrotherHttpSourceConfiguration.PollIntervalClassification.TooFast);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(800)]
    [InlineData(999)]
    public void ClassifyPollInterval_BetweenMinAndWarning_ReturnsWarning(int ms)
    {
        BrotherHttpSourceConfiguration.ClassifyPollInterval(ms)
            .Should().Be(BrotherHttpSourceConfiguration.PollIntervalClassification.Warning);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(3000)]
    [InlineData(60000)]
    public void ClassifyPollInterval_AtOrAboveSoftThreshold_ReturnsAcceptable(int ms)
    {
        BrotherHttpSourceConfiguration.ClassifyPollInterval(ms)
            .Should().Be(BrotherHttpSourceConfiguration.PollIntervalClassification.Acceptable);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static SourceInstanceConfig BuildSourceInstance(
        string connectionJson, string protocolOverride = "brother-http")
    {
        return new SourceInstanceConfig
        {
            InstanceId = "brother-test-01",
            ProtocolName = protocolOverride,
            DeviceId = "Brother-01",
            DeviceName = "Brother SXd1 (test)",
            DeviceClass = "cnc",
            Enabled = true,
            Polling = new PollingSettings { IntervalMs = 3000 },
            Tags = Array.Empty<string>(),
            Connection = JsonDocument.Parse(connectionJson).RootElement.Clone(),
        };
    }
}
