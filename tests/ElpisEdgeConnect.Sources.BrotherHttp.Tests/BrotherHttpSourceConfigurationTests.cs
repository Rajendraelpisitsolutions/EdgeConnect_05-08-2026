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

    // ── BaseUrl normalization ────────────────────────────────────────────

    [Theory]
    [InlineData("192.168.5.25", "http://192.168.5.25")]                 // bare IP — the reported defect
    [InlineData("cnc-line-a", "http://cnc-line-a")]                     // bare hostname
    [InlineData("192.168.5.25:8080", "http://192.168.5.25:8080")]       // bare host:port
    [InlineData("cnc-line-a:8080", "http://cnc-line-a:8080")]           // parses as scheme "cnc-line-a" without the guard
    [InlineData("  192.168.5.25  ", "http://192.168.5.25")]             // surrounding whitespace
    public void TryNormalizeBaseUrl_BareAddress_PrependsHttp(string input, string expected)
    {
        var result = BrotherHttpSourceConfiguration.TryNormalizeBaseUrl(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("http://192.168.2.110", "http://192.168.2.110")]
    [InlineData("http://192.168.2.110/", "http://192.168.2.110")]
    [InlineData("http://192.168.2.110:8080", "http://192.168.2.110:8080")]
    [InlineData("HTTP://CNC.LOCAL/", "http://cnc.local")]
    public void TryNormalizeBaseUrl_AlreadyHttp_KeepsScheme(string input, string expected)
    {
        var result = BrotherHttpSourceConfiguration.TryNormalizeBaseUrl(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://192.168.2.110", "https://192.168.2.110")]
    [InlineData("https://cnc.local:8443/", "https://cnc.local:8443")]
    public void TryNormalizeBaseUrl_AlreadyHttps_IsNotDowngraded(string input, string expected)
    {
        var result = BrotherHttpSourceConfiguration.TryNormalizeBaseUrl(input);

        result.Should().Be(expected, "an explicit https:// is the operator's choice, not a typo");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalizeBaseUrl_Blank_ReturnsNull(string? input)
    {
        var result = BrotherHttpSourceConfiguration.TryNormalizeBaseUrl(input);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("http://")]        // scheme present, no host
    [InlineData("[bad")]           // still unparseable once http:// is implied
    [InlineData("http://[bad")]    // unterminated IPv6 literal
    [InlineData("ftp://cnc")]      // parseable, but Brother is HTTP only
    public void TryNormalizeBaseUrl_Unusable_ReturnsNull(string input)
    {
        var result = BrotherHttpSourceConfiguration.TryNormalizeBaseUrl(input);

        result.Should().BeNull();
    }

    [Fact]
    public void TryNormalizeBaseUrl_PathAndCasing_PreservesPathCase()
    {
        // Brother endpoint paths are case-sensitive on most firmware, so only
        // the host is lowercased.
        var result = BrotherHttpSourceConfiguration.TryNormalizeBaseUrl("CNC.LOCAL/Sub/Path/");

        result.Should().Be("http://cnc.local/Sub/Path");
    }

    [Fact]
    public void InvalidBaseUrlMessage_NamesAcceptedForms_AndQuotesWhatWasTyped()
    {
        var message = BrotherHttpSourceConfiguration.InvalidBaseUrlMessage("[bad");

        message.Should().Contain("[bad");
        message.Should().Contain("192.168.2.110", "the operator needs an example of the accepted form");
        message.Should().NotContain("absolute URI", "developer vocabulary has no place in an operator error");
    }

    [Fact]
    public void InvalidBaseUrlMessage_Blank_SaysTheFieldIsRequired()
    {
        var message = BrotherHttpSourceConfiguration.InvalidBaseUrlMessage("   ");

        message.Should().Contain("required");
    }

    [Fact]
    public void FromSourceInstance_BareIpInConfig_IsNormalizedToHttpUrl()
    {
        // A hand-edited gateway.json or a bulk CSV import must get the same
        // treatment as the Studio wizard — the fix lives in this factory, so
        // every writer benefits.
        var instance = BuildSourceInstance(connectionJson: """
        { "baseUrl": "192.168.5.25" }
        """);

        var config = BrotherHttpSourceConfiguration.FromSourceInstance(instance);

        config.BaseUrl.Should().Be("http://192.168.5.25");
    }

    [Fact]
    public void FromSourceInstance_UnusableBaseUrl_IsPreservedVerbatimForValidation()
    {
        var instance = BuildSourceInstance(connectionJson: """
        { "baseUrl": "[bad" }
        """);

        var config = BrotherHttpSourceConfiguration.FromSourceInstance(instance);

        config.BaseUrl.Should().Be("[bad",
            "downstream validation quotes what the operator actually typed");
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
