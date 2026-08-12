// ============================================================================
// File: Buffer/SqliteBufferCapabilityHandleTests.cs
// Covers: K1.2d R12 step 4 — the façade-anchored SqliteRouteStoreHandle exposed by
//         SqliteBuffer.GetCapabilityHandle() + the façade's ActivateReplayStateTrackingAsync
//         delegate. Asserts: providers are null before activation and both non-null after
//         (never one-null-one-not); the handle's Buffer IS the façade and both provider
//         slots ARE the one wrapped owner; a handle is an immutable snapshot (taken before
//         activation it stays null; a fresh handle after activation is non-null); the
//         providers capture through the same owner; and after disposing the façade the
//         providers throw the disposed error (single disposal authority).
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md §R4/§R5/§R12 step 4.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferCapabilityHandleTests
{
    private const string Route = "route-a";
    private const string ReplaySink = "sp";
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Handle_Before_Activation_Has_Null_Providers()
    {
        var path = NewFilePath();
        try
        {
            await using var buffer = await SqliteBuffer.OpenAsync(Route, path, SmallSqlitePolicy());

            var handle = buffer.GetCapabilityHandle();

            handle.Buffer.Should().BeSameAs(buffer);
            handle.ReplayBoundaryProvider.Should().BeNull();
            handle.ReplaySessionStateProvider.Should().BeNull();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Handle_After_Activation_Exposes_Both_Providers_On_One_Owner()
    {
        var path = NewFilePath();
        try
        {
            await using var buffer = await SqliteBuffer.OpenAsync(Route, path, SmallSqlitePolicy());
            var result = await buffer.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct);
            result.Outcome.Should().Be(ReplayTrackingActivationOutcome.Activated);
            result.ActivationHead.Should().Be(0);

            var handle = buffer.GetCapabilityHandle();

            handle.Buffer.Should().BeSameAs(buffer);
            handle.ReplayBoundaryProvider.Should().NotBeNull();
            handle.ReplaySessionStateProvider.Should().NotBeNull();
            // Single owner behind both slots — one writer mutex / lock / connection set.
            handle.ReplayBoundaryProvider.Should().BeSameAs(handle.ReplaySessionStateProvider);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Handle_Taken_Before_Activation_Stays_Null_A_Fresh_One_Is_Populated()
    {
        var path = NewFilePath();
        try
        {
            await using var buffer = await SqliteBuffer.OpenAsync(Route, path, SmallSqlitePolicy());

            var before = buffer.GetCapabilityHandle();
            await buffer.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct);
            var after = buffer.GetCapabilityHandle();

            before.ReplayBoundaryProvider.Should().BeNull();          // immutable snapshot
            before.ReplaySessionStateProvider.Should().BeNull();
            after.ReplayBoundaryProvider.Should().NotBeNull();
            after.ReplaySessionStateProvider.Should().NotBeNull();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Handle_Providers_Capture_Through_The_Same_Owner()
    {
        var path = NewFilePath();
        try
        {
            await using var buffer = await SqliteBuffer.OpenAsync(Route, path, SmallSqlitePolicy());
            await buffer.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct);
            var handle = buffer.GetCapabilityHandle();

            var boundary = await handle.ReplayBoundaryProvider!.CaptureReplayBoundaryAsync(ReplaySink, Ct);
            boundary.CutoffExclusive.Should().Be(0);

            var start = await handle.ReplaySessionStateProvider!.CaptureBirthStateAsync(Route, ReplaySink, Ct);
            start.Snapshot.Count.Should().Be(0);
            start.Boundary.CutoffExclusive.Should().Be(0);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Activation_Via_Facade_Is_Idempotent()
    {
        var path = NewFilePath();
        try
        {
            await using var buffer = await SqliteBuffer.OpenAsync(Route, path, SmallSqlitePolicy());

            (await buffer.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct)).Outcome
                .Should().Be(ReplayTrackingActivationOutcome.Activated);
            (await buffer.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct)).Outcome
                .Should().Be(ReplayTrackingActivationOutcome.AlreadyEnabled);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Handle_Providers_Throw_Disposed_After_Facade_Disposed()
    {
        var path = NewFilePath();
        try
        {
            var buffer = await SqliteBuffer.OpenAsync(Route, path, SmallSqlitePolicy());
            await buffer.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct);
            var handle = buffer.GetCapabilityHandle();

            await buffer.DisposeAsync(); // single disposal authority — disposes the owner once
            await buffer.DisposeAsync(); // idempotent

            var act = async () => await handle.ReplaySessionStateProvider!.CaptureBirthStateAsync(Route, ReplaySink, Ct);
            var ex = (await act.Should().ThrowAsync<ObjectDisposedException>()).Which;
            // Preserve the established SqliteBuffer disposed identity (PR #181), not the semaphore's.
            ex.ObjectName.Should().Be(typeof(SqliteBuffer).FullName);
        }
        finally { TryDelete(path); }
    }
}
