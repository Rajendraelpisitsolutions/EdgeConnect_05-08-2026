// ============================================================================
// Tests: OpcUaReconfigureExecutorTests — pin the pre-validation contract
//        for the production executor. Per PR 6b amendment + user lock
//        2026-05-29, the executor MUST validate the post-reconfigure
//        total against the 100K per-session ceiling BEFORE any
//        server-side mutation. Pinning this here prevents a future
//        refactor from accidentally moving the check after the first
//        RemoveItem / AddItems call (which would leave subs in a
//        torn state when over-cap configs are submitted).
//
//        The executor's per-Subscription mutation paths (RemoveItem,
//        ApplyChanges, new-Subscription allocation) hit the OPC stack
//        and require a live server to exercise; those are covered at
//        the adapter integration level with a substituted executor,
//        and validated end-to-end in PR 7 against UA Sample Server.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
//            PR 6b plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Opc.Ua.Client;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaReconfigureExecutorTests
{
    private static OpcUaClientSourceConfiguration ConfigWithFauxItems(int fauxCount) => new()
    {
        InstanceId = "opcua-test",
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "factorytalk",
        EndpointUrl = "opc.tcp://factorytalk.pilot.local:4840",
        // FauxMonitoredItemsList reports Count without allocating 100K
        // MonitoredItemConfig records — the executor's pre-validation
        // only reads Count and throws before iterating.
        MonitoredItems = new FauxMonitoredItemsList(fauxCount),
    };

    [Fact]
    public async Task ApplyAsync_OverCap_ThrowsBeforeAnyMutation()
    {
        // Amendment (user lock 2026-05-29): pin that cap-exceeded
        // reconfigures throw BEFORE touching live subscriptions. A
        // future refactor that moved the check after Remove/Add would
        // leave subs torn — this test catches that regression.
        var session = Substitute.For<ISession>();
        var existingSubs = new List<Subscription>();
        var newConfig = ConfigWithFauxItems(
            OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession + 1);

        // The diff doesn't matter for this path — the cap check fires
        // off newConfig.MonitoredItems.Count, not the diff contents.
        var diff = new OpcUaMonitoredItemDiffResult
        {
            Added = Array.Empty<MonitoredItemConfig>(),
            Removed = Array.Empty<MonitoredItemConfig>(),
            Modified = Array.Empty<MonitoredItemModification>(),
            Unchanged = Array.Empty<MonitoredItemConfig>(),
        };

        var executor = new DefaultOpcUaReconfigureExecutor(NullLogger.Instance);
        var act = () => executor.ApplyAsync(session, existingSubs, diff, newConfig, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OPCUA.TOO_MANY_MONITORED_ITEMS_AFTER_RECONFIGURE*");

        // Defensive — no session-level mutation either (no AddSubscription).
        session.DidNotReceiveWithAnyArgs().AddSubscription(default!);
    }

    [Fact]
    public async Task ApplyAsync_IdempotentDiff_ReturnsEmptyResult_NoMutation()
    {
        // Sanity: an idempotent diff (all Unchanged, zero Added /
        // Removed / Modified) hits the executor's loops with empty
        // collections and returns zero counters without touching the
        // session.
        var session = Substitute.For<ISession>();
        var existingSubs = new List<Subscription>();
        var newConfig = ConfigWithFauxItems(0);

        var diff = new OpcUaMonitoredItemDiffResult
        {
            Added = Array.Empty<MonitoredItemConfig>(),
            Removed = Array.Empty<MonitoredItemConfig>(),
            Modified = Array.Empty<MonitoredItemModification>(),
            Unchanged = Array.Empty<MonitoredItemConfig>(),
        };

        var executor = new DefaultOpcUaReconfigureExecutor(NullLogger.Instance);
        var result = await executor.ApplyAsync(session, existingSubs, diff, newConfig, CancellationToken.None);

        result.ItemsAdded.Should().Be(0);
        result.ItemsRemoved.Should().Be(0);
        result.ItemsModified.Should().Be(0);
        result.FinalSubscriptions.Should().BeEmpty();
        result.NewSubscriptions.Should().BeEmpty();
        result.RemovedSubscriptions.Should().BeEmpty();

        session.DidNotReceiveWithAnyArgs().AddSubscription(default!);
    }

    /// <summary>
    /// Faux list that reports <see cref="Count"/> without allocating real
    /// MonitoredItemConfig records. The executor's pre-validation path
    /// only reads Count before throwing; the indexer / enumerator throw
    /// to surface any unintended iteration.
    /// </summary>
    private sealed class FauxMonitoredItemsList : IReadOnlyList<MonitoredItemConfig>
    {
        public FauxMonitoredItemsList(int count) { Count = count; }
        public int Count { get; }
        public MonitoredItemConfig this[int index] =>
            throw new InvalidOperationException(
                "FauxMonitoredItemsList indexer should not be reached — "
                + "pre-validation must throw before iteration.");
        public IEnumerator<MonitoredItemConfig> GetEnumerator() =>
            throw new InvalidOperationException(
                "FauxMonitoredItemsList enumeration should not be reached.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
