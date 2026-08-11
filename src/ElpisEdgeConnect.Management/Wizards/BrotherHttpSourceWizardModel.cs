// ============================================================================
// File: Wizards/BrotherHttpSourceWizardModel.cs
// Purpose: In-memory state for the Add-Brother-HTTP-Source wizard. Razor
//          two-way-binds form fields to this POCO; on Save the wizard
//          calls BuildSourceInstance() to produce a canonical
//          SourceInstanceConfig (with Brother-specific fields packed into
//          the opaque Connection JsonElement, matching
//          BrotherHttpSourceConfiguration.FromSourceInstance).
//
//          Mirrors Focas2SourceWizardModel's pattern: a group picker for
//          DataPoints + identity / connection / backoff sections. No
//          Browse-Controller probe in v1 (Brother HTTP catalog is fixed —
//          discovery comes from the static BrotherTagMap).
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §10 step 11,
//            v3.1 §C.2 (demo state evolution), Q10 (polling clamps),
//            Q12 (wizard scope = minimum-viable but complete)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.BrotherHttp;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>Operator's data-point-selection choice (mirrors FOCAS2's enum).</summary>
public enum BrotherHttpDataPointSelectionMode
{
    /// <summary>Emit an empty <c>dataPoints</c> array (= adapter collects everything).</summary>
    CollectAll,

    /// <summary>Emit only the prefixes/paths for the operator's selected groups.</summary>
    Selective,
}

/// <summary>
/// One toggleable group in the Brother wizard's data-point picker. Maps a
/// category (e.g. "Tools") to the canonical-path prefixes that the runtime
/// adapter's DataPoints filter recognises.
/// </summary>
public sealed record BrotherHttpDataPointGroup(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<string> EmittedPaths);

/// <summary>
/// Wizard state for adding a new Brother HTTP source. Razor binds form
/// fields directly to these properties; on Save,
/// <see cref="BuildSourceInstance"/> produces a canonical
/// <see cref="SourceInstanceConfig"/>.
/// </summary>
public sealed class BrotherHttpSourceWizardModel
{
    /// <summary>Stable protocol identifier — matches BrotherHttpSourceConfiguration.ProtocolNameConstant.</summary>
    public const string ProtocolName = "brother-http";

    // ── Identity ────────────────────────────────────────────────────────

    /// <summary>Stable instance id (e.g. <c>"brother-line-A-cnc-1"</c>).</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device identifier.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device display name.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Device class — Brother sources are CNCs by definition.</summary>
    public string DeviceClass { get; set; } = "cnc";

    /// <summary>Whether the source is enabled when the new draft is applied.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Top-level polling interval in ms. Q10 default = 3000 (matches the 100-CNC working assumption).</summary>
    public int PollIntervalMs { get; set; } = 3000;

    // ── Connection ──────────────────────────────────────────────────────

    /// <summary>
    /// Brother CNC address — an IP, host name, or <c>host:port</c>
    /// (e.g. <c>"192.168.2.110"</c>); <c>http://</c> is implied and added by
    /// <see cref="BrotherHttpSourceConfiguration.TryNormalizeBaseUrl"/> when
    /// the operator omits it. Demo-mode
    /// (<c>EDGECONNECT_BROTHER_FAKE_MODE=true</c>) ignores this.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Consecutive HTTPD_MCNINFO failures before Faulted state (Q4).</summary>
    public int FaultThresholdConsecutiveFailures { get; set; } = 3;

    // ── Backoff (advanced) ─────────────────────────────────────────────

    /// <summary>Initial delay in ms after first endpoint failure.</summary>
    public int InitialBackoffMs { get; set; } = 5000;

    /// <summary>Maximum backoff delay in ms.</summary>
    public int MaxBackoffMs { get; set; } = 120_000;

    /// <summary>Multiplier applied to backoff on each consecutive failure.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    // ── Data points (group picker) ─────────────────────────────────────

    /// <summary>Operator's choice between "Collect all" (default) and "Limit to specific groups".</summary>
    public BrotherHttpDataPointSelectionMode DataPointsMode { get; set; } = BrotherHttpDataPointSelectionMode.CollectAll;

    /// <summary>Keys of the groups (from <see cref="DataPointGroups"/>) the operator has selected.</summary>
    public HashSet<string> SelectedGroupKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Catalogue of selectable data-point groups. Each group lists the
    /// canonical-path prefixes that activate its emission in
    /// <c>BrotherHttpSourceAdapter.BuildPoints</c>. MachineInfo and Status
    /// are intentionally NOT in this list — those are always emitted.
    /// </summary>
    public static readonly IReadOnlyList<BrotherHttpDataPointGroup> DataPointGroups = new[]
    {
        new BrotherHttpDataPointGroup(
            "program",
            "Program",
            "Active program O-number from /MNTP_CYCLETIME.",
            new[] { "Program/" }),

        new BrotherHttpDataPointGroup(
            "cycleTime",
            "Cycle time",
            "Cycle / cutting / operation / power-on / end-counter / cutting-ratio fields.",
            new[] { "CycleTime/" }),

        new BrotherHttpDataPointGroup(
            "production",
            "Production",
            "Parts count + per-counter slot count/target (counters 1-4, sparse).",
            new[] { "Production/" }),

        new BrotherHttpDataPointGroup(
            "tools",
            "Tools",
            "ATC magazine slot positions + per-tool name/type/life metadata.",
            new[] { "Tools/" }),

        new BrotherHttpDataPointGroup(
            "alarms",
            "Alarms",
            "Active alarms (after informational 0501 + maintenance-keyword filtering).",
            new[] { "Alarms/" }),

        new BrotherHttpDataPointGroup(
            "maintenance",
            "Maintenance",
            "Maintenance warnings (from alarm filter) + maintenance notices (from /MNTP_MAINTNOTICE).",
            new[] { "Maintenance/" }),
    };

    /// <summary>
    /// Build the runtime <c>dataPoints</c> list from the operator's selection.
    /// Mirrors FOCAS2's "every-group-selected collapses to empty" semantics.
    /// </summary>
    public IReadOnlyList<string> BuildDataPointsList()
    {
        if (DataPointsMode == BrotherHttpDataPointSelectionMode.CollectAll)
        {
            return Array.Empty<string>();
        }

        if (SelectedGroupKeys.Count == DataPointGroups.Count &&
            DataPointGroups.All(g => SelectedGroupKeys.Contains(g.Key)))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var group in DataPointGroups)
        {
            if (SelectedGroupKeys.Contains(group.Key))
            {
                result.AddRange(group.EmittedPaths);
            }
        }
        return result;
    }

    /// <summary>
    /// Project the wizard state into a canonical
    /// <see cref="SourceInstanceConfig"/>. The Brother-specific fields land
    /// in the opaque <c>Connection</c> <see cref="JsonElement"/> — the
    /// canonical type stays protocol-agnostic.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="BaseUrl"/> is blank or <see cref="TimeoutSeconds"/>
    /// is non-positive.
    /// </exception>
    public SourceInstanceConfig BuildSourceInstance()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException(
                "BaseUrl is required to build a Brother HTTP source — BrotherHttpSourceConfiguration.FromSourceInstance rejects empty BaseUrl.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"TimeoutSeconds must be > 0; got {TimeoutSeconds}.");
        }

        var dataPoints = BuildDataPointsList();

        // Persist the canonical form so the saved config carries an explicit
        // scheme even when the operator typed a bare IP. Unnormalisable input
        // is written back verbatim — config validation, not the wizard, owns
        // rejecting it, and quoting what was typed keeps that error readable.
        var canonicalBaseUrl =
            BrotherHttpSourceConfiguration.TryNormalizeBaseUrl(BaseUrl) ?? BaseUrl.Trim();

        var connection = new JsonObject
        {
            ["baseUrl"] = canonicalBaseUrl,
            ["timeoutSeconds"] = TimeoutSeconds,
            ["faultThresholdConsecutiveFailures"] = FaultThresholdConsecutiveFailures,
            ["initialBackoffMs"] = InitialBackoffMs,
            ["maxBackoffMs"] = MaxBackoffMs,
            ["backoffMultiplier"] = BackoffMultiplier,
        };

        var dpArray = new JsonArray();
        foreach (var path in dataPoints)
        {
            dpArray.Add(path);
        }
        connection["dataPoints"] = dpArray;

        var json = connection.ToJsonString();
        using var doc = JsonDocument.Parse(json);

        return new SourceInstanceConfig
        {
            InstanceId = InstanceId,
            ProtocolName = ProtocolName,
            DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? InstanceId : DeviceId,
            DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? InstanceId : DeviceName,
            DeviceClass = DeviceClass,
            Enabled = Enabled,
            Polling = new PollingSettings { IntervalMs = PollIntervalMs },
            Connection = doc.RootElement.Clone(),
        };
    }

    /// <summary>
    /// Inverse of <see cref="BuildSourceInstance"/> — populate a fresh wizard
    /// model from a canonical <see cref="SourceInstanceConfig"/>. Used by
    /// Edit-mode routing (M.2d.2 §5.5) to hydrate the wizard form with the
    /// source's current settings. Round-trip invariant: re-emitting after
    /// hydrate produces a byte-equivalent <see cref="SourceInstanceConfig"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/>'s <c>ProtocolName</c> is not
    /// <see cref="ProtocolName"/>.
    /// </exception>
    public static BrotherHttpSourceWizardModel HydrateFromExisting(SourceInstanceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProtocolName, ProtocolName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Source has protocol '{source.ProtocolName}', expected '{ProtocolName}'.",
                nameof(source));
        }

        var model = new BrotherHttpSourceWizardModel
        {
            InstanceId = source.InstanceId,
            DeviceId = source.DeviceId,
            DeviceName = source.DeviceName ?? string.Empty,
            DeviceClass = source.DeviceClass ?? "cnc",
            Enabled = source.Enabled,
            PollIntervalMs = source.Polling.IntervalMs,
        };

        if (source.Connection is not { } conn || conn.ValueKind != JsonValueKind.Object)
        {
            return model;
        }

        if (conn.TryGetProperty("baseUrl", out var url) && url.ValueKind == JsonValueKind.String)
        {
            model.BaseUrl = url.GetString() ?? string.Empty;
        }
        if (conn.TryGetProperty("timeoutSeconds", out var timeout) && timeout.TryGetInt32(out var timeoutValue))
        {
            model.TimeoutSeconds = timeoutValue;
        }
        if (conn.TryGetProperty("faultThresholdConsecutiveFailures", out var fault) && fault.TryGetInt32(out var faultValue))
        {
            model.FaultThresholdConsecutiveFailures = faultValue;
        }
        if (conn.TryGetProperty("initialBackoffMs", out var initBackoff) && initBackoff.TryGetInt32(out var initBackoffValue))
        {
            model.InitialBackoffMs = initBackoffValue;
        }
        if (conn.TryGetProperty("maxBackoffMs", out var maxBackoff) && maxBackoff.TryGetInt32(out var maxBackoffValue))
        {
            model.MaxBackoffMs = maxBackoffValue;
        }
        if (conn.TryGetProperty("backoffMultiplier", out var mult) && mult.TryGetDouble(out var multValue))
        {
            model.BackoffMultiplier = multValue;
        }

        if (conn.TryGetProperty("dataPoints", out var dpEl) && dpEl.ValueKind == JsonValueKind.Array)
        {
            var paths = new List<string>();
            foreach (var entry in dpEl.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { } s)
                {
                    paths.Add(s);
                }
            }

            if (paths.Count == 0)
            {
                model.DataPointsMode = BrotherHttpDataPointSelectionMode.CollectAll;
            }
            else
            {
                model.DataPointsMode = BrotherHttpDataPointSelectionMode.Selective;
                foreach (var path in paths)
                {
                    foreach (var group in DataPointGroups)
                    {
                        if (group.EmittedPaths.Contains(path, StringComparer.Ordinal))
                        {
                            model.SelectedGroupKeys.Add(group.Key);
                            break;
                        }
                    }
                }
            }
        }

        return model;
    }
}
