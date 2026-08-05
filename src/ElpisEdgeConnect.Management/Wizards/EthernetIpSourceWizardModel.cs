// ============================================================================
// File: Wizards/EthernetIpSourceWizardModel.cs
// Purpose: In-memory state for the Add/Edit-EtherNet/IP-Source wizard. Razor
//          two-way-binds every field to this POCO; on Save the wizard calls
//          BuildSourceInstance() to produce a canonical SourceInstanceConfig
//          (with the EtherNet/IP-specific block packed into the opaque
//          Connection JsonElement exactly as
//          EthernetIpSourceConfiguration.FromSourceInstance expects).
//
//          MVP wizard: manual tag list, no UDT browse. The CPU-family default
//          path is applied on family change so the L8x "1,0" default is never
//          silently missing.
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §3.1, §5.2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.EthernetIp;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Wizard state for adding / editing an EtherNet/IP source. Razor binds form
/// fields directly to these properties; on Save, <see cref="BuildSourceInstance"/>
/// produces a canonical <see cref="SourceInstanceConfig"/>.
/// </summary>
public sealed class EthernetIpSourceWizardModel
{
    /// <summary>The protocol-name constant this wizard emits.</summary>
    public const string ProtocolName = "ethernetip";

    // ─── Identity ────────────────────────────────────────────────────────

    /// <summary>Stable instance id (e.g. <c>"plc-line-3"</c>). Regex enforced by Core.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device identifier.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device display name.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Device class — typically <c>"plc"</c>.</summary>
    public string DeviceClass { get; set; } = "plc";

    /// <summary>Whether the source is enabled when the new draft is applied.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Top-level polling interval in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = 250;

    // ─── Connection ──────────────────────────────────────────────────────

    /// <summary>Gateway IP / hostname of the controller's Ethernet port.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>CIP routing path (e.g. <c>"1,0"</c> for Logix front ports).</summary>
    public string Path { get; set; } = "1,0";

    /// <summary>CPU family — ControlLogix / CompactLogix / GuardLogix / MicroLogix / Micro800 / Slc500 / Plc5.</summary>
    public string CpuFamily { get; set; } = "ControlLogix";

    /// <summary>CIP session connect timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; set; } = 3000;

    /// <summary>Per-read response timeout in milliseconds.</summary>
    public int RequestTimeoutMs { get; set; } = 2000;

    // ─── Retry / Backoff / Circuit Breaker ──────────────────────────────

    /// <summary>Max per-read retries before surfacing a failure.</summary>
    public int MaxTransactionRetries { get; set; } = 2;

    /// <summary>Initial backoff in ms after a fatal session failure.</summary>
    public int InitialBackoffMs { get; set; } = 1000;

    /// <summary>Maximum backoff in ms — caps exponential growth.</summary>
    public int MaxBackoffMs { get; set; } = 30_000;

    /// <summary>Exponential multiplier between consecutive failures.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Consecutive failures that open the circuit breaker.</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Breaker cool-down window in ms before the next probe.</summary>
    public int CircuitBreakerResetMs { get; set; } = 30_000;

    // ─── Tag definitions ────────────────────────────────────────────────

    /// <summary>Per-tag definitions. Empty initial list; operator adds rows in the wizard.</summary>
    public List<EthernetIpTagWizardRow> Tags { get; set; } = new();

    /// <summary>Acceptable CPU-family values (used to populate the wizard dropdown).</summary>
    public static readonly IReadOnlyList<string> CpuFamilies = new[]
    {
        "ControlLogix", "CompactLogix", "GuardLogix", "MicroLogix", "Micro800", "Slc500", "Plc5",
    };

    /// <summary>Acceptable element-type values (matches the adapter's parser allowlist).</summary>
    public static readonly IReadOnlyList<string> Datatypes = new[]
    {
        "BOOL", "SINT", "INT", "DINT", "LINT", "REAL", "LREAL", "STRING",
    };

    /// <summary>
    /// The default CIP path for a CPU-family name. The wizard calls this when
    /// the operator changes the family so the L8x <c>"1,0"</c> default is never
    /// silently missing. Unknown families fall back to <c>"1,0"</c>.
    /// </summary>
    public static string DefaultPathFor(string cpuFamily) =>
        (EthernetIpCpuFamilyExtensions.ParseOrNull(cpuFamily) ?? EthernetIpCpuFamily.ControlLogix).DefaultPath();

    /// <summary>
    /// Validate one tag row against the adapter's rules. Returns an empty list
    /// when the row is structurally valid; Path values match the wizard cells.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateTag(EthernetIpTagWizardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var errors = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(row.Name))
        {
            errors.Add(new ValidationIssue { Code = "ETHERNETIP.CONFIG_MISSING_FIELD", Message = "Tag name is required.", Path = "Name" });
        }
        if (string.IsNullOrWhiteSpace(row.Address))
        {
            errors.Add(new ValidationIssue { Code = "ETHERNETIP.CONFIG_MISSING_FIELD", Message = "Tag address is required.", Path = "Address" });
        }

        var elementType = EthernetIpElementTypeExtensions.ParseOrNull(row.Datatype);
        if (elementType is null)
        {
            errors.Add(new ValidationIssue
            {
                Code = "ETHERNETIP.CONFIG_INVALID",
                Message = $"Datatype '{row.Datatype}' is not recognised. Choose one of: {string.Join(", ", Datatypes)}.",
                Path = "Datatype",
            });
        }
        else if ((row.Scale is not null || row.Offset is not null) && !elementType.Value.SupportsScaleOffset())
        {
            errors.Add(new ValidationIssue
            {
                Code = "ETHERNETIP.CONFIG_INVALID",
                Message = $"{elementType} tags cannot carry scale/offset — those apply to numeric types only.",
                Path = "Scale",
            });
        }

        if (row.ScanRateMs <= 0)
        {
            errors.Add(new ValidationIssue { Code = "ETHERNETIP.CONFIG_OUT_OF_RANGE", Message = "Scan rate must be > 0 ms.", Path = "ScanRateMs" });
        }

        return errors;
    }

    /// <summary>
    /// Validate the whole wizard model — identity, connection, per-tag rows, and
    /// duplicate tag names. Returns <see cref="ValidationResult.Success"/> when
    /// the model is structurally ready to Save.
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(InstanceId))
        {
            errors.Add(new ValidationIssue { Code = "ETHERNETIP.CONFIG_MISSING_FIELD", Message = "Source id is required.", Path = "InstanceId" });
        }
        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add(new ValidationIssue { Code = "ETHERNETIP.CONFIG_MISSING_FIELD", Message = "Host is required.", Path = "Host" });
        }
        if (string.IsNullOrWhiteSpace(DeviceClass) ||
            !System.Text.RegularExpressions.Regex.IsMatch(DeviceClass, "^[a-z0-9-]+$"))
        {
            errors.Add(new ValidationIssue { Code = "ETHERNETIP.CONFIG_INVALID", Message = "Device class must be lowercase letters, digits, or hyphens (e.g. \"plc\").", Path = "DeviceClass" });
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Tags)
        {
            errors.AddRange(ValidateTag(row));
            if (!string.IsNullOrWhiteSpace(row.Name) && !seen.Add(row.Name))
            {
                errors.Add(new ValidationIssue { Code = "ETHERNETIP.CONFIG_INVALID", Message = $"Duplicate tag name '{row.Name}'.", Path = "Name" });
            }
        }

        return errors.Count > 0
            ? new ValidationResult { IsValid = false, Errors = errors }
            : ValidationResult.Success();
    }

    /// <summary>
    /// Project the wizard state into a canonical <see cref="SourceInstanceConfig"/>.
    /// The EtherNet/IP-specific fields land in the opaque <c>Connection</c>
    /// <see cref="JsonElement"/> so the canonical type stays protocol-agnostic.
    /// </summary>
    public SourceInstanceConfig BuildSourceInstance()
    {
        var connection = new JsonObject
        {
            ["host"] = Host,
            ["path"] = Path,
            ["cpuFamily"] = CpuFamily,
            ["connectTimeoutMs"] = ConnectTimeoutMs,
            ["requestTimeoutMs"] = RequestTimeoutMs,
            ["maxTransactionRetries"] = MaxTransactionRetries,
            ["initialBackoffMs"] = InitialBackoffMs,
            ["maxBackoffMs"] = MaxBackoffMs,
            ["backoffMultiplier"] = BackoffMultiplier,
            ["circuitBreakerThreshold"] = CircuitBreakerThreshold,
            ["circuitBreakerResetMs"] = CircuitBreakerResetMs,
        };

        var tagArray = new JsonArray();
        foreach (var tag in Tags)
        {
            var tagNode = new JsonObject
            {
                ["name"] = tag.Name,
                ["address"] = tag.Address,
                ["scanRateMs"] = tag.ScanRateMs,
            };
            if (!string.IsNullOrWhiteSpace(tag.Datatype))
            {
                tagNode["datatype"] = tag.Datatype;
            }
            if (tag.Scale is { } scale)
            {
                tagNode["scale"] = scale;
            }
            if (tag.Offset is { } offset)
            {
                tagNode["offset"] = offset;
            }
            if (!string.IsNullOrWhiteSpace(tag.Unit))
            {
                tagNode["unit"] = tag.Unit;
            }
            tagArray.Add(tagNode);
        }
        connection["tags"] = tagArray;

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
    /// Edit-mode routing to hydrate the wizard form with current settings.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/>'s <c>ProtocolName</c> is not <c>"ethernetip"</c>.
    /// </exception>
    public static EthernetIpSourceWizardModel HydrateFromExisting(SourceInstanceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProtocolName, ProtocolName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Source has protocol '{source.ProtocolName}', expected '{ProtocolName}'.",
                nameof(source));
        }

        var model = new EthernetIpSourceWizardModel
        {
            InstanceId = source.InstanceId,
            DeviceId = source.DeviceId,
            DeviceName = source.DeviceName ?? string.Empty,
            DeviceClass = source.DeviceClass ?? "plc",
            Enabled = source.Enabled,
            PollIntervalMs = source.Polling.IntervalMs,
        };

        if (source.Connection is not { } conn || conn.ValueKind != JsonValueKind.Object)
        {
            return model;
        }

        if (conn.TryGetProperty("host", out var host) && host.ValueKind == JsonValueKind.String)
        {
            model.Host = host.GetString() ?? string.Empty;
        }
        if (conn.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
        {
            model.Path = path.GetString() ?? string.Empty;
        }
        if (conn.TryGetProperty("cpuFamily", out var fam) && fam.ValueKind == JsonValueKind.String)
        {
            model.CpuFamily = fam.GetString() ?? "ControlLogix";
        }
        if (conn.TryGetProperty("connectTimeoutMs", out var ctMs) && ctMs.TryGetInt32(out var ctVal))
        {
            model.ConnectTimeoutMs = ctVal;
        }
        if (conn.TryGetProperty("requestTimeoutMs", out var rtMs) && rtMs.TryGetInt32(out var rtVal))
        {
            model.RequestTimeoutMs = rtVal;
        }
        if (conn.TryGetProperty("maxTransactionRetries", out var mr) && mr.TryGetInt32(out var mrVal))
        {
            model.MaxTransactionRetries = mrVal;
        }
        if (conn.TryGetProperty("initialBackoffMs", out var ib) && ib.TryGetInt32(out var ibVal))
        {
            model.InitialBackoffMs = ibVal;
        }
        if (conn.TryGetProperty("maxBackoffMs", out var mb) && mb.TryGetInt32(out var mbVal))
        {
            model.MaxBackoffMs = mbVal;
        }
        if (conn.TryGetProperty("backoffMultiplier", out var bm) && bm.TryGetDouble(out var bmVal))
        {
            model.BackoffMultiplier = bmVal;
        }
        if (conn.TryGetProperty("circuitBreakerThreshold", out var cbt) && cbt.TryGetInt32(out var cbtVal))
        {
            model.CircuitBreakerThreshold = cbtVal;
        }
        if (conn.TryGetProperty("circuitBreakerResetMs", out var cbr) && cbr.TryGetInt32(out var cbrVal))
        {
            model.CircuitBreakerResetMs = cbrVal;
        }

        if (conn.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tagEl in tagsEl.EnumerateArray())
            {
                if (tagEl.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var row = new EthernetIpTagWizardRow { Datatype = null };

                if (tagEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    row.Name = nameEl.GetString() ?? string.Empty;
                }
                if (tagEl.TryGetProperty("address", out var addrEl) && addrEl.ValueKind == JsonValueKind.String)
                {
                    row.Address = addrEl.GetString() ?? string.Empty;
                }
                if (tagEl.TryGetProperty("datatype", out var dtEl) && dtEl.ValueKind == JsonValueKind.String)
                {
                    row.Datatype = dtEl.GetString();
                }
                if (tagEl.TryGetProperty("scanRateMs", out var scanEl) && scanEl.TryGetInt32(out var scanVal))
                {
                    row.ScanRateMs = scanVal;
                }
                if (tagEl.TryGetProperty("scale", out var scaleEl) && scaleEl.TryGetDouble(out var scaleVal))
                {
                    row.Scale = scaleVal;
                }
                if (tagEl.TryGetProperty("offset", out var offEl) && offEl.TryGetDouble(out var offVal))
                {
                    row.Offset = offVal;
                }
                if (tagEl.TryGetProperty("unit", out var unitEl) && unitEl.ValueKind == JsonValueKind.String)
                {
                    row.Unit = unitEl.GetString();
                }

                model.Tags.Add(row);
            }
        }

        return model;
    }
}

/// <summary>One tag row in the wizard's tag table. Strings for enum-like fields so MudSelect binds cleanly.</summary>
public sealed class EthernetIpTagWizardRow
{
    /// <summary>Canonical tag name emitted to the pipeline.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Controller tag address / symbolic name (e.g. <c>"Program:MainProgram.Speed"</c>).</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Element-type hint (BOOL / SINT / INT / DINT / LINT / REAL / LREAL / STRING).</summary>
    public string? Datatype { get; set; } = "DINT";

    /// <summary>Scan period in milliseconds.</summary>
    public int ScanRateMs { get; set; } = 1000;

    /// <summary>Optional linear scale factor: <c>scaled = raw * Scale + Offset</c>.</summary>
    public double? Scale { get; set; }

    /// <summary>Optional additive offset; see <see cref="Scale"/>.</summary>
    public double? Offset { get; set; }

    /// <summary>Optional engineering unit string copied to the canonical data point.</summary>
    public string? Unit { get; set; }
}
