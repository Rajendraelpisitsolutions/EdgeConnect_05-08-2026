// ============================================================================
// File: Model/CanonicalDataPointBuilderTests.cs
// Covers: builder fluent API, required-field validation, WithGoodQuality helper.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Model;

public sealed class CanonicalDataPointBuilderTests
{
    private static CanonicalDataPointBuilder ValidBuilder() =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST-001")
            .WithSource("focas-jyoti17", "focas2")
            .WithDevice("Jyoti17CNC", "Jyoti 17 CNC")
            .WithTag("spindle.speed", "Spindle/Speed")
            .WithValue(3500.0, CanonicalValueType.Double)
            .WithUnit("rpm")
            .WithGoodQuality(DateTime.UtcNow)
            .WithSequence(1);

    [Fact]
    public void Build_WithAllRequiredFields_Succeeds()
    {
        var point = ValidBuilder().Build();

        point.GatewayId.Should().Be("GW-TEST-001");
        point.SourceInstanceId.Should().Be("focas-jyoti17");
        point.ProtocolName.Should().Be("focas2");
        point.DeviceId.Should().Be("Jyoti17CNC");
        point.DeviceName.Should().Be("Jyoti 17 CNC");
        point.TagName.Should().Be("spindle.speed");
        point.TagPath.Should().Be("Spindle/Speed");
        point.Value.Should().Be(3500.0);
        point.ValueType.Should().Be(CanonicalValueType.Double);
        point.Unit.Should().Be("rpm");
        point.Quality.Should().Be(DataQuality.Good);
        point.SequenceNumber.Should().Be(1);
    }

    [Fact]
    public void Build_WithoutGateway_Throws()
    {
        var builder = new CanonicalDataPointBuilder()
            .WithSource("src", "proto")
            .WithDevice("dev")
            .WithTag("tag", "path")
            .WithValue(1, CanonicalValueType.Integer)
            .WithGoodQuality(DateTime.UtcNow);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GatewayId*");
    }

    [Fact]
    public void Build_WithoutSourceInstance_Throws()
    {
        var builder = new CanonicalDataPointBuilder()
            .WithGateway("gw")
            .WithDevice("dev")
            .WithTag("tag", "path")
            .WithValue(1, CanonicalValueType.Integer)
            .WithGoodQuality(DateTime.UtcNow);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SourceInstanceId*");
    }

    [Fact]
    public void Build_WithoutDevice_Throws()
    {
        var builder = new CanonicalDataPointBuilder()
            .WithGateway("gw")
            .WithSource("src", "proto")
            .WithTag("tag", "path")
            .WithValue(1, CanonicalValueType.Integer)
            .WithGoodQuality(DateTime.UtcNow);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DeviceId*");
    }

    [Fact]
    public void Build_WithoutTag_Throws()
    {
        var builder = new CanonicalDataPointBuilder()
            .WithGateway("gw")
            .WithSource("src", "proto")
            .WithDevice("dev")
            .WithValue(1, CanonicalValueType.Integer)
            .WithGoodQuality(DateTime.UtcNow);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TagName*");
    }

    [Fact]
    public void Build_WithoutTimestamps_Throws()
    {
        // When neither timestamp is set, Build() checks DeviceTimestamp first
        // and throws on that. Pinning the specific message prevents the test
        // from silently passing if the device-vs-gateway distinction is broken.
        var builder = new CanonicalDataPointBuilder()
            .WithGateway("gw")
            .WithSource("src", "proto")
            .WithDevice("dev")
            .WithTag("tag", "path")
            .WithValue(1, CanonicalValueType.Integer)
            .WithQuality(DataQuality.Good);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DeviceTimestamp*");
    }

    // ========================================================================
    // Coverage of individually reachable validation branches.
    //
    // Without these tests, deleting the corresponding `if` in Build() would
    // not be caught by any test, because the existing happy-path-minus-one
    // tests above hit a different validation branch first via the order in
    // which Build() checks fields.
    // ========================================================================

    [Fact]
    public void Build_WithoutTagPath_Throws()
    {
        // TagName set but TagPath empty — pins the _tagPath validation branch
        // independently of the _tagName branch.
        var builder = new CanonicalDataPointBuilder()
            .WithGateway("gw")
            .WithSource("src", "proto")
            .WithDevice("dev")
            .WithTag("tag", string.Empty)
            .WithValue(1, CanonicalValueType.Integer)
            .WithGoodQuality(DateTime.UtcNow);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TagPath*");
    }

    [Fact]
    public void Build_WithoutProtocolName_Throws()
    {
        // SourceInstanceId set but ProtocolName empty — pins the _protocolName
        // validation branch independently of the _sourceInstanceId branch.
        var builder = new CanonicalDataPointBuilder()
            .WithGateway("gw")
            .WithSource("src", string.Empty)
            .WithDevice("dev")
            .WithTag("tag", "path")
            .WithValue(1, CanonicalValueType.Integer)
            .WithGoodQuality(DateTime.UtcNow);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProtocolName*");
    }

    [Fact]
    public void Build_WithoutDeviceTimestamp_Throws()
    {
        // Pin the _deviceTimestamp == default check distinctly from the
        // _gatewayTimestamp check. Setting only the gateway timestamp leaves
        // the device timestamp at its default value, which must be rejected.
        var builder = new CanonicalDataPointBuilder()
            .WithGateway("gw")
            .WithSource("src", "proto")
            .WithDevice("dev")
            .WithTag("tag", "path")
            .WithValue(1, CanonicalValueType.Integer)
            .WithQuality(DataQuality.Good)
            .WithTimestamps(deviceTimestamp: default, gatewayTimestamp: DateTime.UtcNow);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DeviceTimestamp*");
    }

    [Fact]
    public void Build_WithoutGatewayTimestamp_Throws()
    {
        // The complementary case: device timestamp set but gateway timestamp
        // missing. Together with Build_WithoutDeviceTimestamp_Throws this
        // pins both halves of the timestamp validation independently.
        var builder = new CanonicalDataPointBuilder()
            .WithGateway("gw")
            .WithSource("src", "proto")
            .WithDevice("dev")
            .WithTag("tag", "path")
            .WithValue(1, CanonicalValueType.Integer)
            .WithQuality(DataQuality.Good)
            .WithTimestamps(deviceTimestamp: DateTime.UtcNow, gatewayTimestamp: default);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GatewayTimestamp*");
    }

    [Fact]
    public void WithGoodQuality_SetsBothTimestamps()
    {
        var deviceTime = new DateTime(2026, 4, 7, 10, 30, 0, DateTimeKind.Utc);
        var before = DateTime.UtcNow;
        var point = ValidBuilder()
            .WithGoodQuality(deviceTime)
            .Build();
        var after = DateTime.UtcNow;

        point.DeviceTimestamp.Should().Be(deviceTime);
        point.GatewayTimestamp.Should().BeOnOrAfter(before);
        point.GatewayTimestamp.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void WithQuality_SetsQualityAndReason()
    {
        var point = ValidBuilder()
            .WithValue(null, CanonicalValueType.Null)
            .WithQuality(DataQuality.Bad, "device offline")
            .WithTimestamps(DateTime.UtcNow, DateTime.UtcNow)
            .Build();

        point.Quality.Should().Be(DataQuality.Bad);
        point.QualityReason.Should().Be("device offline");
    }
}
