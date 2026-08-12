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
//
//          Datatype: EtherNet/IP gives us nothing to ask. There is no browse,
//          and the library exposes only element size — which cannot separate
//          DINT from REAL (both 4 bytes). Test Read takes the datatype as an
//          INPUT, so it cannot answer the question either. The only signal at
//          authoring time is the name the controller engineer chose, and Logix
//          naming is conventional enough to read: SuggestDatatype turns that
//          convention into a SUGGESTION that fills the cell only while it is
//          still untouched (the S7 wizard's "suggest, never coerce" contract).
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §3.1, §5.2
// ============================================================================

using System;
using System.Buffers;
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

    // ─── Datatype suggestion (naming convention, not knowledge) ─────────

    // Separators that end a Logix scope prefix or a member path:
    // "Program:MainProgram.Tank1_Level" → the LEAF is "Tank1_Level".
    // Cached because this runs on every keystroke of every address cell, and
    // LastIndexOfAny(new[] { '.', ':' }) would allocate the array each time.
    private static readonly SearchValues<char> LeafSeparators = SearchValues.Create(".:");

    private static readonly SearchValues<char> Digits = SearchValues.Create("0123456789");

    // The three word groups below are CONVENTION, not fact. They say how a
    // controller engineer usually names a value, which is the only thing we
    // can read without a browse. Nothing here claims to know the controller,
    // so every group is deliberately small and boring — a suggestion that is
    // sometimes absent costs an operator one dropdown; a suggestion that is
    // confidently wrong costs them a wrong-typed tag they never looked at.

    /// <summary>Names that read as an on/off condition — flags, commands, states.</summary>
    private static readonly HashSet<string> BoolWords = new(StringComparer.Ordinal)
    {
        "RUNNING", "STARTED", "STOPPED", "START", "STOP", "ENABLE", "ENABLED", "EN",
        "DISABLE", "DISABLED", "RESET", "FAULT", "ALARM", "WARNING", "READY", "BUSY",
        "DONE", "COMPLETE", "OK", "ACTIVE", "INACTIVE", "ESTOP", "PB", "BTN", "BUTTON",
        "SWITCH", "FLAG", "BIT", "VALID", "PRESENT", "OPEN", "CLOSED", "JAM", "TRIP",
        "INHIBIT", "INTERLOCK", "PERMISSIVE", "ACK", "REQ", "REQUEST", "AUTO", "MANUAL",
        "HOME", "HOMED", "STATUS", "ONLINE", "HEARTBEAT",
    };

    /// <summary>Names that read as an analog measurement or setpoint.</summary>
    private static readonly HashSet<string> RealWords = new(StringComparer.Ordinal)
    {
        "TEMP", "TEMPERATURE", "PRESSURE", "PSI", "VACUUM", "FLOW", "RATE", "LEVEL",
        "SPEED", "RPM", "TORQUE", "VOLTAGE", "VOLT", "VOLTS", "AMP", "AMPS", "POWER",
        "KW", "WEIGHT", "FORCE", "HUMIDITY", "SETPOINT", "POSITION", "ANGLE", "DEG",
        "DIAMETER", "THICKNESS", "PERCENT", "PCT", "RATIO", "TENSION", "VIBRATION",
        "DENSITY",
    };

    /// <summary>Names that read as a whole-number tally or ordinal.</summary>
    private static readonly HashSet<string> CountWords = new(StringComparer.Ordinal)
    {
        "COUNT", "COUNTER", "CNT", "CTR", "TOTAL", "QTY", "QUANTITY", "INDEX", "IDX",
        "STEP", "SEQ", "SEQUENCE", "CYCLE", "REJECT", "PIECE",
    };

    /// <summary>
    /// Suggest an element type from the tag address. This is a reading of the
    /// NAME the controller engineer chose — it is not knowledge of the
    /// controller, and it is never authoritative: EtherNet/IP exposes no way to
    /// ask a tag its type here (there is no browse, and element size cannot
    /// separate DINT from REAL). Returns <see langword="null"/> when the name
    /// says nothing recognisable, which leaves the operator to choose rather
    /// than handing them a wrong guess dressed as a decision.
    /// <para>
    /// Reads the LEAF of the address: scope prefixes
    /// (<c>Program:MainProgram.X</c>), member paths (<c>Motor.X</c>) and array
    /// subscripts (<c>Tanks[2]</c>) are stripped first. A numeric member
    /// (<c>Status.3</c>) is a bit access on an integer, so it reads as BOOL.
    /// Words are then matched RIGHT TO LEFT, because the last word is the noun
    /// that names the value — that keeps <c>Alarm_Count</c> a count and
    /// <c>Cycle_Count_Alarm</c> a flag.
    /// </para>
    /// </summary>
    public static string? SuggestDatatype(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var span = address.AsSpan().Trim();
        var separator = span.LastIndexOfAny(LeafSeparators);
        var leaf = separator >= 0 ? span[(separator + 1)..] : span;

        // "Tanks[2]" — the subscript is an element index, not part of the name.
        var bracket = leaf.IndexOf('[');
        if (bracket >= 0)
        {
            leaf = leaf[..bracket];
        }

        if (leaf.IsEmpty)
        {
            return null;
        }

        // "Status.3" — a numeric member of an integer is a bit access. Only
        // after a separator: a bare number is not a tag name.
        if (separator >= 0 && leaf.IndexOfAnyExcept(Digits) < 0)
        {
            return "BOOL";
        }

        var words = SplitWords(leaf);
        for (var i = words.Count - 1; i >= 0; i--)
        {
            if (MatchWord(words[i]) is { } suggestion)
            {
                return suggestion;
            }
        }

        return null;
    }

    /// <summary>
    /// Look one word up in the three convention groups, falling back to the
    /// singular so "Alarms" / "Cycles" / "Rejects" read the same as their
    /// singular form. The exact word is tried first so "STATUS" is never
    /// mangled into "STATU".
    /// </summary>
    private static string? MatchWord(string word)
    {
        if (BoolWords.Contains(word))
        {
            return "BOOL";
        }
        if (RealWords.Contains(word))
        {
            return "REAL";
        }
        if (CountWords.Contains(word))
        {
            return "DINT";
        }

        return word.Length > 3 && word.EndsWith('S') ? MatchWord(word[..^1]) : null;
    }

    /// <summary>
    /// Split a leaf name into upper-cased words on both conventions Logix
    /// engineers use: separators ("Tank1_Level" → TANK, LEVEL) and casing
    /// ("MotorTemp" → MOTOR, TEMP; "RPMActual" → RPM, ACTUAL). Whole-word
    /// matching is what keeps "Overflow" from reading as a FLOW measurement.
    /// </summary>
    private static List<string> SplitWords(ReadOnlySpan<char> leaf)
    {
        var words = new List<string>();
        var start = -1;

        for (var i = 0; i < leaf.Length; i++)
        {
            var c = leaf[i];
            if (!char.IsLetter(c))
            {
                if (start >= 0)
                {
                    words.Add(leaf[start..i].ToString().ToUpperInvariant());
                    start = -1;
                }
                continue;
            }
            if (start < 0)
            {
                start = i;
                continue;
            }

            var previous = leaf[i - 1];
            var camelBoundary = char.IsUpper(c) && char.IsLower(previous);
            var acronymBoundary = char.IsUpper(c) && char.IsUpper(previous)
                && i + 1 < leaf.Length && char.IsLower(leaf[i + 1]);
            if (camelBoundary || acronymBoundary)
            {
                words.Add(leaf[start..i].ToString().ToUpperInvariant());
                start = i;
            }
        }

        if (start >= 0)
        {
            words.Add(leaf[start..].ToString().ToUpperInvariant());
        }

        return words;
    }

    // ─── What the suggestion tells the operator ─────────────────────────
    //
    // SuggestDatatype returning null is a decision, not a gap: a confidently
    // wrong type costs the operator a mis-typed tag they never look at again.
    // What was missing is that the wizard never SAID so — an empty cell reads
    // as "the wizard forgot", which is how a considered limitation got filed as
    // a bug. These two notes make the outcome visible either way: we read the
    // name and guessed (say so, and say it is a guess), or we read it and could
    // not tell (say that too). Neither is a validation error — they never block
    // Save and never render in an error colour.

    /// <summary>
    /// Note shown when an address is present but its name says nothing this
    /// wizard can read. Guidance, not an error.
    /// </summary>
    public const string DatatypeNotInferredNote = "Can't infer from this tag name — choose a datatype.";

    /// <summary>
    /// Note shown when the datatype in the cell came from the address-driven
    /// suggestion, so the operator can see it is a reading of the name rather
    /// than a fact about the controller — and override it.
    /// </summary>
    public const string DatatypeSuggestedNote = "Suggested from the tag name — change it if the controller differs.";

    /// <summary>
    /// True when the wizard looked at the tag name and could not tell: an
    /// address has been entered, the datatype cell is still empty, and
    /// <see cref="SuggestDatatype"/> declined to guess.
    /// <para>
    /// A blank address is deliberately NOT this case — that is a row the
    /// operator has not reached yet, not a failed reading, and the same
    /// distinction <see cref="ValidateTag"/> already makes for the empty
    /// datatype cell. A name the wizard CAN read but whose cell the operator
    /// cleared is not this case either: the reading did not fail there.
    /// </para>
    /// </summary>
    public static bool DatatypeCannotBeInferred(EthernetIpTagWizardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return !string.IsNullOrWhiteSpace(row.Address)
            && string.IsNullOrWhiteSpace(row.Datatype)
            && SuggestDatatype(row.Address) is null;
    }

    /// <summary>
    /// Fill an untouched datatype cell from what the address NAME reads as.
    /// "Suggest, never coerce" (the S7 wizard's contract): a cell that already
    /// holds a value — operator-chosen, or hydrated from an existing source —
    /// is never overwritten. Returns <see langword="true"/> only when a
    /// suggestion was actually applied, which is what lets the wizard caption
    /// the value as a guess.
    /// </summary>
    public static bool TryApplyDatatypeSuggestion(EthernetIpTagWizardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!string.IsNullOrWhiteSpace(row.Datatype))
        {
            return false;
        }

        row.Datatype = SuggestDatatype(row.Address);
        return !string.IsNullOrWhiteSpace(row.Datatype);
    }

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

        // An empty cell gets its own sentence. Falling through to the
        // "not recognised" branch reported "Datatype '' is not recognised",
        // which sends the operator hunting for a spelling mistake in an empty
        // box — an unfinished row is not a typo.
        if (string.IsNullOrWhiteSpace(row.Datatype))
        {
            errors.Add(new ValidationIssue
            {
                Code = "ETHERNETIP.CONFIG_MISSING_FIELD",
                Message = $"Datatype is not set yet. Pick the element type the controller uses for this tag: {string.Join(", ", Datatypes)}.",
                Path = "Datatype",
            });
        }
        else
        {
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

    /// <summary>
    /// Element-type hint (BOOL / SINT / INT / DINT / LINT / REAL / LREAL / STRING).
    /// <c>null</c> means "not chosen yet", which is the honest starting state:
    /// nothing about a new row tells us the controller's type. It also makes
    /// the row eligible for the address-driven suggestion — once a value is
    /// present, whether the operator picked it or accepted a suggestion, it is
    /// never overwritten. Clearing the cell hands the row back to the
    /// suggestion.
    /// </summary>
    public string? Datatype { get; set; }

    /// <summary>Scan period in milliseconds.</summary>
    public int ScanRateMs { get; set; } = 1000;

    /// <summary>Optional linear scale factor: <c>scaled = raw * Scale + Offset</c>.</summary>
    public double? Scale { get; set; }

    /// <summary>Optional additive offset; see <see cref="Scale"/>.</summary>
    public double? Offset { get; set; }

    /// <summary>Optional engineering unit string copied to the canonical data point.</summary>
    public string? Unit { get; set; }
}
