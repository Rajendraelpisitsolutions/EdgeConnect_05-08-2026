// ============================================================================
// File: Configuration/DeliveryBoundaryTests.cs
// Covers: K1.1 — DeliveryAcknowledgementBoundary ordering, DeliveryBoundaryRules
//         (required > available rejected; unknown rejected), and the computed
//         DeliveryCapabilities.SupportsBrokerAcknowledgedAtLeastOnce.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class DeliveryBoundaryTests
{
    [Fact]
    public void Boundary_Enum_Is_Ordered()
    {
        ((int)DeliveryAcknowledgementBoundary.None).Should().Be(0);
        ((int)DeliveryAcknowledgementBoundary.LocalTransport).Should().Be(1);
        ((int)DeliveryAcknowledgementBoundary.Broker).Should().Be(2);
        ((int)DeliveryAcknowledgementBoundary.Application).Should().Be(3);
    }

    [Theory]
    [InlineData(DeliveryAcknowledgementBoundary.LocalTransport, DeliveryAcknowledgementBoundary.LocalTransport, true)]
    [InlineData(DeliveryAcknowledgementBoundary.None, DeliveryAcknowledgementBoundary.LocalTransport, true)]
    [InlineData(DeliveryAcknowledgementBoundary.Broker, DeliveryAcknowledgementBoundary.Application, true)]
    [InlineData(DeliveryAcknowledgementBoundary.Broker, DeliveryAcknowledgementBoundary.LocalTransport, false)]
    [InlineData(DeliveryAcknowledgementBoundary.Application, DeliveryAcknowledgementBoundary.Broker, false)]
    public void RequirementIsMet_Compares_Required_Against_Available(
        DeliveryAcknowledgementBoundary required, DeliveryAcknowledgementBoundary available, bool expected)
    {
        DeliveryBoundaryRules.RequirementIsMet(required, available).Should().Be(expected);
    }

    [Fact]
    public void RequirementIsMet_Rejects_Unknown_Value_Before_Comparing()
    {
        var unknown = (DeliveryAcknowledgementBoundary)99;
        var act = () => DeliveryBoundaryRules.RequirementIsMet(unknown, DeliveryAcknowledgementBoundary.Broker);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(DeliveryAcknowledgementBoundary.None, false)]
    [InlineData(DeliveryAcknowledgementBoundary.LocalTransport, false)]
    [InlineData(DeliveryAcknowledgementBoundary.Broker, true)]
    [InlineData(DeliveryAcknowledgementBoundary.Application, true)]
    public void SupportsBrokerAcknowledgedAtLeastOnce_Is_Computed_From_Boundary(
        DeliveryAcknowledgementBoundary boundary, bool expected)
    {
        var caps = new DeliveryCapabilities { SupportsStoreAndForward = true, AcknowledgementBoundary = boundary };
        caps.SupportsBrokerAcknowledgedAtLeastOnce.Should().Be(expected);
    }

    [Fact]
    public void Sparkplug_Style_Sink_Rejects_A_Broker_Requirement()
    {
        // A LocalTransport (QoS-0) sink cannot satisfy a Broker requirement.
        var sinkBoundary = DeliveryAcknowledgementBoundary.LocalTransport;
        DeliveryBoundaryRules.RequirementIsMet(DeliveryAcknowledgementBoundary.Broker, sinkBoundary).Should().BeFalse();
        DeliveryBoundaryRules.RequirementIsMet(DeliveryAcknowledgementBoundary.LocalTransport, sinkBoundary).Should().BeTrue();
    }

    // ---- Required test: an undefined boundary fails CLOSED for the computed capability ----
    [Fact]
    public void SupportsBrokerAcknowledgedAtLeastOnce_Fails_Closed_On_Undefined_Boundary()
    {
        var caps = new DeliveryCapabilities
        {
            SupportsStoreAndForward = true,
            AcknowledgementBoundary = (DeliveryAcknowledgementBoundary)99,
        };
        caps.SupportsBrokerAcknowledgedAtLeastOnce.Should().BeFalse();
    }

    // ---- Required test: ValidateAgainstSink rejects an unknown boundary (fail closed, typed) ----
    [Fact]
    public void ValidateAgainstSink_Rejects_Unknown_Required_Boundary()
    {
        var policy = new DeliveryPolicyConfig
        {
            Mode = DeliveryMode.AtLeastOnce,
            RequiredAcknowledgementBoundary = (DeliveryAcknowledgementBoundary)99,
        };
        var sink = new DeliveryCapabilities
        {
            SupportsStoreAndForward = true,
            AcknowledgementBoundary = DeliveryAcknowledgementBoundary.LocalTransport,
        };

        var result = DeliveryBoundaryRules.ValidateAgainstSink(policy, sink);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(DeliveryPolicyErrorCode.UnknownAcknowledgementBoundary);
    }

    // ---- Required test: AtMostOnce paired with a positive required boundary is rejected ----
    [Fact]
    public void ValidateAgainstSink_Rejects_AtMostOnce_With_Positive_Requirement()
    {
        var policy = new DeliveryPolicyConfig
        {
            Mode = DeliveryMode.AtMostOnce,
            RequiredAcknowledgementBoundary = DeliveryAcknowledgementBoundary.Broker,
        };
        var sink = new DeliveryCapabilities
        {
            SupportsStoreAndForward = true,
            AcknowledgementBoundary = DeliveryAcknowledgementBoundary.Application,
        };

        var result = DeliveryBoundaryRules.ValidateAgainstSink(policy, sink);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(DeliveryPolicyErrorCode.AtMostOnceRequiresNoBoundary);
    }

    [Fact]
    public void ValidateAgainstSink_Rejects_Requirement_Exceeding_Sink()
    {
        var policy = new DeliveryPolicyConfig
        {
            Mode = DeliveryMode.AtLeastOnce,
            RequiredAcknowledgementBoundary = DeliveryAcknowledgementBoundary.Broker,
        };
        var sink = new DeliveryCapabilities
        {
            SupportsStoreAndForward = true,
            AcknowledgementBoundary = DeliveryAcknowledgementBoundary.LocalTransport,
        };

        var result = DeliveryBoundaryRules.ValidateAgainstSink(policy, sink);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(DeliveryPolicyErrorCode.RequirementExceedsSinkBoundary);
    }

    // ---- Required test 10: an unknown DeliveryMode fails closed ----
    [Fact]
    public void ValidateAgainstSink_Rejects_Unknown_Delivery_Mode()
    {
        var policy = new DeliveryPolicyConfig
        {
            Mode = (DeliveryMode)99,
            RequiredAcknowledgementBoundary = DeliveryAcknowledgementBoundary.None,
        };
        var sink = new DeliveryCapabilities
        {
            SupportsStoreAndForward = true,
            AcknowledgementBoundary = DeliveryAcknowledgementBoundary.Broker,
        };

        var result = DeliveryBoundaryRules.ValidateAgainstSink(policy, sink);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(DeliveryPolicyErrorCode.UnknownDeliveryMode);
    }

    [Fact]
    public void ValidateAgainstSink_Accepts_A_Met_Requirement()
    {
        var policy = new DeliveryPolicyConfig
        {
            Mode = DeliveryMode.AtLeastOnce,
            RequiredAcknowledgementBoundary = DeliveryAcknowledgementBoundary.LocalTransport,
        };
        var sink = new DeliveryCapabilities
        {
            SupportsStoreAndForward = true,
            AcknowledgementBoundary = DeliveryAcknowledgementBoundary.Broker,
        };

        DeliveryBoundaryRules.ValidateAgainstSink(policy, sink).IsValid.Should().BeTrue();
    }
}
