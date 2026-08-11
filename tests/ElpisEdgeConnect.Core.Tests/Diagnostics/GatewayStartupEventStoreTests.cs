// ============================================================================
// Tests: GatewayStartupEventStore — pins the append-only, in-memory,
//        process-lifetime contract from M.2b.3.1 plan v3 §1.
//        Bounded retention uses the existing BoundedEventLog<T> primitive;
//        these tests cover the thin wrapper's append + chronological-snapshot
//        semantics and FIFO eviction at capacity.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md §1
// ============================================================================

using System;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class GatewayStartupEventStoreTests
{
    [Fact]
    public void Append_ThenGetAll_ReturnsTheSameEvent()
    {
        var store = new GatewayStartupEventStore();
        var ev = MakeEvent("focas2.fake-mode.activated", "FOCAS2 fake mode is active.");

        store.Append(ev);

        var all = store.GetAll();
        all.Should().ContainSingle().Which.Should().BeEquivalentTo(ev);
    }

    [Fact]
    public void Append_MultipleEvents_GetAllReturnsChronologicalOrder()
    {
        var store = new GatewayStartupEventStore();
        var first = MakeEvent("a.first", "first", DateTime.UtcNow);
        var second = MakeEvent("b.second", "second", DateTime.UtcNow.AddMilliseconds(1));
        var third = MakeEvent("c.third", "third", DateTime.UtcNow.AddMilliseconds(2));

        store.Append(first);
        store.Append(second);
        store.Append(third);

        // BoundedEventLog<T>.Snapshot returns oldest-first.
        store.GetAll().Should().Equal(first, second, third);
    }

    [Fact]
    public void Append_Null_Throws()
    {
        var store = new GatewayStartupEventStore();

        Action act = () => store.Append(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Append_AtCapacity_EvictsOldestPreservingFifo()
    {
        // Use the internal small-capacity ctor to force the eviction path.
        // Production capacity (256) is well above realistic boot-signal volume;
        // a tiny cap here is the only way to exercise BoundedEventLog's drop
        // path through this wrapper.
        var store = new GatewayStartupEventStore(capacity: 3);

        var e1 = MakeEvent("e1", "1");
        var e2 = MakeEvent("e2", "2");
        var e3 = MakeEvent("e3", "3");
        var e4 = MakeEvent("e4", "4");  // overflows — should evict e1

        store.Append(e1);
        store.Append(e2);
        store.Append(e3);
        store.Append(e4);

        store.GetAll().Should().Equal(new[] { e2, e3, e4 },
            "capacity-bound stores must evict the oldest entry, NOT silently drop the newest.");
    }

    [Fact]
    public async Task Append_ConcurrentlyFromManyTasks_AllEventsRetainedUpToCapacity()
    {
        // Pins the thread-safety contract. BoundedEventLog<T> uses a single
        // lock, so this is really verifying we haven't broken that
        // invariant in the wrapper.
        var store = new GatewayStartupEventStore(capacity: 1024);
        const int writersCount = 16;
        const int eventsPerWriter = 50;

        var tasks = new Task[writersCount];
        for (var w = 0; w < writersCount; w++)
        {
            var writerId = w;
            tasks[w] = Task.Run(() =>
            {
                for (var i = 0; i < eventsPerWriter; i++)
                {
                    store.Append(MakeEvent($"w{writerId}.e{i}", $"writer={writerId} i={i}"));
                }
            });
        }
        await Task.WhenAll(tasks);

        store.GetAll().Should().HaveCount(writersCount * eventsPerWriter,
            "every concurrent Append must be retained when the cap is not reached.");
    }

    private static GatewayStartupEvent MakeEvent(string code, string message, DateTime? at = null) =>
        new()
        {
            EventCode = code,
            Message = message,
            Severity = "Critical",
            EmittedAtUtc = at ?? DateTime.UtcNow,
        };
}
