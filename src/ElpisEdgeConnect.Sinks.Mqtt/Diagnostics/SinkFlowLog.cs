// ============================================================================
// File: Diagnostics/SinkFlowLog.cs
// Purpose: Append "posted to destination" lines to the SAME per-source
//          diagnostic log the source adapter writes
//          (%ProgramData%\EdgeConnect\logs\<source>.txt), so one source's log
//          shows the full data flow: value received FROM the source, then value
//          posted TO each destination. Gated by the same opt-in
//          logging-enabled.json the source side uses — coordinated purely
//          through files, with no code coupling between the protocol projects.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ElpisEdgeConnect.Sinks.Mqtt.Diagnostics;

/// <summary>Per-source "posted to destination" logger for the MQTT sink. Opt-in + throttled.</summary>
public static class SinkFlowLog
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, DateTime> LastWrittenUtc = new(StringComparer.Ordinal);
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(30);
    private const long MaxBytes = 10 * 1024 * 1024;

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EdgeConnect",
        "logs");

    private static readonly string EnabledFilePath = Path.Combine(LogDirectory, "logging-enabled.json");

    private static readonly TimeSpan EnabledTtl = TimeSpan.FromSeconds(3);
    private static HashSet<string> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _enabledLoadedUtc = DateTime.MinValue;

    /// <summary>
    /// Record that a tag's value was posted to a destination. No-op unless
    /// logging is enabled for <paramref name="source"/>. Throttled per
    /// source+tag so the file stays readable.
    /// </summary>
    public static void Post(string source, string destination, string tag, string topic, string value, string result)
    {
        if (string.IsNullOrWhiteSpace(source) || !IsEnabled(source))
        {
            return;
        }
        var now = DateTime.UtcNow;
        var key = source + "|post|" + tag;
        lock (Sync)
        {
            if (LastWrittenUtc.TryGetValue(key, out var last) && now - last < ThrottleWindow)
            {
                return;
            }
            LastWrittenUtc[key] = now;
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, Sanitize(source) + ".txt");
                Rotate(path);
                File.AppendAllText(
                    path,
                    $"{now:yyyy-MM-dd HH:mm:ss.fff}Z [POST]"
                        + $" | Source name: {source}"
                        + $" | Destination: {destination} (mqtt)"
                        + $" | Tag name: {tag}"
                        + $" | Topic: {topic}"
                        + $" | Value posted to destination: {value}"
                        + $" | Result: {result}"
                        + Environment.NewLine);
            }
            catch (Exception)
            {
                // Best-effort — logging must never disrupt publishing.
            }
        }
    }

    private static bool IsEnabled(string source)
    {
        var now = DateTime.UtcNow;
        lock (Sync)
        {
            if (now - _enabledLoadedUtc > EnabledTtl)
            {
                _enabled = Load();
                _enabledLoadedUtc = now;
            }
            return _enabled.Contains(source);
        }
    }

    private static HashSet<string> Load()
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

    private static void Rotate(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length > MaxBytes)
            {
                var rolled = path + ".1";
                if (File.Exists(rolled))
                {
                    File.Delete(rolled);
                }
                File.Move(path, rolled);
            }
        }
        catch (IOException)
        {
            // Rotation is opportunistic.
        }
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
