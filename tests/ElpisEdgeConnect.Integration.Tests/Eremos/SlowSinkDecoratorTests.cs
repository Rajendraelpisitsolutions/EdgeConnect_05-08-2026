// ============================================================================
// Tests: SlowSinkDecoratorTests — pins the v2 plan §6.4 contract for the
// PublishAsync delay-injection mechanism.
//
// Verified contracts:
//   * Pass-through for every ISinkAdapter member EXCEPT PublishAsync.
//   * PerPublishDelayMs default is 0 (pass-through behaviour).
//   * Negative PerPublishDelayMs throws.
//   * Setting PerPublishDelayMs at runtime takes effect on the next
//     PublishAsync call (no caching).
//   * PublishCount increments on every PublishAsync regardless of delay.
//   * Cancellation during the delay returns OperationCanceledException
//     (matches the inner adapter's cancellation behaviour).
//   * Pass-through includes the inner sink's PublishResult verbatim.
//
// No bUnit / no broker — pure xUnit + NSubstitute against the ISinkAdapter
// contract.
//
// Reference: docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §6.4
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ElpisEdgeConnect.Integration.Tests.Eremos;

public sealed class SlowSinkDecoratorTests
{
    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Action act = () => _ = new SlowSinkDecorator(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("inner");
    }

    [Fact]
    public void Default_PerPublishDelayMs_IsZero()
    {
        var inner = MakeInnerSink();
        var decorator = new SlowSinkDecorator(inner);
        decorator.PerPublishDelayMs.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void PerPublishDelayMs_Negative_Throws(int value)
    {
        var decorator = new SlowSinkDecorator(MakeInnerSink());
        Action act = () => decorator.PerPublishDelayMs = value;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void IdentityAndState_PassThroughToInner()
    {
        var inner = MakeInnerSink();
        inner.InstanceId.Returns("inner-sink-1");
        inner.ProtocolName.Returns("mqtt");
        inner.Capabilities.Returns(SinkCapabilities.Push);
        inner.State.Returns(AdapterState.Running);

        var decorator = new SlowSinkDecorator(inner);

        decorator.InstanceId.Should().Be("inner-sink-1");
        decorator.ProtocolName.Should().Be("mqtt");
        decorator.Capabilities.Should().Be(SinkCapabilities.Push);
        decorator.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task Lifecycle_AllMethodsPassThroughToInner()
    {
        var inner = MakeInnerSink();
        var decorator = new SlowSinkDecorator(inner);

        var config = MakeSinkConfig();
        await decorator.InitializeAsync(config, CancellationToken.None);
        await decorator.StartAsync(CancellationToken.None);
        await decorator.CheckHealthAsync(CancellationToken.None);
        await decorator.StopAsync(CancellationToken.None);
        await decorator.UpdateCurrentValuesAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
        await decorator.ValidateConfigAsync(config, CancellationToken.None);
        await decorator.DisposeAsync();

        await inner.Received(1).InitializeAsync(config, Arg.Any<CancellationToken>());
        await inner.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await inner.Received(1).CheckHealthAsync(Arg.Any<CancellationToken>());
        await inner.Received(1).StopAsync(Arg.Any<CancellationToken>());
        await inner.Received(1).UpdateCurrentValuesAsync(Arg.Any<IReadOnlyList<CanonicalDataPoint>>(), Arg.Any<CancellationToken>());
        await inner.Received(1).ValidateConfigAsync(config, Arg.Any<CancellationToken>());
        await inner.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task PublishAsync_ZeroDelay_PassesThroughImmediately()
    {
        var inner = MakeInnerSink();
        var expected = PublishResult.Successful(5, TimeSpan.FromMilliseconds(1));
        inner.PublishAsync(Arg.Any<IReadOnlyList<CanonicalDataPoint>>(), Arg.Any<CancellationToken>())
             .Returns(expected);

        var decorator = new SlowSinkDecorator(inner);

        var sw = Stopwatch.StartNew();
        var result = await decorator.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
        sw.Stop();

        result.Should().BeEquivalentTo(expected);
        sw.ElapsedMilliseconds.Should().BeLessThan(50,
            "with PerPublishDelayMs=0 the call must pass through without delay");
        decorator.PublishCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_WithDelay_WaitsAtLeastConfiguredDelay()
    {
        var inner = MakeInnerSink();
        inner.PublishAsync(Arg.Any<IReadOnlyList<CanonicalDataPoint>>(), Arg.Any<CancellationToken>())
             .Returns(PublishResult.Successful(1, TimeSpan.FromMilliseconds(1)));

        var decorator = new SlowSinkDecorator(inner)
        {
            PerPublishDelayMs = 100,
        };

        var sw = Stopwatch.StartNew();
        await decorator.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(95, // small wall-clock slack
            "PerPublishDelayMs=100 must delay the call by ~100ms");
    }

    [Fact]
    public async Task PublishAsync_DelayChangesAtRuntime_NextCallReflectsTheChange()
    {
        // Gate 8's measurement methodology relies on toggling the delay
        // mid-test: 0ms steady-state → 100ms backpressure phase → 0ms
        // recovery. The decorator must observe the new value immediately
        // on the next PublishAsync call (no caching, no warmup).
        var inner = MakeInnerSink();
        inner.PublishAsync(Arg.Any<IReadOnlyList<CanonicalDataPoint>>(), Arg.Any<CancellationToken>())
             .Returns(PublishResult.Successful(1, TimeSpan.FromMilliseconds(1)));

        var decorator = new SlowSinkDecorator(inner);

        // Steady-state: 0ms delay.
        var sw1 = Stopwatch.StartNew();
        await decorator.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
        sw1.Stop();
        sw1.ElapsedMilliseconds.Should().BeLessThan(50);

        // Backpressure phase: bump to 100ms.
        decorator.PerPublishDelayMs = 100;
        var sw2 = Stopwatch.StartNew();
        await decorator.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
        sw2.Stop();
        sw2.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(95);

        // Recovery: drop back to 0ms.
        decorator.PerPublishDelayMs = 0;
        var sw3 = Stopwatch.StartNew();
        await decorator.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
        sw3.Stop();
        sw3.ElapsedMilliseconds.Should().BeLessThan(50,
            "PerPublishDelayMs=0 on recovery must drop the wait window immediately");

        decorator.PublishCount.Should().Be(3);
    }

    [Fact]
    public async Task PublishAsync_CancellationDuringDelay_ThrowsOperationCanceled()
    {
        var inner = MakeInnerSink();
        var decorator = new SlowSinkDecorator(inner)
        {
            PerPublishDelayMs = 5000, // long enough that we'll cancel before it fires
        };

        using var cts = new CancellationTokenSource(50);

        Func<Task> act = () => decorator.PublishAsync(Array.Empty<CanonicalDataPoint>(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PublishAsync_ReturnsInnerSinkResultVerbatim()
    {
        var inner = MakeInnerSink();
        // Use the explicit Failed factory to verify non-success path round-trips
        // without alteration. Mirrors the v2 §6.4 invariant that the decorator
        // only perturbs the delay, never the result.
        var fakeError = new ElpisEdgeConnect.Core.Errors.AdapterError
        {
            Code = "MQTT.PUBLISH_FAILED",
            Category = ElpisEdgeConnect.Core.Errors.ErrorCategory.Network,
            Message = "broker returned 3 retryable errors",
            Retryable = true,
        };
        var expected = PublishResult.Failed(fakeError, TimeSpan.FromMilliseconds(5));
        inner.PublishAsync(Arg.Any<IReadOnlyList<CanonicalDataPoint>>(), Arg.Any<CancellationToken>())
             .Returns(expected);

        var decorator = new SlowSinkDecorator(inner);
        var result = await decorator.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task PublishCount_IncrementsOnEveryCall_RegardlessOfDelay()
    {
        var inner = MakeInnerSink();
        inner.PublishAsync(Arg.Any<IReadOnlyList<CanonicalDataPoint>>(), Arg.Any<CancellationToken>())
             .Returns(PublishResult.Successful(1, TimeSpan.FromMilliseconds(1)));

        var decorator = new SlowSinkDecorator(inner);

        for (var i = 0; i < 5; i++)
        {
            await decorator.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
        }

        decorator.PublishCount.Should().Be(5);
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static ISinkAdapter MakeInnerSink()
    {
        var sink = Substitute.For<ISinkAdapter>();
        sink.InstanceId.Returns("inner-sink");
        sink.ProtocolName.Returns("mqtt");
        sink.Capabilities.Returns(SinkCapabilities.Push);
        sink.State.Returns(AdapterState.Running);
        sink.PublishAsync(Arg.Any<IReadOnlyList<CanonicalDataPoint>>(), Arg.Any<CancellationToken>())
            .Returns(PublishResult.Successful(1, TimeSpan.FromMilliseconds(1)));
        sink.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new AdapterHealth
            {
                State = AdapterState.Running,
                Level = HealthLevel.Healthy,
                CheckedAt = DateTime.UtcNow,
            });
        sink.ValidateConfigAsync(Arg.Any<SinkConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(ValidationResult.Success());
        return sink;
    }

    private static SinkConfiguration MakeSinkConfig() => new StubSinkConfiguration
    {
        InstanceId = "test-sink",
        ProtocolName = "mqtt",
    };

    /// <summary>
    /// Test-only concrete subclass of the abstract SinkConfiguration. Carries
    /// only the base fields — the SlowSinkDecorator passes the config through
    /// to the inner sink verbatim, so the protocol-specific subtype doesn't
    /// matter for the decorator's contract tests.
    /// </summary>
    private sealed record StubSinkConfiguration : SinkConfiguration;
}
