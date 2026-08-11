// ============================================================================
// File: TapStreamWriterTests.cs
// Covers: Live Tap M3 — the SSE stream loop lifecycle. Opening the stream
//         subscribes (activates capture); captures + status frames are emitted;
//         cancelling (client disconnect) unsubscribes, and the route
//         deactivates after the cooldown.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Management.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class TapStreamWriterTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CanonicalDataPoint Point(string tag) =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW").WithSource("src", "mock").WithDevice("dev")
            .WithTag(tag, tag)
            .WithValue(1.0, CanonicalValueType.Double)
            .WithGoodQuality(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithSequence(1)
            .Build();

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) { return; }
            await Task.Delay(5).ConfigureAwait(false);
        }
        throw new TimeoutException("Predicate did not become true within the timeout.");
    }

    [Fact]
    public async Task WriteStream_Subscribes_StreamsCapturesAndStatus_UnsubscribesOnCancel()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var tap = new RouteTap(new RouteTapOptions { Cooldown = TimeSpan.FromSeconds(1) }, utcNow: () => now);
        var blocks = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource();

        var streamTask = TapStreamWriter.WriteStreamAsync(
            tap, "r1",
            writeEvent: (block, _) => { blocks.Enqueue(block); return Task.CompletedTask; },
            pollInterval: TimeSpan.FromMilliseconds(10),
            json: Json,
            ct: cts.Token);

        // Opening the stream subscribed the route → capture is active.
        await WaitForAsync(() => tap.IsTapActive("r1"), TimeSpan.FromSeconds(2));

        // Simulate the data path producing a point.
        tap.CaptureSource("r1", new[] { Point("spindle/speed") });

        await WaitForAsync(
            () => blocks.Any(b => b.StartsWith("event: capture")),
            TimeSpan.FromSeconds(2));

        // Client disconnect.
        cts.Cancel();
        await streamTask; // returns cleanly; subscription disposed in the finally-path

        // Cooldown elapses → the route deactivates and rings are released.
        now = now.AddSeconds(2);
        tap.Sweep();
        tap.IsTapActive("r1").Should().BeFalse();

        // Both event kinds were emitted as SSE blocks.
        blocks.Should().Contain(b => b.StartsWith("event: status\ndata: "));
        blocks.Should().Contain(b =>
            b.StartsWith("event: capture\ndata: ") && b.Contains("spindle/speed") && b.EndsWith("\n\n"));
    }

    [Fact]
    public async Task WriteStream_MasksSensitiveValue_NeverStreamsCleartext()
    {
        var tap = new RouteTap(masker: p =>
            p.TagName == "recipe/secret" ? p with { Value = TapValueMasker.MaskMarker } : p);
        var blocks = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource();

        var streamTask = TapStreamWriter.WriteStreamAsync(
            tap, "r1",
            (block, _) => { blocks.Enqueue(block); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(10), Json, cts.Token);

        await WaitForAsync(() => tap.IsTapActive("r1"), TimeSpan.FromSeconds(2));
        tap.CaptureSource("r1", new[] { Point("recipe/secret") });

        await WaitForAsync(() => blocks.Any(b => b.Contains("recipe/secret")), TimeSpan.FromSeconds(2));
        cts.Cancel();
        await streamTask;

        var captureBlock = blocks.First(b => b.Contains("recipe/secret"));
        captureBlock.Should().Contain("***").And.Contain("\"redacted\":true");
    }
}
