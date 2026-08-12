// ============================================================================
// File: Backup/IBackupBuilder.cs
// Purpose: Single seam for assembling a configuration backup zip.
//          Streams directly to a caller-provided destination stream
//          (typically the HTTP response body) so big audit logs never
//          balloon disk. The API endpoint just wires the zip stream
//          to the response and sets Content-Disposition.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.3
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Management.Backup;

/// <summary>
/// Builds a configuration backup zip and writes it to the supplied
/// stream. Includes manifest, redacted current config, audit log,
/// and redacted history snapshots.
/// </summary>
public interface IBackupBuilder
{
    /// <summary>
    /// Compose a backup archive into <paramref name="destination"/>.
    /// The stream is left open after writing — the caller (typically
    /// the HTTP response pipeline) handles disposal.
    /// </summary>
    /// <param name="destination">Writable stream to receive the zip bytes.</param>
    /// <param name="exportReason">Provenance string for the manifest (see <see cref="Contracts.BackupManifest.ExportReason"/>).</param>
    /// <param name="ct">Cancellation token honoured during disk reads and audit-log enumeration.</param>
    /// <returns>The filename the API endpoint should serve via Content-Disposition.</returns>
    Task<string> BuildAsync(Stream destination, string exportReason, CancellationToken ct);
}
