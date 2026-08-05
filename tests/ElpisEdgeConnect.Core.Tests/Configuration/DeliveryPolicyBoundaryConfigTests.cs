// ============================================================================
// File: Configuration/DeliveryPolicyBoundaryConfigTests.cs
// Covers: K1.1 — DeliveryPolicyConfig.RequiredAcknowledgementBoundary legacy
//         missing-field behavior (absent => None, backward-compatible) and
//         explicit round-trip.
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class DeliveryPolicyBoundaryConfigTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Default_Value_Is_None()
    {
        new DeliveryPolicyConfig().RequiredAcknowledgementBoundary
            .Should().Be(DeliveryAcknowledgementBoundary.None);
    }

    [Fact]
    public void Legacy_Config_Without_The_Field_Deserializes_As_None()
    {
        // A configuration written before the field existed must load without failure
        // and retain existing behavior (None).
        var legacy = "{\"Mode\":\"AtLeastOnce\",\"MaxRetries\":5}";
        var config = JsonSerializer.Deserialize<DeliveryPolicyConfig>(legacy, Options)!;

        config.Mode.Should().Be(DeliveryMode.AtLeastOnce);
        config.RequiredAcknowledgementBoundary.Should().Be(DeliveryAcknowledgementBoundary.None);
    }

    [Fact]
    public void Explicit_Boundary_Round_Trips()
    {
        var original = new DeliveryPolicyConfig
        {
            Mode = DeliveryMode.AtLeastOnce,
            RequiredAcknowledgementBoundary = DeliveryAcknowledgementBoundary.LocalTransport,
        };

        var json = JsonSerializer.Serialize(original, Options);
        json.Should().Contain("LocalTransport");

        var restored = JsonSerializer.Deserialize<DeliveryPolicyConfig>(json, Options)!;
        restored.RequiredAcknowledgementBoundary.Should().Be(DeliveryAcknowledgementBoundary.LocalTransport);
    }

    // ---- A policy deserialized (via the serializer, NOT the production draft->validate->apply
    //      loader) with an out-of-range boundary is caught by the typed validation primitive
    //      (fail closed). This deliberately does NOT exercise the real config loader: wiring the
    //      primitive into draft->validate->apply and surfacing the error to the operator is K4.
    //      It asserts only the primitive the future pipeline will call. ----
    [Fact]
    public void Deserialized_Policy_With_Out_Of_Range_Boundary_Is_Rejected_By_The_Validation_Primitive()
    {
        var json = "{\"Mode\":\"AtLeastOnce\",\"RequiredAcknowledgementBoundary\":99}";
        var config = JsonSerializer.Deserialize<DeliveryPolicyConfig>(json, Options)!;
        config.RequiredAcknowledgementBoundary.Should().Be((DeliveryAcknowledgementBoundary)99);

        var sink = new DeliveryCapabilities
        {
            SupportsStoreAndForward = true,
            AcknowledgementBoundary = DeliveryAcknowledgementBoundary.Broker,
        };

        var result = DeliveryBoundaryRules.ValidateAgainstSink(config, sink);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(DeliveryPolicyErrorCode.UnknownAcknowledgementBoundary);
    }
}
