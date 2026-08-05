// ============================================================================
// File: Wizards/S7SourceWizardModel.cs
// Purpose: In-memory state for the Add-Siemens-S7-Source wizard (M.2b.2).
//          Mirrors ModbusSourceWizardModel: Razor two-way-binds every form
//          field to this POCO; on Save the wizard calls BuildSourceInstance()
//          to produce a canonical SourceInstanceConfig with the S7-specific
//          block packed into the opaque Connection JsonElement exactly as
//          S7SourceConfiguration.FromSourceInstance expects.
//
//          S7 cannot self-describe, so this is a manual tag-address editor
//          (Modbus-style), not a browse flow. The per-tag list lives under
//          the connection key "tags" (NOT "tagDefinitions" — that is Modbus).
//
//          Datatype dropdown values are the 14 S7Datatype enum names; the
//          adapter's S7DatatypeParser is case-insensitive so they round-trip
//          verbatim. The one exception is String, which the wizard splits
//          into the "String" choice plus a StringLength field and composes
//          into the adapter's "string[N]" wire form on emit.
//
//          Datatype/address-width compatibility is validated through the
//          shared S7TagCompatibility checker so a config the wizard accepts
//          never fails adapter Initialize for a deterministic reason
//          (M.2b.2 v2 decision: planner-rejected combos block Save).
// Reference: docs/sessions/2026-06-02-s7-source-wizard-plan-v2.md (M2)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.S7;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Wizard state for adding a new Siemens S7 source. Razor binds form fields
/// directly to these properties; on Save, <see cref="BuildSourceInstance"/>
/// produces a canonical <see cref="SourceInstanceConfig"/>.
/// </summary>
public sealed class S7SourceWizardModel
{
    /// <summary>Protocol-name constant carried on the emitted instance.</summary>
    public const string ProtocolName = "s7";

    // ─── Identity ────────────────────────────────────────────────────────

    /// <summary>Stable instance id (e.g. <c>"s7-press-3"</c>). Regex enforced by Core.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device identifier.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device display name.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Device class — typically <c>"plc"</c>.</summary>
    public string DeviceClass { get; set; } = "plc";

    /// <summary>Optional free-text description. Stored under connection <c>"description"</c>.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the source is enabled when the new draft is applied.</summary>
    public bool Enabled { get; set; } = true;

    // ─── Connection ──────────────────────────────────────────────────────

    /// <summary>PLC IP address or hostname.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>ISO-on-TCP port. S7 default is 102.</summary>
    public int Port { get; set; } = 102;

    /// <summary>Hardware rack number. Usually 0.</summary>
    public int Rack { get; set; }

    /// <summary>CPU slot number. Commonly 1 (S7-1200/1500) or 2 (S7-300).</summary>
    public int Slot { get; set; } = 1;

    /// <summary>Connection type — Basic / OP / PG. Basic works against every CPU.</summary>
    public string ConnectionType { get; set; } = "Basic";

    /// <summary>
    /// Optimized-DB-access flag. Informational at this release — leave off
    /// for absolute DB addresses; standard/non-optimized DBs are the
    /// supported path.
    /// </summary>
    public bool OptimizedDbAccess { get; set; }

    /// <summary>Connect timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; set; } = 2000;

    /// <summary>Per-read request timeout in milliseconds.</summary>
    public int RequestTimeoutMs { get; set; } = 1000;

    /// <summary>Keep the TCP session alive between scans.</summary>
    public bool KeepAlive { get; set; } = true;

    // ─── Retry / Backoff / Circuit Breaker ──────────────────────────────

    /// <summary>Max per-transaction retries before surfacing a failure.</summary>
    public int MaxTransactionRetries { get; set; } = 2;

    /// <summary>Initial backoff in ms after a connect failure.</summary>
    public int InitialBackoffMs { get; set; } = 2000;

    /// <summary>Maximum backoff in ms — caps exponential growth.</summary>
    public int MaxBackoffMs { get; set; } = 60_000;

    /// <summary>Exponential multiplier between consecutive connect failures.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Consecutive connect failures that open the circuit breaker.</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Breaker cool-down window in ms before the next probe.</summary>
    public int CircuitBreakerResetMs { get; set; } = 30_000;

    // ─── Scan planner ───────────────────────────────────────────────────

    /// <summary>Max gap (bytes) the scan planner coalesces across.</summary>
    public int MaxGapBytes { get; set; } = 16;

    /// <summary>Max bytes per single read.</summary>
    public int MaxReadBytes { get; set; } = 200;

    // ─── Tag definitions ────────────────────────────────────────────────

    /// <summary>Per-tag definitions. Empty initial list; operator adds rows in the wizard.</summary>
    public List<S7TagWizardRow> Tags { get; set; } = new();

    /// <summary>The 14 supported S7 datatype dropdown values (match <c>S7Datatype</c> names).</summary>
    public static readonly IReadOnlyList<string> Datatypes = new[]
    {
        "Bool", "Byte", "SInt", "USInt", "Int", "Word", "DInt", "DWord",
        "Real", "LReal", "LInt", "ULInt", "String", "Char",
    };

    /// <summary>Connection-type dropdown values.</summary>
    public static readonly IReadOnlyList<string> ConnectionTypes = new[] { "Basic", "OP", "PG" };

    /// <summary>
    /// Datatype suggestions for an address width hint — surfaced as the
    /// "Address suggests: …" copy and used to auto-fill the datatype while
    /// the field is still untouched (Q2: suggest, never coerce).
    /// </summary>
    public static IReadOnlyList<string> SuggestDatatypes(S7AddressWidthHint widthHint) => widthHint switch
    {
        S7AddressWidthHint.Bit => new[] { "Bool" },
        S7AddressWidthHint.Byte => new[] { "Byte", "SInt", "USInt", "Char" },
        S7AddressWidthHint.Word => new[] { "Int", "Word" },
        S7AddressWidthHint.DWord => new[] { "DInt", "DWord", "Real" },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// Validate a single tag row, returning blocking errors only (empty when
    /// the row is safe to Save). Mirrors the rules the S7 adapter applies at
    /// Initialize: name + address + datatype required; address must parse;
    /// Timer/Counter addresses are unsupported this release; datatype must be
    /// one of the 14; scan rate positive; and the datatype/address-width
    /// combination must not be one the scan planner rejects.
    /// <para>
    /// <see cref="ValidationIssue.Path"/> matches the wizard field name
    /// (<c>"Name"</c>, <c>"Address"</c>, <c>"Datatype"</c>,
    /// <c>"StringLength"</c>, <c>"ScanRateMs"</c>) so the Razor table can
    /// light up the offending cell.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateTag(S7TagWizardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var errors = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(row.Name))
        {
            errors.Add(Issue("Tag name is required.", "Name"));
        }

        if (row.ScanRateMs <= 0)
        {
            errors.Add(Issue("Scan rate must be a positive number of milliseconds.", "ScanRateMs"));
        }

        // Address — required + must parse + Timer/Counter unsupported.
        S7Address? parsed = null;
        if (string.IsNullOrWhiteSpace(row.Address))
        {
            errors.Add(Issue("Address is required.", "Address"));
        }
        else if (TryParseAddress(row.Address, out var addr, out var addressError))
        {
            parsed = addr;
        }
        else
        {
            errors.Add(addressError!);
        }

        // Datatype — required + one of the 14 + (for String) a positive length.
        S7DatatypeSpec? spec = null;
        if (string.IsNullOrWhiteSpace(row.Datatype))
        {
            errors.Add(Issue("Datatype is required.", "Datatype"));
        }
        else if (!Datatypes.Contains(row.Datatype, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(Issue(
                $"Datatype '{row.Datatype}' is not supported. Choose one of: {string.Join(", ", Datatypes)}.",
                "Datatype"));
        }
        else if (string.Equals(row.Datatype, "String", StringComparison.OrdinalIgnoreCase))
        {
            if (row.StringLength is not { } len || len <= 0)
            {
                errors.Add(Issue("String datatype requires a positive length (in characters).", "StringLength"));
            }
            else
            {
                spec = new S7DatatypeSpec(S7Datatype.String, len);
            }
        }
        else
        {
            // S7DatatypeParser is case-insensitive — the enum-name dropdown
            // value parses directly.
            spec = S7DatatypeParser.Parse(row.Datatype, defaultSpec: default);
        }

        // Compatibility — only when both address and datatype resolved. The
        // two planner-rejected combinations are Save-blocking errors.
        if (parsed is { } a && spec is { } s)
        {
            var verdict = S7TagCompatibility.Check(a, s);
            if (verdict.BlocksSave)
            {
                errors.Add(new ValidationIssue
                {
                    Code = verdict.Code!,
                    Message = verdict.Message!,
                    Path = "Datatype",
                });
            }
        }

        return errors;
    }

    /// <summary>
    /// Compatibility verdict for a row whose address parses and datatype
    /// resolves; <c>null</c> when either is not yet valid. Lets the UI render
    /// a per-row warning (⚠) for the planner-accepted-but-suspicious
    /// narrower-than-width case without re-deriving the parse.
    /// </summary>
    public static S7CompatibilityVerdict? TagCompatibility(S7TagWizardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (string.IsNullOrWhiteSpace(row.Address) || string.IsNullOrWhiteSpace(row.Datatype))
        {
            return null;
        }
        if (!TryParseAddress(row.Address, out var addr, out _))
        {
            return null;
        }
        if (!Datatypes.Contains(row.Datatype, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        S7DatatypeSpec spec;
        if (string.Equals(row.Datatype, "String", StringComparison.OrdinalIgnoreCase))
        {
            if (row.StringLength is not { } len || len <= 0)
            {
                return null;
            }
            spec = new S7DatatypeSpec(S7Datatype.String, len);
        }
        else
        {
            spec = S7DatatypeParser.Parse(row.Datatype, defaultSpec: default);
        }

        return S7TagCompatibility.Check(addr, spec);
    }

    /// <summary>
    /// Validate the whole model: identity + connection essentials, every tag
    /// row, plus cross-row rules — duplicate tag names BLOCK (canonical
    /// identity must be unique), duplicate tag addresses WARN (the adapter
    /// coalesces them, so it is legal but usually a mistake), and the
    /// narrower-than-width compatibility case WARNS.
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(InstanceId))
        {
            errors.Add(Issue("Source id is required.", "InstanceId"));
        }
        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add(Issue("Host is required.", "Host"));
        }

        // Per-row errors + per-row compatibility warnings.
        for (var i = 0; i < Tags.Count; i++)
        {
            var row = Tags[i];
            foreach (var issue in ValidateTag(row))
            {
                errors.Add(issue with { Path = $"Tags[{i}].{issue.Path}" });
            }

            var verdict = TagCompatibility(row);
            if (verdict is { Severity: S7CompatibilitySeverity.Warning } w)
            {
                warnings.Add(new ValidationIssue { Code = w.Code!, Message = w.Message!, Path = $"Tags[{i}].Datatype" });
            }
        }

        // Duplicate tag names — block (ordinal; canonical TagName identity).
        foreach (var dup in Tags
                     .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                     .GroupBy(t => t.Name.Trim(), StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            errors.Add(Issue(
                $"Tag name '{dup.Key}' is used by {dup.Count()} tags. Tag names must be unique.",
                "Name"));
        }

        // Duplicate normalized addresses — warn (legal; usually unintended).
        foreach (var dup in Tags
                     .Select(t => TryParseAddress(t.Address, out var a, out _) ? a.ToString() : null)
                     .Where(s => s is not null)
                     .GroupBy(s => s!, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            warnings.Add(Issue(
                $"Address '{dup.Key}' is read by {dup.Count()} tags. This is allowed but usually unintended.",
                "Address"));
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Project the wizard state into a canonical
    /// <see cref="SourceInstanceConfig"/>. The S7-specific fields land in the
    /// opaque <c>Connection</c> <see cref="JsonElement"/> keyed by
    /// <see cref="S7ConnectionKeys"/>. S7 drives per-tag scan rates, so the
    /// top-level <c>Polling</c> is left at its default.
    /// </summary>
    public SourceInstanceConfig BuildSourceInstance()
    {
        var deviceId = string.IsNullOrWhiteSpace(DeviceId) ? InstanceId : DeviceId;
        var deviceName = string.IsNullOrWhiteSpace(DeviceName) ? InstanceId : DeviceName;
        var deviceClass = string.IsNullOrWhiteSpace(DeviceClass) ? "plc" : DeviceClass;

        var connection = new JsonObject
        {
            [S7ConnectionKeys.Host] = Host,
            [S7ConnectionKeys.Port] = Port,
            [S7ConnectionKeys.Rack] = Rack,
            [S7ConnectionKeys.Slot] = Slot,
            [S7ConnectionKeys.ConnectionType] = ConnectionType,
            [S7ConnectionKeys.OptimizedDbAccess] = OptimizedDbAccess,
            [S7ConnectionKeys.ConnectTimeoutMs] = ConnectTimeoutMs,
            [S7ConnectionKeys.RequestTimeoutMs] = RequestTimeoutMs,
            [S7ConnectionKeys.KeepAlive] = KeepAlive,
            [S7ConnectionKeys.MaxTransactionRetries] = MaxTransactionRetries,
            [S7ConnectionKeys.InitialBackoffMs] = InitialBackoffMs,
            [S7ConnectionKeys.MaxBackoffMs] = MaxBackoffMs,
            [S7ConnectionKeys.BackoffMultiplier] = BackoffMultiplier,
            [S7ConnectionKeys.CircuitBreakerThreshold] = CircuitBreakerThreshold,
            [S7ConnectionKeys.CircuitBreakerResetMs] = CircuitBreakerResetMs,
            [S7ConnectionKeys.MaxGapBytes] = MaxGapBytes,
            [S7ConnectionKeys.MaxReadBytes] = MaxReadBytes,
            [S7ConnectionKeys.DeviceId] = deviceId,
            [S7ConnectionKeys.DeviceName] = deviceName,
            [S7ConnectionKeys.DeviceClass] = deviceClass,
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
                [S7ConnectionKeys.TagName] = tag.Name,
                [S7ConnectionKeys.Address] = tag.Address,
                [S7ConnectionKeys.ScanRateMs] = tag.ScanRateMs,
            };
            if (!string.IsNullOrWhiteSpace(tag.Datatype))
            {
                tagNode[S7ConnectionKeys.Datatype] = ComposeDatatype(tag);
            }
            if (!string.IsNullOrWhiteSpace(tag.Unit))
            {
                tagNode[S7ConnectionKeys.Unit] = tag.Unit;
            }
            if (tag.Scale is { } scale)
            {
                tagNode[S7ConnectionKeys.Scale] = scale;
            }
            if (tag.Offset is { } offset)
            {
                tagNode[S7ConnectionKeys.Offset] = offset;
            }
            tagArray.Add(tagNode);
        }
        connection[S7ConnectionKeys.Tags] = tagArray;

        var json = connection.ToJsonString();
        using var doc = JsonDocument.Parse(json);

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
    /// Inverse of <see cref="BuildSourceInstance"/> — populate a fresh wizard
    /// model from a canonical <see cref="SourceInstanceConfig"/>. Used by
    /// Edit-mode routing to hydrate the form. Tag order is preserved; the
    /// canonical <c>"string[N]"</c> datatype splits back into the wizard's
    /// <c>Datatype="String"</c> + <c>StringLength=N</c> pair.
    /// <para>
    /// Round-trip invariant: re-emitting after hydrate produces a
    /// byte-equivalent <see cref="SourceInstanceConfig"/>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/>'s <c>ProtocolName</c> is not <c>"s7"</c>.
    /// </exception>
    public static S7SourceWizardModel HydrateFromExisting(SourceInstanceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProtocolName, ProtocolName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Source has protocol '{source.ProtocolName}', expected '{ProtocolName}'.",
                nameof(source));
        }

        var model = new S7SourceWizardModel
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

        // Identity is authoritative in the connection block (that is what
        // S7SourceConfiguration.FromSourceInstance reads), so prefer it.
        model.DeviceId = ReadString(conn, S7ConnectionKeys.DeviceId) ?? model.DeviceId;
        model.DeviceName = ReadString(conn, S7ConnectionKeys.DeviceName) ?? model.DeviceName;
        model.DeviceClass = ReadString(conn, S7ConnectionKeys.DeviceClass) ?? model.DeviceClass;
        model.Description = ReadString(conn, "description");

        model.Host = ReadString(conn, S7ConnectionKeys.Host) ?? string.Empty;
        model.Port = ReadInt(conn, S7ConnectionKeys.Port, model.Port);
        model.Rack = ReadInt(conn, S7ConnectionKeys.Rack, model.Rack);
        model.Slot = ReadInt(conn, S7ConnectionKeys.Slot, model.Slot);
        model.ConnectionType = ReadString(conn, S7ConnectionKeys.ConnectionType) ?? model.ConnectionType;
        model.OptimizedDbAccess = ReadBool(conn, S7ConnectionKeys.OptimizedDbAccess, model.OptimizedDbAccess);
        model.ConnectTimeoutMs = ReadInt(conn, S7ConnectionKeys.ConnectTimeoutMs, model.ConnectTimeoutMs);
        model.RequestTimeoutMs = ReadInt(conn, S7ConnectionKeys.RequestTimeoutMs, model.RequestTimeoutMs);
        model.KeepAlive = ReadBool(conn, S7ConnectionKeys.KeepAlive, model.KeepAlive);
        model.MaxTransactionRetries = ReadInt(conn, S7ConnectionKeys.MaxTransactionRetries, model.MaxTransactionRetries);
        model.InitialBackoffMs = ReadInt(conn, S7ConnectionKeys.InitialBackoffMs, model.InitialBackoffMs);
        model.MaxBackoffMs = ReadInt(conn, S7ConnectionKeys.MaxBackoffMs, model.MaxBackoffMs);
        model.BackoffMultiplier = ReadDouble(conn, S7ConnectionKeys.BackoffMultiplier, model.BackoffMultiplier);
        model.CircuitBreakerThreshold = ReadInt(conn, S7ConnectionKeys.CircuitBreakerThreshold, model.CircuitBreakerThreshold);
        model.CircuitBreakerResetMs = ReadInt(conn, S7ConnectionKeys.CircuitBreakerResetMs, model.CircuitBreakerResetMs);
        model.MaxGapBytes = ReadInt(conn, S7ConnectionKeys.MaxGapBytes, model.MaxGapBytes);
        model.MaxReadBytes = ReadInt(conn, S7ConnectionKeys.MaxReadBytes, model.MaxReadBytes);

        if (conn.TryGetProperty(S7ConnectionKeys.Tags, out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tagEl in tagsEl.EnumerateArray())
            {
                if (tagEl.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var row = new S7TagWizardRow { Datatype = null };
                row.Name = ReadString(tagEl, S7ConnectionKeys.TagName) ?? string.Empty;
                row.Address = ReadString(tagEl, S7ConnectionKeys.Address) ?? string.Empty;
                row.ScanRateMs = ReadInt(tagEl, S7ConnectionKeys.ScanRateMs, 1000);
                row.Unit = ReadString(tagEl, S7ConnectionKeys.Unit);
                row.Scale = ReadOptionalDouble(tagEl, S7ConnectionKeys.Scale);
                row.Offset = ReadOptionalDouble(tagEl, S7ConnectionKeys.Offset);

                var datatype = ReadString(tagEl, S7ConnectionKeys.Datatype);
                if (datatype is not null)
                {
                    DecomposeDatatype(datatype, row);
                }

                model.Tags.Add(row);
            }
        }

        return model;
    }

    /// <summary>
    /// Map an imported <see cref="S7TagDefinition"/> (from
    /// <c>S7TagCsvImporter</c>) into a wizard row. The datatype is normalized
    /// to its canonical dropdown name (e.g. <c>"int"</c> → <c>"Int"</c>,
    /// <c>"string[16]"</c> → <c>"String"</c> + length 16); when the imported
    /// datatype is blank it is derived from the address width (DBX→Bool,
    /// DBB→Byte, DBW→Word, DBD→DWord) so every imported row is a complete,
    /// valid wizard row the operator can review and edit.
    /// </summary>
    public static S7TagWizardRow ToWizardRow(S7TagDefinition tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        var row = new S7TagWizardRow
        {
            Name = tag.Name,
            Address = tag.Address,
            ScanRateMs = tag.ScanRateMs,
            Unit = tag.Unit,
            Scale = tag.Scale,
            Offset = tag.Offset,
            Datatype = null,
        };

        if (!string.IsNullOrWhiteSpace(tag.Datatype))
        {
            try
            {
                var spec = S7DatatypeParser.Parse(tag.Datatype, defaultSpec: default);
                if (spec.Datatype == S7Datatype.String)
                {
                    row.Datatype = "String";
                    row.StringLength = spec.MaxStringChars;
                }
                else
                {
                    row.Datatype = spec.Datatype.ToString();
                }
            }
            catch (ArgumentException)
            {
                // Leave the raw token so the row's own validation surfaces it.
                row.Datatype = tag.Datatype;
            }
        }
        else
        {
            row.Datatype = DeriveDatatypeName(tag.Address);
        }

        return row;
    }

    private static string? DeriveDatatypeName(string address)
    {
        try
        {
            return S7AddressParser.Parse(address).WidthHint switch
            {
                S7AddressWidthHint.Bit => "Bool",
                S7AddressWidthHint.Byte => "Byte",
                S7AddressWidthHint.Word => "Word",
                S7AddressWidthHint.DWord => "DWord",
                _ => null,
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compose the wizard's datatype representation into the adapter wire
    /// form. String becomes <c>"string[N]"</c>; everything else passes
    /// through unchanged (S7DatatypeParser lowercases on parse).
    /// </summary>
    private static string ComposeDatatype(S7TagWizardRow tag) =>
        string.Equals(tag.Datatype, "String", StringComparison.OrdinalIgnoreCase)
        && tag.StringLength is { } len
        && len > 0
            ? $"string[{len}]"
            : tag.Datatype!;

    /// <summary>
    /// Split the adapter wire form back into the wizard's representation.
    /// <c>"string[N]"</c> → <c>Datatype="String"</c> + <c>StringLength=N</c>;
    /// any other value is restored verbatim.
    /// </summary>
    private static void DecomposeDatatype(string datatype, S7TagWizardRow row)
    {
        const string Prefix = "string[";
        if (datatype.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && datatype.EndsWith(']')
            && int.TryParse(
                datatype.AsSpan(Prefix.Length, datatype.Length - Prefix.Length - 1),
                out var len))
        {
            row.Datatype = "String";
            row.StringLength = len;
        }
        else
        {
            row.Datatype = datatype;
        }
    }

    private static bool TryParseAddress(string address, out S7Address parsed, out ValidationIssue? error)
    {
        parsed = default;
        error = null;
        try
        {
            parsed = S7AddressParser.Parse(address);
        }
        catch (ArgumentException)
        {
            error = Issue(
                $"'{address}' is not a valid S7 address. Examples: DB1.DBW0, DB1.DBX0.0, M10.2, IW4, Q0.1.",
                "Address");
            return false;
        }

        // Timer/Counter parse but are not supported through the adapter
        // read/decode path this release (M.2b.2 v2 decision).
        if (parsed.Area is S7MemoryArea.Timer or S7MemoryArea.Counter)
        {
            var areaWord = parsed.Area == S7MemoryArea.Timer ? "Timer" : "Counter";
            error = new ValidationIssue
            {
                Code = "S7.UNSUPPORTED_TIMER_COUNTER_ADDRESS",
                Message = $"{areaWord} addresses are recognized but not supported in this release. " +
                          "Use DB, M, I, or Q addresses instead. Examples: DB1.DBW0, M10.2, IW4, Q0.1.",
                Path = "Address",
            };
            return false;
        }

        return true;
    }

    private static ValidationIssue Issue(string message, string path) =>
        new() { Code = "S7.CONFIG_INVALID", Message = message, Path = path };

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int ReadInt(JsonElement obj, string name, int defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : defaultValue;

    private static bool ReadBool(JsonElement obj, string name, bool defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : defaultValue;

    private static double ReadDouble(JsonElement obj, string name, double defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : defaultValue;

    private static double? ReadOptionalDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

/// <summary>
/// One tag row in the wizard's tag table. Maps to <c>S7TagDefinition</c> on
/// the Core side, but uses strings for the datatype so the dropdown binds
/// cleanly and an untouched datatype (<c>null</c>) is detectable for the
/// suggest-don't-coerce behavior.
/// </summary>
public sealed class S7TagWizardRow
{
    /// <summary>Canonical tag name emitted to the pipeline.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Operator-authored S7 address — e.g. <c>"DB1.DBW0"</c>, <c>"M10.2"</c>.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Datatype — one of the 14 <see cref="S7SourceWizardModel.Datatypes"/>
    /// names. <c>null</c> means "untouched" so the wizard may auto-fill a
    /// suggestion; once the operator picks a value it is never overwritten.
    /// </summary>
    public string? Datatype { get; set; }

    /// <summary>Character length for the <c>String</c> datatype; ignored otherwise.</summary>
    public int? StringLength { get; set; }

    /// <summary>Scan period in milliseconds.</summary>
    public int ScanRateMs { get; set; } = 1000;

    /// <summary>Optional engineering unit string.</summary>
    public string? Unit { get; set; }

    /// <summary>Optional linear scale factor: <c>scaled = raw * Scale + Offset</c>.</summary>
    public double? Scale { get; set; }

    /// <summary>Optional additive offset; see <see cref="Scale"/>.</summary>
    public double? Offset { get; set; }
}
