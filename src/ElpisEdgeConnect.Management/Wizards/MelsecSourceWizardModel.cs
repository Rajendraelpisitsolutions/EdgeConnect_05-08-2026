// ============================================================================
// File: Wizards/MelsecSourceWizardModel.cs
// Purpose: In-memory state for the Add/Edit-MELSEC-Source wizard. Mirrors
//          S7SourceWizardModel: Razor two-way-binds every field to this POCO;
//          on Save, BuildSourceInstance() packs a canonical SourceInstanceConfig
//          with the MELSEC block in the opaque Connection JSON keyed by
//          MelsecConnectionKeys, exactly as MelsecSourceConfiguration.
//          FromSourceInstance expects.
//
//          Defaults are sourced from the MelsecSourceConfiguration record (one
//          source of truth — no duplicated literals). Slice-1 mode is FIXED
//          (Tcp / Mc3EBinary / Modern); the wizard offers no editable mode
//          dropdown. Hydrate PRESERVES any hand-edited unsupported mode so
//          Validate() surfaces CONFIG_MODE_NOT_IMPLEMENTED and blocks Save —
//          it never silently normalizes. NormalizeToSlice1() is the explicit,
//          operator-initiated reset.
//
//          Address/datatype validation goes through the REAL backend
//          (MelsecAddressParser / MelsecDatatypeParser) so a config the wizard
//          accepts never fails adapter Initialize for a deterministic reason.
// Reference: docs/sessions/2026-07-01-melsec-wizard-ui-plan-v2.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Profiles;
using ElpisEdgeConnect.Sources.Melsec.Scanning;
using ElpisEdgeConnect.Sources.Melsec.Wire;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Wizard state for adding/editing a Mitsubishi MELSEC source (SLMP / MC 3E
/// binary over TCP, read-only). Razor binds fields directly; on Save,
/// <see cref="BuildSourceInstance"/> produces a canonical <see cref="SourceInstanceConfig"/>.
/// </summary>
public sealed class MelsecSourceWizardModel
{
    /// <summary>Protocol-name constant carried on the emitted instance.</summary>
    public const string ProtocolName = "melsec";

    // Defaults come from the backend config record — never re-declared here.
    private static readonly MelsecSourceConfiguration Defaults = new()
    {
        InstanceId = string.Empty,
        ProtocolName = ProtocolName,
        DeviceId = string.Empty,
        Host = string.Empty,
    };

    // ─── Identity ────────────────────────────────────────────────────────
    /// <summary>Stable instance id (e.g. <c>"melsec-press-2"</c>).</summary>
    public string InstanceId { get; set; } = string.Empty;
    /// <summary>Operator-readable device identifier.</summary>
    public string DeviceId { get; set; } = string.Empty;
    /// <summary>Operator-readable device display name.</summary>
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>Device class — typically <c>"plc"</c>.</summary>
    public string DeviceClass { get; set; } = "plc";
    /// <summary>Optional free-text description (stored under connection <c>"description"</c>).</summary>
    public string? Description { get; set; }
    /// <summary>Whether the source is enabled when the draft applies.</summary>
    public bool Enabled { get; set; } = true;

    // ─── Connection ──────────────────────────────────────────────────────
    /// <summary>PLC / Ethernet-module IP address or hostname.</summary>
    public string Host { get; set; } = string.Empty;
    /// <summary>MC/SLMP TCP port (no universal default — operator must set).</summary>
    public int Port { get; set; } = Defaults.Port;

    // Slice-1 mode is FIXED. These fields exist so hydrate can preserve a
    // hand-edited unsupported value for the validation banner (never silent).
    /// <summary>Transport (Slice 1: <c>Tcp</c> only).</summary>
    public string TransportProtocol { get; set; } = MelsecTransportProtocol.Tcp.ToString();
    /// <summary>MC frame mode (Slice 1: <c>Mc3EBinary</c> only).</summary>
    public string FrameMode { get; set; } = MelsecFrameMode.Mc3EBinary.ToString();
    /// <summary>CPU family profile (Slice 1: <c>Modern</c> only).</summary>
    public string DeviceProfile { get; set; } = MelsecDeviceProfile.Modern.ToString();

    // ─── 3E route header ─────────────────────────────────────────────────
    /// <summary>Network number (byte, 0x00–0xFF).</summary>
    public int NetworkNo { get; set; } = Defaults.NetworkNo;
    /// <summary>PC / station number (byte, 0x00–0xFF).</summary>
    public int PcNo { get; set; } = Defaults.PcNo;
    /// <summary>Request destination module I/O number (ushort, 0x0000–0xFFFF).</summary>
    public int RequestDestModuleIoNo { get; set; } = Defaults.RequestDestModuleIoNo;
    /// <summary>Request destination module station number (byte, 0x00–0xFF).</summary>
    public int RequestDestModuleStationNo { get; set; } = Defaults.RequestDestModuleStationNo;
    /// <summary>Device-side monitoring timer in ms (encoded in 250 ms units, ceil-up).</summary>
    public int MonitoringTimerMs { get; set; } = Defaults.MonitoringTimerMs;

    // ─── Timeouts / retry / breaker ──────────────────────────────────────
    /// <summary>Connect timeout in ms.</summary>
    public int ConnectTimeoutMs { get; set; } = Defaults.ConnectTimeoutMs;
    /// <summary>Per-read socket timeout in ms (must be ≥ encoded monitoring timer).</summary>
    public int RequestTimeoutMs { get; set; } = Defaults.RequestTimeoutMs;
    /// <summary>Keep the TCP session alive between scans.</summary>
    public bool KeepAlive { get; set; } = Defaults.KeepAlive;
    /// <summary>Max per-transaction retries.</summary>
    public int MaxTransactionRetries { get; set; } = Defaults.MaxTransactionRetries;
    /// <summary>Initial backoff (ms).</summary>
    public int InitialBackoffMs { get; set; } = Defaults.InitialBackoffMs;
    /// <summary>Maximum backoff (ms).</summary>
    public int MaxBackoffMs { get; set; } = Defaults.MaxBackoffMs;
    /// <summary>Backoff multiplier.</summary>
    public double BackoffMultiplier { get; set; } = Defaults.BackoffMultiplier;
    /// <summary>Circuit-breaker trip threshold.</summary>
    public int CircuitBreakerThreshold { get; set; } = Defaults.CircuitBreakerThreshold;
    /// <summary>Circuit-breaker reset window (ms).</summary>
    public int CircuitBreakerResetMs { get; set; } = Defaults.CircuitBreakerResetMs;

    // ─── Scan planner ────────────────────────────────────────────────────
    /// <summary>Max word gap the planner coalesces.</summary>
    public int MaxGapWords { get; set; } = Defaults.MaxGapWords;
    /// <summary>Max points per batch read (conservative default 480; hard cap 960).</summary>
    public int MaxPointsPerRequest { get; set; } = Defaults.MaxPointsPerRequest;

    // ─── Tags ────────────────────────────────────────────────────────────
    /// <summary>Per-tag definitions (operator adds rows in the wizard).</summary>
    public List<MelsecTagWizardRow> Tags { get; set; } = new();

    /// <summary>
    /// Dropdown value suggested for an address that resolves to a boolean (bit
    /// device, or word-bit form such as <c>D100.3</c>).
    /// </summary>
    public const string BoolDatatype = "Bool";

    /// <summary>
    /// Dropdown value suggested for a word address. Signed 16-bit is the
    /// single-word default; the operator narrows it (UInt16 / 32-bit) by hand.
    /// </summary>
    public const string DefaultWordDatatype = "Int16";

    /// <summary>Supported datatype dropdown values (match <see cref="MelsecDatatype"/> names).</summary>
    public static readonly IReadOnlyList<string> Datatypes = new[]
    {
        BoolDatatype, DefaultWordDatatype, "UInt16", "Int32", "UInt32", "Float32",
    };

    /// <summary>Word-order dropdown values (shown only for 32-bit datatypes).</summary>
    public static readonly IReadOnlyList<string> WordOrders = new[] { "LowWordFirst", "HighWordFirst" };

    /// <summary>True when the datatype spans two words (word order applies).</summary>
    public static bool DatatypeUsesWordOrder(string? datatype) =>
        datatype is "Int32" or "UInt32" or "Float32";

    /// <summary>Profiles operators may pick (A-2O) — driven by the registry gate bit.</summary>
    public static IReadOnlyList<MelsecProfileDefinition> SelectableProfiles =>
        MelsecProfiles.All.Where(p => p.IsOperatorSelectable).ToList();

    /// <summary>
    /// Resolve the model's <see cref="DeviceProfile"/> string to a registry
    /// definition, falling back to Modern for unknown/unavailable values (the
    /// profile error itself is raised by <see cref="Validate"/>; tag validation
    /// still runs against a concrete profile to avoid cascading noise).
    /// </summary>
    public MelsecProfileDefinition ResolvedProfile =>
        Enum.TryParse<MelsecDeviceProfile>(DeviceProfile, ignoreCase: true, out var key)
            && MelsecProfiles.TryResolve(key, out var def)
            && def.IsOperatorSelectable
            ? def
            : MelsecProfiles.Modern;

    /// <summary>Explicit, operator-initiated reset of the fixed Slice-1 mode fields.</summary>
    public void NormalizeToSlice1()
    {
        TransportProtocol = MelsecTransportProtocol.Tcp.ToString();
        FrameMode = MelsecFrameMode.Mc3EBinary.ToString();
        DeviceProfile = MelsecDeviceProfile.Modern.ToString();
    }

    /// <summary>
    /// Datatype the address itself implies, or <c>null</c> when nothing can be
    /// inferred (address empty, still half-typed, or otherwise unparseable —
    /// which is NOT an error here, only an absence of a suggestion).
    /// Uses the real backend parser, so a suggestion never contradicts
    /// <see cref="ValidateTag(MelsecTagWizardRow, MelsecProfileDefinition)"/>.
    /// The returned string is always one of <see cref="Datatypes"/>.
    /// </summary>
    public static string? SuggestDatatype(string? address, MelsecProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(address)) return null;
        return MelsecAddressParser.TryParse(address, profile, out var parsed, out _)
            ? (parsed.ResolvesToBool ? BoolDatatype : DefaultWordDatatype)
            : null;
    }

    /// <summary>Suggest a datatype against the shipped Modern profile.</summary>
    public static string? SuggestDatatype(string? address) =>
        SuggestDatatype(address, MelsecProfiles.Modern);

    /// <summary>
    /// Pre-fill <see cref="MelsecTagWizardRow.Datatype"/> from the row's address
    /// — SUGGEST, NEVER COERCE. Fills only while the cell is still untouched
    /// (null/blank); an operator-chosen datatype is never overwritten, and an
    /// unparseable address leaves the cell exactly as it was. Returns true only
    /// when a suggestion was actually written.
    /// </summary>
    public static bool TryApplyDatatypeSuggestion(MelsecTagWizardRow row, MelsecProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!string.IsNullOrWhiteSpace(row.Datatype)) return false;

        var suggestion = SuggestDatatype(row.Address, profile);
        if (suggestion is null) return false;

        row.Datatype = suggestion;
        return true;
    }

    /// <summary>Apply a datatype suggestion against the shipped Modern profile.</summary>
    public static bool TryApplyDatatypeSuggestion(MelsecTagWizardRow row) =>
        TryApplyDatatypeSuggestion(row, MelsecProfiles.Modern);

    /// <summary>
    /// Validate a single tag row through the real backend parser. Blocking
    /// errors only; <see cref="ValidationIssue.Path"/> matches the wizard field
    /// (<c>Name</c> / <c>Address</c> / <c>Datatype</c> / <c>ScanRateMs</c>).
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateTag(MelsecTagWizardRow row) =>
        ValidateTag(row, MelsecProfiles.Modern);

    /// <summary>
    /// Validate a single tag row against a specific family profile (A-2O):
    /// the profile drives the device set (e.g. no ZR on iQ-F) and per-device
    /// radix (octal X/Y operator labels on iQ-F).
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateTag(MelsecTagWizardRow row, MelsecProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(row.Name))
        {
            errors.Add(Issue("Tag name is required.", "Name"));
        }
        if (row.ScanRateMs <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = MelsecScanPlanner.InvalidScanRate,
                Message = "Scan rate must be a positive number of milliseconds.",
                Path = "ScanRateMs",
            });
        }

        MelsecAddress? parsed = null;
        if (string.IsNullOrWhiteSpace(row.Address))
        {
            errors.Add(Issue("Address is required.", "Address"));
        }
        else if (MelsecAddressParser.TryParse(row.Address, profile, out var addr, out var addrError))
        {
            parsed = addr;
        }
        else
        {
            errors.Add(new ValidationIssue { Code = addrError.Code, Message = addrError.Message, Path = "Address" });
        }

        MelsecDatatype? datatype = null;
        if (string.IsNullOrWhiteSpace(row.Datatype))
        {
            errors.Add(Issue("Datatype is required.", "Datatype"));
        }
        else if (!Datatypes.Contains(row.Datatype, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(Issue($"Datatype '{row.Datatype}' is not supported. Choose one of: {string.Join(", ", Datatypes)}.", "Datatype"));
        }
        else if (MelsecDatatypeParser.TryParse(row.Datatype, out var dt, out _))
        {
            datatype = dt;
        }

        if (parsed is { } address && datatype is { } resolved)
        {
            if (resolved == MelsecDatatype.Bool && !address.ResolvesToBool)
            {
                errors.Add(new ValidationIssue
                {
                    Code = MelsecScanPlanner.DatatypeMismatch,
                    Message = "Bool requires a word-bit address (e.g. D100.3) or a bit device (M/X/Y/B).",
                    Path = "Datatype",
                });
            }
            else if (resolved != MelsecDatatype.Bool && address.ResolvesToBool)
            {
                errors.Add(new ValidationIssue
                {
                    Code = MelsecScanPlanner.DatatypeMismatch,
                    Message = $"A bit / word-bit address requires the Bool datatype, not {resolved}.",
                    Path = "Datatype",
                });
            }
            // A-3b: timer/counter current values (TN/STN/CN) are single-word only.
            else if (address.Device.SingleWordOnly
                     && resolved is MelsecDatatype.Int32 or MelsecDatatype.UInt32 or MelsecDatatype.Float32)
            {
                errors.Add(new ValidationIssue
                {
                    Code = MelsecScanPlanner.DatatypeMismatch,
                    Message = $"{address.Device.Symbol} is a single-word current value — use Int16 or UInt16, not {resolved}.",
                    Path = "Datatype",
                });
            }
        }

        return errors;
    }

    /// <summary>
    /// Validate the whole model: identity + connection (Slice-1 mode/port/route/
    /// timer/planner) + every tag row + cross-row rules (duplicate names block,
    /// duplicate addresses warn).
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(InstanceId)) errors.Add(Issue("Source id is required.", "InstanceId"));
        if (string.IsNullOrWhiteSpace(Host)) errors.Add(Issue("Host is required.", "Host"));
        if (Port is <= 0 or > ushort.MaxValue) errors.Add(new ValidationIssue { Code = "MELSEC.CONFIG_INVALID_PORT", Message = "Port must be 1–65535.", Path = "Port" });

        // Slice-1 mode is fixed — a hand-edited unsupported value blocks Save.
        if (!string.Equals(TransportProtocol, MelsecTransportProtocol.Tcp.ToString(), StringComparison.OrdinalIgnoreCase))
            errors.Add(new ValidationIssue { Code = "MELSEC.CONFIG_MODE_NOT_IMPLEMENTED", Message = $"Transport '{TransportProtocol}' is not implemented in Slice 1 (TCP only). Reset to the supported mode to continue.", Path = "TransportProtocol" });
        if (!string.Equals(FrameMode, MelsecFrameMode.Mc3EBinary.ToString(), StringComparison.OrdinalIgnoreCase))
            errors.Add(new ValidationIssue { Code = "MELSEC.CONFIG_MODE_NOT_IMPLEMENTED", Message = $"Frame mode '{FrameMode}' is not implemented in Slice 1 (MC 3E binary only). Reset to the supported mode to continue.", Path = "FrameMode" });
        // A-2O: a profile is valid when it resolves in the registry AND is
        // operator-selectable (the gate bit). Everything else blocks Save.
        var profileOk = Enum.TryParse<MelsecDeviceProfile>(DeviceProfile, ignoreCase: true, out var profileKey)
                        && MelsecProfiles.TryResolve(profileKey, out var resolvedDef)
                        && resolvedDef.IsOperatorSelectable;
        if (!profileOk)
            errors.Add(new ValidationIssue { Code = "MELSEC.CONFIG_PROFILE_NOT_IMPLEMENTED", Message = $"Device profile '{DeviceProfile}' is not available in this release (available: {string.Join(", ", MelsecProfiles.All.Where(p => p.IsOperatorSelectable).Select(p => p.Key.ToString()))}). Reset to a supported profile to continue.", Path = "DeviceProfile" });

        // Route-header ranges = backend byte/ushort bounds.
        RangeCheck(errors, NetworkNo, 0, 0xFF, "Network No.", "NetworkNo");
        RangeCheck(errors, PcNo, 0, 0xFF, "PC No.", "PcNo");
        RangeCheck(errors, RequestDestModuleIoNo, 0, 0xFFFF, "Destination module I/O No.", "RequestDestModuleIoNo");
        RangeCheck(errors, RequestDestModuleStationNo, 0, 0xFF, "Destination station No.", "RequestDestModuleStationNo");

        // Monitoring timer encoding + coherence (real backend rules).
        try
        {
            _ = MelsecMonitoringTimer.Encode(MonitoringTimerMs);
            if (!MelsecMonitoringTimer.TryValidateCoherence(MonitoringTimerMs, RequestTimeoutMs, out var timerErr))
                errors.Add(new ValidationIssue { Code = "MELSEC.CONFIG_TIMEOUT_INCOHERENT", Message = timerErr!, Path = "RequestTimeoutMs" });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            errors.Add(new ValidationIssue { Code = "MELSEC.CONFIG_TIMER_RANGE", Message = ex.Message, Path = "MonitoringTimerMs" });
        }

        if (MaxPointsPerRequest is <= 0 or > MelsecScanPlanner.HardWordCap)
            errors.Add(new ValidationIssue { Code = "MELSEC.CONFIG_POINTS_CAP", Message = $"Max points per request must be 1–{MelsecScanPlanner.HardWordCap}.", Path = "MaxPointsPerRequest" });
        if (MaxGapWords < 0)
            errors.Add(new ValidationIssue { Code = "MELSEC.CONFIG_INVALID_PLANNER", Message = "Max gap (words) must be ≥ 0.", Path = "MaxGapWords" });

        var tagProfile = ResolvedProfile;
        for (var i = 0; i < Tags.Count; i++)
        {
            foreach (var issue in ValidateTag(Tags[i], tagProfile))
            {
                errors.Add(issue with { Path = $"Tags[{i}].{issue.Path}" });
            }
        }

        foreach (var dup in Tags
                     .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                     .GroupBy(t => t.Name.Trim(), StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            errors.Add(Issue($"Tag name '{dup.Key}' is used by {dup.Count()} tags. Tag names must be unique.", "Name"));
        }

        foreach (var dup in Tags
                     .Select(t => MelsecAddressParser.TryParse(t.Address, tagProfile, out var a, out _) ? a.ToString() : null)
                     .Where(s => s is not null)
                     .GroupBy(s => s!, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            warnings.Add(Issue($"Address '{dup.Key}' is read by {dup.Count()} tags. This is allowed but usually unintended.", "Address"));
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors, Warnings = warnings };
    }

    /// <summary>Project the wizard state into a canonical <see cref="SourceInstanceConfig"/>.</summary>
    public SourceInstanceConfig BuildSourceInstance()
    {
        var deviceId = string.IsNullOrWhiteSpace(DeviceId) ? InstanceId : DeviceId;
        var deviceName = string.IsNullOrWhiteSpace(DeviceName) ? InstanceId : DeviceName;
        var deviceClass = string.IsNullOrWhiteSpace(DeviceClass) ? "plc" : DeviceClass;

        var connection = new JsonObject
        {
            [MelsecConnectionKeys.Host] = Host,
            [MelsecConnectionKeys.Port] = Port,
            [MelsecConnectionKeys.TransportProtocol] = TransportProtocol,
            [MelsecConnectionKeys.FrameMode] = FrameMode,
            [MelsecConnectionKeys.DeviceProfile] = DeviceProfile,
            [MelsecConnectionKeys.NetworkNo] = NetworkNo,
            [MelsecConnectionKeys.PcNo] = PcNo,
            [MelsecConnectionKeys.RequestDestModuleIoNo] = RequestDestModuleIoNo,
            [MelsecConnectionKeys.RequestDestModuleStationNo] = RequestDestModuleStationNo,
            [MelsecConnectionKeys.MonitoringTimerMs] = MonitoringTimerMs,
            [MelsecConnectionKeys.ConnectTimeoutMs] = ConnectTimeoutMs,
            [MelsecConnectionKeys.RequestTimeoutMs] = RequestTimeoutMs,
            [MelsecConnectionKeys.KeepAlive] = KeepAlive,
            [MelsecConnectionKeys.MaxTransactionRetries] = MaxTransactionRetries,
            [MelsecConnectionKeys.InitialBackoffMs] = InitialBackoffMs,
            [MelsecConnectionKeys.MaxBackoffMs] = MaxBackoffMs,
            [MelsecConnectionKeys.BackoffMultiplier] = BackoffMultiplier,
            [MelsecConnectionKeys.CircuitBreakerThreshold] = CircuitBreakerThreshold,
            [MelsecConnectionKeys.CircuitBreakerResetMs] = CircuitBreakerResetMs,
            [MelsecConnectionKeys.MaxGapWords] = MaxGapWords,
            [MelsecConnectionKeys.MaxPointsPerRequest] = MaxPointsPerRequest,
            [MelsecConnectionKeys.DeviceId] = deviceId,
            [MelsecConnectionKeys.DeviceName] = deviceName,
            [MelsecConnectionKeys.DeviceClass] = deviceClass,
        };
        if (!string.IsNullOrWhiteSpace(Description))
        {
            connection["description"] = Description;
        }

        var tagArray = new JsonArray();
        foreach (var tag in Tags)
        {
            var tagNode = new JsonObject
            {
                [MelsecConnectionKeys.TagName] = tag.Name,
                [MelsecConnectionKeys.Address] = tag.Address,
                [MelsecConnectionKeys.ScanRateMs] = tag.ScanRateMs,
            };
            if (!string.IsNullOrWhiteSpace(tag.Datatype))
            {
                tagNode[MelsecConnectionKeys.Datatype] = tag.Datatype;
            }
            // Word order is only meaningful for 32-bit; omit at the default to keep JSON minimal + round-trip stable.
            if (DatatypeUsesWordOrder(tag.Datatype)
                && !string.Equals(tag.WordOrder, MelsecWordOrder.LowWordFirst.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                tagNode[MelsecConnectionKeys.WordOrder] = tag.WordOrder;
            }
            if (!string.IsNullOrWhiteSpace(tag.Unit))
            {
                tagNode[MelsecConnectionKeys.Unit] = tag.Unit;
            }
            if (tag.Scale is { } scale)
            {
                tagNode[MelsecConnectionKeys.Scale] = scale;
            }
            if (tag.Offset is { } offset)
            {
                tagNode[MelsecConnectionKeys.Offset] = offset;
            }
            tagArray.Add(tagNode);
        }
        connection[MelsecConnectionKeys.Tags] = tagArray;

        using var doc = JsonDocument.Parse(connection.ToJsonString());
        return new SourceInstanceConfig
        {
            InstanceId = InstanceId,
            ProtocolName = ProtocolName,
            DeviceId = deviceId,
            DeviceName = deviceName,
            DeviceClass = deviceClass,
            Enabled = Enabled,
            Connection = doc.RootElement.Clone(),
        };
    }

    /// <summary>
    /// Inverse of <see cref="BuildSourceInstance"/> — hydrate a fresh model from
    /// an existing source (Edit mode). PRESERVES a hand-edited unsupported mode
    /// (never normalizes); Validate() then blocks Save until corrected.
    /// </summary>
    public static MelsecSourceWizardModel HydrateFromExisting(SourceInstanceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProtocolName, ProtocolName, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Source has protocol '{source.ProtocolName}', expected '{ProtocolName}'.", nameof(source));
        }

        var model = new MelsecSourceWizardModel
        {
            InstanceId = source.InstanceId,
            DeviceId = source.DeviceId,
            DeviceName = source.DeviceName ?? string.Empty,
            DeviceClass = source.DeviceClass ?? "plc",
            Enabled = source.Enabled,
        };

        if (source.Connection is not { } conn || conn.ValueKind != JsonValueKind.Object)
        {
            return model;
        }

        model.DeviceId = ReadString(conn, MelsecConnectionKeys.DeviceId) ?? model.DeviceId;
        model.DeviceName = ReadString(conn, MelsecConnectionKeys.DeviceName) ?? model.DeviceName;
        model.DeviceClass = ReadString(conn, MelsecConnectionKeys.DeviceClass) ?? model.DeviceClass;
        model.Description = ReadString(conn, "description");

        model.Host = ReadString(conn, MelsecConnectionKeys.Host) ?? string.Empty;
        model.Port = ReadInt(conn, MelsecConnectionKeys.Port, model.Port);
        model.TransportProtocol = ReadString(conn, MelsecConnectionKeys.TransportProtocol) ?? model.TransportProtocol;
        model.FrameMode = ReadString(conn, MelsecConnectionKeys.FrameMode) ?? model.FrameMode;
        model.DeviceProfile = ReadString(conn, MelsecConnectionKeys.DeviceProfile) ?? model.DeviceProfile;
        model.NetworkNo = ReadInt(conn, MelsecConnectionKeys.NetworkNo, model.NetworkNo);
        model.PcNo = ReadInt(conn, MelsecConnectionKeys.PcNo, model.PcNo);
        model.RequestDestModuleIoNo = ReadInt(conn, MelsecConnectionKeys.RequestDestModuleIoNo, model.RequestDestModuleIoNo);
        model.RequestDestModuleStationNo = ReadInt(conn, MelsecConnectionKeys.RequestDestModuleStationNo, model.RequestDestModuleStationNo);
        model.MonitoringTimerMs = ReadInt(conn, MelsecConnectionKeys.MonitoringTimerMs, model.MonitoringTimerMs);
        model.ConnectTimeoutMs = ReadInt(conn, MelsecConnectionKeys.ConnectTimeoutMs, model.ConnectTimeoutMs);
        model.RequestTimeoutMs = ReadInt(conn, MelsecConnectionKeys.RequestTimeoutMs, model.RequestTimeoutMs);
        model.KeepAlive = ReadBool(conn, MelsecConnectionKeys.KeepAlive, model.KeepAlive);
        model.MaxTransactionRetries = ReadInt(conn, MelsecConnectionKeys.MaxTransactionRetries, model.MaxTransactionRetries);
        model.InitialBackoffMs = ReadInt(conn, MelsecConnectionKeys.InitialBackoffMs, model.InitialBackoffMs);
        model.MaxBackoffMs = ReadInt(conn, MelsecConnectionKeys.MaxBackoffMs, model.MaxBackoffMs);
        model.BackoffMultiplier = ReadDouble(conn, MelsecConnectionKeys.BackoffMultiplier, model.BackoffMultiplier);
        model.CircuitBreakerThreshold = ReadInt(conn, MelsecConnectionKeys.CircuitBreakerThreshold, model.CircuitBreakerThreshold);
        model.CircuitBreakerResetMs = ReadInt(conn, MelsecConnectionKeys.CircuitBreakerResetMs, model.CircuitBreakerResetMs);
        model.MaxGapWords = ReadInt(conn, MelsecConnectionKeys.MaxGapWords, model.MaxGapWords);
        model.MaxPointsPerRequest = ReadInt(conn, MelsecConnectionKeys.MaxPointsPerRequest, model.MaxPointsPerRequest);

        if (conn.TryGetProperty(MelsecConnectionKeys.Tags, out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tagEl in tagsEl.EnumerateArray())
            {
                if (tagEl.ValueKind != JsonValueKind.Object) continue;
                model.Tags.Add(new MelsecTagWizardRow
                {
                    Name = ReadString(tagEl, MelsecConnectionKeys.TagName) ?? string.Empty,
                    Address = ReadString(tagEl, MelsecConnectionKeys.Address) ?? string.Empty,
                    Datatype = ReadString(tagEl, MelsecConnectionKeys.Datatype),
                    WordOrder = ReadString(tagEl, MelsecConnectionKeys.WordOrder) ?? MelsecWordOrder.LowWordFirst.ToString(),
                    ScanRateMs = ReadInt(tagEl, MelsecConnectionKeys.ScanRateMs, 1000),
                    Unit = ReadString(tagEl, MelsecConnectionKeys.Unit),
                    Scale = ReadOptionalDouble(tagEl, MelsecConnectionKeys.Scale),
                    Offset = ReadOptionalDouble(tagEl, MelsecConnectionKeys.Offset),
                });
            }
        }

        return model;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────
    private static void RangeCheck(List<ValidationIssue> errors, int value, int min, int max, string label, string path)
    {
        if (value < min || value > max)
        {
            errors.Add(new ValidationIssue
            {
                Code = "MELSEC.CONFIG_ROUTE_RANGE",
                Message = $"{label} must be 0x{min:X}–0x{max:X}.",
                Path = path,
            });
        }
    }

    private static ValidationIssue Issue(string message, string path) =>
        new() { Code = "MELSEC.CONFIG_INVALID", Message = message, Path = path };

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int ReadInt(JsonElement obj, string name, int defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : defaultValue;

    private static bool ReadBool(JsonElement obj, string name, bool defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : defaultValue;

    private static double ReadDouble(JsonElement obj, string name, double defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : defaultValue;

    private static double? ReadOptionalDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

/// <summary>One tag row in the MELSEC wizard's tag table.</summary>
public sealed class MelsecTagWizardRow
{
    /// <summary>Canonical tag name emitted to the pipeline.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Operator-authored MELSEC address — e.g. <c>"D100"</c>, <c>"W1A"</c>, <c>"D100.3"</c>.</summary>
    public string Address { get; set; } = string.Empty;
    /// <summary>Datatype — one of <see cref="MelsecSourceWizardModel.Datatypes"/>; null = untouched.</summary>
    public string? Datatype { get; set; }
    /// <summary>Word order for 32-bit datatypes; ignored otherwise. Default LowWordFirst.</summary>
    public string WordOrder { get; set; } = "LowWordFirst";
    /// <summary>Scan period in milliseconds.</summary>
    public int ScanRateMs { get; set; } = 1000;
    /// <summary>Optional engineering unit.</summary>
    public string? Unit { get; set; }
    /// <summary>Optional linear scale factor (scaled = raw * Scale + Offset).</summary>
    public double? Scale { get; set; }
    /// <summary>Optional additive offset.</summary>
    public double? Offset { get; set; }
}
