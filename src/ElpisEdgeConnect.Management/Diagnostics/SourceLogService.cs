// ============================================================================
// File: Diagnostics/SourceLogService.cs
// Purpose: Read/write the per-source diagnostic log files that source adapters
//          write under %ProgramData%\EdgeConnect\logs. Logging is OPT-IN per
//          source; the enabled set lives in logging-enabled.json (a JSON array
//          of source ids) — the SAME file the adapter's DataIssueLog reads — so
//          toggling here turns adapter logging on/off with no code coupling to
//          any specific protocol project (they coordinate through the file).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>
/// File-backed accessor for the per-source diagnostic logs and their opt-in
/// enable flag. Stateless; every call reads/writes the files directly.
/// </summary>
public static class SourceLogService
{
    /// <summary>Directory holding the per-source log files (matches DataIssueLog).</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EdgeConnect",
        "logs");

    private static string EnabledFilePath => Path.Combine(LogDirectory, "logging-enabled.json");

    private static string LogFilePath(string source) =>
        Path.Combine(LogDirectory, Sanitize(source) + ".txt");

    /// <summary>True when logging is enabled for <paramref name="source"/>.</summary>
    public static bool IsEnabled(string source) => LoadEnabled().Contains(source);

    /// <summary>
    /// Turn logging on/off for one source and persist the choice. Enabling
    /// creates the log file immediately (so it exists in the logs folder as soon
    /// as the box is checked); disabling DELETES the source's log file(s) so no
    /// log file exists while the box is unchecked.
    /// </summary>
    public static void SetEnabled(string source, bool enabled)
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

            var logPath = LogFilePath(source);
            if (enabled)
            {
                // Create the file now so it's present the moment logging is on.
                File.AppendAllText(
                    logPath,
                    $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [INFO] source={source} "
                        + $"logging ENABLED — recording device connectivity and data reads.{Environment.NewLine}");
            }
            else
            {
                // Unchecked → no log file should exist.
                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }
                var rolled = logPath + ".1";
                if (File.Exists(rolled))
                {
                    File.Delete(rolled);
                }
            }
        }
        catch (IOException)
        {
            // Best-effort; the UI surfaces the true state via a re-read.
        }
    }

    /// <summary>
    /// Return the last <paramref name="maxLines"/> lines of the source's log
    /// file (newest at the end), or an empty list when logging has produced no
    /// file yet. Never throws.
    /// </summary>
    public static IReadOnlyList<string> ReadTail(string source, int maxLines)
    {
        var path = LogFilePath(source);
        if (maxLines <= 0 || !File.Exists(path))
        {
            return Array.Empty<string>();
        }
        try
        {
            var lines = File.ReadAllLines(path);
            return lines.Length <= maxLines ? lines : lines[^maxLines..];
        }
        catch (IOException)
        {
            return Array.Empty<string>();
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
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Absent / corrupt → nothing enabled.
        }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string Sanitize(string source)
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
