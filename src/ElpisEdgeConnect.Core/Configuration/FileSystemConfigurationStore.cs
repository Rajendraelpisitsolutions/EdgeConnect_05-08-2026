// ============================================================================
// File: Configuration/FileSystemConfigurationStore.cs
// Purpose: Production IConfigurationStore that writes to the real filesystem.
//          Uses an atomic write pattern (write to .tmp, fsync, rename) so a
//          crash mid-write leaves the previous content intact.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2 (atomic writes)
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Production <see cref="IConfigurationStore"/> backed by the real
/// filesystem. Implements atomic writes by writing to a temp file, flushing
/// to disk, and renaming over the destination.
/// </summary>
/// <remarks>
/// <para>
/// Atomic write contract: <c>WriteCurrentAsync</c> and <c>WriteHistoryAsync</c>
/// guarantee that a successful return means the new content is durably
/// visible. A crash between the temp-file write and the rename leaves the
/// previous content intact (modulo OS-level fsync semantics).
/// </para>
/// <para>
/// The store is thread-safe at the call level (each call is independent)
/// but the file system itself is the only synchronization primitive. The
/// configuration manager holds an async mutex around its mutating sequences
/// to keep audit + history + current.json transitions consistent.
/// </para>
/// </remarks>
public sealed class FileSystemConfigurationStore : IConfigurationStore
{
    private readonly ConfigurationStorageLayout _layout;

    /// <summary>Construct a store rooted at the given storage layout.</summary>
    public FileSystemConfigurationStore(ConfigurationStorageLayout layout)
    {
        System.ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _layout.EnsureDirectoriesExist();
    }

    /// <inheritdoc/>
    public async ValueTask<string?> ReadCurrentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_layout.CurrentConfigPath))
        {
            return null;
        }
        return await File.ReadAllTextAsync(_layout.CurrentConfigPath, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask WriteCurrentAsync(string json, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(json);
        await AtomicWriteAsync(_layout.CurrentConfigPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_layout.DraftsDirectory))
        {
            return new ValueTask<IReadOnlyList<DraftId>>((IReadOnlyList<DraftId>)System.Array.Empty<DraftId>());
        }

        var ids = Directory.EnumerateFiles(_layout.DraftsDirectory, "*.json")
            .Select(p => Path.GetFileNameWithoutExtension(p)!)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => new DraftId(n))
            .ToList();

        return new ValueTask<IReadOnlyList<DraftId>>(ids);
    }

    /// <inheritdoc/>
    public async ValueTask<string?> ReadDraftAsync(DraftId draftId, CancellationToken cancellationToken)
    {
        var path = _layout.DraftPath(draftId);
        if (!File.Exists(path))
        {
            return null;
        }
        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask WriteDraftAsync(DraftId draftId, string json, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(json);
        await AtomicWriteAsync(_layout.DraftPath(draftId), json, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask DeleteDraftAsync(DraftId draftId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = _layout.DraftPath(draftId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<ConfigurationVersionId>> ListHistoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_layout.HistoryDirectory))
        {
            return new ValueTask<IReadOnlyList<ConfigurationVersionId>>(
                (IReadOnlyList<ConfigurationVersionId>)System.Array.Empty<ConfigurationVersionId>());
        }

        // Filter out audit.log; only *.json files are version snapshots.
        var versions = Directory.EnumerateFiles(_layout.HistoryDirectory, "*.json")
            .Select(p => Path.GetFileNameWithoutExtension(p)!)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .Select(n => new ConfigurationVersionId(n))
            .ToList();

        return new ValueTask<IReadOnlyList<ConfigurationVersionId>>(versions);
    }

    /// <inheritdoc/>
    public async ValueTask<string?> ReadHistoryAsync(ConfigurationVersionId versionId, CancellationToken cancellationToken)
    {
        var path = _layout.HistoryPath(versionId);
        if (!File.Exists(path))
        {
            return null;
        }
        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask WriteHistoryAsync(ConfigurationVersionId versionId, string json, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(json);
        await AtomicWriteAsync(_layout.HistoryPath(versionId), json, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask DeleteHistoryAsync(ConfigurationVersionId versionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = _layout.HistoryPath(versionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask AppendAuditLineAsync(string line, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(line);
        if (line.Contains('\n'))
        {
            throw new System.ArgumentException("Audit lines must not contain newlines.", nameof(line));
        }

        // Open in append mode with WriteThrough so the OS flushes the
        // append before the call returns. This is the strongest atomicity
        // guarantee available without per-line fsync overhead.
        await using var stream = new FileStream(
            _layout.AuditLogPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough);

        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ReadAuditLinesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!File.Exists(_layout.AuditLogPath))
        {
            yield break;
        }

        using var reader = new StreamReader(_layout.AuditLogPath, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }

    // ------------------------------------------------------------------------
    // Atomic write: write to {path}.tmp, flush to disk, rename over {path}.
    // A crash between the temp write and the rename leaves the previous
    // content intact. The rename is atomic on POSIX and on NTFS.
    // ------------------------------------------------------------------------
    private static async ValueTask AtomicWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = path + ".tmp";

        await using (var stream = new FileStream(
            tmp,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough))
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        // Atomic rename over the destination. File.Move with overwrite=true
        // is atomic on Windows (since .NET 6) and on POSIX.
        File.Move(tmp, path, overwrite: true);
    }
}
