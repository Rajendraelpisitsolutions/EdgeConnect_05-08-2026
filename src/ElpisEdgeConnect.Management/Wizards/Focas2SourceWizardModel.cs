// ============================================================================
// File: Wizards/Focas2SourceWizardModel.cs
// Purpose: In-memory state for the Add-FOCAS2-Source wizard. Razor
//          two-way-binds every form field to this POCO; on Save the
//          wizard calls BuildSourceInstance() to produce a canonical
//          SourceInstanceConfig (with the FOCAS2-specific block packed
//          into the opaque Connection JsonElement, exactly as
//          Focas2SourceConfiguration.FromSourceInstance expects).
//
//          KEY DIVERGENCE FROM MODBUS WIZARD:
//          FOCAS2 tags are DISCOVERED from the controller — not declared
//          per-row by the operator. So instead of a tag-definitions table,
//          this model carries a hierarchical group-picker:
//
//            * DataPointsMode = CollectAll → empty DataPoints (= everything)
//            * DataPointsMode = Selective  → picker emits prefix/path strings
//              recognised by Focas2SourceAdapter.CollectAll's gating logic.
//
//          The exact emission shape per group is locked against what the
//          adapter's HasDataPoint / HasAnyDataPoint calls accept (see
//          Focas2SourceAdapter.CollectAll). Some gates are prefix-match
//          ("Axes/", "Spindle/", "Tool/", "MtLinki/") and some are
//          exact-match ("Program/MainProgram", "Alarms/Active",
//          "Production/CycleTime", "Production/PartsCount"); each group's
//          EmittedPaths reflect what the runtime requires.
// Reference: docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md §6.1
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Operator's choice for how Browse-discoverable data points are emitted
/// into the runtime config.
/// </summary>
public enum Focas2DataPointSelectionMode
{
    /// <summary>Emit an empty <c>dataPoints</c> array (= adapter collects everything).</summary>
    CollectAll,

    /// <summary>Emit only the prefixes/paths for the operator's selected groups.</summary>
    Selective,
}

/// <summary>
/// One toggleable group in the wizard's data-point picker. Maps a category
/// (e.g. "Axes") to the FOCAS2 path strings that activate its collector
/// in <c>Focas2SourceAdapter.CollectAll</c>.
/// </summary>
/// <param name="Key">Stable group identifier used in <see cref="Focas2SourceWizardModel.SelectedGroupKeys"/>.</param>
/// <param name="DisplayName">Operator-facing label.</param>
/// <param name="Description">One-line operator-facing description.</param>
/// <param name="EmittedPaths">Path strings written into the runtime <c>dataPoints</c> when this group is selected.</param>
public sealed record Focas2DataPointGroup(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<string> EmittedPaths);

/// <summary>
/// Wizard state for adding a new FOCAS2 source. Razor binds form fields
/// directly to these properties; on Save, <see cref="BuildSourceInstance"/>
/// produces a canonical <see cref="SourceInstanceConfig"/>.
/// </summary>
public sealed class Focas2SourceWizardModel
{
    // ─── Identity ────────────────────────────────────────────────────────

    /// <summary>Stable instance id (e.g. <c>"focas-cell-A"</c>). Regex enforced by Core.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device identifier.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device display name.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Device class — FOCAS2 sources are CNCs by definition, so <c>"cnc"</c> by default.</summary>
    public string DeviceClass { get; set; } = "cnc";

    /// <summary>Whether the source is enabled when the new draft is applied.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Top-level polling interval in milliseconds. FOCAS2 polling is slower
    /// than Modbus (network + native-DLL round-trip per poll) — 1000 ms is
    /// a sensible starting point for production CNCs.
    /// </summary>
    public int PollIntervalMs { get; set; } = 1000;

    // ─── Connection ──────────────────────────────────────────────────────

    /// <summary>CNC controller IP address (required, parsed at adapter validation time).</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>FOCAS2 port. 8193 is the Fanuc default.</summary>
    public int Port { get; set; } = 8193;

    /// <summary>Connection timeout in seconds (per attempt).</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// When true, keep the FOCAS2 handle open across polls (~5 ms per poll).
    /// When false, open and close per poll (~20-50 ms per poll).
    /// </summary>
    public bool KeepAlive { get; set; } = true;

    // ─── Backoff (advanced) ─────────────────────────────────────────────

    /// <summary>Initial delay in ms after first connection failure.</summary>
    public int InitialBackoffMs { get; set; } = 5000;

    /// <summary>Maximum backoff delay in ms.</summary>
    public int MaxBackoffMs { get; set; } = 120_000;

    /// <summary>Multiplier applied to backoff on each consecutive failure.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Maximum number of handle allocation retries per connect attempt.</summary>
    public int MaxConnectRetries { get; set; } = 5;

    // ─── Data points (group picker) ─────────────────────────────────────

    /// <summary>Operator's choice between "Collect all" (default) and "Limit to specific groups".</summary>
    public Focas2DataPointSelectionMode DataPointsMode { get; set; } = Focas2DataPointSelectionMode.CollectAll;

    /// <summary>
    /// Keys of the groups (from <see cref="DataPointGroups"/>) the operator has selected.
    /// Razor checkboxes two-way-bind to membership of this set.
    /// </summary>
    public HashSet<string> SelectedGroupKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Catalogue of selectable data-point groups. Each group lists the
    /// FOCAS2 path strings that activate its collector in
    /// <c>Focas2SourceAdapter.CollectAll</c>. Status data is intentionally
    /// NOT in this list — the adapter ALWAYS collects Status data when
    /// connected; there is no opt-out at the runtime layer.
    /// </summary>
    public static readonly IReadOnlyList<Focas2DataPointGroup> DataPointGroups = new[]
    {
        new Focas2DataPointGroup(
            "program",
            "Program",
            "Main program and currently running program O-numbers.",
            new[] { "Program/MainProgram", "Program/RunningProgram" }),

        new Focas2DataPointGroup(
            "axes",
            "Axes",
            "Absolute / machine / relative positions and distance-to-go for every controlled axis.",
            new[] { "Axes/" }),

        new Focas2DataPointGroup(
            "spindle",
            "Spindle & feed",
            "Spindle speed, spindle load, and actual feed rate.",
            new[] { "Spindle/" }),

        new Focas2DataPointGroup(
            "alarms",
            "Alarms",
            "Active alarm messages and alarm count.",
            new[] { "Alarms/Active" }),

        new Focas2DataPointGroup(
            "production",
            "Production",
            "Cycle time and parts counter.",
            new[] { "Production/CycleTime", "Production/PartsCount" }),

        new Focas2DataPointGroup(
            "tool",
            "Tool",
            "Current tool number, tool offsets, and tool life management data.",
            new[] { "Tool/" }),

        new Focas2DataPointGroup(
            "mtlinki",
            "MT-LINKi compatibility",
            "MT-LINKi-shape diagnostics (servo / spindle temperatures, fan status, PMC signals).",
            new[] { "MtLinki/" }),
    };

    /// <summary>
    /// Build the runtime <c>dataPoints</c> list from the operator's
    /// selection. Honours <see cref="DataPointsMode"/> and the
    /// "every-group-selected collapses to empty" semantic (Locked O
    /// in the M.2b.3 plan).
    /// </summary>
    public IReadOnlyList<string> BuildDataPointsList()
    {
        if (DataPointsMode == Focas2DataPointSelectionMode.CollectAll)
        {
            return Array.Empty<string>();
        }

        // Every selectable group selected → semantically identical to "Collect all".
        // Collapse to empty so the runtime payload stays compact and future
        // tag-map growth is inherited automatically.
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
    /// <see cref="SourceInstanceConfig"/>. The FOCAS2-specific fields
    /// land in the opaque <c>Connection</c> <see cref="JsonElement"/>
    /// — the canonical type stays protocol-agnostic.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="IpAddress"/> is blank or <see cref="Port"/>
    /// is outside the valid ushort range. Both are defensive — the wizard
    /// UI should disable Save before these get here, but the merger and
    /// adapter both rely on these invariants too.
    /// </exception>
    public SourceInstanceConfig BuildSourceInstance()
    {
        if (string.IsNullOrWhiteSpace(IpAddress))
        {
            throw new InvalidOperationException(
                "IpAddress is required to build a FOCAS2 source — Focas2SourceConfiguration.FromSourceInstance rejects empty addresses.");
        }

        if (Port < 0 || Port > 65535)
        {
            throw new InvalidOperationException(
                $"Port {Port} is outside the valid ushort range [0..65535] — FOCAS2 port is a ushort.");
        }

        var dataPoints = BuildDataPointsList();

        var connection = new JsonObject
        {
            ["ipAddress"] = IpAddress,
            ["port"] = Port,
            ["timeoutSeconds"] = TimeoutSeconds,
            ["keepAlive"] = KeepAlive,
            ["initialBackoffMs"] = InitialBackoffMs,
            ["maxBackoffMs"] = MaxBackoffMs,
            ["backoffMultiplier"] = BackoffMultiplier,
            ["maxConnectRetries"] = MaxConnectRetries,
        };

        var dpArray = new JsonArray();
        foreach (var path in dataPoints)
        {
            dpArray.Add(path);
        }
        connection["dataPoints"] = dpArray;

        // Convert JsonNode tree to JsonElement (the opaque shape SourceInstanceConfig holds).
        var json = connection.ToJsonString();
        using var doc = JsonDocument.Parse(json);

        return new SourceInstanceConfig
        {
            InstanceId = InstanceId,
            ProtocolName = "focas2",
            DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? InstanceId : DeviceId,
            DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? InstanceId : DeviceName,
            DeviceClass = DeviceClass,
            Enabled = Enabled,
            Polling = new PollingSettings { IntervalMs = PollIntervalMs },
            Connection = doc.RootElement.Clone(),  // Clone so it outlives the using.
        };
    }

    /// <summary>
    /// Inverse of <see cref="BuildSourceInstance"/> — populate a fresh wizard
    /// model from a canonical <see cref="SourceInstanceConfig"/> previously
    /// emitted by the same wizard. Used by Edit-mode routing to hydrate the
    /// wizard form with the source's current settings.
    /// <para>
    /// Round-trip invariant (M.2d.2 §5.5): for any model M produced by this
    /// wizard, <c>HydrateFromExisting(M.BuildSourceInstance()).BuildSourceInstance()</c>
    /// is byte-equivalent to <c>M.BuildSourceInstance()</c>.
    /// </para>
    /// <para>
    /// Reverse-maps the runtime <c>dataPoints</c> array to
    /// <see cref="DataPointsMode"/> + <see cref="SelectedGroupKeys"/>:
    /// empty → <see cref="Focas2DataPointSelectionMode.CollectAll"/>; non-empty
    /// → <see cref="Focas2DataPointSelectionMode.Selective"/> with each path
    /// matched against <see cref="DataPointGroups"/>' <c>EmittedPaths</c>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/>'s <c>ProtocolName</c> is not <c>"focas2"</c>.
    /// </exception>
    public static Focas2SourceWizardModel HydrateFromExisting(SourceInstanceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProtocolName, "focas2", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Source has protocol '{source.ProtocolName}', expected 'focas2'.",
                nameof(source));
        }

        var model = new Focas2SourceWizardModel
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

        if (conn.TryGetProperty("ipAddress", out var ip) && ip.ValueKind == JsonValueKind.String)
        {
            model.IpAddress = ip.GetString() ?? string.Empty;
        }
        if (conn.TryGetProperty("port", out var port) && port.TryGetInt32(out var portValue))
        {
            model.Port = portValue;
        }
        if (conn.TryGetProperty("timeoutSeconds", out var timeout) && timeout.TryGetInt32(out var timeoutValue))
        {
            model.TimeoutSeconds = timeoutValue;
        }
        if (conn.TryGetProperty("keepAlive", out var keepAlive) &&
            (keepAlive.ValueKind == JsonValueKind.True || keepAlive.ValueKind == JsonValueKind.False))
        {
            model.KeepAlive = keepAlive.GetBoolean();
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
        if (conn.TryGetProperty("maxConnectRetries", out var retries) && retries.TryGetInt32(out var retriesValue))
        {
            model.MaxConnectRetries = retriesValue;
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
                model.DataPointsMode = Focas2DataPointSelectionMode.CollectAll;
            }
            else
            {
                model.DataPointsMode = Focas2DataPointSelectionMode.Selective;
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
