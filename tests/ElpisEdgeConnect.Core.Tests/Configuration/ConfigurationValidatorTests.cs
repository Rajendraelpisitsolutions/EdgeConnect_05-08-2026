// ============================================================================
// File: Configuration/ConfigurationValidatorTests.cs
// Covers: ConfigurationValidator pipeline (DataAnnotations + cross-record + license).
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Licensing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class ConfigurationValidatorTests
{
    private static ConfigurationValidator NewDefault() => new();

    // ========================================================================
    // Happy path
    // ========================================================================

    [Fact]
    public async Task Validate_ValidMinimal_ReturnsSuccess()
    {
        var validator = NewDefault();
        var config = B2TestFixtures.ValidMinimal();

        var result = await validator.ValidateAsync(config, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_ValidWithMultiple_ReturnsSuccess()
    {
        var validator = NewDefault();
        var config = B2TestFixtures.ValidWithMultiple();

        var result = await validator.ValidateAsync(config, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    // ========================================================================
    // DataAnnotations stage
    // ========================================================================

    [Fact]
    public async Task Validate_BadGatewayId_ReportsDataAnnotationsError()
    {
        var validator = NewDefault();
        var config = B2TestFixtures.ValidMinimal() with
        {
            Gateway = new GatewaySettings
            {
                GatewayId = "invalid id with spaces",
                GatewayName = "Test",
            },
        };

        var result = await validator.ValidateAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path != null && e.Path.Contains("GatewayId"));
    }

    [Fact]
    public async Task Validate_DataAnnotations_WalksNestedRecords()
    {
        var validator = NewDefault();
        var config = B2TestFixtures.ValidMinimal() with
        {
            Sources =
            [
                new SourceInstanceConfig
                {
                    InstanceId = "src-1",
                    ProtocolName = "focas2",
                    DeviceId = "Dev-1",
                    Polling = new PollingSettings { IntervalMs = -1 }, // negative — invalid
                },
            ],
        };

        var result = await validator.ValidateAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path != null && e.Path.Contains("IntervalMs"));
    }

    // ========================================================================
    // Cross-record stage
    // ========================================================================

    [Fact]
    public async Task Validate_RouteReferencesMissingSource_ReportsCrossRecordError()
    {
        var validator = NewDefault();
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "route-1",
                    Name = "Test Route",
                    SourceInstanceId = "missing",
                    SinkInstanceIds = ["sink-1"],
                },
            ],
        };

        var result = await validator.ValidateAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.RouteSourceNotFound);
    }

    [Fact]
    public async Task Validate_AggregatesErrorsFromAllStages()
    {
        // A config that fails BOTH a DataAnnotations rule (bad gateway id)
        // AND a cross-record rule (missing sink reference). Both stages
        // should run and both errors should be reported.
        var validator = NewDefault();
        var config = new GatewayConfiguration
        {
            Gateway = new GatewaySettings
            {
                GatewayId = "invalid id with spaces",
                GatewayName = "Test",
            },
            Sources =
            [
                new SourceInstanceConfig { InstanceId = "src-1", ProtocolName = "focas2", DeviceId = "Dev-1" },
            ],
            Sinks = [],
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "route-1",
                    Name = "Test",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["missing-sink"],
                },
            ],
        };

        var result = await validator.ValidateAsync(config, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        // Both stages contributed errors
        result.Errors.Should().Contain(e => e.Path != null && e.Path.Contains("GatewayId"));
        result.Errors.Should().Contain(e => e.Code == CoreErrors.RouteSinkNotFound);
    }

    // ========================================================================
    // License gate stage
    // ========================================================================

    [Fact]
    public async Task Validate_LicenseGateRejects_ReturnsLicenseError()
    {
        var rejectingGate = new RejectingLicenseGate("focas2", "Module focas2 not licensed");
        var validator = new ConfigurationValidator(CrossRecordValidator.Instance, rejectingGate);

        var result = await validator.ValidateAsync(B2TestFixtures.ValidMinimal(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.LicenseModuleDisabled);
    }

    [Fact]
    public async Task Validate_DefaultLicenseGate_PermitsEverything()
    {
        // The parameterless constructor uses AllowAllLicenseGate.
        var validator = new ConfigurationValidator();

        var result = await validator.ValidateAsync(B2TestFixtures.ValidMinimal(), CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    // ========================================================================
    // Null handling
    // ========================================================================

    [Fact]
    public async Task Validate_NullConfiguration_ReturnsFailureNotThrows()
    {
        var validator = NewDefault();

        var result = await validator.ValidateAsync(null!, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.ConfigInvalid);
    }

    // ========================================================================
    // Test gate that always rejects with a fixed module + reason
    // ========================================================================
    private sealed class RejectingLicenseGate : ILicenseGate
    {
        private readonly string _module;
        private readonly string _reason;

        public RejectingLicenseGate(string module, string reason)
        {
            _module = module;
            _reason = reason;
        }

        public ValueTask<LicenseGateResult> EvaluateAsync(GatewayConfiguration configuration, CancellationToken cancellationToken)
        {
            return new ValueTask<LicenseGateResult>(new LicenseGateResult
            {
                Allowed = false,
                BlockedModules = [_module],
                Reasons = [_reason],
            });
        }
    }
}
