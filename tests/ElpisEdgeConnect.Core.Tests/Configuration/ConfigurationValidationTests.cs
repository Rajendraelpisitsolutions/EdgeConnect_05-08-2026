// ============================================================================
// File: Configuration/ConfigurationValidationTests.cs
// Covers: B1 DataAnnotations attributes on configuration records.
//
// SCOPE NOTE (per Q4 decision): these tests use Validator.TryValidateObject
// on individual records ONLY. They do NOT walk nested records and they do
// NOT prototype the recursive ConfigurationValidator that B2 will build.
// Each record is validated in isolation against its own attributes.
// ============================================================================

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class ConfigurationValidationTests
{
    /// <summary>
    /// Wrapper around <see cref="Validator.TryValidateObject"/> that returns
    /// the full results list. <c>validateAllProperties: true</c> ensures
    /// every property's attributes fire, not just <see cref="RequiredAttribute"/>.
    /// </summary>
    private static (bool IsValid, IList<ValidationResult> Results) Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(instance);
        var isValid = Validator.TryValidateObject(instance, context, results, validateAllProperties: true);
        return (isValid, results);
    }

    // ========================================================================
    // GatewaySettings — Required, Range, RegularExpression
    // ========================================================================

    [Fact]
    public void GatewaySettings_AllRequiredFields_Valid()
    {
        var settings = new GatewaySettings
        {
            GatewayId = "GW-TEST-001",
            GatewayName = "Test Gateway",
        };

        var (isValid, results) = Validate(settings);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void GatewaySettings_EmptyGatewayId_Invalid()
    {
        var settings = new GatewaySettings
        {
            GatewayId = "",
            GatewayName = "Test Gateway",
        };

        var (isValid, results) = Validate(settings);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(GatewaySettings.GatewayId)));
    }

    [Theory]
    [InlineData("invalid id with spaces")]
    [InlineData("-leading-hyphen")]
    [InlineData("contains/slash")]
    [InlineData("special!chars")]
    public void GatewaySettings_InvalidGatewayIdPattern_Invalid(string badId)
    {
        var settings = new GatewaySettings
        {
            GatewayId = badId,
            GatewayName = "Test",
        };

        var (isValid, results) = Validate(settings);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(GatewaySettings.GatewayId)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void GatewaySettings_InvalidHealthCheckPort_Invalid(int port)
    {
        var settings = new GatewaySettings
        {
            GatewayId = "GW-1",
            GatewayName = "Test",
            HealthCheckPort = port,
        };

        var (isValid, results) = Validate(settings);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(GatewaySettings.HealthCheckPort)));
    }

    [Theory]
    [InlineData("verbose")]
    [InlineData("INFORMATION")]
    [InlineData("Info")]
    [InlineData("information")] // lowercase: pins case-sensitivity (mutation #2 from B1 review)
    [InlineData("warning")]     // lowercase: same
    [InlineData("DEBUG")]       // uppercase: same
    public void GatewaySettings_InvalidLogLevel_Invalid(string badLevel)
    {
        var settings = new GatewaySettings
        {
            GatewayId = "GW-1",
            GatewayName = "Test",
            LogLevel = badLevel,
        };

        var (isValid, results) = Validate(settings);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(GatewaySettings.LogLevel)));
    }

    [Theory]
    [InlineData("Trace")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Critical")]
    [InlineData("None")]
    public void GatewaySettings_AllValidLogLevels_Accepted(string level)
    {
        var settings = new GatewaySettings
        {
            GatewayId = "GW-1",
            GatewayName = "Test",
            LogLevel = level,
        };

        var (isValid, _) = Validate(settings);

        isValid.Should().BeTrue();
    }

    // ========================================================================
    // SourceInstanceConfig — Required, RegularExpression
    // ========================================================================

    [Fact]
    public void SourceInstanceConfig_HappyPath_Valid()
    {
        var source = new SourceInstanceConfig
        {
            InstanceId = "focas-jyoti17",
            ProtocolName = "focas2",
            DeviceId = "Jyoti17CNC",
        };

        var (isValid, _) = Validate(source);

        isValid.Should().BeTrue();
    }

    [Theory]
    // Pin the SourceInstanceConfig.InstanceId regex `^[A-Za-z0-9][A-Za-z0-9._-]*$`
    // by exercising every legal character class explicitly. Without these
    // positive cases, tightening the regex (e.g., to disallow underscores
    // or dots) would slip through review — mutation #5 from the B1 review.
    [InlineData("focas-jyoti17")]      // hyphen
    [InlineData("focas_jyoti17")]      // underscore
    [InlineData("focas.jyoti.17")]     // dots
    [InlineData("Mixed-Case_id.1")]    // every legal character class together
    [InlineData("0starting-with-digit")] // digit start
    [InlineData("a")]                   // single character
    public void SourceInstanceConfig_LegalInstanceIdCharacters_Accepted(string instanceId)
    {
        var source = new SourceInstanceConfig
        {
            InstanceId = instanceId,
            ProtocolName = "focas2",
            DeviceId = "dev-1",
        };

        var (isValid, results) = Validate(source);

        isValid.Should().BeTrue($"InstanceId '{instanceId}' must be accepted; {results.Count} errors");
    }

    [Theory]
    // Apply the same coverage to DeviceId, which uses the same regex as
    // InstanceId. The two fields share a regex pattern but the test
    // exercises them independently to catch a regression where one is
    // tightened without the other.
    [InlineData("Jyoti_17_CNC")]
    [InlineData("Device.Name.With.Dots")]
    [InlineData("dev-1")]
    public void SourceInstanceConfig_LegalDeviceIdCharacters_Accepted(string deviceId)
    {
        var source = new SourceInstanceConfig
        {
            InstanceId = "src-1",
            ProtocolName = "focas2",
            DeviceId = deviceId,
        };

        var (isValid, _) = Validate(source);

        isValid.Should().BeTrue($"DeviceId '{deviceId}' must be accepted");
    }

    [Theory]
    // Same coverage for RouteConfig.RouteId.
    [InlineData("route_with_underscores")]
    [InlineData("route.with.dots")]
    [InlineData("Mixed-Case_route.1")]
    public void RouteConfig_LegalRouteIdCharacters_Accepted(string routeId)
    {
        var route = new RouteConfig
        {
            RouteId = routeId,
            Name = "test",
            SourceInstanceId = "src",
            SinkInstanceIds = new[] { "sink" },
        };

        var (isValid, _) = Validate(route);

        isValid.Should().BeTrue($"RouteId '{routeId}' must be accepted");
    }

    [Theory]
    [InlineData("UPPERCASE-NOT-ALLOWED")]
    [InlineData("123-numeric-start-only-ok")]  // actually valid: starts with digit
    [InlineData("trailing space ")]
    [InlineData("special$char")]
    public void SourceInstanceConfig_VariousProtocolNamePatterns(string protocolName)
    {
        // ProtocolName must be lowercase letter start, lowercase + digit + hyphen.
        // The 123- variant is valid for InstanceId/DeviceId but NOT for ProtocolName
        // because ProtocolName requires a leading lowercase letter.
        var source = new SourceInstanceConfig
        {
            InstanceId = "src-1",
            ProtocolName = protocolName,
            DeviceId = "dev-1",
        };

        var (isValid, results) = Validate(source);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SourceInstanceConfig.ProtocolName)));
    }

    [Fact]
    public void SourceInstanceConfig_EmptyDeviceId_Invalid()
    {
        var source = new SourceInstanceConfig
        {
            InstanceId = "src-1",
            ProtocolName = "focas2",
            DeviceId = "",
        };

        var (isValid, results) = Validate(source);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SourceInstanceConfig.DeviceId)));
    }

    // ========================================================================
    // SinkInstanceConfig — Required, RegularExpression
    // ========================================================================

    [Fact]
    public void SinkInstanceConfig_HappyPath_Valid()
    {
        var sink = new SinkInstanceConfig
        {
            InstanceId = "mqtt-eremos-main",
            ProtocolName = "mqtt",
        };

        var (isValid, _) = Validate(sink);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void SinkInstanceConfig_UppercaseProtocolName_Invalid()
    {
        var sink = new SinkInstanceConfig
        {
            InstanceId = "mqtt-1",
            ProtocolName = "MQTT",
        };

        var (isValid, results) = Validate(sink);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SinkInstanceConfig.ProtocolName)));
    }

    // ========================================================================
    // RouteConfig — Required
    // ========================================================================

    [Fact]
    public void RouteConfig_HappyPath_Valid()
    {
        var route = new RouteConfig
        {
            RouteId = "test-route",
            Name = "Test Route",
            SourceInstanceId = "src-1",
            SinkInstanceIds = new[] { "sink-1" },
        };

        var (isValid, _) = Validate(route);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void RouteConfig_EmptyName_Invalid()
    {
        var route = new RouteConfig
        {
            RouteId = "r",
            Name = "",
            SourceInstanceId = "src",
            SinkInstanceIds = new[] { "sink" },
        };

        var (isValid, results) = Validate(route);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(RouteConfig.Name)));
    }

    // ========================================================================
    // BufferPolicyConfig — Range
    // ========================================================================

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void BufferPolicyConfig_NegativeMaxDepth_Invalid(int depth)
    {
        var policy = new BufferPolicyConfig { MaxDepth = depth };

        var (isValid, results) = Validate(policy);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(BufferPolicyConfig.MaxDepth)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3651)]
    public void BufferPolicyConfig_OutOfRangeMaxAgeDays_Invalid(int days)
    {
        var policy = new BufferPolicyConfig { MaxAgeDays = days };

        var (isValid, results) = Validate(policy);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(BufferPolicyConfig.MaxAgeDays)));
    }

    [Theory]
    [InlineData(0)]      // lower bound (no age cap)
    [InlineData(1)]      // minimum meaningful retention
    [InlineData(7)]      // blueprint §8.1 sample default
    [InlineData(3650)]   // upper bound (mutation #1 from B1 review — pins the limit)
    public void BufferPolicyConfig_MaxAgeDaysAtAndWithinRange_Valid(int days)
    {
        var policy = new BufferPolicyConfig { MaxAgeDays = days };

        var (isValid, _) = Validate(policy);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void BufferPolicyConfig_DefaultsAreValid()
    {
        var policy = new BufferPolicyConfig();

        var (isValid, _) = Validate(policy);

        isValid.Should().BeTrue();
    }

    // ========================================================================
    // DeliveryPolicyConfig — Range
    // ========================================================================

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void DeliveryPolicyConfig_OutOfRangeMaxRetries_Invalid(int retries)
    {
        var policy = new DeliveryPolicyConfig { MaxRetries = retries };

        var (isValid, results) = Validate(policy);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(DeliveryPolicyConfig.MaxRetries)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void DeliveryPolicyConfig_NonPositiveBackoff_Invalid(int backoffMs)
    {
        var policy = new DeliveryPolicyConfig { InitialBackoffMs = backoffMs };

        var (isValid, results) = Validate(policy);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(DeliveryPolicyConfig.InitialBackoffMs)));
    }

    // ========================================================================
    // PollingSettings + PublishingSettings — Range
    // ========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PollingSettings_NonPositiveInterval_Invalid(int intervalMs)
    {
        var polling = new PollingSettings { IntervalMs = intervalMs };

        var (isValid, results) = Validate(polling);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(PollingSettings.IntervalMs)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100_001)]
    public void PublishingSettings_OutOfRangeBatchSize_Invalid(int batchSize)
    {
        var pub = new PublishingSettings { BatchSize = batchSize };

        var (isValid, results) = Validate(pub);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(PublishingSettings.BatchSize)));
    }

    [Fact]
    public void PublishingSettings_Defaults_Valid()
    {
        var pub = new PublishingSettings();

        var (isValid, _) = Validate(pub);

        isValid.Should().BeTrue();
    }

    // ========================================================================
    // ManagementApiSettings — Range
    // ========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void ManagementApiSettings_OutOfRangePort_Invalid(int port)
    {
        var api = new ManagementApiSettings { Port = port };

        var (isValid, results) = Validate(api);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ManagementApiSettings.Port)));
    }
}
