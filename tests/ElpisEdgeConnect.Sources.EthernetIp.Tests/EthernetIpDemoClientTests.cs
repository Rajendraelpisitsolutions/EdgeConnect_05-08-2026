// ============================================================================
// Tests: EthernetIpDemoClient — the synthetic IEthernetIpClient used in demo
//        mode. Pins: connect always succeeds, reads are deterministic for a
//        fixed clock, each element type decodes to the right CLR type and an
//        in-range value, values vary over time, distinct addresses trace
//        distinct curves, cancellation/disposal/not-connected are honored, and
//        there is no network/native dependency.
// Reference: docs/decisions/0029-s7-demo-mode.md (mirrors ADR-0012)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

public class EthernetIpDemoClientTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static EthernetIpDemoClient At(DateTime fixedNow) => new(() => fixedNow);

    private static EthernetIpConnectionParameters Params() => new()
    {
        Host = "192.168.0.10",
        CpuFamily = EthernetIpCpuFamily.ControlLogix,
    };

    private static async Task<EthernetIpDemoClient> ConnectedAt(DateTime now)
    {
        var client = At(now);
        await client.ConnectAsync(Params(), CancellationToken.None);
        return client;
    }

    [Fact]
    public async Task ConnectAsync_AlwaysSucceeds_AndSetsConnected()
    {
        var client = At(T0);

        await client.ConnectAsync(Params(), CancellationToken.None);

        client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task ReadTagAsync_IsDeterministic_ForAFixedClock()
    {
        var client = await ConnectedAt(T0);

        var a = await client.ReadTagAsync("Speed", EthernetIpElementType.Dint, CancellationToken.None);
        var b = await client.ReadTagAsync("Speed", EthernetIpElementType.Dint, CancellationToken.None);

        a.Success.Should().BeTrue();
        a.Value.Should().Be(b.Value);
    }

    [Theory]
    [InlineData(EthernetIpElementType.Bool, CanonicalValueType.Boolean, typeof(bool))]
    [InlineData(EthernetIpElementType.Sint, CanonicalValueType.Integer, typeof(int))]
    [InlineData(EthernetIpElementType.Int, CanonicalValueType.Integer, typeof(int))]
    [InlineData(EthernetIpElementType.Dint, CanonicalValueType.Integer, typeof(int))]
    [InlineData(EthernetIpElementType.Lint, CanonicalValueType.Long, typeof(long))]
    [InlineData(EthernetIpElementType.Real, CanonicalValueType.Float, typeof(float))]
    [InlineData(EthernetIpElementType.Lreal, CanonicalValueType.Double, typeof(double))]
    [InlineData(EthernetIpElementType.String, CanonicalValueType.String, typeof(string))]
    public async Task ReadTagAsync_DecodesToExpectedTypeForEachElementType(
        EthernetIpElementType elementType, CanonicalValueType expectedCanonical, Type expectedClrType)
    {
        var client = await ConnectedAt(T0);

        var r = await client.ReadTagAsync("Tag1", elementType, CancellationToken.None);

        r.Success.Should().BeTrue();
        r.ValueType.Should().Be(expectedCanonical);
        r.Value.Should().BeOfType(expectedClrType);
    }

    [Theory]
    [InlineData(EthernetIpElementType.Sint, 10.0, 90.0)]
    [InlineData(EthernetIpElementType.Int, 100.0, 900.0)]
    [InlineData(EthernetIpElementType.Dint, 1000.0, 9000.0)]
    [InlineData(EthernetIpElementType.Lint, 10000.0, 90000.0)]
    [InlineData(EthernetIpElementType.Real, 50.0, 150.0)]
    [InlineData(EthernetIpElementType.Lreal, 50.0, 150.0)]
    public async Task ReadTagAsync_NumericValuesAreInRange(
        EthernetIpElementType elementType, double min, double max)
    {
        var client = await ConnectedAt(T0.AddSeconds(3));

        var r = await client.ReadTagAsync("Tag1", elementType, CancellationToken.None);

        var asDouble = Convert.ToDouble(r.Value);
        asDouble.Should().BeInRange(min, max);
    }

    [Fact]
    public async Task ReadTagAsync_StringValue_HasDemoPrefix()
    {
        var client = await ConnectedAt(T0);

        var r = await client.ReadTagAsync("Label", EthernetIpElementType.String, CancellationToken.None);

        r.Value.Should().BeOfType<string>().Which.Should().StartWith("DEMO-");
    }

    [Fact]
    public async Task ReadTagAsync_VariesOverTime()
    {
        var now = T0;
        var client = new EthernetIpDemoClient(() => now);
        await client.ConnectAsync(Params(), CancellationToken.None);

        // Sample a full 30s sine period — regardless of the address phase, a
        // sine takes more than one value across a period, so this never flakes.
        var values = new HashSet<object?>();
        foreach (var offsetSeconds in new[] { 0, 5, 10, 15, 20, 25 })
        {
            now = T0.AddSeconds(offsetSeconds);
            var r = await client.ReadTagAsync("Speed", EthernetIpElementType.Dint, CancellationToken.None);
            values.Add(r.Value);
        }

        values.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task ReadTagAsync_DistinctAddresses_TraceDistinctCurves()
    {
        var client = await ConnectedAt(T0);

        // Different addresses get different phase offsets, so at one instant the
        // values should not all collapse to a single number.
        var values = new HashSet<object?>();
        foreach (var addr in new[] { "TagA", "TagB", "TagC", "TagD", "TagE" })
        {
            var r = await client.ReadTagAsync(addr, EthernetIpElementType.Dint, CancellationToken.None);
            values.Add(r.Value);
        }

        values.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task ReadTagAsync_BeforeConnect_ThrowsFatal()
    {
        var client = At(T0);

        var act = async () => await client.ReadTagAsync("Speed", EthernetIpElementType.Dint, CancellationToken.None);

        await act.Should().ThrowAsync<EthernetIpFatalException>();
    }

    [Fact]
    public async Task ReadTagAsync_HonorsCancellation()
    {
        var client = await ConnectedAt(T0);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.ReadTagAsync("Speed", EthernetIpElementType.Dint, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Disconnect_MarksDisconnected()
    {
        var client = await ConnectedAt(T0);

        client.Disconnect();

        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_MarksDisconnected()
    {
        var client = await ConnectedAt(T0);

        await client.DisposeAsync();

        client.IsConnected.Should().BeFalse();
    }
}
