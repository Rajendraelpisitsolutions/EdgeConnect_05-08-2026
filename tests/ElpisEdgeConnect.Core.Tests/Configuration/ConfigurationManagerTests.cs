// ============================================================================
// File: Configuration/ConfigurationManagerTests.cs
// Covers: Full draft → validate → apply → rollback lifecycle.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class ConfigurationManagerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task<(ConfigurationManager Manager, InMemoryConfigurationStore Store)> CreateInitializedAsync()
    {
        var store = new InMemoryConfigurationStore();
        var initial = B2TestFixtures.ValidMinimal();
        await store.WriteCurrentAsync(JsonSerializer.Serialize(initial, JsonOptions), CancellationToken.None);
        var manager = new ConfigurationManager(store);
        await manager.InitializeAsync(CancellationToken.None);
        return (manager, store);
    }

    // ========================================================================
    // Initialization
    // ========================================================================

    [Fact]
    public async Task Initialize_WithValidCurrent_LoadsAndValidates()
    {
        var (manager, _) = await CreateInitializedAsync();

        var current = await manager.GetCurrentAsync(CancellationToken.None);
        current.Should().NotBeNull();
        current.Gateway.GatewayId.Should().Be("GW-TEST-001");
    }

    [Fact]
    public async Task Initialize_MissingCurrent_AutoProvisionsEmptySeed()
    {
        // ADR-0016 Rule 5 — first-run self-provisioning. Previously
        // threw CORE.CONFIG_FILE_NOT_FOUND; now produces a minimal
        // empty-state seed so Studio can launch and the operator's
        // first onboarding flow becomes the v1 applied config.
        var store = new InMemoryConfigurationStore();
        var manager = new ConfigurationManager(store, hostnameProvider: () => "test-host");

        await manager.InitializeAsync(CancellationToken.None);

        var current = await manager.GetCurrentAsync(CancellationToken.None);
        current.Should().NotBeNull();
        current.Gateway.GatewayId.Should().Be("gw-test-host");
        current.Gateway.GatewayName.Should().Be("EdgeConnect on test-host");
        current.Sources.Should().BeEmpty();
        current.Sinks.Should().BeEmpty();
        current.Routes.Should().BeEmpty();
    }

    [Fact]
    public async Task Initialize_MissingCurrent_PersistsSeedToStore()
    {
        // The seed is written via the normal atomic-write path so it
        // becomes the system's v1 applied config. A second Initialize
        // (e.g., after a process restart) reads it back rather than
        // re-provisioning.
        var store = new InMemoryConfigurationStore();
        var manager = new ConfigurationManager(store, hostnameProvider: () => "persisted");
        await manager.InitializeAsync(CancellationToken.None);

        var json = await store.ReadCurrentAsync(CancellationToken.None);

        json.Should().NotBeNull();
        json!.Should().Contain("\"GatewayId\": \"gw-persisted\"");
    }

    [Fact]
    public async Task Initialize_MissingCurrent_AppendsAutoProvisionedAuditEntry()
    {
        // The audit chain captures the auto-provision as a first-version
        // event so the gateway's lifetime history is complete from v0.
        var store = new InMemoryConfigurationStore();
        var manager = new ConfigurationManager(store, hostnameProvider: () => "audit-host");

        await manager.InitializeAsync(CancellationToken.None);

        var auditLog = new ConfigurationAuditLog(store);
        var entries = new List<ConfigurationAuditEntry>();
        await foreach (var entry in auditLog.ReadAllAsync(verifyChain: true, CancellationToken.None))
        {
            entries.Add(entry);
        }

        entries.Should().HaveCount(1);
        var entry0 = entries[0];
        entry0.Action.Should().Be(ConfigurationAuditAction.AutoProvisioned);
        entry0.Actor.Should().Be("system");
        entry0.VersionId.Should().Be(ConfigurationVersionId.Initial);
        entry0.PreviousVersionId.Should().BeNull();
        entry0.PreviousHash.Should().Be(ConfigurationAuditLog.GenesisHash);
        entry0.Summary.Should().Contain("Auto-provisioned");
        entry0.Summary.Should().Contain("gw-audit-host");
        entry0.Changes.Should().BeEmpty();
        entry0.DraftId.Should().BeNull();
    }

    [Fact]
    public async Task Initialize_TwiceAfterMissingCurrent_IsIdempotent()
    {
        // Once provisioned, subsequent InitializeAsync calls on the same
        // process are no-ops (mutex + _initialized guard already cover
        // this — the test is belt-and-suspenders).
        var store = new InMemoryConfigurationStore();
        var manager = new ConfigurationManager(store, hostnameProvider: () => "twice");

        await manager.InitializeAsync(CancellationToken.None);
        await manager.InitializeAsync(CancellationToken.None);

        var current = await manager.GetCurrentAsync(CancellationToken.None);
        current.Gateway.GatewayId.Should().Be("gw-twice");

        // Audit log should still have ONE entry, not two.
        var auditLog = new ConfigurationAuditLog(store);
        var entries = new List<ConfigurationAuditEntry>();
        await foreach (var entry in auditLog.ReadAllAsync(verifyChain: true, CancellationToken.None))
        {
            entries.Add(entry);
        }
        entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task Initialize_AfterRestart_ReadsPreviouslyProvisionedSeed()
    {
        // Simulate a process restart: provision once, then construct a
        // fresh ConfigurationManager against the same store. The second
        // manager reads the persisted seed rather than re-provisioning.
        var store = new InMemoryConfigurationStore();
        var firstManager = new ConfigurationManager(store, hostnameProvider: () => "first-boot");
        await firstManager.InitializeAsync(CancellationToken.None);

        // Different hostname provider — if this manager re-provisioned,
        // GatewayId would change. It must not.
        var secondManager = new ConfigurationManager(store, hostnameProvider: () => "should-be-ignored");
        await secondManager.InitializeAsync(CancellationToken.None);

        var current = await secondManager.GetCurrentAsync(CancellationToken.None);
        current.Gateway.GatewayId.Should().Be("gw-first-boot");
    }

    [Theory]
    [InlineData("simple", "gw-simple")]
    [InlineData("Mixed-Case", "gw-mixed-case")]
    [InlineData("with spaces", "gw-with-spaces")]
    [InlineData("with.dots.allowed", "gw-with.dots.allowed")]
    [InlineData("with_underscores_allowed", "gw-with_underscores_allowed")]
    [InlineData("!@#leading-non-alnum", "gw-leading-non-alnum")]
    [InlineData("trailing-dashes---", "gw-trailing-dashes")]
    [InlineData("multiple   spaces", "gw-multiple-spaces")]
    [InlineData("WS2022.example.com", "gw-ws2022.example.com")]
    [InlineData("", "gw-host")]
    [InlineData("   ", "gw-host")]
    [InlineData("...", "gw-host")]
    public async Task Initialize_MissingCurrent_HostnameSlugifiesCorrectly(string hostname, string expectedGatewayId)
    {
        var store = new InMemoryConfigurationStore();
        var manager = new ConfigurationManager(store, hostnameProvider: () => hostname);

        await manager.InitializeAsync(CancellationToken.None);
        var current = await manager.GetCurrentAsync(CancellationToken.None);

        current.Gateway.GatewayId.Should().Be(expectedGatewayId);
    }

    [Fact]
    public async Task Initialize_MalformedJson_ThrowsConfigFileCorrupt()
    {
        var store = new InMemoryConfigurationStore();
        await store.WriteCurrentAsync("{ this is not valid json", CancellationToken.None);
        var manager = new ConfigurationManager(store);

        var act = async () => await manager.InitializeAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConfigurationException>()
            .Where(ex => ex.Error.Code == CoreErrors.ConfigFileCorrupt);
    }

    [Fact]
    public async Task Methods_BeforeInitialize_Throw()
    {
        var store = new InMemoryConfigurationStore();
        var manager = new ConfigurationManager(store);

        var act = async () => await manager.GetCurrentAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConfigurationException>()
            .Where(ex => ex.Error.Code == CoreErrors.RuntimeNotInitialized);
    }

    // ========================================================================
    // Draft lifecycle
    // ========================================================================

    [Fact]
    public async Task CreateDraft_PersistsAndReturnsId()
    {
        var (manager, _) = await CreateInitializedAsync();
        var newConfig = B2TestFixtures.ValidWithMultiple();

        var draftId = await manager.CreateDraftAsync(newConfig, "test-actor", CancellationToken.None);

        draftId.IsEmpty.Should().BeFalse();
        var loaded = await manager.GetDraftAsync(draftId, CancellationToken.None);
        loaded.Should().NotBeNull();
        loaded!.Sources.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDraft_UnknownId_ReturnsNull()
    {
        var (manager, _) = await CreateInitializedAsync();
        var loaded = await manager.GetDraftAsync(DraftId.NewId(), CancellationToken.None);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task ListDrafts_ReturnsCreatedDrafts()
    {
        var (manager, _) = await CreateInitializedAsync();
        var d1 = await manager.CreateDraftAsync(B2TestFixtures.ValidMinimal(), null, CancellationToken.None);
        var d2 = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);

        var drafts = await manager.ListDraftsAsync(CancellationToken.None);

        drafts.Should().Contain(d1).And.Contain(d2);
    }

    [Fact]
    public async Task ValidateDraft_HappyPath_ReturnsSuccess()
    {
        var (manager, _) = await CreateInitializedAsync();
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);

        var result = await manager.ValidateDraftAsync(draftId, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateDraft_MissingDraft_ReturnsFileNotFound()
    {
        var (manager, _) = await CreateInitializedAsync();

        var result = await manager.ValidateDraftAsync(DraftId.NewId(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.ConfigFileNotFound);
    }

    [Fact]
    public async Task DiscardDraft_RemovesFile()
    {
        var (manager, _) = await CreateInitializedAsync();
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidMinimal(), null, CancellationToken.None);

        await manager.DiscardDraftAsync(draftId, null, CancellationToken.None);

        (await manager.GetDraftAsync(draftId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task DiscardDraft_UnknownId_NoOp()
    {
        var (manager, _) = await CreateInitializedAsync();
        var act = async () => await manager.DiscardDraftAsync(DraftId.NewId(), null, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    // ========================================================================
    // Apply
    // ========================================================================

    [Fact]
    public async Task Apply_HappyPath_UpdatesCurrentAndAppendsAudit()
    {
        var (manager, store) = await CreateInitializedAsync();
        var newConfig = B2TestFixtures.ValidWithMultiple();
        var draftId = await manager.CreateDraftAsync(newConfig, "alice", CancellationToken.None);

        var result = await manager.ApplyDraftAsync(draftId, "alice", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AuditEntry.Should().NotBeNull();
        result.AuditEntry!.Action.Should().Be(ConfigurationAuditAction.Applied);
        result.AuditEntry.Actor.Should().Be("alice");

        var current = await manager.GetCurrentAsync(CancellationToken.None);
        current.Sources.Should().HaveCount(2);

        // Audit log: one entry for the draft creation, one for the apply.
        store.AuditLineCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Apply_DeletesDraftFile()
    {
        var (manager, _) = await CreateInitializedAsync();
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);

        await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);

        (await manager.GetDraftAsync(draftId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Apply_WritesPreviousConfigToHistory()
    {
        var (manager, store) = await CreateInitializedAsync();
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
        var beforeVersion = manager.CurrentVersionId;

        await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);

        var historyVersions = await store.ListHistoryAsync(CancellationToken.None);
        historyVersions.Should().Contain(beforeVersion);
    }

    [Fact]
    public async Task Apply_EmitsCurrentChangedEvent()
    {
        var (manager, _) = await CreateInitializedAsync();
        ConfigurationChangeEventArgs? captured = null;
        manager.CurrentChanged += (_, e) => captured = e;

        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
        await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.NewConfiguration.Sources.Should().HaveCount(2);
        captured.Changes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Apply_InvalidDraft_NoStateChange()
    {
        var (manager, _) = await CreateInitializedAsync();
        var beforeVersion = manager.CurrentVersionId;

        // Build a draft with a route that references a missing source
        var bad = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "bad-route",
                    Name = "Bad",
                    SourceInstanceId = "does-not-exist",
                    SinkInstanceIds = ["sink-1"],
                },
            ],
        };
        var draftId = await manager.CreateDraftAsync(bad, null, CancellationToken.None);

        var result = await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ValidationResult.Errors.Should().Contain(e => e.Code == CoreErrors.RouteSourceNotFound);
        manager.CurrentVersionId.Should().Be(beforeVersion);
    }

    [Fact]
    public async Task Apply_UnknownDraftId_ReturnsFailure()
    {
        var (manager, _) = await CreateInitializedAsync();

        var result = await manager.ApplyDraftAsync(DraftId.NewId(), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ValidationResult.Errors.Should().Contain(e => e.Code == CoreErrors.ConfigFileNotFound);
    }

    // ========================================================================
    // Rollback
    // ========================================================================

    [Fact]
    public async Task Rollback_ToExistingVersion_RestoresConfig()
    {
        var (manager, _) = await CreateInitializedAsync();
        var v0 = manager.CurrentVersionId;

        // Apply once so we have a history entry to roll back to.
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
        var applyResult = await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);
        applyResult.Success.Should().BeTrue();

        // Roll back to the earlier version (which is now in history).
        var rollback = await manager.RollbackAsync(v0, "alice", CancellationToken.None);

        rollback.Success.Should().BeTrue();
        rollback.AuditEntry!.Action.Should().Be(ConfigurationAuditAction.RolledBack);

        var current = await manager.GetCurrentAsync(CancellationToken.None);
        current.Sources.Should().HaveCount(1); // back to the minimal fixture
    }

    [Fact]
    public async Task Rollback_CreatesNewVersionId()
    {
        var (manager, _) = await CreateInitializedAsync();
        var v0 = manager.CurrentVersionId;
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
        await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);
        var v1 = manager.CurrentVersionId;

        var rollback = await manager.RollbackAsync(v0, null, CancellationToken.None);

        rollback.VersionId.Should().NotBe(v0);
        rollback.VersionId.Should().NotBe(v1);
        manager.CurrentVersionId.Should().Be(rollback.VersionId);
    }

    [Fact]
    public async Task Rollback_UnknownVersion_ReturnsFailure()
    {
        var (manager, _) = await CreateInitializedAsync();

        var result = await manager.RollbackAsync(new ConfigurationVersionId("does-not-exist"), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ValidationResult.Errors.Should().Contain(e => e.Code == CoreErrors.ConfigFileNotFound);
    }

    // ========================================================================
    // History and audit log
    // ========================================================================

    [Fact]
    public async Task GetHistory_AfterMultipleApplies_ListsThemMostRecentFirst()
    {
        var (manager, _) = await CreateInitializedAsync();

        for (var i = 0; i < 3; i++)
        {
            var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
            await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);
        }

        var history = await manager.GetHistoryAsync(CancellationToken.None);

        history.Should().NotBeEmpty();
        // Most recent should sort first by version id (descending).
        var versionIds = history.Select(h => h.VersionId.Value).ToList();
        versionIds.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetAuditLog_StreamsAllEntries()
    {
        var (manager, _) = await CreateInitializedAsync();
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), "actor1", CancellationToken.None);
        await manager.ApplyDraftAsync(draftId, "actor1", CancellationToken.None);

        var entries = new List<ConfigurationAuditEntry>();
        await foreach (var entry in manager.GetAuditLogAsync(verifyChain: true, CancellationToken.None))
        {
            entries.Add(entry);
        }

        entries.Should().HaveCountGreaterThanOrEqualTo(2);
        entries.Should().Contain(e => e.Action == ConfigurationAuditAction.DraftCreated);
        entries.Should().Contain(e => e.Action == ConfigurationAuditAction.Applied);
    }

    // ========================================================================
    // Concurrency
    // ========================================================================

    [Fact]
    public async Task ConcurrentApply_BothSerialize_ProduceTwoVersions()
    {
        var (manager, _) = await CreateInitializedAsync();
        var d1 = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
        var d2 = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);

        var task1 = manager.ApplyDraftAsync(d1, null, CancellationToken.None);
        var task2 = manager.ApplyDraftAsync(d2, null, CancellationToken.None);
        var results = await Task.WhenAll(task1, task2);

        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
        results[0].VersionId.Should().NotBe(results[1].VersionId);
    }

    // ========================================================================
    // B2 close-out: pin C2-R1, C2-R2, mutation gaps
    // ========================================================================

    /// <summary>
    /// C2-R1 pin: if WriteCurrent fails, the audit log must NOT be appended.
    /// Catches the original ordering bug where audit was written before
    /// current.json hit disk.
    /// </summary>
    [Fact]
    public async Task Apply_WriteCurrentFails_AuditNotAppended()
    {
        var (manager, store) = await CreateInitializedAsync();
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
        var auditLinesBefore = store.AuditLineCount;

        store.FailOnOperation = "WriteCurrent";

        var act = async () => await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);
        await act.Should().ThrowAsync<System.IO.IOException>();

        store.AuditLineCount.Should().Be(auditLinesBefore,
            "audit must not be appended if the durable write fails");
    }

    /// <summary>
    /// Mutation #1 pin: WriteCurrent must be called BEFORE AppendAuditLine
    /// during apply. Locks the C2-R1 ordering against accidental regression.
    /// </summary>
    [Fact]
    public async Task Apply_OperationOrder_Pinned()
    {
        var (manager, store) = await CreateInitializedAsync();
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);

        // Snapshot the operation log AFTER setup, then apply.
        var preApplyCount = store.OperationLog.Count;
        await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);

        var applyOps = store.OperationLog.Skip(preApplyCount).ToList();
        var writeCurrentIdx = applyOps.IndexOf("WriteCurrent");
        var appendAuditIdx = applyOps.IndexOf("AppendAuditLine");

        writeCurrentIdx.Should().BeGreaterOrEqualTo(0);
        appendAuditIdx.Should().BeGreaterOrEqualTo(0);
        writeCurrentIdx.Should().BeLessThan(appendAuditIdx,
            "WriteCurrent is the durable commit point and must precede the audit append");
    }

    /// <summary>
    /// C2-R2 pin: even if DeleteDraftAsync throws, the apply must report
    /// success and the in-memory state must be promoted to the new version.
    /// </summary>
    [Fact]
    public async Task Apply_DeleteDraftFails_StateStillPromoted()
    {
        var (manager, store) = await CreateInitializedAsync();
        var oldVersion = manager.CurrentVersionId;
        var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);

        store.FailOnOperation = "DeleteDraft";

        var result = await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);

        result.Success.Should().BeTrue();
        manager.CurrentVersionId.Should().NotBe(oldVersion);
        manager.CurrentVersionId.Should().Be(result.VersionId);
    }

    /// <summary>
    /// Mutation #6 pin: rollback must not overwrite the existing history
    /// file for the version it is rolling back to. The history file content
    /// must be preserved verbatim.
    /// </summary>
    [Fact]
    public async Task Rollback_DoesNotClobberHistory()
    {
        var (manager, store) = await CreateInitializedAsync();
        var d1 = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
        var apply1 = await manager.ApplyDraftAsync(d1, null, CancellationToken.None);
        var v1 = apply1.VersionId;

        // Apply a second change so v1 is in history.
        var d2 = await manager.CreateDraftAsync(B2TestFixtures.ValidMinimal(), null, CancellationToken.None);
        await manager.ApplyDraftAsync(d2, null, CancellationToken.None);

        var historyBefore = await store.ReadHistoryAsync(v1, CancellationToken.None);
        historyBefore.Should().NotBeNull();

        // Roll back to v1.
        var rollback = await manager.RollbackAsync(v1, null, CancellationToken.None);
        rollback.Success.Should().BeTrue();

        var historyAfter = await store.ReadHistoryAsync(v1, CancellationToken.None);
        historyAfter.Should().Be(historyBefore, "rollback must not modify the source history file");
    }

    /// <summary>
    /// Mutation #9 pin: discarding one draft must not affect any other
    /// draft owned by the manager.
    /// </summary>
    [Fact]
    public async Task DiscardDraft_OtherDraftsUnaffected()
    {
        var (manager, _) = await CreateInitializedAsync();
        var d1 = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
        var d2 = await manager.CreateDraftAsync(B2TestFixtures.ValidMinimal(), null, CancellationToken.None);

        await manager.DiscardDraftAsync(d1, null, CancellationToken.None);

        (await manager.GetDraftAsync(d1, CancellationToken.None)).Should().BeNull();
        (await manager.GetDraftAsync(d2, CancellationToken.None)).Should().NotBeNull();
    }
}
