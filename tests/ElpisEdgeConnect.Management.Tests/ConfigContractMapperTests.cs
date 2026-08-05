// ============================================================================
// Tests: ConfigContractMapper — pure transformations Core → wire DTO.
//        Pinned because the DTOs ARE the public M.2a API contract.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Management.Configuration;
using ElpisEdgeConnect.Management.Contracts.Config;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class ConfigContractMapperTests
{
    [Fact]
    public void ToProblem_PreservesCodeMessageAndPath()
    {
        var issue = new ValidationIssue
        {
            Code = "CORE.CONFIG_MISSING_FIELD",
            Message = "host is required",
            Path = "sources[0].connection.host",
        };

        var dto = ConfigContractMapper.ToProblem(issue);

        dto.Code.Should().Be("CORE.CONFIG_MISSING_FIELD");
        dto.Message.Should().Be("host is required");
        dto.Path.Should().Be("sources[0].connection.host");
    }

    [Fact]
    public void ToValidationResult_DistinguishesErrorsAndWarnings()
    {
        var validatedAt = DateTime.UtcNow;
        var result = new ValidationResult
        {
            IsValid = false,
            Errors = new[]
            {
                new ValidationIssue { Code = "CORE.CONFIG_INVALID", Message = "bad" },
            },
            Warnings = new[]
            {
                new ValidationIssue { Code = "CORE.CONFIG_DEPRECATED_FIELD", Message = "deprecated" },
                new ValidationIssue { Code = "LICENSE.GRACE", Message = "in grace" },
            },
        };

        var dto = ConfigContractMapper.ToValidationResult(result, validatedAt);

        dto.IsValid.Should().BeFalse();
        dto.Errors.Should().HaveCount(1);
        dto.Warnings.Should().HaveCount(2);
        dto.ValidatedAtUtc.Should().Be(validatedAt);
    }

    [Fact]
    public void ToValidationResult_StampsCallerProvidedTimestamp()
    {
        // Wire-contract guard: ValidatedAtUtc is the API endpoint's clock,
        // not the Core validator's clock. This pins that mapping
        // direction so a future "use Core's timestamp" refactor doesn't
        // silently shift the field meaning.
        var pinned = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var result = ValidationResult.Success();

        var dto = ConfigContractMapper.ToValidationResult(result, pinned);
        dto.ValidatedAtUtc.Should().Be(pinned);
    }

    [Fact]
    public void ToConfigChange_EnumsRenderAsStrings()
    {
        var change = new ConfigurationChange
        {
            Kind = ConfigurationChangeKind.Added,
            EntityKind = ConfigurationEntityKind.Sink,
            EntityId = "mqtt-1",
            Path = null,
            Summary = "Added MQTT sink",
        };

        var dto = ConfigContractMapper.ToConfigChange(change);

        dto.Kind.Should().Be("Added");
        dto.EntityKind.Should().Be("Sink");
        dto.EntityId.Should().Be("mqtt-1");
        dto.Path.Should().BeNull();
        dto.Summary.Should().Be("Added MQTT sink");
    }

    [Fact]
    public void ToConfigChanges_PreservesOrder()
    {
        var changes = new[]
        {
            new ConfigurationChange { Kind = ConfigurationChangeKind.Added, EntityKind = ConfigurationEntityKind.Source, EntityId = "src-1", Summary = "added" },
            new ConfigurationChange { Kind = ConfigurationChangeKind.Modified, EntityKind = ConfigurationEntityKind.Route, EntityId = "r-1", Path = "buffer.mode", Summary = "buffer mode" },
            new ConfigurationChange { Kind = ConfigurationChangeKind.Removed, EntityKind = ConfigurationEntityKind.Sink, EntityId = "snk-1", Summary = "removed" },
        };

        var dtos = ConfigContractMapper.ToConfigChanges(changes);

        dtos.Should().HaveCount(3);
        dtos[0].EntityId.Should().Be("src-1");
        dtos[1].EntityId.Should().Be("r-1");
        dtos[2].EntityId.Should().Be("snk-1");
    }

    [Fact]
    public void ToApplyResult_PopulatesNewVersionPreviousAndChanges()
    {
        var newVersion = ConfigurationVersionId.NewId();
        var previousVersion = new ConfigurationVersionId("v-prev");
        var audit = new ConfigurationAuditEntry
        {
            Timestamp = new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc),
            VersionId = newVersion,
            Action = ConfigurationAuditAction.Applied,
            Actor = "alice",
            Summary = "Applied draft",
            PreviousHash = ConfigurationAuditLog.GenesisHash,
            Changes = new[]
            {
                new ConfigurationChange { Kind = ConfigurationChangeKind.Added, EntityKind = ConfigurationEntityKind.Sink, EntityId = "mqtt-1", Summary = "added MQTT sink" },
            },
        };
        var coreResult = new ConfigurationApplyResult
        {
            Success = true,
            VersionId = newVersion,
            ValidationResult = ValidationResult.Success(),
            AuditEntry = audit,
        };

        var dto = ConfigContractMapper.ToApplyResult(coreResult, previousVersion, gatewayId: "gw-test");

        dto.NewVersionId.Should().Be(newVersion.Value);
        dto.PreviousVersionId.Should().Be("v-prev");
        dto.Actor.Should().Be("alice");
        dto.AppliedAtUtc.Should().Be(audit.Timestamp);
        dto.Changes.Should().HaveCount(1);
        dto.Changes[0].EntityId.Should().Be("mqtt-1");
        dto.GatewayId.Should().Be("gw-test");
        dto.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ToApplyResult_ThrowsOnFailureResult()
    {
        // Caller is supposed to route Success=false to the
        // ValidationResultDto 409 response, not to ApplyResultDto.
        // The mapper enforces that contract.
        var failed = ConfigurationApplyResult.Failed(ValidationResult.Failure("X", "bad"));
        var prev = new ConfigurationVersionId("v-prev");

        var act = () => ConfigContractMapper.ToApplyResult(failed, prev, gatewayId: null);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Success=true*");
    }

    [Fact]
    public void ToApplyResult_HandlesEmptyPreviousVersion()
    {
        // First-ever apply has no previous — Core sets it to
        // ConfigurationVersionId.Initial-but-empty in some paths. The mapper
        // surfaces an empty string on the wire rather than the synthetic
        // "initial-0000-…" placeholder, so operators don't see noise.
        var newVersion = ConfigurationVersionId.NewId();
        var audit = new ConfigurationAuditEntry
        {
            Timestamp = DateTime.UtcNow,
            VersionId = newVersion,
            Action = ConfigurationAuditAction.Applied,
            Actor = "system",
            Summary = "first apply",
            PreviousHash = ConfigurationAuditLog.GenesisHash,
        };
        var coreResult = new ConfigurationApplyResult
        {
            Success = true,
            VersionId = newVersion,
            ValidationResult = ValidationResult.Success(),
            AuditEntry = audit,
        };

        var dto = ConfigContractMapper.ToApplyResult(coreResult, previousVersionId: default, gatewayId: null);

        dto.PreviousVersionId.Should().BeEmpty();
    }

    [Fact]
    public void ToHistoryEntry_FlagsCurrentVersion()
    {
        var entry = new ConfigurationHistoryEntry
        {
            VersionId = new ConfigurationVersionId("v-42"),
            AppliedAt = DateTime.UtcNow,
            Summary = "applied",
            SizeBytes = 9_400,
        };

        var current = ConfigContractMapper.ToHistoryEntry(entry, isCurrent: true);
        var older = ConfigContractMapper.ToHistoryEntry(entry, isCurrent: false);

        current.IsCurrent.Should().BeTrue();
        older.IsCurrent.Should().BeFalse();
        current.VersionId.Should().Be("v-42");
        current.SizeBytes.Should().Be(9_400);
    }

    [Fact]
    public void ToDraftMetadata_AuditLookupHitPopulatesActorAndCreatedAt()
    {
        var draftId = new DraftId("draft-test");
        var auditCreatedAt = new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);
        var lookup = new Dictionary<string, (string Actor, DateTime CreatedAtUtc)>
        {
            ["draft-test"] = ("alice", auditCreatedAt),
        };

        var dto = ConfigContractMapper.ToDraftMetadata(
            draftId, sizeBytes: 1_234,
            fallbackCreatedAtUtc: DateTime.UtcNow,  // should be ignored when lookup hits
            auditLookup: lookup,
            gatewayId: "gw-test");

        dto.Actor.Should().Be("alice");
        dto.CreatedAtUtc.Should().Be(auditCreatedAt);
        dto.SizeBytes.Should().Be(1_234);
        dto.GatewayId.Should().Be("gw-test");
    }

    [Fact]
    public void ToDraftMetadata_AuditLookupMissFallsBackToUnknownActorAndFileTime()
    {
        var draftId = new DraftId("draft-orphan");
        var fileCreated = new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc);
        var lookup = new Dictionary<string, (string Actor, DateTime CreatedAtUtc)>();

        var dto = ConfigContractMapper.ToDraftMetadata(
            draftId, sizeBytes: 500,
            fallbackCreatedAtUtc: fileCreated,
            auditLookup: lookup,
            gatewayId: null);

        dto.Actor.Should().Be("unknown",
            "audit log corruption or out-of-band file copy shouldn't break the list endpoint");
        dto.CreatedAtUtc.Should().Be(fileCreated);
    }

    // ─── ConfigApi helper tests ──────────────────────────────────────────

    [Theory]
    [InlineData(null, "system")]
    [InlineData("", "system")]
    [InlineData("   ", "system")]
    [InlineData("alice", "alice")]
    [InlineData("  alice  ", "alice")]
    public void NormaliseActor_DefaultsToSystemWhenNullOrEmpty(string? input, string expected)
    {
        ConfigApi.NormaliseActor(input).Should().Be(expected);
    }

    [Fact]
    public void TryParseDraftId_AcceptsValidIds()
    {
        var ok = ConfigApi.TryParseDraftId("draft-20260408T143022Z-a4f1b2", out var id, out var error);
        ok.Should().BeTrue();
        error.Should().BeEmpty();
        id.Value.Should().Be("draft-20260408T143022Z-a4f1b2");
    }

    [Fact]
    public void TryParseDraftId_RejectsEmptyAndWhitespace()
    {
        ConfigApi.TryParseDraftId("", out _, out var error1).Should().BeFalse();
        error1.Should().Contain("Invalid");

        ConfigApi.TryParseDraftId("   ", out _, out var error2).Should().BeFalse();
        error2.Should().Contain("Invalid");
    }

    // ════════════════════════════════════════════════════════════════
    // M.P2.2 phase 3 — ApplyResultDto.Reload + ReloadOutcomeResolver
    //
    // Pins the wire contract and the bridge that runs inside the apply
    // / rollback endpoints:
    //
    //   * ApplyResultDto JSON round-trips Reload=Completed and
    //     Reload=InProgress shapes.
    //   * Reload=null is OMITTED from the JSON entirely
    //     (JsonIgnoreCondition.WhenWritingNull). Critical: distinguishes
    //     "no observation surface" (null) from "still running" (InProgress).
    //   * ReloadOutcomeResolver returns the mapped DTO when the registry
    //     resolves inside the wait window, an InProgress placeholder on
    //     timeout, and null when no registry is registered.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyResultDto_JsonRoundTrip_WithReloadCompleted()
    {
        var dto = MakeApplyResult() with
        {
            Reload = new ReloadOutcomeDto
            {
                Status = "Completed",
                NewVersionId = "v-1",
                AppliedInstances = new[] { "src-a", "r-a" },
                RestartedInstances = new[] { "src-b" },
                FaultedInstances = new[]
                {
                    new FaultedReloadEntryDto
                    {
                        InstanceId = "src-c",
                        Kind = "Source",
                        ErrorCode = "HOST.RECONCILE_FAILED",
                        Message = "bang",
                    },
                },
                ElapsedMs = 137,
            },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        var roundtripped = System.Text.Json.JsonSerializer.Deserialize<ApplyResultDto>(json);

        roundtripped.Should().NotBeNull();
        roundtripped!.Reload.Should().NotBeNull();
        roundtripped.Reload!.Status.Should().Be("Completed");
        roundtripped.Reload.AppliedInstances.Should().BeEquivalentTo(new[] { "src-a", "r-a" });
        roundtripped.Reload.RestartedInstances.Should().BeEquivalentTo(new[] { "src-b" });
        roundtripped.Reload.FaultedInstances.Should().ContainSingle(f =>
            f.InstanceId == "src-c" && f.Kind == "Source" && f.ErrorCode == "HOST.RECONCILE_FAILED");
        roundtripped.Reload.ElapsedMs.Should().Be(137);
        roundtripped.Reload.SupersededBy.Should().BeNull();
    }

    [Fact]
    public void ApplyResultDto_JsonRoundTrip_WithReloadInProgress()
    {
        var dto = MakeApplyResult() with
        {
            Reload = new ReloadOutcomeDto
            {
                Status = "InProgress",
                NewVersionId = "v-2",
                AppliedInstances = Array.Empty<string>(),
                RestartedInstances = Array.Empty<string>(),
                FaultedInstances = Array.Empty<FaultedReloadEntryDto>(),
                ElapsedMs = 10_000,
            },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        var roundtripped = System.Text.Json.JsonSerializer.Deserialize<ApplyResultDto>(json);

        roundtripped!.Reload!.Status.Should().Be("InProgress");
        roundtripped.Reload.AppliedInstances.Should().BeEmpty();
        roundtripped.Reload.ElapsedMs.Should().Be(10_000);
    }

    [Fact]
    public void ApplyResultDto_JsonRoundTrip_WithoutReload_OmitsField()
    {
        // Critical UX invariant: Reload=null is NOT serialized as
        // `"reload": null` — the field is OMITTED. That keeps the wire
        // distinction between "no observation surface" (field absent)
        // and "still running" (field present with Status=InProgress).
        var dto = MakeApplyResult() with { Reload = null };

        var json = System.Text.Json.JsonSerializer.Serialize(dto);

        json.Should().NotContain("\"reload\"", "JsonIgnoreCondition.WhenWritingNull must omit the field entirely");
        var roundtripped = System.Text.Json.JsonSerializer.Deserialize<ApplyResultDto>(json);
        roundtripped!.Reload.Should().BeNull();
    }

    [Fact]
    public async Task ReloadOutcomeResolver_WhenReconcileFastEnough_ReturnsCompletedDto()
    {
        // Registry resolves the outcome inside the wait window → the
        // resolver returns the mapped DTO with Status=Completed.
        var registry = new ReloadOutcomeRegistry();
        var versionId = ConfigurationVersionId.NewId();
        registry.EnqueueCompleted(new ReloadOutcome
        {
            Status = ReloadStatus.Completed,
            NewVersionId = versionId,
            AppliedInstances = new[] { "src-fast" },
            RestartedInstances = Array.Empty<string>(),
            FaultedInstances = Array.Empty<FaultedReloadEntry>(),
            ElapsedMs = 12,
        });

        var sp = new ServiceCollection()
            .AddSingleton<IReloadOutcomeRegistry>(registry)
            .AddSingleton(new HostOptions
            {
                ConfigDirectory = "/tmp/cfg",
                LicensePath = "/tmp/lic",
                GatewayIdentityPath = "/tmp/id",
                ReloadOutcomeWaitMs = 5000,
            })
            .BuildServiceProvider();

        var dto = await ReloadOutcomeResolver.ResolveAsync(sp, versionId, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Status.Should().Be("Completed");
        dto.NewVersionId.Should().Be(versionId.Value);
        dto.AppliedInstances.Should().BeEquivalentTo(new[] { "src-fast" });
        dto.ElapsedMs.Should().Be(12);
    }

    [Fact]
    public async Task ReloadOutcomeResolver_WhenReconcileSlow_ReturnsInProgressDto()
    {
        // Registry is registered but the outcome never arrives → the
        // resolver hits its wait window and returns an InProgress
        // placeholder. Timeout is bounded; the call MUST return.
        var registry = new ReloadOutcomeRegistry();
        var versionId = ConfigurationVersionId.NewId();

        var sp = new ServiceCollection()
            .AddSingleton<IReloadOutcomeRegistry>(registry)
            .AddSingleton(new HostOptions
            {
                ConfigDirectory = "/tmp/cfg",
                LicensePath = "/tmp/lic",
                GatewayIdentityPath = "/tmp/id",
                ReloadOutcomeWaitMs = 50, // very short for test
            })
            .BuildServiceProvider();

        var dto = await ReloadOutcomeResolver.ResolveAsync(sp, versionId, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Status.Should().Be("InProgress");
        dto.NewVersionId.Should().Be(versionId.Value);
        dto.ElapsedMs.Should().Be(50, "InProgress placeholder reports the wait window the API observed");
        dto.AppliedInstances.Should().BeEmpty();
        dto.RestartedInstances.Should().BeEmpty();
        dto.FaultedInstances.Should().BeEmpty();
    }

    [Fact]
    public async Task ReloadOutcomeResolver_WhenRegistryNotRegistered_ReturnsNull()
    {
        // The critical UX invariant: no registry → resolver returns null,
        // NOT InProgress. ApplyResultDto.Reload is then omitted from the
        // wire by JsonIgnoreCondition.WhenWritingNull. Operators reading
        // an apply response see no Reload field at all — distinct from
        // an InProgress that says "poll diagnostics".
        var sp = new ServiceCollection().BuildServiceProvider();

        var dto = await ReloadOutcomeResolver.ResolveAsync(
            sp, ConfigurationVersionId.NewId(), CancellationToken.None);

        dto.Should().BeNull();
    }

    // ─── Helpers ──────────────────────────────────────────────────

    private static ApplyResultDto MakeApplyResult() => new()
    {
        NewVersionId = "v-roundtrip",
        PreviousVersionId = "v-prev",
        AppliedAtUtc = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc),
        Actor = "alice",
        Changes = Array.Empty<ConfigChangeDto>(),
        Warnings = Array.Empty<ValidationProblemDto>(),
        GatewayId = "gw-test",
    };
}
