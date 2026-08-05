// ============================================================================
// File: Configuration/CrossRecordValidatorTests.cs
// Covers: Each of the 10 cross-record rules from B2 assumption 3.
//
// One test per rule, plus a few combined cases. Each test is named after
// the rule number for easy traceability.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class CrossRecordValidatorTests
{
    private static readonly CrossRecordValidator Validator = CrossRecordValidator.Instance;

    [Fact]
    public void HappyPath_ValidConfig_ReturnsSuccess()
    {
        var result = Validator.Validate(B2TestFixtures.ValidWithMultiple());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // Rule 1: route source must exist
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule1_RouteSourceMissing_Rejected()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "missing-source",
                    SinkInstanceIds = ["sink-1"],
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.RouteSourceNotFound);
    }

    // ------------------------------------------------------------------------
    // Rule 2: route sinks must exist
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule2_RouteSinkMissing_Rejected()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["missing-sink"],
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.RouteSinkNotFound);
    }

    // ------------------------------------------------------------------------
    // Rule 3: route must have at least one sink
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule3_EmptySinkList_Rejected()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = [],
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.RouteNoSinks);
    }

    // ------------------------------------------------------------------------
    // Rule 4: duplicate source ids
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule4_DuplicateSourceIds_Rejected()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Sources =
            [
                new SourceInstanceConfig { InstanceId = "src-1", ProtocolName = "focas2", DeviceId = "Dev-1" },
                new SourceInstanceConfig { InstanceId = "src-1", ProtocolName = "modbus", DeviceId = "Dev-2" },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.ConfigDuplicateSourceId);
    }

    [Fact]
    public void Rule4_TripleDuplicateSourceIds_StillCaughtOnce()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Sources =
            [
                new SourceInstanceConfig { InstanceId = "src-1", ProtocolName = "focas2", DeviceId = "A" },
                new SourceInstanceConfig { InstanceId = "src-1", ProtocolName = "modbus", DeviceId = "B" },
                new SourceInstanceConfig { InstanceId = "src-1", ProtocolName = "mtlinki", DeviceId = "C" },
            ],
        };

        var result = Validator.Validate(config);

        result.Errors.Should().Contain(e => e.Code == CoreErrors.ConfigDuplicateSourceId);
    }

    // ------------------------------------------------------------------------
    // Rule 5: duplicate sink ids
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule5_DuplicateSinkIds_Rejected()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Sinks =
            [
                new SinkInstanceConfig { InstanceId = "sink-1", ProtocolName = "mqtt" },
                new SinkInstanceConfig { InstanceId = "sink-1", ProtocolName = "http" },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.ConfigDuplicateSinkId);
    }

    // ------------------------------------------------------------------------
    // Rule 6: duplicate route ids
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule6_DuplicateRouteIds_Rejected()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "route-1",
                    Name = "A",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                },
                new RouteConfig
                {
                    RouteId = "route-1",
                    Name = "B",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.ConfigDuplicateRouteId);
    }

    // ------------------------------------------------------------------------
    // Rule 9: source and sink with the same id are PERMITTED (different namespaces)
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule9_SourceAndSinkSharingId_Permitted()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Sources = [new SourceInstanceConfig { InstanceId = "shared", ProtocolName = "focas2", DeviceId = "D" }],
            Sinks = [new SinkInstanceConfig { InstanceId = "shared", ProtocolName = "mqtt" }],
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "shared",
                    SinkInstanceIds = ["shared"],
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------------
    // Rule 7: AtMostOnce requires BufferMode.None
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule7_AtMostOnceWithStoreAndForward_Rejected()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                    Delivery = new DeliveryPolicyConfig { Mode = DeliveryMode.AtMostOnce },
                    Buffer = new BufferPolicyConfig { Mode = BufferMode.StoreAndForward },
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.RouteAtMostOnceRequiresNoBuffer);
    }

    [Fact]
    public void Rule7_AtMostOnceWithNoBuffer_Accepted()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                    Delivery = new DeliveryPolicyConfig { Mode = DeliveryMode.AtMostOnce },
                    Buffer = new BufferPolicyConfig { Mode = BufferMode.None },
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------------
    // Rule 8: StoreAndForward requires AtLeastOnce
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule8_StoreAndForwardWithAtMostOnce_Rejected()
    {
        // Same condition as rule 7 but checked from the other side. Both
        // codes should appear because the rules are independent.
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                    Delivery = new DeliveryPolicyConfig { Mode = DeliveryMode.AtMostOnce },
                    Buffer = new BufferPolicyConfig { Mode = BufferMode.StoreAndForward },
                },
            ],
        };

        var result = Validator.Validate(config);

        result.Errors.Should().Contain(e => e.Code == CoreErrors.RouteStoreAndForwardRequiresAtLeastOnce);
    }

    [Fact]
    public void Rule8_StoreAndForwardWithAtLeastOnce_Accepted()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                    Delivery = new DeliveryPolicyConfig { Mode = DeliveryMode.AtLeastOnce },
                    Buffer = new BufferPolicyConfig { Mode = BufferMode.StoreAndForward },
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------------
    // Rule 10: tag mapping targets must be non-empty
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule10_EmptyTagMappingTarget_Rejected()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                    Transforms = new TransformProfileConfig
                    {
                        TagMapping = new Dictionary<string, string>
                        {
                            ["validSource"] = "valid.target",
                            ["badSource"] = "  ",
                        },
                    },
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.ConfigInvalidTagMapping);
    }

    // ------------------------------------------------------------------------
    // C1 rules 11-14: transform profile sanity (C1 close-out pins for
    // mutations 21 and 22 from the independent review).
    // ------------------------------------------------------------------------

    private static GatewayConfiguration WithTransforms(TransformProfileConfig profile) =>
        B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                    Transforms = profile,
                },
            ],
        };

    [Fact]
    public void Rule11_AbsoluteDeadband_Negative_Rejected()
    {
        var config = WithTransforms(new TransformProfileConfig
        {
            Deadband = new Dictionary<string, double> { ["t"] = -5 },
        });

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.PipelineInvalidDeadbandThreshold);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rule11_AbsoluteDeadband_NonFinite_Rejected(double value)
    {
        var config = WithTransforms(new TransformProfileConfig
        {
            Deadband = new Dictionary<string, double> { ["t"] = value },
        });

        Validator.Validate(config).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rule11_AbsoluteDeadband_Zero_Accepted()
    {
        // Zero is allowed — a zero-threshold deadband is a valid
        // "emit every change" configuration.
        var config = WithTransforms(new TransformProfileConfig
        {
            Deadband = new Dictionary<string, double> { ["t"] = 0 },
        });

        Validator.Validate(config).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Rule12_PercentDeadband_OutOfRange_Rejected(double value)
    {
        var config = WithTransforms(new TransformProfileConfig
        {
            DeadbandPercent = new Dictionary<string, double> { ["t"] = value },
        });

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.PipelineInvalidDeadbandThreshold);
    }

    [Fact]
    public void Rule12_PercentDeadband_ExactlyOne_Accepted()
    {
        // Pin the upper bound: 1.0 must be accepted (closed interval).
        var config = WithTransforms(new TransformProfileConfig
        {
            DeadbandPercent = new Dictionary<string, double> { ["t"] = 1.0 },
        });

        Validator.Validate(config).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rule13_DeadbandDualMode_Conflict_Rejected()
    {
        var config = WithTransforms(new TransformProfileConfig
        {
            Deadband = new Dictionary<string, double> { ["t"] = 5 },
            DeadbandPercent = new Dictionary<string, double> { ["t"] = 0.05 },
        });

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.PipelineDeadbandConflict);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rule14_RateLimit_NotStrictlyPositive_Rejected(int ms)
    {
        var config = WithTransforms(new TransformProfileConfig
        {
            RateLimitMs = new Dictionary<string, int> { ["t"] = ms },
        });

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.PipelineInvalidRateLimit);
    }

    // ------------------------------------------------------------------------
    // C3 commit 1 — rule 12: delivery policy field consistency
    // ------------------------------------------------------------------------

    private static GatewayConfiguration WithDeliveryPolicy(DeliveryPolicyConfig delivery) =>
        B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                    Delivery = delivery,
                },
            ],
        };

    [Fact]
    public void Rule12_MaxBackoffLessThanInitial_Rejected()
    {
        var config = WithDeliveryPolicy(new DeliveryPolicyConfig
        {
            InitialBackoffMs = 500,
            MaxBackoffMs = 100, // < InitialBackoffMs
        });

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.RouteBackoffRangeInvalid);
    }

    [Fact]
    public void Rule12_MaxBackoffEqualToInitial_Accepted()
    {
        // Equal is valid: a constant backoff with no exponential growth.
        var config = WithDeliveryPolicy(new DeliveryPolicyConfig
        {
            InitialBackoffMs = 500,
            MaxBackoffMs = 500,
        });

        Validator.Validate(config).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rule12_MaxBackoffGreaterThanInitial_Accepted()
    {
        var config = WithDeliveryPolicy(new DeliveryPolicyConfig
        {
            InitialBackoffMs = 100,
            MaxBackoffMs = 30_000,
        });

        Validator.Validate(config).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rule12_DefaultDeliveryPolicy_IsValid()
    {
        // The locked C3 defaults must validate clean.
        var config = WithDeliveryPolicy(new DeliveryPolicyConfig());

        var result = Validator.Validate(config);

        result.IsValid.Should().BeTrue(
            "the locked default DeliveryPolicyConfig (5 retries / 100ms initial / 30s max / 2.0× / 10% jitter) must pass validation as the default policy.");
    }

    [Fact]
    public void NullConfiguration_ReturnsFailureWithoutThrowing()
    {
        var result = Validator.Validate(null!);

        result.IsValid.Should().BeFalse();
    }
}
