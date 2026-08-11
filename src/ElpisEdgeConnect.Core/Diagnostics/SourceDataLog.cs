// ============================================================================
// File: Diagnostics/SourceDataLog.cs
// Purpose: Shared, protocol-agnostic, OPT-IN per-source diagnostic log written
//          to %ProgramData%\EdgeConnect\logs\<sourceInstanceId>.txt. It is the
//          generalization of the Modbus adapter's DataIssueLog so that ANY
//          source adapter (S7, MELSEC, EtherNet/IP, ...) can record the same
//          "device connectivity + data read" trail without each protocol
//          project duplicating the logic or referencing another protocol
//          project (which the architecture forbids — adapters depend on Core
//          only). It lives in Core because it is pure infrastructure with no
//          protocol knowledge.
//
//          Coordination is purely file-based: the enabled-source set lives in
//          logs\logging-enabled.json — the SAME file the management API writes
//          when the operator toggles a source's "Logs" switch, and the same
//          file Modbus's DataIssueLog and the MQTT sink's SinkFlowLog read. All
//          writers therefore honour one opt-in choice and append to one
//          per-source file, so a source's log shows its full data flow.
//
//          Writes are THROTTLED per source+key (default 30s) because poll loops
//          re-read every tag on every cycle; each file self-rotates past a size
//          cap; every IO failure is swallowed — diagnostics logging must never
//          disrupt the data pipeline.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Opt-in, throttled, per-source diagnostic text log shared by all source
/// adapters. Nothing is written unless the operator has enabled logging for the
/// source (persisted in <c>logs\logging-enabled.json</c>). Thread-safe; all IO
/// is best-effort and never throws into the caller.
/// </summary>
public static class SourceDataLog
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

    /// <summary>Path of the JSON file listing the source ids that have logging enabled.</summary>
    public static string EnabledFilePath => Path.Combine(LogDirectory, "logging-enabled.json");

    /// <summary>Absolute path of the log file for a given source instance.</summary>
    /// <param name="source">The source instance id.</param>
    public static string FilePathFor(string source) =>
        Path.Combine(LogDirectory, SanitizeFileName(source) + ".txt");

    /// <summary>True when the operator has enabled logging for <paramref name="source"/>.</summary>
    /// <param name="source">The source instance id.</param>
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
    /// Ensure the source's log file exists, writing an un-throttled <c>INFO</c>
    /// line. Call when a source starts so the file is present (and restarts are
    /// recorded) even before any data or issue occurs. No-op when logging is
    /// disabled for the source.
    /// </summary>
    /// <param name="source">The source instance id (selects the log file).</param>
    /// <param name="detail">Human-readable session detail.</param>
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
    /// Append one line to the source's own log file, unless an identical
    /// source+<paramref name="issueKey"/> was written within the throttle window
    /// (then it is silently skipped). No-op when logging is disabled.
    /// </summary>
    /// <param name="category">Classification tag, e.g. <c>DATA</c>, <c>DEVICE</c>, or <c>CODE</c>.</param>
    /// <param name="source">Source instance id — selects the log file.</param>
    /// <param name="issueKey">Stable de-dupe key used for throttling repeats.</param>
    /// <param name="detail">Human-readable detail (labels, addresses, values, reason).</param>
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
