// ============================================================================
// File: Model/CanonicalDataPointFactoryTests.cs
// Covers: Factory identity binding, monotonic sequence generation,
//         thread-safe sequence under concurrent access.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 19.6 (per-source monotonic)
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Model;

public sealed class CanonicalDataPointFactoryTests
{
    private static CanonicalDataPointFactory CreateFactory() =>
        new(
            gatewayId: "GW-TEST-001",
            sourceInstanceId: "focas-jyoti17",
            protocolName: "focas2",
            deviceId: "Jyoti17CNC",
            deviceName: "Jyoti 17 CNC");

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Construction_WithBlankGatewayId_Throws(string blank)
    {
        var act = () => new CanonicalDataPointFactory(blank, "src", "proto", "dev");
        act.Should().Throw<ArgumentException>().WithParameterName("gatewayId");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Construction_WithBlankSourceInstanceId_Throws(string blank)
    {
        var act = () => new CanonicalDataPointFactory("gw", blank, "proto", "dev");
        act.Should().Throw<ArgumentException>().WithParameterName("sourceInstanceId");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Construction_WithBlankProtocolName_Throws(string blank)
    {
        var act = () => new CanonicalDataPointFactory("gw", "src", blank, "dev");
        act.Should().Throw<ArgumentException>().WithParameterName("protocolName");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Construction_WithBlankDeviceId_Throws(string blank)
    {
        var act = () => new CanonicalDataPointFactory("gw", "src", "proto", blank);
        act.Should().Throw<ArgumentException>().WithParameterName("deviceId");
    }

    [Fact]
    public void Identity_ExposedViaProperties()
    {
        var factory = CreateFactory();

        factory.GatewayId.Should().Be("GW-TEST-001");
        factory.SourceInstanceId.Should().Be("focas-jyoti17");
        factory.ProtocolName.Should().Be("focas2");
        factory.LastSequenceNumber.Should().Be(0);
    }

    [Fact]
    public void NextSequence_StartsAtOne()
    {
        var factory = CreateFactory();

        factory.NextSequence().Should().Be(1);
        factory.NextSequence().Should().Be(2);
        factory.NextSequence().Should().Be(3);
        factory.LastSequenceNumber.Should().Be(3);
    }

    [Fact]
    public void NewBuilder_ReturnsBuilderPrepopulatedWithIdentity()
    {
        var factory = CreateFactory();

        var point = factory.NewBuilder()
            .WithTag("spindle.speed", "Spindle/Speed")
            .WithValue(3500.0, CanonicalValueType.Double)
            .WithUnit("rpm")
            .WithGoodQuality(DateTime.UtcNow)
            .Build();

        point.GatewayId.Should().Be("GW-TEST-001");
        point.SourceInstanceId.Should().Be("focas-jyoti17");
        point.ProtocolName.Should().Be("focas2");
        point.DeviceId.Should().Be("Jyoti17CNC");
        point.DeviceName.Should().Be("Jyoti 17 CNC");
        point.SequenceNumber.Should().Be(1);
    }

    [Fact]
    public void CreatePoint_FastPath_ProducesCompleteCanonicalPoint()
    {
        var factory = CreateFactory();
        var now = DateTime.UtcNow;

        var point = factory.CreatePoint(
            tagName: "axis.x.position",
            tagPath: "Axes/X/Position",
            value: 125.456,
            valueType: CanonicalValueType.Double,
            quality: DataQuality.Good,
            deviceTimestamp: now,
            gatewayTimestamp: now,
            unit: "mm");

        point.GatewayId.Should().Be("GW-TEST-001");
        point.SourceInstanceId.Should().Be("focas-jyoti17");
        point.TagName.Should().Be("axis.x.position");
        point.Value.Should().Be(125.456);
        point.Unit.Should().Be("mm");
        point.SequenceNumber.Should().Be(1);
    }

    [Fact]
    public void SequenceNumbers_MonotonicAcrossMixedCreationPaths()
    {
        var factory = CreateFactory();
        var now = DateTime.UtcNow;

        var p1 = factory.NewBuilder()
            .WithTag("a", "a")
            .WithValue(1, CanonicalValueType.Integer)
            .WithGoodQuality(now)
            .Build();

        var p2 = factory.CreatePoint("b", "b", 2, CanonicalValueType.Integer, DataQuality.Good, now, now);

        var p3 = factory.NewBuilder()
            .WithTag("c", "c")
            .WithValue(3, CanonicalValueType.Integer)
            .WithGoodQuality(now)
            .Build();

        p1.SequenceNumber.Should().Be(1);
        p2.SequenceNumber.Should().Be(2);
        p3.SequenceNumber.Should().Be(3);
    }

    [Fact]
    public async Task SequenceNumbers_MonotonicUnderConcurrentLoad()
    {
        // Section 19.6: Per-source monotonic sequence must hold under concurrent access.
        // Simulate 10 threads each producing 100,000 points — 1M total.
        const int threadCount = 10;
        const int pointsPerThread = 100_000;
        const int expectedTotal = threadCount * pointsPerThread;

        var factory = CreateFactory();
        var allSequences = new ConcurrentBag<long>();
        var now = DateTime.UtcNow;

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < pointsPerThread; i++)
            {
                var point = factory.CreatePoint(
                    tagName: "tag",
                    tagPath: "tag",
                    value: i,
                    valueType: CanonicalValueType.Integer,
                    quality: DataQuality.Good,
                    deviceTimestamp: now,
                    gatewayTimestamp: now);
                allSequences.Add(point.SequenceNumber);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        allSequences.Should().HaveCount(expectedTotal);

        var sorted = allSequences.OrderBy(s => s).ToArray();
        sorted[0].Should().Be(1);
        sorted[^1].Should().Be(expectedTotal);

        // All sequences must be strictly unique and form a contiguous range 1..N
        for (var i = 0; i < sorted.Length; i++)
        {
            sorted[i].Should().Be(i + 1);
        }

        factory.LastSequenceNumber.Should().Be(expectedTotal);
    }

    [Fact]
    public async Task NewBuilder_SafeForConcurrentUse()
    {
        // Multiple threads calling NewBuilder() and Build() in parallel must
        // never produce duplicate sequence numbers or corrupt builder state.
        const int threadCount = 16;
        const int pointsPerThread = 10_000;
        var factory = CreateFactory();
        var results = new ConcurrentBag<long>();
        var now = DateTime.UtcNow;

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < pointsPerThread; i++)
            {
                var point = factory.NewBuilder()
                    .WithTag("t", "t")
                    .WithValue(1, CanonicalValueType.Integer)
                    .WithGoodQuality(now)
                    .Build();
                results.Add(point.SequenceNumber);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        results.Should().HaveCount(threadCount * pointsPerThread);
        results.Distinct().Should().HaveCount(threadCount * pointsPerThread);
    }
}
