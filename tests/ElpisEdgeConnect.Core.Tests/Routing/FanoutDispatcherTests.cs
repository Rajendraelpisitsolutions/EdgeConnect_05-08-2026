// ============================================================================
// File: Routing/FanoutDispatcherTests.cs
// Purpose: Phase 2 pin tests for FanoutDispatcher. Covers the wake-only
//          contract, per-sink independence, edge coalescing, and the
//          structural rule that dispatcher methods carry no payload type.
// Milestone: C3 Commit 2 (phase 2 — wake-only fanout).
// ============================================================================

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class FanoutDispatcherTests
{
    [Fact]
    public async Task RegisteredSink_WakesOnNotifyAll()
    {
        using var d = new FanoutDispatcher();
        d.RegisterSink("s1");

        // Initial state is signaled once so the first poll drains.
        await d.WaitForSignalAsync("s1", CancellationToken.None);

        var waitTask = d.WaitForSignalAsync("s1", CancellationToken.None);
        waitTask.IsCompleted.Should().BeFalse();

        d.NotifyAll();
        await waitTask; // completes
    }

    [Fact]
    public async Task Notify_WakesOnlyTargetedSink()
    {
        using var d = new FanoutDispatcher();
        d.RegisterSink("s1");
        d.RegisterSink("s2");

        // Drain initial signals.
        await d.WaitForSignalAsync("s1", CancellationToken.None);
        await d.WaitForSignalAsync("s2", CancellationToken.None);

        var w1 = d.WaitForSignalAsync("s1", CancellationToken.None);
        var w2 = d.WaitForSignalAsync("s2", CancellationToken.None);

        d.Notify("s1");
        await w1;
        w2.IsCompleted.Should().BeFalse();

        d.Notify("s2");
        await w2;
    }

    [Fact]
    public async Task Notify_IsEdgeCoalesced()
    {
        using var d = new FanoutDispatcher();
        d.RegisterSink("s1");
        await d.WaitForSignalAsync("s1", CancellationToken.None); // drain initial

        d.NotifyAll();
        d.NotifyAll();
        d.NotifyAll();

        await d.WaitForSignalAsync("s1", CancellationToken.None); // first wait consumes
        var second = d.WaitForSignalAsync("s1", CancellationToken.None);
        second.IsCompleted.Should().BeFalse(); // extras coalesced
    }

    [Fact]
    public async Task Deregister_RemovesSink()
    {
        using var d = new FanoutDispatcher();
        d.RegisterSink("s1");
        d.DeregisterSink("s1");

        var act = async () => await d.WaitForSignalAsync("s1", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WaitForSignal_CancelsCleanly()
    {
        using var d = new FanoutDispatcher();
        d.RegisterSink("s1");
        await d.WaitForSignalAsync("s1", CancellationToken.None); // drain initial

        using var cts = new CancellationTokenSource();
        var waitTask = d.WaitForSignalAsync("s1", cts.Token);
        cts.Cancel();

        var act = async () => await waitTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

/// <summary>
/// Structural pin: the wake-only contract says the dispatcher NEVER carries
/// CanonicalDataPoint payloads. This test fails loudly if anyone adds a
/// data-bearing parameter type to a dispatcher method.
/// </summary>
public sealed class FanoutDispatcherStructureTests
{
    [Fact]
    public void DispatcherMethods_DoNotAcceptCanonicalDataPointPayloads()
    {
        var forbidden = typeof(CanonicalDataPoint);
        var methods = typeof(FanoutDispatcher)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var m in methods)
        {
            foreach (var p in m.GetParameters())
            {
                var t = p.ParameterType;
                var isCdp = t == forbidden
                    || (t.IsGenericType && t.GetGenericArguments().Any(a => a == forbidden))
                    || (t.IsArray && t.GetElementType() == forbidden);
                isCdp.Should().BeFalse(
                    $"FanoutDispatcher.{m.Name} must not take a CanonicalDataPoint payload — " +
                    "the wake-only contract is locked.");
            }
        }
    }
}
