// ============================================================================
// File: Diagnostics/DataIssueLog.cs
// Purpose: Best-effort, append-only diagnostics log for DATA ISSUES (Bad/null
//          points) and ERRORS, written PER SOURCE to
//          %ProgramData%\EdgeConnect\logs\<sourceInstanceId>.txt.
//          Each entry is tagged DEVICE (connectivity / slave / transport /
//          config-at-device) or CODE (decode / internal adapter fault) so an
//          operator can tell a wiring/PLC problem from a software problem, and
//          each source keeps its own file so problems don't blur together.
//
//          Writes are THROTTLED per source+issue key (default 30s) because the
//          poll loop re-emits a Bad point for every tag on every cycle — without
//          a throttle the file would grow by thousands of identical lines/minute.
//          Each file self-rotates once it passes a size cap. Any IO failure is
//          swallowed: diagnostics logging must never disrupt the data pipeline.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Diagnostics;

/// <summary>
/// Per-source append-only text log of data issues and errors under
/// <c>%ProgramData%\EdgeConnect\logs\&lt;source&gt;.txt</c>. Thread-safe and throttled.
///
/// <para>
/// Logging is OPT-IN per source: nothing is written unless the operator has
/// enabled logging for that source (Overview card → Logs). The enabled set is
/// persisted to <c>logs\logging-enabled.json</c> so the choice survives restarts
/// and is shared between the writer (this class, in the adapter) and the reader
/// (the management API that toggles it) via one file — no cross-project coupling.
/// </para>
/// </summary>
public static class DataIssueLog
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, DateTime> LastWrittenUtc = new(StringComparer.Ordinal);
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(30);
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB per source file, then rotate to .1

    // Cached "which sources have logging enabled" set, re-read from disk on a
    // short TTL so a UI toggle takes effect within a few seconds without a file
    // read on every log call.
    private static readonly TimeSpan EnabledTtl = TimeSpan.FromSeconds(3);
    private static HashSet<string> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _enabledLoadedUtc = DateTime.MinValue;

    /// <summary>Directory holding the per-source log files.</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EdgeConnect",
        "logs");

    /// <summary>Path of the file listing the source ids that have logging enabled.</summary>
    public static string EnabledFilePath => Path.Combine(LogDirectory, "logging-enabled.json");

    /// <summary>Absolute path of the log file for a given source instance.</summary>
    public static string FilePathFor(string source) =>
        Path.Combine(LogDirectory, SanitizeFileName(source) + ".txt");

    /// <summary>True when the operator has enabled logging for <paramref name="source"/>.</summary>
    public static bool IsEnabled(string source)
    {
        var now = DateTime.UtcNow;
        lock (Sync)
        {
            if (now - _enabledLoadedUtc > EnabledTtl)
            {
                _enabled = LoadEnabled();
                _enabledLoadedUtc = now;
            }
            return _enabled.Contains(source);
        }
    }

    /// <summary>
    /// Turn logging on/off for one source and persist the choice. Also creates a
    /// first log line when turning on so the file exists immediately.
    /// </summary>
    public static void SetEnabled(string source, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }
        lock (Sync)
        {
            var set = LoadEnabled();
            if (enabled)
            {
                set.Add(source);
            }
            else
            {
                set.Remove(source);
            }
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.WriteAllText(EnabledFilePath, JsonSerializer.Serialize(set.ToArray()));
            }
            catch
            {
                // Best-effort persistence.
            }
            _enabled = set;
            _enabledLoadedUtc = DateTime.UtcNow;
        }
        if (enabled)
        {
            Session(source, "logging ENABLED by operator — recording device connectivity and data reads.");
        }
    }

    private static HashSet<string> LoadEnabled()
    {
        try
        {
            if (File.Exists(EnabledFilePath))
            {
                var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(EnabledFilePath));
                if (ids is not null)
                {
                    return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // Corrupt/absent file → nothing enabled.
        }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensure the source's log file exists, writing an un-throttled <c>INFO</c>
    /// line. Call this when a source starts so the file is present for reading
    /// even before any issue occurs (and so restarts are recorded). Creates the
    /// <c>logs</c> directory and the file if missing.
    /// </summary>
    public static void Session(string source, string detail)
    {
        if (!IsEnabled(source))
        {
            return;
        }
        var now = DateTime.UtcNow;
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var path = FilePathFor(source);
                RotateIfNeeded(path);
                File.AppendAllText(
                    path,
                    $"{now:yyyy-MM-dd HH:mm:ss.fff}Z [INFO] source={source} {detail}{Environment.NewLine}");
            }
            catch
            {
                // Best-effort only.
            }
        }
    }

    /// <summary>
    /// Append one issue line to the source's own log file, unless an identical
    /// source+<paramref name="issueKey"/> was logged within the throttle window
    /// (in which case it is silently skipped).
    /// </summary>
    /// <param name="category">Classification — <c>DEVICE</c> or <c>CODE</c>.</param>
    /// <param name="source">Source instance id — selects the log file.</param>
    /// <param name="issueKey">Stable de-dupe key for throttling repeats.</param>
    /// <param name="detail">Human-readable detail (codes, addresses, reason).</param>
    public static void Log(string category, string source, string issueKey, string detail)
    {
        if (!IsEnabled(source))
        {
            return;
        }
        var now = DateTime.UtcNow;
        var throttleKey = source + "|" + issueKey;
        lock (Sync)
        {
            if (LastWrittenUtc.TryGetValue(throttleKey, out var last) && now - last < ThrottleWindow)
            {
                return;
            }
            LastWrittenUtc[throttleKey] = now;

            try
            {
                Directory.CreateDirectory(LogDirectory);
                var path = FilePathFor(source);
                RotateIfNeeded(path);
                File.AppendAllText(
                    path,
                    $"{now:yyyy-MM-dd HH:mm:ss.fff}Z [{category}] source={source} {detail}{Environment.NewLine}");
            }
            catch
            {
                // Best-effort only — never let a log write break the pipeline.
            }
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxBytes)
            {
                return;
            }
            var rolled = path + ".1";
            if (File.Exists(rolled))
            {
                File.Delete(rolled);
            }
            File.Move(path, rolled);
        }
        catch
        {
            // Rotation is opportunistic; ignore failures.
        }
    }

    /// <summary>Make a source id safe to use as a file name.</summary>
    private static string SanitizeFileName(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "unknown-source";
        }
        var invalid = Path.GetInvalidFileNameChars();
        var chars = source.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }
        return new string(chars);
    }
}
