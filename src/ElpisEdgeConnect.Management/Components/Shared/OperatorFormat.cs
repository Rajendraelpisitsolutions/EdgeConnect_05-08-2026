// ============================================================================
// File: Components/Shared/OperatorFormat.cs
// Purpose: Operator-facing formatting helpers shared across Studio pages and
//          wizards. Turns raw values (UTC timestamps, byte counts, enum
//          members, protocol keys) into short, human-readable strings so the
//          UI never shows a raw ISO timestamp, an un-separated large number,
//          or a lowercase protocol slug to an operator.
//
// This is a plain .cs file (not .razor) on purpose: the Razor parser misreads
// the '<' in switch expressions / generic comparisons, so this logic must live
// outside a component.
//
// Reference: operator-UI redesign report §10 (shared formatting helper).
// ============================================================================

using System;
using System.Globalization;
using ElpisEdgeConnect.Core.Configuration;
using MudBlazor;

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>
/// Static, allocation-light formatting helpers for operator-facing text.
/// Every member is null-safe and returns a friendly placeholder rather than
/// throwing or emitting a raw machine value.
/// </summary>
public static class OperatorFormat
{
    private static readonly TimeSpan DefaultStaleWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Renders a coarse relative age such as <c>"2 min ago"</c>. A
    /// <see langword="null"/> or <see cref="DateTime.MinValue"/> input returns
    /// <c>"never"</c>. Future timestamps (clock skew) render as <c>"just now"</c>.
    /// </summary>
    /// <param name="utc">The instant, interpreted as UTC.</param>
    public static string Ago(DateTime? utc)
    {
        if (!TryNormalizeUtc(utc, out var value))
        {
            return "never";
        }

        var delta = DateTime.UtcNow - value;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalSeconds < 45)
        {
            return "just now";
        }
        if (delta.TotalMinutes < 60)
        {
            var mins = (int)Math.Round(delta.TotalMinutes);
            return $"{mins} min ago";
        }
        if (delta.TotalHours < 24)
        {
            var hrs = (int)Math.Round(delta.TotalHours);
            return $"{hrs} hr ago";
        }
        if (delta.TotalDays < 7)
        {
            var days = (int)Math.Floor(delta.TotalDays);
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }

        var weeks = (int)Math.Floor(delta.TotalDays / 7);
        return weeks == 1 ? "1 week ago" : $"{weeks} weeks ago";
    }

    /// <summary>
    /// Renders the instant as a local-time string (never a raw ISO timestamp).
    /// A <see langword="null"/> or <see cref="DateTime.MinValue"/> input returns
    /// <c>"never"</c>.
    /// </summary>
    /// <param name="utc">The instant, interpreted as UTC.</param>
    public static string Absolute(DateTime? utc)
    {
        if (!TryNormalizeUtc(utc, out var value))
        {
            return "never";
        }

        // Local, culture-friendly "MMM d, yyyy h:mm tt" — deliberately not the
        // "o"/round-trip ISO form an operator should never have to read.
        return value.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Reports whether <paramref name="utc"/> is older than <paramref name="window"/>
    /// (default 5 minutes). A <see langword="null"/> or <see cref="DateTime.MinValue"/>
    /// input is considered stale (no fresh data has ever been seen).
    /// </summary>
    /// <param name="utc">The instant, interpreted as UTC.</param>
    /// <param name="window">Freshness window; defaults to 5 minutes when omitted.</param>
    public static bool IsStale(DateTime? utc, TimeSpan? window = null)
    {
        if (!TryNormalizeUtc(utc, out var value))
        {
            return true;
        }

        var w = window ?? DefaultStaleWindow;
        return DateTime.UtcNow - value > w;
    }

    /// <summary>
    /// Formats an integral count with thousands separators (e.g. <c>1234567</c>
    /// → <c>"1,234,567"</c>).
    /// </summary>
    public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats a byte count as a compact human-readable size such as
    /// <c>"1.2 MB"</c>. Zero renders as <c>"0 B"</c> (never a dash). Negative
    /// inputs are clamped to <c>"0 B"</c>.
    /// </summary>
    public static string Bytes(long value)
    {
        if (value <= 0)
        {
            return "0 B";
        }

        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        // Whole bytes stay integer; scaled units get one decimal place.
        return unit == 0
            ? $"{(long)size} {units[unit]}"
            : string.Format(CultureInfo.CurrentCulture, "{0:0.0} {1}", size, units[unit]);
    }

    /// <summary>
    /// Maps a <see cref="BufferMode"/> to an operator-friendly description of
    /// what the mode means for data durability.
    /// </summary>
    public static string BufferModeLabel(BufferMode mode) => mode switch
    {
        BufferMode.None => "No buffering (drops on outage)",
        BufferMode.InMemory => "In-memory (lost on restart)",
        BufferMode.StoreAndForward => "Store & forward (survives restart)",
        _ => mode.ToString(),
    };

    /// <summary>
    /// Maps a protocol/sink kind slug (as stored in <c>gateway.json</c>) to its
    /// display name — e.g. <c>"opcua-server"</c> → <c>"OPC UA Server"</c>. Unknown
    /// or empty kinds fall back to the trimmed original so nothing renders blank.
    /// </summary>
    public static string SinkKindLabel(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "Unknown";
        }

        return kind.Trim().ToLowerInvariant() switch
        {
            "mqtt" => "MQTT",
            "opcua-server" => "OPC UA Server",
            "opcua-client" => "OPC UA Client",
            "http" => "HTTP webhook",
            "tcp" => "TCP",
            _ => kind.Trim(),
        };
    }

    /// <summary>
    /// Material icon carrying the same meaning as an adapter/route status colour,
    /// so state is never signalled by colour alone.
    ///
    /// Roughly one operator in twelve cannot separate the green from the red, and
    /// a factory floor is rarely well lit — a green dot and a red dot are the same
    /// dot. Every status indicator in the Studio therefore pairs this glyph with
    /// the colour, and (wherever there is room) with a word as well.
    ///
    /// Deliberately shape-distinct, not merely different icons: a tick, a
    /// triangle, a pause bar and an octagonal error mark are separable in
    /// monochrome and at 16px.
    /// </summary>
    /// <param name="stateName">Adapter or route state name as reported by the API.</param>
    public static string StateIcon(string? stateName) => stateName switch
    {
        "Running" => Icons.Material.Filled.CheckCircle,
        "Degraded" or "Configured / Not running" => Icons.Material.Filled.Warning,
        "Failed" or "Faulted" or "Blocked" => Icons.Material.Filled.Error,
        "Stopped" or "Stopping" => Icons.Material.Filled.PauseCircleOutline,
        "Disabled" => Icons.Material.Filled.PowerSettingsNew,
        "Starting" or "Draining" => Icons.Material.Filled.Sync,
        "Configured" => Icons.Material.Filled.RadioButtonUnchecked,
        _ => Icons.Material.Filled.HelpOutline,
    };

    /// <summary>
    /// <see cref="StateIcon(string?)"/> for a destination, which carries two
    /// runtime flags the adapter state name does not: a degraded sink is
    /// refusing data, and a draining one is finishing its queue before it stops.
    /// Checked in that order so the more urgent condition wins — mirrors the
    /// precedence every <c>StateColor(RouteSinkSummaryDto)</c> already uses.
    /// </summary>
    /// <param name="isDegraded">True when the sink is rejecting data.</param>
    /// <param name="isDraining">True when the sink is draining its buffer.</param>
    /// <param name="adapterStateName">Adapter state name as reported by the API.</param>
    public static string SinkStateIcon(bool isDegraded, bool isDraining, string? adapterStateName)
    {
        if (isDegraded)
        {
            return Icons.Material.Filled.Warning;
        }
        if (isDraining)
        {
            return Icons.Material.Filled.Sync;
        }
        return StateIcon(adapterStateName);
    }

    /// <summary>
    /// Normalizes a nullable <see cref="DateTime"/> to a UTC value, rejecting
    /// <see langword="null"/> and <see cref="DateTime.MinValue"/> (the "never"
    /// sentinels). A <see cref="DateTimeKind.Unspecified"/> input is assumed UTC.
    /// </summary>
    private static bool TryNormalizeUtc(DateTime? utc, out DateTime value)
    {
        value = default;
        if (utc is null || utc.Value == default)
        {
            return false;
        }

        value = utc.Value.Kind switch
        {
            DateTimeKind.Utc => utc.Value,
            DateTimeKind.Local => utc.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc),
        };
        return true;
    }
}
