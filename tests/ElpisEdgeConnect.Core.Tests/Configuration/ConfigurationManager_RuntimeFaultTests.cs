// ============================================================================
// Tests: ConfigurationManager.AppendRuntimeFaultAsync — the system-
//        actor audit-chain integration that backs M.P2.1's fail-soft
//        startup. Verifies:
//           * Entries are written with actor="system" and action=
//             RuntimeConfigurationFault (the ChatGPT review's explicit
//             separation of actor identity).
//           * Hash chaining works correctly with the new optional
//             RuntimeFault field (no chain break for entries that
//             omit it; chain continues across mixed entry kinds).
//           * Version id reflects the currently-applied configuration,
//             not the version the operator was editing (the fault was
//             observed against the live config).
//           * Operator-driven entries (drafts, applies) remain
//             unaffected and their actor strings pass through verbatim.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class ConfigurationManager_RuntimeFaultTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task<ConfigurationManager> CreateInitializedAsync()
    {
        var store = new InMemoryConfigurationStore();
        var initial = B2TestFixtures.ValidMinimal();
        await store.WriteCurrentAsync(JsonSerializer.Serialize(initial, JsonOptions), CancellationToken.None);
        var manager = new ConfigurationManager(store);
        await manager.InitializeAsync(CancellationToken.None);
        return manager;
    }

    [Fact]
    public async Task AppendRuntimeFault_WritesSystemActorEntry()
    {
        var manager = await CreateInitializedAsync();
        var fault = MakeFault("modbus-line-3", ConfigurationFaultKind.Source);

        var entry = await manager.AppendRuntimeFaultAsync(fault, CancellationToken.None);

        entry.Actor.Should().Be("system",
            "the ChatGPT review explicitly required runtime faults to be tagged actor=system");
        entry.Action.Should().Be(ConfigurationAuditAction.RuntimeConfigurationFault);
        entry.RuntimeFault.Should().NotBeNull();
        entry.RuntimeFault!.InstanceId.Should().Be("modbus-line-3");
        entry.RuntimeFault.Kind.Should().Be(ConfigurationFaultKind.Source);
        entry.Changes.Should().BeEmpty(
            "runtime faults are observations, not changes — Changes is the diff field");
        entry.DraftId.Should().BeNull(
            "runtime faults are not operator-driven and have no associated draft");
    }

    [Fact]
    public async Task AppendRuntimeFault_VersionIdReflectsCurrentConfig()
    {
        // Faults are observed AGAINST the currently-applied version — that
        // version id is the forensic anchor in the chain. If a subsequent
        // apply replaces the bad config, the new entries carry the new
        // version id; the historical fault entries still point at the
        // version that caused them.
        var manager = await CreateInitializedAsync();
        var currentVersion = manager.CurrentVersionId;

        var entry = await manager.AppendRuntimeFaultAsync(
            MakeFault("plc-1", ConfigurationFaultKind.Source),
            CancellationToken.None);

        entry.VersionId.Should().Be(currentVersion);
    }

    [Fact]
    public async Task AppendRuntimeFault_AppearsInGetAuditLogAsync()
    {
        var manager = await CreateInitializedAsync();
        await manager.AppendRuntimeFaultAsync(
            MakeFault("plc-1", ConfigurationFaultKind.Source),
            CancellationToken.None);

        var entries = new List<ConfigurationAuditEntry>();
        await foreach (var e in manager.GetAuditLogAsync(verifyChain: false, CancellationToken.None))
        {
            entries.Add(e);
        }

        entries.Should().ContainSingle(e =>
            e.Action == ConfigurationAuditAction.RuntimeConfigurationFault &&
            e.Actor == "system" &&
            e.RuntimeFault != null &&
            e.RuntimeFault.InstanceId == "plc-1");
    }

    [Fact]
    public async Task AppendRuntimeFault_PreservesHashChain_OnVerify()
    {
        // The chain MUST verify cleanly after a runtime-fault entry —
        // this is the load-bearing safety guarantee of the schema
        // extension. WhenWritingNull on the JsonSerializerOptions
        // ensures the new optional RuntimeFault field doesn't break
        // hash recompute for entries that omit it.
        var manager = await CreateInitializedAsync();

        await manager.AppendRuntimeFaultAsync(
            MakeFault("plc-1", ConfigurationFaultKind.Source),
            CancellationToken.None);

        var entries = new List<ConfigurationAuditEntry>();
        await foreach (var e in manager.GetAuditLogAsync(verifyChain: true, CancellationToken.None))
        {
            entries.Add(e);
        }

        entries.Should().NotBeEmpty(
            "the chain must verify with no exceptions thrown");
    }

    [Fact]
    public async Task AppendRuntimeFault_ChainsCorrectlyAcrossMixedEntryKinds()
    {
        // A realistic sequence: operator creates a draft, faults appear
        // on next boot, operator applies a fix. The chain must traverse
        // all of it correctly with verifyChain=true.
        var manager = await CreateInitializedAsync();

        // 1. Operator draft (different actor)
        var draftConfig = B2TestFixtures.ValidWithMultiple();
        await manager.CreateDraftAsync(draftConfig, "alice", CancellationToken.None);

        // 2. System-generated runtime fault
        await manager.AppendRuntimeFaultAsync(
            MakeFault("plc-1", ConfigurationFaultKind.Source),
            CancellationToken.None);

        // 3. Another system fault
        await manager.AppendRuntimeFaultAsync(
            MakeFault("opcua-1", ConfigurationFaultKind.Sink),
            CancellationToken.None);

        // Re-read with chain verification
        var entries = new List<ConfigurationAuditEntry>();
        await foreach (var e in manager.GetAuditLogAsync(verifyChain: true, CancellationToken.None))
        {
            entries.Add(e);
        }

        entries.Should().HaveCount(3);
        entries[0].Actor.Should().Be("alice");
        entries[1].Actor.Should().Be("system");
        entries[2].Actor.Should().Be("system");
        entries.Skip(1).All(e =>
            e.Action == ConfigurationAuditAction.RuntimeConfigurationFault).Should().BeTrue();
    }

    [Fact]
    public async Task AppendRuntimeFault_NullFault_Throws()
    {
        var manager = await CreateInitializedAsync();
        var act = async () => await manager.AppendRuntimeFaultAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ───── Helpers ──────────────────────────────────────────────────────

    private static ConfigurationFault MakeFault(
        string instanceId,
        ConfigurationFaultKind kind) => new()
    {
        Kind = kind,
        InstanceId = instanceId,
        ErrorCode = kind switch
        {
            ConfigurationFaultKind.Source => "CONFIG.SOURCE_WITHOUT_ROUTE",
            ConfigurationFaultKind.Sink => "CONFIG.SINK_KIND_UNKNOWN",
            ConfigurationFaultKind.Route => "CONFIG.ROUTE_REFERENCES_MISSING_SOURCE",
            _ => "CONFIG.UNKNOWN",
        },
        Message = $"test fault for {instanceId}",
        ObservedAtUtc = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc),
    };
}
