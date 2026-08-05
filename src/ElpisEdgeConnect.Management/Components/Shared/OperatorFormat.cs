// OperatorFormat.cs — operator-facing value formatting for the Studio UI.
//
// Purpose: one place that turns raw runtime values into words a maintenance
// engineer reads. Replaces several copy-pasted private helpers that had already
// drifted apart (Config.razor, Bundle.razor, Checklist.razor, Diagnostics.razor,
// Sources.razor, SourceDetail.razor, SinkDetail.razor, RouteDetail.razor).
//
// This lives in a .cs file rather than a @code block on purpose. The Razor
// parser misreads `<` in a switch expression as an opening HTML tag — see the
// comment at Config.razor:581 that forced if/else there — so the readable form
// of this logic is only available outside .razor.
//
// UI layer only. Implements no architectural decision and touches no contract.

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>
/// Formats runtime values as operator-facing text. Every method tolerates
/// missing data and returns readable placeholder text rather than throwing
/// or rendering an empty cell.
/// </summary>
public static class OperatorFormat
{
    /// <summary>
    /// How old an error may be before it stops counting as a live fault.
    /// A 19-minute-old error rendered as a current alarm sends an operator to
    /// a machine that recovered long ago — the single most expensive thing
    /// this UI can get wrong.
    /// </summary>
    public static readonly TimeSpan DefaultStaleWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// <see cref="DefaultStaleWindow"/> rendered for operator-facing copy, so
    /// page text that quotes the freshness rule cannot drift from the constant
    /// that enforces it.
    /// </summary>
    public static string DefaultStaleWindowText =>
        DefaultStaleWindow.TotalMinutes == 1
            ? "1 minute"
            : ((int)DefaultStaleWindow.TotalMinutes).ToString(CultureInfo.CurrentCulture) + " minutes";

    /// <summary>
    /// Normalises a timestamp to UTC. Values reaching the Studio over JSON
    /// carry Kind=Utc, but values read in-process can arrive Unspecified;
    /// treating those as local would shift every age by the site's offset.
    /// </summary>
    private static DateTime? AsUtc(DateTime? value)
    {
        if (value is not { } v)
        {
            return null;
        }

        return v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        };
    }

    /// <summary>
    /// Age as an operator reads it: "2 min ago". Pair every call with a
    /// title="@OperatorFormat.Absolute(x)" so the exact instant stays
    /// available on hover.
    /// </summary>
    public static string Ago(DateTime? utc)
    {
        if (AsUtc(utc) is not { } v)
        {
            return "never";
        }

        var age = DateTime.UtcNow - v;
        var seconds = age.TotalSeconds;

        if (seconds < 5) return "just now";
        if (seconds < 60) return $"{(int)seconds} sec ago";
        if (seconds < 3600) return $"{(int)age.TotalMinutes} min ago";

        if (seconds < 86400)
        {
            var hours = (int)age.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        var days = (int)age.TotalDays;
        return days == 1 ? "1 day ago" : $"{days} days ago";
    }

    /// <summary>Exact local instant, for the hover title behind <see cref="Ago"/>.</summary>
    public static string Absolute(DateTime? utc)
    {
        if (AsUtc(utc) is not { } v)
        {
            return "No timestamp recorded";
        }

        return v.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) + " local";
    }

    /// <summary>
    /// True when a timestamp is too old to describe as current. A missing
    /// timestamp counts as stale: absent evidence is not evidence of a fault.
    /// </summary>
    public static bool IsStale(DateTime? utc, TimeSpan? window = null)
    {
        if (AsUtc(utc) is not { } v)
        {
            return true;
        }

        return DateTime.UtcNow - v > (window ?? DefaultStaleWindow);
    }

    /// <summary>Thousands-separated count: 10000 -> "10,000".</summary>
    public static string Count(long value) =>
        value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Byte size an operator can act on: 1257714 -> "1.2 MB". Zero renders as
    /// "0 B", not the em dash the old per-page copies used — an empty bundle
    /// and an unmeasured one are different facts.
    /// </summary>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024L) return $"{bytes / 1024d:N1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d):N1} MB";
        return $"{bytes / (1024d * 1024d * 1024d):N1} GB";
    }

    /// <summary>
    /// Buffer mode in operator language: what actually happens to data when a
    /// destination goes away. Shared so Overview, Routes, RouteDetail and the
    /// route wizard cannot describe the same mode three different ways.
    /// </summary>
    /// <summary>
    /// Config keys such as "opcua-server" are not words an operator uses.
    /// Shared so the list page and the detail page cannot drift apart.
    /// </summary>
    public static string SinkKindLabel(string kind) => kind switch
    {
        "mqtt" => "MQTT",
        "opcua-server" => "OPC UA Server",
        "http" => "HTTP",
        "tcp" => "TCP",
        "unknown" => "Not yet known",
        _ => kind,
    };

    public static string BufferModeLabel(string mode) => mode switch
    {
        "StoreAndForward" => "held on disk if a destination drops",
        "InMemory" => "held in memory — lost if the gateway restarts",
        "None" => "not buffered — dropped if a destination drops",
        _ => mode,
    };
}
