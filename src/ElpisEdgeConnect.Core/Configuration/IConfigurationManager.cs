// ============================================================================
// File: Configuration/IConfigurationManager.cs
// Purpose: The configuration manager contract — draft / validate / apply /
//          rollback lifecycle plus history and audit access. The host and
//          (eventually) the management REST API consume this interface.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2, ARCHITECTURE_BLUEPRINT.md §8.2
//
// LOCKED: This interface is the public surface that B2 ships. Adding methods
// is acceptable; removing or renaming methods is a breaking change.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Manages the lifecycle of the gateway's persistent configuration: load,
/// draft, validate, apply, rollback, history, and audit. Implementations
/// must serialize all mutating operations through a single mutex so that
/// concurrent applies don't corrupt the audit log or history.
/// </summary>
/// <remarks>
/// <para>
/// Per the Q4 design decision, no initial <see cref="CurrentChanged"/>
/// event is emitted during <see cref="InitializeAsync"/>; subscribers
/// must call <see cref="GetCurrentAsync"/> for their baseline.
/// </para>
/// </remarks>
public interface IConfigurationManager
{
    /// <summary>
    /// Initialise the manager: load the active configuration from the
    /// store, validate it, and discover the current version id from the
    /// audit log. Must be called once before any other method. Throws
    /// <c>ConfigurationException</c> with a clear error if the active
    /// configuration is missing, malformed, or fails validation.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>The version id of the currently active configuration.</summary>
    ConfigurationVersionId CurrentVersionId { get; }

    /// <summary>Read the active configuration. Cheap; the manager caches the deserialized form.</summary>
    ValueTask<GatewayConfiguration> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persist a new draft configuration. The draft is NOT validated at
    /// this stage; drafts can be invalid (the user might be iterating).
    /// </summary>
    Task<DraftId> CreateDraftAsync(
        GatewayConfiguration draft,
        string? actor,
        CancellationToken cancellationToken);

    /// <summary>Read a draft by id. Returns null if the draft does not exist.</summary>
    Task<GatewayConfiguration?> GetDraftAsync(DraftId draftId, CancellationToken cancellationToken);

    /// <summary>List every persisted draft id.</summary>
    Task<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Validate a draft through the full pipeline (DataAnnotations →
    /// cross-record → license gate). Does not modify state.
    /// </summary>
    Task<ValidationResult> ValidateDraftAsync(DraftId draftId, CancellationToken cancellationToken);

    /// <summary>
    /// Apply a draft as the new current configuration. Re-runs validation
    /// inside the apply mutex (defense against concurrent modifications)
    /// and atomically updates current.json + history + audit log + emits
    /// <see cref="CurrentChanged"/>. On validation failure no state changes.
    /// </summary>
    Task<ConfigurationApplyResult> ApplyDraftAsync(
        DraftId draftId,
        string? actor,
        CancellationToken cancellationToken);

    /// <summary>Discard a draft. No-op if the draft does not exist.</summary>
    Task DiscardDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken);

    /// <summary>
    /// Roll back to a previous version. Treated as a new apply operation:
    /// produces a fresh version id, writes a new audit entry marked
    /// <see cref="ConfigurationAuditAction.RolledBack"/>, and emits
    /// <see cref="CurrentChanged"/>. Does not overwrite history.
    /// </summary>
    Task<ConfigurationApplyResult> RollbackAsync(
        ConfigurationVersionId targetVersionId,
        string? actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// List configuration history (most recent first). Returns lightweight
    /// metadata only; the full configurations live in history files and
    /// are loaded on demand by <see cref="RollbackAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ConfigurationHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stream every entry in the audit log in order, optionally with
    /// hash-chain integrity verification.
    /// </summary>
    IAsyncEnumerable<ConfigurationAuditEntry> GetAuditLogAsync(
        bool verifyChain,
        CancellationToken cancellationToken);

    /// <summary>
    /// Append a system-generated runtime-fault audit entry. Used by the
    /// host's startup pipeline (M.P2.1) and the hot-reload coordinator
    /// (M.P2.2) when a cross-record validation failure is detected
    /// against the already-applied configuration. Actor is fixed to
    /// <c>"system"</c>, action to
    /// <see cref="ConfigurationAuditAction.RuntimeConfigurationFault"/>,
    /// and the version id is the current configuration's version (the
    /// version against which the fault was observed). Returns the entry
    /// that was written, so callers can verify hash chaining in tests.
    /// </summary>
    /// <remarks>
    /// This is the durable record of the fault; the live in-memory view
    /// lives in <see cref="Diagnostics.IConfigurationFaultRegistry"/>.
    /// </remarks>
    ValueTask<ConfigurationAuditEntry> AppendRuntimeFaultAsync(
        Diagnostics.ConfigurationFault fault,
        CancellationToken cancellationToken);

    /// <summary>
    /// Raised after a successful apply or rollback. The new current
    /// configuration is in the event payload along with the structured
    /// diff. No initial event is emitted during <see cref="InitializeAsync"/>.
    /// </summary>
    event EventHandler<ConfigurationChangeEventArgs>? CurrentChanged;
}
