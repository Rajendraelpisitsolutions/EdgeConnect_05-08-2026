// ============================================================================
// File: Buffer/SqliteBufferReplayCapabilityTests.cs
// Covers: K1.3 slice 1 — the IReplayRouteBuffer capability on the real SqliteBuffer.
//         Activation returns the persisted generation + both capture providers together
//         (lifecycle-honest: no provider promise before activation); a reopened store
//         returns its persisted NON-ZERO generation (never assumes 0); a mismatched
//         persisted replay sink id fails closed; and the capability surface exposes NO
//         generation-advancement (fixed-generation route model).
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.2-amendment.md
//            §B2 / §C1 (slice-1 tests 2, 3, 4, 8, 14).
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferReplayCapabilityTests
{
    private const string Route = "route-a";
    private const string ReplaySink = "sp";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CanonicalDataPoint Point(long seq) =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice("dev-1")
            .WithTag("tag", "Spindle/Speed")
            .WithValue((double)seq, CanonicalValueType.Double)
            .WithGoodQuality(BaseUtc.AddSeconds(seq))
            .WithSequence(seq)
            .Build();

    [Fact]
    public async Task Capability_Activation_Returns_Generation_And_Both_Providers()
    {
        var path = NewFilePath();
        try
        {
            await using var buffer = await SqliteBuffer.OpenAsync(Route, path, SmallSqlitePolicy());
            var cap = (IReplayRouteBuffer)buffer;

            cap.IsReplayTrackingEnabled.Should().BeFalse(); // no provider promise before activation

            var activation = await cap.ActivateReplayAsync(Route, ReplaySink, Ct);

            activation.Generation.Value.Should().Be(0);
            activation.BoundaryProvider.Should().NotBeNull();
            activation.SessionStateProvider.Should().NotBeNull();
            cap.IsReplayTrackingEnabled.Should().BeTrue();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capability_Reopen_Returns_Persisted_NonZero_Generation()
    {
        var path = NewFilePath();
        try
        {
            // Drive the store to generation 1 (append → drain the replay sink to head → advance).
            await using (var store = await SqliteRouteStore.OpenAsync(Route, path, SmallSqlitePolicy()))
            {
                await store.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct);
                await store.AppendAsync(new[] { Point(0) }, 0, Ct);
                await store.AckAsync(ReplaySink, 0, Ct);          // cursor → 1 == head
                await store.AdvanceGenerationAsync(0, 1, Ct);
            }

            await using var buffer = await SqliteBuffer.OpenAsync(Route, path, SmallSqlitePolicy());
            var activation = await ((IReplayRouteBuffer)buffer).ActivateReplayAsync(Route, ReplaySink, Ct);

            activation.Generation.Value.Should().Be(1); // persisted, non-zero — not a hardcoded 0
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capability_Activate_With_Different_Sink_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await using var buffer = await SqliteBuffer.OpenAsync(Route, path, SmallSqlitePolicy());
            var cap = (IReplayRouteBuffer)buffer;
            await cap.ActivateReplayAsync(Route, ReplaySink, Ct);

            var act = async () => await cap.ActivateReplayAsync(Route, "a-different-sink", Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreReplaySinkMismatch);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Capability_Interface_Exposes_No_Generation_Advancement()
    {
        // K1.3 runs at a fixed generation — the route capability must not surface any way to
        // advance it (that would be an accidental path back to the empty-birth defect).
        typeof(IReplayRouteBuffer).GetMethods()
            .Select(m => m.Name)
            .Should().NotContain(n => n.Contains("Advance", StringComparison.Ordinal)
                                   || n.Contains("Generation", StringComparison.Ordinal));
    }
}
