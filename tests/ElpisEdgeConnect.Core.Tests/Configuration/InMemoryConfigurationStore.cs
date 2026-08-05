// ============================================================================
// File: Configuration/InMemoryConfigurationStore.cs
// Purpose: Test helper — IConfigurationStore that lives entirely in memory.
//          Used by all B2 unit tests so they don't touch the filesystem.
// ============================================================================

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

/// <summary>
/// In-memory <see cref="IConfigurationStore"/> for unit tests. Thread-safe
/// at the per-call level via concurrent collections; no atomicity
/// guarantees beyond what the underlying dictionary provides (which is
/// fine for single-threaded test execution).
/// </summary>
internal sealed class InMemoryConfigurationStore : IConfigurationStore
{
    private string? _current;
    private readonly ConcurrentDictionary<string, string> _drafts = new();
    private readonly ConcurrentDictionary<string, string> _history = new();
    private readonly List<string> _auditLines = new();
    private readonly object _auditLock = new();
    private readonly List<string> _operationLog = new();
    private readonly object _opLock = new();

    /// <summary>Test hook: name of the operation to throw on (e.g., "WriteCurrent", "DeleteDraft", "AppendAuditLine"). Null disables.</summary>
    public string? FailOnOperation { get; set; }

    /// <summary>Snapshot of operation names in call order. Used to pin call ordering in tests.</summary>
    public IReadOnlyList<string> OperationLog
    {
        get
        {
            lock (_opLock)
            {
                return new List<string>(_operationLog);
            }
        }
    }

    private void Record(string op)
    {
        lock (_opLock)
        {
            _operationLog.Add(op);
        }
        if (FailOnOperation == op)
        {
            throw new System.IO.IOException($"Injected fault on {op}.");
        }
    }

    /// <summary>Number of audit lines currently stored. Used by tests for assertions.</summary>
    public int AuditLineCount
    {
        get
        {
            lock (_auditLock)
            {
                return _auditLines.Count;
            }
        }
    }

    /// <summary>Test hook: read a specific audit line by index.</summary>
    public string GetAuditLine(int index)
    {
        lock (_auditLock)
        {
            return _auditLines[index];
        }
    }

    /// <summary>Test hook: corrupt one audit line by replacing its content.</summary>
    public void CorruptAuditLine(int index, string replacement)
    {
        lock (_auditLock)
        {
            _auditLines[index] = replacement;
        }
    }

    public ValueTask<string?> ReadCurrentAsync(CancellationToken cancellationToken) =>
        new(_current);

    public ValueTask WriteCurrentAsync(string json, CancellationToken cancellationToken)
    {
        Record("WriteCurrent");
        _current = json;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken)
    {
        var list = new List<DraftId>(_drafts.Keys.Count);
        foreach (var key in _drafts.Keys)
        {
            list.Add(new DraftId(key));
        }
        return new ValueTask<IReadOnlyList<DraftId>>(list);
    }

    public ValueTask<string?> ReadDraftAsync(DraftId draftId, CancellationToken cancellationToken)
    {
        _drafts.TryGetValue(draftId.Value, out var json);
        return new ValueTask<string?>(json);
    }

    public ValueTask WriteDraftAsync(DraftId draftId, string json, CancellationToken cancellationToken)
    {
        _drafts[draftId.Value] = json;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteDraftAsync(DraftId draftId, CancellationToken cancellationToken)
    {
        Record("DeleteDraft");
        _drafts.TryRemove(draftId.Value, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ConfigurationVersionId>> ListHistoryAsync(CancellationToken cancellationToken)
    {
        var list = new List<ConfigurationVersionId>(_history.Keys.Count);
        var sorted = new SortedSet<string>(_history.Keys, System.StringComparer.Ordinal);
        foreach (var key in sorted)
        {
            list.Add(new ConfigurationVersionId(key));
        }
        return new ValueTask<IReadOnlyList<ConfigurationVersionId>>(list);
    }

    public ValueTask<string?> ReadHistoryAsync(ConfigurationVersionId versionId, CancellationToken cancellationToken)
    {
        _history.TryGetValue(versionId.Value, out var json);
        return new ValueTask<string?>(json);
    }

    public ValueTask WriteHistoryAsync(ConfigurationVersionId versionId, string json, CancellationToken cancellationToken)
    {
        Record("WriteHistory");
        _history[versionId.Value] = json;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteHistoryAsync(ConfigurationVersionId versionId, CancellationToken cancellationToken)
    {
        _history.TryRemove(versionId.Value, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask AppendAuditLineAsync(string line, CancellationToken cancellationToken)
    {
        Record("AppendAuditLine");
        lock (_auditLock)
        {
            _auditLines.Add(line);
        }
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<string> ReadAuditLinesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Snapshot the lines under the lock so iteration is consistent
        // even if another caller appends concurrently.
        List<string> snapshot;
        lock (_auditLock)
        {
            snapshot = new List<string>(_auditLines);
        }

        foreach (var line in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
            await Task.Yield();
        }
    }
}
