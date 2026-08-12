// ============================================================================
// File: Configuration/IConfigurationStore.cs
// Purpose: Storage abstraction over the configuration filesystem layout.
//          Production code uses FileSystemConfigurationStore; tests
//          substitute an in-memory implementation.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Abstraction over the configuration filesystem layout. The interface is
/// intentionally string-typed (not <see cref="GatewayConfiguration"/>) so
/// the store concerns itself only with bytes; serialization happens in the
/// configuration manager.
/// </summary>
/// <remarks>
/// All read methods return null when the requested artifact is missing.
/// All write methods are atomic — a successful return guarantees the
/// content is durably visible to subsequent reads (modulo OS-level fsync
/// semantics, which the production implementation honors).
/// </remarks>
public interface IConfigurationStore
{
    /// <summary>
    /// Read the active configuration JSON from the store. Returns null if
    /// the file does not exist (e.g., on first start before any apply).
    /// </summary>
    ValueTask<string?> ReadCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atomically replace the active configuration JSON. The previous
    /// content is no longer visible after this call returns.
    /// </summary>
    ValueTask WriteCurrentAsync(string json, CancellationToken cancellationToken);

    /// <summary>
    /// List all draft ids currently stored. Order is unspecified.
    /// </summary>
    ValueTask<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Read a draft's JSON content. Returns null if the draft does not exist.
    /// </summary>
    ValueTask<string?> ReadDraftAsync(DraftId draftId, CancellationToken cancellationToken);

    /// <summary>
    /// Write a new draft. Overwrites any existing draft with the same id.
    /// </summary>
    ValueTask WriteDraftAsync(DraftId draftId, string json, CancellationToken cancellationToken);

    /// <summary>
    /// Delete a draft. No-op if the draft does not exist.
    /// </summary>
    ValueTask DeleteDraftAsync(DraftId draftId, CancellationToken cancellationToken);

    /// <summary>
    /// List all history version ids currently stored, in chronological
    /// order (oldest first).
    /// </summary>
    ValueTask<IReadOnlyList<ConfigurationVersionId>> ListHistoryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Read a history version's JSON content. Returns null if not found.
    /// </summary>
    ValueTask<string?> ReadHistoryAsync(ConfigurationVersionId versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Write a new history file. Should not overwrite an existing file
    /// (the version id is supposed to be unique); implementations may
    /// choose to overwrite or to throw on conflict.
    /// </summary>
    ValueTask WriteHistoryAsync(ConfigurationVersionId versionId, string json, CancellationToken cancellationToken);

    /// <summary>
    /// Delete a history file. No-op if the file does not exist. Used by
    /// the retention policy to prune old versions.
    /// </summary>
    ValueTask DeleteHistoryAsync(ConfigurationVersionId versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Append a single audit log line. The line MUST NOT contain a newline
    /// (the store appends one). The append is atomic at the OS level.
    /// </summary>
    ValueTask AppendAuditLineAsync(string line, CancellationToken cancellationToken);

    /// <summary>
    /// Stream every audit log line in order. Yields the raw JSON text of
    /// each line; deserialization is the caller's responsibility.
    /// </summary>
    IAsyncEnumerable<string> ReadAuditLinesAsync(CancellationToken cancellationToken);
}
