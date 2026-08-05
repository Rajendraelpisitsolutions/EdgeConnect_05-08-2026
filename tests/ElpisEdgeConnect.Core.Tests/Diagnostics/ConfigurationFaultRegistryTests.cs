// ============================================================================
// Tests: ConfigurationFaultRegistry — thread-safe in-memory tracker of
//        runtime-observed configuration faults. The registry is the
//        backbone of the M.P2.1 fail-soft startup pattern, so the
//        invariants pinned here (replacement on re-register, clear on
//        successful re-init, snapshot semantics, thread safety) are
//        load-bearing for every protocol's registration extension.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class ConfigurationFaultRegistryTests
{
    [Fact]
    public void EmptyRegistry_ReturnsEmptySnapshot()
    {
        var registry = new ConfigurationFaultRegistry();

        registry.GetFaults().Should().BeEmpty();
        registry.GetFaultsFor(ConfigurationFaultKind.Source).Should().BeEmpty();
        registry.IsFaulted(ConfigurationFaultKind.Source, "anything").Should().BeFalse();
    }

    [Fact]
    public void Register_AddsFault_AndIsFaultedReturnsTrue()
    {
        var registry = new ConfigurationFaultRegistry();
        var fault = MakeFault("modbus-line-3", ConfigurationFaultKind.Source);

        registry.Register(fault);

        registry.IsFaulted(ConfigurationFaultKind.Source, "modbus-line-3").Should().BeTrue();
        registry.GetFaults().Should().ContainSingle().Which.Should().Be(fault);
    }

    [Fact]
    public void Register_SameKeyTwice_ReplacesPriorEntry()
    {
        // Locked invariant: re-registering for an already-faulted instance
        // REPLACES the prior entry (rather than appending). Matches the
        // operational model where a re-init failure supersedes the prior
        // failure record.
        var registry = new ConfigurationFaultRegistry();
        registry.Register(MakeFault("plc-1", ConfigurationFaultKind.Source, errorCode: "FIRST"));
        registry.Register(MakeFault("plc-1", ConfigurationFaultKind.Source, errorCode: "SECOND"));

        var faults = registry.GetFaults();
        faults.Should().ContainSingle();
        faults[0].ErrorCode.Should().Be("SECOND");
    }

    [Fact]
    public void Register_DifferentKinds_SameInstanceId_AreSeparate()
    {
        // (Kind, InstanceId) is the key — a source and a route with the
        // same instance-id-shaped name (rare but possible) are distinct
        // faults.
        var registry = new ConfigurationFaultRegistry();
        registry.Register(MakeFault("entity-1", ConfigurationFaultKind.Source));
        registry.Register(MakeFault("entity-1", ConfigurationFaultKind.Route));

        registry.GetFaults().Should().HaveCount(2);
        registry.IsFaulted(ConfigurationFaultKind.Source, "entity-1").Should().BeTrue();
        registry.IsFaulted(ConfigurationFaultKind.Route, "entity-1").Should().BeTrue();
    }

    [Fact]
    public void ClearFor_RemovesMatchingEntry_AndReturnsTrue()
    {
        var registry = new ConfigurationFaultRegistry();
        registry.Register(MakeFault("plc-1", ConfigurationFaultKind.Source));

        var removed = registry.ClearFor(ConfigurationFaultKind.Source, "plc-1");

        removed.Should().BeTrue();
        registry.IsFaulted(ConfigurationFaultKind.Source, "plc-1").Should().BeFalse();
        registry.GetFaults().Should().BeEmpty();
    }

    [Fact]
    public void ClearFor_MissingEntry_ReturnsFalse_DoesNotThrow()
    {
        var registry = new ConfigurationFaultRegistry();

        var removed = registry.ClearFor(ConfigurationFaultKind.Source, "never-registered");

        removed.Should().BeFalse();
    }

    [Fact]
    public void GetFaults_OrdersByObservedAtUtc_Ascending()
    {
        // Studio renders these in observation order — the registry
        // promises ascending order so client-side resorting isn't needed.
        var registry = new ConfigurationFaultRegistry();
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        registry.Register(MakeFault("c", ConfigurationFaultKind.Source, observedAtUtc: now.AddSeconds(3)));
        registry.Register(MakeFault("a", ConfigurationFaultKind.Source, observedAtUtc: now.AddSeconds(1)));
        registry.Register(MakeFault("b", ConfigurationFaultKind.Source, observedAtUtc: now.AddSeconds(2)));

        var faults = registry.GetFaults();
        faults.Select(f => f.InstanceId).Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void GetFaultsFor_FiltersByKind()
    {
        var registry = new ConfigurationFaultRegistry();
        registry.Register(MakeFault("plc-1", ConfigurationFaultKind.Source));
        registry.Register(MakeFault("mqtt-1", ConfigurationFaultKind.Sink));
        registry.Register(MakeFault("plc-2", ConfigurationFaultKind.Source));
        registry.Register(MakeFault("route-x", ConfigurationFaultKind.Route));

        registry.GetFaultsFor(ConfigurationFaultKind.Source).Should().HaveCount(2);
        registry.GetFaultsFor(ConfigurationFaultKind.Sink).Should().ContainSingle();
        registry.GetFaultsFor(ConfigurationFaultKind.Route).Should().ContainSingle();
    }

    [Fact]
    public void GetFaults_ReturnsIndependentSnapshot()
    {
        // Mutating the returned list must NOT affect the registry —
        // ConcurrentDictionary's Values returns a live view, so the
        // registry's ToList() materialisation is what protects us. Pin it.
        var registry = new ConfigurationFaultRegistry();
        registry.Register(MakeFault("plc-1", ConfigurationFaultKind.Source));

        var snapshot = registry.GetFaults() as List<ConfigurationFault>;
        snapshot!.Clear();

        registry.GetFaults().Should().ContainSingle(
            "the registry must not expose mutable internal state");
    }

    [Fact]
    public async Task Register_FromManyTasksConcurrently_AllEntriesPresent()
    {
        // M.P2.2's reload coordinator will exercise this from multiple
        // supervisor tasks during a single hot-reload — the registry
        // must be safe under concurrent writers.
        var registry = new ConfigurationFaultRegistry();
        var tasks = new Task[100];

        for (int i = 0; i < tasks.Length; i++)
        {
            var id = $"plc-{i}";
            tasks[i] = Task.Run(() => registry.Register(MakeFault(id, ConfigurationFaultKind.Source)));
        }

        await Task.WhenAll(tasks);

        registry.GetFaults().Should().HaveCount(100);
    }

    [Fact]
    public void Register_NullArgument_Throws()
    {
        var registry = new ConfigurationFaultRegistry();
        ((Action)(() => registry.Register(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Register_MissingRequiredFields_Throws()
    {
        // Defensive: the record requires the fields, but a misconfigured
        // caller using a faulty test builder could still pass empties.
        var registry = new ConfigurationFaultRegistry();
        var invalid = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Source,
            InstanceId = "",
            ErrorCode = "X",
            Message = "x",
            ObservedAtUtc = DateTime.UtcNow,
        };
        ((Action)(() => registry.Register(invalid))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ClearFor_NullOrEmptyInstanceId_Throws()
    {
        var registry = new ConfigurationFaultRegistry();
        ((Action)(() => registry.ClearFor(ConfigurationFaultKind.Source, "")))
            .Should().Throw<ArgumentException>();
    }

    // ───── Helpers ──────────────────────────────────────────────────────

    private static ConfigurationFault MakeFault(
        string instanceId,
        ConfigurationFaultKind kind,
        string errorCode = "CONFIG.TEST_FAULT",
        DateTime? observedAtUtc = null) => new()
    {
        Kind = kind,
        InstanceId = instanceId,
        ErrorCode = errorCode,
        Message = $"fault for {instanceId}",
        ObservedAtUtc = observedAtUtc ?? new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc),
    };
}
