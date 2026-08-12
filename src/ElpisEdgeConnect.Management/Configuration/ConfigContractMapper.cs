// ============================================================================
// File: Configuration/ConfigContractMapper.cs
// Purpose: Pure static mappers from Core configuration types to the
//          M.2a wire DTOs. Mirror pattern from
//          Diagnostics/RouteSummaryMapper — stateless, side-effect-free,
//          fully unit-testable in isolation.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2a
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Contracts.Config;

namespace ElpisEdgeConnect.Management.Configuration;

/// <summary>
/// Maps Core configuration types into the management API's wire DTOs.
/// Pure / stateless — register-once, no DI needed for the static methods.
/// </summary>
public static class ConfigContractMapper
{
    /// <summary>Project one validation issue.</summary>
    public static ValidationProblemDto ToProblem(ValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new ValidationProblemDto
        {
            Code = issue.Code,
            Message = issue.Message,
            Path = issue.Path,
        };
    }

    /// <summary>Project one validation result, stamping ValidatedAtUtc with the caller's clock.</summary>
    public static ValidationResultDto ToValidationResult(ValidationResult result, DateTime validatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ValidationResultDto
        {
            IsValid = result.IsValid,
            Errors = result.Errors.Select(ToProblem).ToList(),
            Warnings = result.Warnings.Select(ToProblem).ToList(),
            ValidatedAtUtc = validatedAtUtc,
        };
    }

    /// <summary>Project one Core change record. Enum names render as strings on the wire.</summary>
    public static ConfigChangeDto ToConfigChange(ConfigurationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new ConfigChangeDto
        {
            Kind = change.Kind.ToString(),
            EntityKind = change.EntityKind.ToString(),
            EntityId = change.EntityId,
            Path = change.Path,
            Summary = change.Summary,
        };
    }

    /// <summary>Project the change list from a diff or an audit entry.</summary>
    public static IReadOnlyList<ConfigChangeDto> ToConfigChanges(IReadOnlyList<ConfigurationChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var result = new List<ConfigChangeDto>(changes.Count);
        foreach (var c in changes) result.Add(ToConfigChange(c));
        return result;
    }

    /// <summary>
    /// Project a Core <see cref="ConfigurationApplyResult"/> into the wire-shape
    /// success response. Assumes <c>result.Success == true</c>; the
    /// caller is responsible for routing failures to a
    /// <see cref="ValidationResultDto"/> 409 response instead.
    /// </summary>
    public static ApplyResultDto ToApplyResult(
        ConfigurationApplyResult result,
        ConfigurationVersionId previousVersionId,
        string? gatewayId,
        ReloadOutcome? reload = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success)
        {
            throw new ArgumentException(
                "ConfigurationApplyResult must be Success=true; map failures to ValidationResultDto instead.",
                nameof(result));
        }
        var audit = result.AuditEntry ?? throw new ArgumentException(
            "Successful ConfigurationApplyResult must carry an AuditEntry.",
            nameof(result));

        return new ApplyResultDto
        {
            NewVersionId = result.VersionId.Value,
            PreviousVersionId = previousVersionId.IsEmpty ? string.Empty : previousVersionId.Value,
            AppliedAtUtc = audit.Timestamp,
            Actor = audit.Actor,
            Changes = ToConfigChanges(audit.Changes),
            Warnings = result.ValidationResult.Warnings.Select(ToProblem).ToList(),
            GatewayId = gatewayId,
            Reload = reload is null ? null : ToReloadOutcome(reload),
        };
    }

    /// <summary>
    /// Project a Core <see cref="ReloadOutcome"/> into the wire DTO.
    /// Status / Kind enums are translated to stable strings so additions
    /// to the Core enums do not silently leak through to API consumers.
    /// </summary>
    public static ReloadOutcomeDto ToReloadOutcome(ReloadOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new ReloadOutcomeDto
        {
            Status = outcome.Status switch
            {
                ReloadStatus.Completed => "Completed",
                ReloadStatus.InProgress => "InProgress",
                ReloadStatus.Skipped => "Skipped",
                _ => outcome.Status.ToString(),
            },
            NewVersionId = outcome.NewVersionId.Value,
            AppliedInstances = outcome.AppliedInstances.ToList(),
            RestartedInstances = outcome.RestartedInstances.ToList(),
            FaultedInstances = outcome.FaultedInstances.Select(ToFaultedReloadEntry).ToList(),
            SupersededBy = outcome.SupersededBy is { } s && !s.IsEmpty ? s.Value : null,
            ElapsedMs = outcome.ElapsedMs,
        };
    }

    /// <summary>Project one <see cref="FaultedReloadEntry"/> to the wire DTO.</summary>
    public static FaultedReloadEntryDto ToFaultedReloadEntry(FaultedReloadEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new FaultedReloadEntryDto
        {
            InstanceId = entry.InstanceId,
            Kind = entry.Kind switch
            {
                ConfigurationFaultKind.Source => "Source",
                ConfigurationFaultKind.Sink => "Sink",
                ConfigurationFaultKind.Route => "Route",
                _ => entry.Kind.ToString(),
            },
            ErrorCode = entry.ErrorCode,
            Message = entry.Message,
        };
    }

    /// <summary>
    /// Build a placeholder <see cref="ReloadOutcomeDto"/> for the
    /// <c>InProgress</c> case (apply endpoint's wait window elapsed
    /// before the coordinator enqueued an outcome). The instance lists
    /// are empty and <see cref="ReloadOutcomeDto.ElapsedMs"/> carries
    /// the wait-window length so the operator sees how long the API
    /// waited.
    /// </summary>
    public static ReloadOutcomeDto InProgressPlaceholder(
        ConfigurationVersionId newVersionId,
        long waitElapsedMs) => new()
    {
        Status = "InProgress",
        NewVersionId = newVersionId.Value,
        AppliedInstances = Array.Empty<string>(),
        RestartedInstances = Array.Empty<string>(),
        FaultedInstances = Array.Empty<FaultedReloadEntryDto>(),
        SupersededBy = null,
        ElapsedMs = waitElapsedMs,
    };

    /// <summary>
    /// Project a Core history entry. The <paramref name="isCurrent"/> flag
    /// is computed against <c>IConfigurationManager.CurrentVersionId</c>
    /// outside the mapper since the comparison is request-scoped state.
    /// </summary>
    public static HistoryEntryDto ToHistoryEntry(ConfigurationHistoryEntry entry, bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new HistoryEntryDto
        {
            VersionId = entry.VersionId.Value,
            AppliedAtUtc = entry.AppliedAt,
            Summary = entry.Summary,
            SizeBytes = entry.SizeBytes,
            IsCurrent = isCurrent,
        };
    }

    /// <summary>
    /// Project a draft id + audit / filesystem metadata into the wire-shape
    /// drafts-list row.
    /// </summary>
    /// <remarks>
    /// The API endpoint builds the <paramref name="auditLookup"/> dictionary
    /// once per request by walking the audit log; this mapper just consumes
    /// it. Drafts that have no corresponding <c>CONFIG.DRAFT_CREATED</c>
    /// audit entry (legacy / out-of-band) get <c>Actor = "unknown"</c> and
    /// fall back to the file's creation time.
    /// </remarks>
    public static DraftMetadataDto ToDraftMetadata(
        DraftId draftId,
        long sizeBytes,
        DateTime fallbackCreatedAtUtc,
        IReadOnlyDictionary<string, (string Actor, DateTime CreatedAtUtc)> auditLookup,
        string? gatewayId)
    {
        ArgumentNullException.ThrowIfNull(auditLookup);
        var actor = "unknown";
        var createdAt = fallbackCreatedAtUtc;
        if (auditLookup.TryGetValue(draftId.Value, out var info))
        {
            actor = info.Actor;
            createdAt = info.CreatedAtUtc;
        }
        return new DraftMetadataDto
        {
            DraftId = draftId.Value,
            CreatedAtUtc = createdAt,
            Actor = actor,
            SizeBytes = sizeBytes,
            GatewayId = gatewayId,
        };
    }
}
