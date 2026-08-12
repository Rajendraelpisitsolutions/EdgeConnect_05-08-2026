// ============================================================================
// File: Configuration/ConfigurationIntegrationTests.cs
// Covers: Full B2 lifecycle against the real FileSystemConfigurationStore.
//         The closest B2 comes to true integration testing — every test
//         uses a temp directory and exercises the manager end-to-end.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class ConfigurationIntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _root;
    private readonly ConfigurationStorageLayout _layout;
    private readonly FileSystemConfigurationStore _store;

    public ConfigurationIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "edgeconnect-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _layout = new ConfigurationStorageLayout(_root);
        _store = new FileSystemConfigurationStore(_layout);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private async Task<ConfigurationManager> InitializeManagerAsync()
    {
        var initial = JsonSerializer.Serialize(B2TestFixtures.ValidMinimal(), JsonOptions);
        await _store.WriteCurrentAsync(initial, CancellationToken.None);
        var manager = new ConfigurationManager(_store);
        await manager.InitializeAsync(CancellationToken.None);
        return manager;
    }

    [Fact]
    public async Task FullLifecycle_CreateValidateApplyReadRollback()
    {
        var manager = await InitializeManagerAsync();
        var v0 = manager.CurrentVersionId;

        // Create a draft
        var newConfig = B2TestFixtures.ValidWithMultiple();
        var draftId = await manager.CreateDraftAsync(newConfig, "alice", CancellationToken.None);

        // Validate it
        var validation = await manager.ValidateDraftAsync(draftId, CancellationToken.None);
        validation.IsValid.Should().BeTrue();

        // Apply it
        var apply = await manager.ApplyDraftAsync(draftId, "alice", CancellationToken.None);
        apply.Success.Should().BeTrue();
        var v1 = apply.VersionId;

        // Read the new current
        var current = await manager.GetCurrentAsync(CancellationToken.None);
        current.Sources.Should().HaveCount(2);

        // Roll back
        var rollback = await manager.RollbackAsync(v0, "bob", CancellationToken.None);
        rollback.Success.Should().BeTrue();
        rollback.VersionId.Should().NotBe(v0);
        rollback.VersionId.Should().NotBe(v1);

        // Final state matches the original
        var rolledBack = await manager.GetCurrentAsync(CancellationToken.None);
        rolledBack.Sources.Should().HaveCount(1);
    }

    [Fact]
    public async Task Restart_ManagerRebuildsStateFromDisk()
    {
        // First manager: apply something and dispose
        await using (var manager1 = await InitializeManagerAsync())
        {
            var draftId = await manager1.CreateDraftAsync(
                B2TestFixtures.ValidWithMultiple(),
                "actor",
                CancellationToken.None);
            await manager1.ApplyDraftAsync(draftId, "actor", CancellationToken.None);
        }

        // Second manager pointed at the same store should pick up the applied config
        var manager2 = new ConfigurationManager(_store);
        await manager2.InitializeAsync(CancellationToken.None);

        var current = await manager2.GetCurrentAsync(CancellationToken.None);
        current.Sources.Should().HaveCount(2);
    }

    [Fact]
    public async Task Restart_AuditChainSurvivesRestart()
    {
        await using (var manager1 = await InitializeManagerAsync())
        {
            for (var i = 0; i < 3; i++)
            {
                var draftId = await manager1.CreateDraftAsync(
                    B2TestFixtures.ValidWithMultiple(),
                    null,
                    CancellationToken.None);
                await manager1.ApplyDraftAsync(draftId, null, CancellationToken.None);
            }
        }

        var manager2 = new ConfigurationManager(_store);
        await manager2.InitializeAsync(CancellationToken.None);

        var entries = new System.Collections.Generic.List<ConfigurationAuditEntry>();
        await foreach (var e in manager2.GetAuditLogAsync(verifyChain: true, CancellationToken.None))
        {
            entries.Add(e);
        }

        entries.Should().NotBeEmpty();
        entries.Count(e => e.Action == ConfigurationAuditAction.Applied).Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task LongRun_TwentyApplies_HistoryHonorsRetention()
    {
        var manager = await InitializeManagerAsync();

        for (var i = 0; i < 25; i++)
        {
            var draftId = await manager.CreateDraftAsync(
                B2TestFixtures.ValidWithMultiple(),
                null,
                CancellationToken.None);
            var result = await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        var history = await _store.ListHistoryAsync(CancellationToken.None);
        history.Count.Should().BeLessThanOrEqualTo(20);
    }

    [Fact]
    public async Task DraftPersistence_DraftSurvivesRestart()
    {
        // Create a draft, dispose, then re-init: the draft must still be there.
        DraftId draftId;
        await using (var manager1 = await InitializeManagerAsync())
        {
            draftId = await manager1.CreateDraftAsync(
                B2TestFixtures.ValidWithMultiple(),
                null,
                CancellationToken.None);
        }

        var manager2 = new ConfigurationManager(_store);
        await manager2.InitializeAsync(CancellationToken.None);

        var loaded = await manager2.GetDraftAsync(draftId, CancellationToken.None);
        loaded.Should().NotBeNull();
        loaded!.Sources.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyInvalidDraft_AtomicFailure_NoFilesystemSideEffects()
    {
        var manager = await InitializeManagerAsync();

        // Invalid draft: route references missing source
        var bad = B2TestFixtures.ValidMinimal() with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "bad",
                    Name = "Bad",
                    SourceInstanceId = "ghost",
                    SinkInstanceIds = ["sink-1"],
                },
            ],
        };
        var draftId = await manager.CreateDraftAsync(bad, null, CancellationToken.None);

        var historyBefore = (await _store.ListHistoryAsync(CancellationToken.None)).Count;
        var currentBefore = await _store.ReadCurrentAsync(CancellationToken.None);

        var result = await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);

        result.Success.Should().BeFalse();
        var historyAfter = (await _store.ListHistoryAsync(CancellationToken.None)).Count;
        var currentAfter = await _store.ReadCurrentAsync(CancellationToken.None);

        historyAfter.Should().Be(historyBefore, "failed apply must not write history");
        currentAfter.Should().Be(currentBefore, "failed apply must not modify current.json");
    }

    [Fact]
    public async Task SchemaValidatorSubstitution_FileLoadFailsWithSchemaError()
    {
        // Substitute a stub schema validator that always fails. The manager
        // should treat this as a corrupt-config error during initialize.
        var initial = JsonSerializer.Serialize(B2TestFixtures.ValidMinimal(), JsonOptions);
        await _store.WriteCurrentAsync(initial, CancellationToken.None);

        var alwaysFail = new AlwaysFailingSchemaValidator();
        var manager = new ConfigurationManager(_store, alwaysFail);

        var act = async () => await manager.InitializeAsync(CancellationToken.None);
        await act.Should().ThrowAsync<ElpisEdgeConnect.Core.Errors.ConfigurationException>();
    }

    private sealed class AlwaysFailingSchemaValidator : IConfigurationSchemaValidator
    {
        public ValueTask<ElpisEdgeConnect.Core.Adapters.ValidationResult> ValidateAsync(string json, CancellationToken cancellationToken)
        {
            return new ValueTask<ElpisEdgeConnect.Core.Adapters.ValidationResult>(
                ElpisEdgeConnect.Core.Adapters.ValidationResult.Failure("CORE.CONFIG_SCHEMA_FAIL", "Stubbed failure"));
        }
    }
}
