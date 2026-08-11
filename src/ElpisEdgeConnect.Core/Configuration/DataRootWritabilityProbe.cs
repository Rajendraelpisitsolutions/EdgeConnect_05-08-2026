// ============================================================================
// File: Configuration/DataRootWritabilityProbe.cs
// Purpose: Startup pre-flight that checks whether the gateway data root is
//          writable by the current process identity. Surfaces an unwritable
//          data dir (e.g. Windows ProgramData files created under an elevated
//          context, leaving a non-admin runtime user read-only) EARLY and
//          CLEARLY — as a CORE.CONFIG_DATA_NOT_WRITABLE fault — instead of
//          letting it fail deep inside the first audit-writing operation
//          (config apply/rollback, diagnostic-bundle generation) with a raw
//          UnauthorizedAccessException.
//
//          Probes are side-effect-free: directory writability is checked with
//          a create-then-delete of a uniquely-named temp file; existing files
//          are opened for write and closed WITHOUT writing (FileMode.Append /
//          FileMode.Open never modify content). Cross-platform: a write-open on
//          a non-writable path throws on Windows and Linux alike.
// Reference: docs/sessions/2026-05-31-followup-chips.md (item 1); ADR-0003
//            (fail-soft startup is default).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>The outcome of a <see cref="DataRootWritabilityProbe"/> pass.</summary>
public sealed record DataRootWritabilityResult
{
    /// <summary>True when every probed surface is writable by the current identity.</summary>
    public required bool IsWritable { get; init; }

    /// <summary>
    /// Human-readable description of each unwritable surface (path + what
    /// failed). Empty when <see cref="IsWritable"/> is true.
    /// </summary>
    public required IReadOnlyList<string> Issues { get; init; }

    /// <summary>The all-clear result.</summary>
    public static DataRootWritabilityResult Writable { get; } =
        new() { IsWritable = true, Issues = Array.Empty<string>() };
}

/// <summary>
/// Checks that the gateway can write the files it needs to append/replace at
/// runtime: the configuration <c>history</c> directory (new versions, drafts,
/// atomic-write temp files), the append-only <c>audit.log</c>, and the active
/// <c>current.json</c>. Detection only — never attempts to repair permissions.
/// </summary>
public static class DataRootWritabilityProbe
{
    /// <summary>
    /// Probe the writability of the data root described by
    /// <paramref name="layout"/>. Side-effect-free (no content is written; any
    /// temp probe file is deleted before returning).
    /// </summary>
    public static DataRootWritabilityResult Probe(ConfigurationStorageLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var issues = new List<string>();

        // 1. The history directory must accept new files (history versions,
        //    drafts, and the *.tmp files atomic writes stage there).
        ProbeDirectory(layout.HistoryDirectory, issues);

        // 2. The audit log is APPENDED in place (FileMode.Append on the existing
        //    file) — the exact operation that fails when a non-owner runtime
        //    user inherited read-only on a ProgramData file. Only meaningful
        //    once the file exists; before that, directory writability covers it.
        ProbeExistingFile(layout.AuditLogPath, FileMode.Append, "append to audit log", issues);

        // 3. current.json is replaced on every config apply. Write-open is a
        //    proxy for "can modify" (the atomic rename also needs the dir to be
        //    writable, covered by step 1).
        ProbeExistingFile(layout.CurrentConfigPath, FileMode.Open, "modify active configuration", issues);

        return issues.Count == 0
            ? DataRootWritabilityResult.Writable
            : new DataRootWritabilityResult { IsWritable = false, Issues = issues };
    }

    private static void ProbeDirectory(string directory, List<string> issues)
    {
        try
        {
            Directory.CreateDirectory(directory); // idempotent; also probes parent writability
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            issues.Add($"{directory} — cannot create files ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static void ProbeExistingFile(string path, FileMode mode, string what, List<string> issues)
    {
        // Only probe files that already exist — a missing file is covered by the
        // directory probe, and opening it would create an empty file (a side
        // effect we explicitly avoid).
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            // FileMode.Append / FileMode.Open with FileAccess.Write opens for
            // writing WITHOUT truncating; closing immediately writes nothing.
            using (new FileStream(path, mode, FileAccess.Write, FileShare.ReadWrite))
            {
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            issues.Add($"{path} — cannot {what} ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
