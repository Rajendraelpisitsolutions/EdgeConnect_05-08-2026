// ============================================================================
// File: Wizards/ModbusSourceWizardModel.cs
// Purpose: In-memory state for the Add-Modbus-Source wizard. Razor
//          two-way-binds every form field to this POCO; on Save the
//          wizard calls BuildSourceInstance() to produce a canonical
//          SourceInstanceConfig (with the Modbus-specific block packed
//          into the opaque Connection JsonElement, exactly as
//          ModbusTcpSourceConfiguration.FromSourceInstance expects).
//
//          Kept deliberately wire-shape friendly (string enums for
//          register class / byte order / encapsulation) so MudSelect
//          can bind directly. The conversion to canonical types
//          happens in BuildSourceInstance().
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2b.1
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.ModbusTcp;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Wizard state for adding a new Modbus TCP source. Razor binds form
/// fields directly to these properties; on Save, <see cref="BuildSourceInstance"/>
/// produces a canonical <see cref="SourceInstanceConfig"/>.
/// </summary>
public sealed class ModbusSourceWizardModel
{
    // ─── Identity ────────────────────────────────────────────────────────

    /// <summary>
    /// Protocol id this source is saved as — <c>"modbustcp"</c> (native TCP) or
    /// <c>"modbusrtu"</c> (serial / RTU-over-TCP). ADR-0033. The wizard sets this
    /// from its mode; both share this model and the Modbus adapter.
    /// </summary>
    public string ProtocolName { get; set; } = "modbustcp";

    /// <summary>Stable instance id (e.g. <c>"modbus-line-7"</c>). Regex enforced by Core.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device identifier.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device display name.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Device class — typically <c>"plc"</c> or <c>"cnc"</c>.</summary>
    public string DeviceClass { get; set; } = "plc";

    /// <summary>Whether the source is enabled when the new draft is applied.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Top-level polling interval in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = 200;

    // ─── Connection ──────────────────────────────────────────────────────

    /// <summary>Slave / gateway hostname or IP.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>TCP port (default 502).</summary>
    public int Port { get; set; } = 502;

    /// <summary>Wire encapsulation — <c>"Tcp"</c> (default), <c>"RtuOverTcp"</c>, or <c>"SerialRtu"</c>.</summary>
    public string Encapsulation { get; set; } = "Tcp";

    /// <summary>Default slave unit id (0..247).</summary>
    public int DefaultUnitId { get; set; } = 1;

    /// <summary>TCP handshake timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; set; } = 3000;

    /// <summary>Per-request read/write timeout in milliseconds.</summary>
    public int RequestTimeoutMs { get; set; } = 2000;

    /// <summary>Keep TCP socket open across polls.</summary>
    public bool KeepAlive { get; set; } = true;

    // ─── Serial (SerialRtu encapsulation only) ──────────────────────────

    /// <summary>Serial port (e.g. <c>COM3</c> / <c>/dev/ttyUSB0</c>). Required for SerialRtu.</summary>
    public string SerialPort { get; set; } = string.Empty;

    /// <summary>Serial baud rate (default 9600). Modbus RTU is always 8 data bits.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>Serial parity — None / Even / Odd / Mark / Space.</summary>
    public string Parity { get; set; } = "None";

    /// <summary>Serial stop bits — One / Two / OnePointFive.</summary>
    public string StopBits { get; set; } = "One";

    /// <summary>Serial flow control — None / XOnXOff / RequestToSend / RequestToSendXOnXOff.</summary>
    public string Handshake { get; set; } = "None";

    /// <summary>Encapsulation choices for the wizard dropdown.</summary>
    public static readonly IReadOnlyList<string> Encapsulations = new[] { "Tcp", "RtuOverTcp", "SerialRtu" };

    /// <summary>Serial parity choices for the wizard dropdown.</summary>
    public static readonly IReadOnlyList<string> Parities = new[] { "None", "Even", "Odd", "Mark", "Space" };

    /// <summary>Serial stop-bit choices for the wizard dropdown.</summary>
    public static readonly IReadOnlyList<string> StopBitsOptions = new[] { "One", "Two", "OnePointFive" };

    /// <summary>Serial handshake choices for the wizard dropdown.</summary>
    public static readonly IReadOnlyList<string> Handshakes =
        new[] { "None", "XOnXOff", "RequestToSend", "RequestToSendXOnXOff" };

    // ─── Retry / Backoff / Circuit Breaker ──────────────────────────────

    /// <summary>Max per-transaction retries before surfacing a failure.</summary>
    public int MaxTransactionRetries { get; set; } = 2;

    /// <summary>Initial backoff in ms after a connect failure.</summary>
    public int InitialBackoffMs { get; set; } = 1000;

    /// <summary>Maximum backoff in ms — caps exponential growth.</summary>
    public int MaxBackoffMs { get; set; } = 30_000;

    /// <summary>Exponential multiplier between consecutive connect failures.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Consecutive connect failures that open the circuit breaker.</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Breaker cool-down window in ms before the next probe.</summary>
    public int CircuitBreakerResetMs { get; set; } = 30_000;

    // ─── Scan planner ───────────────────────────────────────────────────

    /// <summary>
    /// Max gap (registers/bits) that the scan planner coalesces across. Default
    /// 0 = read each tag with its own transaction (never coalesce). This is the
    /// safe default: it works on ANY device, including sparse register maps
    /// (registers spaced apart, e.g. only every 4th) where coalescing would read
    /// a block spanning non-existent registers and the whole read is rejected
    /// with "illegal data address". Operators can raise it for contiguous maps to
    /// cut round-trips, but 0 means a freshly-added source just works.
    /// </summary>
    // NB: left uninitialized — int defaults to 0, which is the intended default
    // (CA1805 forbids an explicit "= 0").
    public int MaxGapRegisters { get; set; }

    /// <summary>
    /// Notation the operator is using for tag addresses. Emitted as
    /// <c>addressBase</c>; the config parser converts the entered addresses to
    /// zero-based logical addresses. Default <see cref="ModbusAddressBase.ZeroBased"/>
    /// preserves the historical behaviour.
    /// </summary>
    public ModbusAddressBase AddressBase { get; set; } = ModbusAddressBase.ZeroBased;

    // ─── Tag definitions ────────────────────────────────────────────────

    /// <summary>Per-tag definitions. Empty initial list; operator adds rows in the wizard.</summary>
    public List<ModbusTagWizardRow> Tags { get; set; } = new();

    /// <summary>Acceptable register class values (used to populate the wizard dropdown).</summary>
    public static readonly IReadOnlyList<string> RegisterClasses = new[]
    {
        "Coil", "DiscreteInput", "HoldingRegister", "InputRegister",
    };

    /// <summary>
    /// Acceptable datatype values (matches Core's validator allowlist).
    /// <para>
    /// The <c>string</c> entry is special: when selected, the operator
    /// also fills in <see cref="ModbusTagWizardRow.StringLength"/> and
    /// the wizard composes the two into the wire form <c>stringN</c>
    /// (e.g. <c>string16</c>) when emitting the canonical
    /// <see cref="SourceInstanceConfig"/>. Added in M.2b.6.2 v2.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> Datatypes = new[]
    {
        "bool", "int16", "uint16", "int32", "uint32",
        "float32", "int64", "uint64", "float64",
        "string",
    };

    /// <summary>
    /// The datatype implied by a register class, or <see langword="null"/> when
    /// the class isn't recognised.
    /// <para>
    /// This is not a guess. A coil and a discrete input ARE single bits on the
    /// wire — <see cref="ModbusTagValidator"/> rejects any other datatype for
    /// them — so <c>bool</c> is the only value that can be right. Holding and
    /// input registers are 16-bit words that legitimately carry several widths
    /// (a 32-bit value spans two of them), so they keep the historical
    /// <c>uint16</c> default rather than pretending to know more than they do.
    /// </para>
    /// <para>
    /// Every value returned here is present in <see cref="Datatypes"/>; a value
    /// outside that list would render the wizard's datatype cell blank.
    /// </para>
    /// </summary>
    public static string? SuggestDatatypeForRegisterClass(string? registerClass) =>
        registerClass?.Trim().ToUpperInvariant() switch
        {
            "COIL" or "DISCRETEINPUT" => "bool",
            "HOLDINGREGISTER" or "INPUTREGISTER" => "uint16",
            _ => null,
        };

    /// <summary>Acceptable byte-order values for multi-register datatypes.</summary>
    public static readonly IReadOnlyList<string> ByteOrders = new[]
    {
        "AB", "BA",                       // 2-byte
        "ABCD", "CDAB", "BADC", "DCBA",   // 4-byte
        "ABCDEFGH", "HGFEDCBA",           // 8-byte
    };

    /// <summary>
    /// Cross-validate one tag row against the same rules the Modbus
    /// adapter applies at startup. Composes with the protocol's own
    /// <see cref="ModbusTagValidator"/> rather than duplicating the
    /// datatype/byte-order width logic — when the adapter learns a
    /// new datatype, the wizard inherits it for free.
    /// <para>
    /// Returns an empty list when the row is structurally valid.
    /// Path values match the field names <see cref="ModbusTagValidator"/>
    /// emits (e.g. <c>"ByteOrder"</c>, <c>"Datatype"</c>) so the Razor
    /// table can render an inline error against the specific cell
    /// the operator must fix.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Implements M.2b.6.2 §3.A. Locked-N composition discipline:
    /// the wizard maps its <see cref="ModbusTagWizardRow"/> to a
    /// <see cref="ModbusTagDefinition"/> and delegates to the
    /// shared validator. Wizard-specific dropdown values
    /// (<see cref="RegisterClasses"/>, <see cref="ByteOrders"/>)
    /// always parse cleanly into the adapter's enums, but the
    /// defensive parse-fail branches stay in place so future
    /// wizard refactors (free-text fields, Edit-via-Wizard) don't
    /// surface unparseable input as a silent throw.
    /// </remarks>
    public static IReadOnlyList<ValidationIssue> ValidateTag(
        ModbusTagWizardRow row,
        ModbusAddressBase addressBase = ModbusAddressBase.ZeroBased)
    {
        ArgumentNullException.ThrowIfNull(row);

        var errors = new List<ValidationIssue>();

        if (!Enum.TryParse<ModbusRegisterClass>(row.RegisterClass, ignoreCase: true, out var registerClass))
        {
            errors.Add(new ValidationIssue
            {
                Code = "MODBUS.CONFIG_INVALID",
                Message = $"Register class '{row.RegisterClass}' is not recognised. Choose one of: {string.Join(", ", RegisterClasses)}.",
                Path = "RegisterClass",
            });
            return errors;
        }

        ModbusByteOrder? byteOrder;
        try
        {
            byteOrder = ModbusByteOrderExtensions.ParseOrNull(row.ByteOrder);
        }
        catch (ArgumentException ex)
        {
            errors.Add(new ValidationIssue
            {
                Code = "MODBUS.CONFIG_INVALID",
                Message = ex.Message,
                Path = "ByteOrder",
            });
            return errors;
        }

        // M.2b.6.2 v2 — string datatype is split in the wizard into the
        // base "string" choice plus a separate length field. Compose
        // them into the wire form "stringN" before handing to the
        // shared validator. v2 Locked rules 1+2 demand the length be
        // present and positive; surface a wizard-specific issue with
        // Path="StringLength" so the Razor cell can light up the
        // String length column directly.
        var datatypeForValidator = row.Datatype;
        if (string.Equals(row.Datatype, "string", StringComparison.OrdinalIgnoreCase))
        {
            if (row.StringLength is not { } len || len <= 0)
            {
                errors.Add(new ValidationIssue
                {
                    Code = "MODBUS.CONFIG_INVALID",
                    Message = "String datatype requires a positive String length (in characters).",
                    Path = "StringLength",
                });
                return errors;
            }
            datatypeForValidator = $"string{len}";
        }

        var tag = new ModbusTagDefinition
        {
            Name = row.Name,
            UnitId = (byte)row.UnitId,
            RegisterClass = registerClass,
            Address = (ushort)row.Address,
            ScanRateMs = row.ScanRateMs,
            Datatype = datatypeForValidator,
            ByteOrder = byteOrder,
            Scale = row.Scale,
            Offset = row.Offset,
            Unit = row.Unit,
        };

        ModbusTagValidator.Validate(tag, pathPrefix: string.Empty, errors, addressBase);
        return errors;
    }

    /// <summary>
    /// Project the wizard state into a canonical
    /// <see cref="SourceInstanceConfig"/>. The Modbus-specific fields
    /// land in the opaque <c>Connection</c> <see cref="JsonElement"/>
    /// so the canonical type stays protocol-agnostic — exactly the
    /// property ChatGPT's review highlighted.
    /// </summary>
    public SourceInstanceConfig BuildSourceInstance()
    {
        var connection = new JsonObject
        {
            ["host"] = Host,
            ["port"] = Port,
            ["encapsulation"] = Encapsulation,
            ["defaultUnitId"] = DefaultUnitId,
            ["connectTimeoutMs"] = ConnectTimeoutMs,
            ["requestTimeoutMs"] = RequestTimeoutMs,
            ["keepAlive"] = KeepAlive,
            ["maxTransactionRetries"] = MaxTransactionRetries,
            ["initialBackoffMs"] = InitialBackoffMs,
            ["maxBackoffMs"] = MaxBackoffMs,
            ["backoffMultiplier"] = BackoffMultiplier,
            ["circuitBreakerThreshold"] = CircuitBreakerThreshold,
            ["circuitBreakerResetMs"] = CircuitBreakerResetMs,
            ["maxGapRegisters"] = MaxGapRegisters,
            ["addressBase"] = AddressBase.ToString(),
        };

        // Serial fields are only meaningful for the SerialRtu encapsulation;
        // emitting them conditionally keeps the TCP / RTU-over-TCP connection
        // JSON (and its round-trip) byte-identical to before.
        if (string.Equals(Encapsulation, "SerialRtu", StringComparison.OrdinalIgnoreCase))
        {
            connection["serialPort"] = SerialPort;
            connection["baudRate"] = BaudRate;
            connection["parity"] = Parity;
            connection["stopBits"] = StopBits;
            connection["handshake"] = Handshake;
        }

        var tagArray = new JsonArray();
        foreach (var tag in Tags)
        {
            var tagNode = new JsonObject
            {
                ["name"] = tag.Name,
                ["unitId"] = tag.UnitId,
                ["registerClass"] = tag.RegisterClass,
                ["address"] = tag.Address,
                ["scanRateMs"] = tag.ScanRateMs,
            };
            if (!string.IsNullOrWhiteSpace(tag.Datatype))
            {
                // M.2b.6.2 v2 — collapse the wizard's split "string" +
                // StringLength representation into the wire form
                // "stringN" that ModbusTcpSourceConfiguration.FromSourceInstance
                // parses. Non-string datatypes pass through unchanged.
                var datatypeOut = (
                    string.Equals(tag.Datatype, "string", StringComparison.OrdinalIgnoreCase)
                    && tag.StringLength is { } len
                    && len > 0)
                    ? $"string{len}"
                    : tag.Datatype;
                tagNode["datatype"] = datatypeOut;
            }
            if (!string.IsNullOrWhiteSpace(tag.ByteOrder))
            {
                tagNode["byteOrder"] = tag.ByteOrder;
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
        connection["tagDefinitions"] = tagArray;

        // Convert JsonNode tree to JsonElement (the opaque shape SourceInstanceConfig holds).
        var json = connection.ToJsonString();
        using var doc = JsonDocument.Parse(json);

        return new SourceInstanceConfig
        {
            InstanceId = InstanceId,
            ProtocolName = string.IsNullOrWhiteSpace(ProtocolName) ? "modbustcp" : ProtocolName,
            DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? InstanceId : DeviceId,
            DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? InstanceId : DeviceName,
            DeviceClass = DeviceClass,
            Enabled = Enabled,
            Polling = new PollingSettings { IntervalMs = PollIntervalMs },
            Connection = doc.RootElement.Clone(),  // Clone so it outlives the using
        };
    }

    /// <summary>
    /// Inverse of <see cref="BuildSourceInstance"/> — populate a fresh wizard
    /// model from a canonical <see cref="SourceInstanceConfig"/>. Used by
    /// Edit-mode routing (M.2d.2 §5.5) to hydrate the wizard form with the
    /// source's current settings. Tag order is preserved exactly; per-tag
    /// optional fields are restored to <c>null</c> when absent from the
    /// emitted JSON (overriding the default-value initialisation that
    /// <see cref="ModbusTagWizardRow"/>'s constructor would otherwise apply).
    /// <para>
    /// Round-trip invariant: re-emitting after hydrate produces a
    /// byte-equivalent <see cref="SourceInstanceConfig"/>. Special case:
    /// the canonical <c>"stringN"</c> datatype is split back into the
    /// wizard's <c>Datatype = "string"</c> + <c>StringLength = N</c> pair
    /// so the UI presents the operator's original split form.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/>'s <c>ProtocolName</c> is not <c>"modbustcp"</c>.
    /// </exception>
    public static ModbusSourceWizardModel HydrateFromExisting(SourceInstanceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!ModbusTcpSourceConfiguration.IsModbusProtocolName(source.ProtocolName))
        {
            throw new ArgumentException(
                $"Source has protocol '{source.ProtocolName}', expected 'modbustcp' or 'modbusrtu'.",
                nameof(source));
        }

        var model = new ModbusSourceWizardModel
        {
            ProtocolName = source.ProtocolName,
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
        if (conn.TryGetProperty("port", out var port) && port.TryGetInt32(out var portValue))
        {
            model.Port = portValue;
        }
        if (conn.TryGetProperty("encapsulation", out var enc) && enc.ValueKind == JsonValueKind.String)
        {
            model.Encapsulation = enc.GetString() ?? "Tcp";
        }
        if (conn.TryGetProperty("defaultUnitId", out var defUnit) && defUnit.TryGetInt32(out var defUnitValue))
        {
            model.DefaultUnitId = defUnitValue;
        }
        if (conn.TryGetProperty("connectTimeoutMs", out var connTimeout) && connTimeout.TryGetInt32(out var connTimeoutValue))
        {
            model.ConnectTimeoutMs = connTimeoutValue;
        }
        if (conn.TryGetProperty("requestTimeoutMs", out var reqTimeout) && reqTimeout.TryGetInt32(out var reqTimeoutValue))
        {
            model.RequestTimeoutMs = reqTimeoutValue;
        }
        if (conn.TryGetProperty("keepAlive", out var keepAlive) &&
            (keepAlive.ValueKind == JsonValueKind.True || keepAlive.ValueKind == JsonValueKind.False))
        {
            model.KeepAlive = keepAlive.GetBoolean();
        }
        if (conn.TryGetProperty("serialPort", out var serialPort) && serialPort.ValueKind == JsonValueKind.String)
        {
            model.SerialPort = serialPort.GetString() ?? string.Empty;
        }
        if (conn.TryGetProperty("baudRate", out var baudRate) && baudRate.TryGetInt32(out var baudRateValue))
        {
            model.BaudRate = baudRateValue;
        }
        if (conn.TryGetProperty("parity", out var parity) && parity.ValueKind == JsonValueKind.String)
        {
            model.Parity = parity.GetString() ?? "None";
        }
        if (conn.TryGetProperty("stopBits", out var stopBits) && stopBits.ValueKind == JsonValueKind.String)
        {
            model.StopBits = stopBits.GetString() ?? "One";
        }
        if (conn.TryGetProperty("handshake", out var handshake) && handshake.ValueKind == JsonValueKind.String)
        {
            model.Handshake = handshake.GetString() ?? "None";
        }
        if (conn.TryGetProperty("maxTransactionRetries", out var maxRetries) && maxRetries.TryGetInt32(out var maxRetriesValue))
        {
            model.MaxTransactionRetries = maxRetriesValue;
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
        if (conn.TryGetProperty("circuitBreakerThreshold", out var cbThreshold) && cbThreshold.TryGetInt32(out var cbThresholdValue))
        {
            model.CircuitBreakerThreshold = cbThresholdValue;
        }
        if (conn.TryGetProperty("circuitBreakerResetMs", out var cbReset) && cbReset.TryGetInt32(out var cbResetValue))
        {
            model.CircuitBreakerResetMs = cbResetValue;
        }
        if (conn.TryGetProperty("addressBase", out var addrBase) && addrBase.ValueKind == JsonValueKind.String)
        {
            model.AddressBase = ModbusAddressBaseExtensions.Parse(
                addrBase.GetString(), ModbusAddressBase.ZeroBased);
        }
        if (conn.TryGetProperty("maxGapRegisters", out var maxGap) && maxGap.TryGetInt32(out var maxGapValue))
        {
            model.MaxGapRegisters = maxGapValue;
        }

        if (conn.TryGetProperty("tagDefinitions", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tagEl in tagsEl.EnumerateArray())
            {
                if (tagEl.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var row = new ModbusTagWizardRow
                {
                    // BuildSourceInstance omits these only when null/whitespace,
                    // so reset the constructor's defaults here — the per-property
                    // probes below restore exactly the fields the emit included.
                    Datatype = null,
                };

                if (tagEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    row.Name = nameEl.GetString() ?? string.Empty;
                }
                if (tagEl.TryGetProperty("unitId", out var uidEl) && uidEl.TryGetInt32(out var uidValue))
                {
                    row.UnitId = uidValue;
                }
                if (tagEl.TryGetProperty("registerClass", out var rcEl) && rcEl.ValueKind == JsonValueKind.String)
                {
                    row.RegisterClass = rcEl.GetString() ?? "HoldingRegister";
                }
                if (tagEl.TryGetProperty("address", out var addrEl) && addrEl.TryGetInt32(out var addrValue))
                {
                    row.Address = addrValue;
                }
                if (tagEl.TryGetProperty("scanRateMs", out var scanEl) && scanEl.TryGetInt32(out var scanValue))
                {
                    row.ScanRateMs = scanValue;
                }

                // M.2b.6.2 v2 — the wire form "stringN" splits back into the
                // wizard's split representation: Datatype="string" + StringLength=N.
                // Anything else (including a bare "string" without digits) is
                // restored verbatim so the validator can surface it.
                if (tagEl.TryGetProperty("datatype", out var dtEl) && dtEl.ValueKind == JsonValueKind.String)
                {
                    var dtStr = dtEl.GetString() ?? string.Empty;
                    const string StringPrefix = "string";
                    if (dtStr.StartsWith(StringPrefix, StringComparison.OrdinalIgnoreCase) &&
                        dtStr.Length > StringPrefix.Length &&
                        int.TryParse(dtStr.AsSpan(StringPrefix.Length), out var len))
                    {
                        row.Datatype = "string";
                        row.StringLength = len;
                    }
                    else
                    {
                        row.Datatype = dtStr;
                    }
                }
                if (tagEl.TryGetProperty("byteOrder", out var boEl) && boEl.ValueKind == JsonValueKind.String)
                {
                    row.ByteOrder = boEl.GetString();
                }
                if (tagEl.TryGetProperty("scale", out var scaleEl) && scaleEl.TryGetDouble(out var scaleValue))
                {
                    row.Scale = scaleValue;
                }
                if (tagEl.TryGetProperty("offset", out var offsetEl) && offsetEl.TryGetDouble(out var offsetValue))
                {
                    row.Offset = offsetValue;
                }
                if (tagEl.TryGetProperty("unit", out var unitEl) && unitEl.ValueKind == JsonValueKind.String)
                {
                    row.Unit = unitEl.GetString();
                }

                // A tag that already exists was applied with this datatype —
                // whatever it is, including absent. Pin it so an Edit-mode
                // register-class change re-suggests nothing and the operator
                // leaves with the datatype they arrived with.
                row.MarkDatatypeOperatorChosen();

                model.Tags.Add(row);
            }
        }

        return model;
    }
}

/// <summary>
/// One tag row in the wizard's tag-definitions table. Maps 1:1 to
/// <c>ModbusTagDefinition</c> on the Core side, but uses strings for
/// enum-like fields so MudSelect binds cleanly.
/// </summary>
public sealed class ModbusTagWizardRow
{
    /// <summary>Canonical tag name emitted to the pipeline.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Slave unit id behind the TCP gateway (0..247).</summary>
    public int UnitId { get; set; } = 1;

    /// <summary>Register class — Coil / DiscreteInput / HoldingRegister / InputRegister.</summary>
    public string RegisterClass { get; set; } = "HoldingRegister";

    /// <summary>Zero-based register/coil address.</summary>
    public int Address { get; set; }

    /// <summary>
    /// String view of <see cref="Address"/> for two-way <c>@bind-Value</c> to a
    /// <c>MudTextField</c>. The wizard binds the Address cell to this (like the
    /// Name cell) instead of a <c>MudNumericField</c>, which failed to propagate
    /// typed values into the model under Blazor Server. Empty or non-numeric
    /// input leaves the address unchanged; valid input is clamped to 0..65535.
    /// </summary>
    public string AddressText
    {
        get => Address.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse((value ?? string.Empty).Trim(), out var parsed))
            {
                Address = System.Math.Clamp(parsed, 0, 65535);
            }
        }
    }

    /// <summary>Scan period in milliseconds.</summary>
    public int ScanRateMs { get; set; } = 1000;

    private string? _datatype = "uint16";

    /// <summary>
    /// Datatype hint (bool / int16 / uint16 / int32 / uint32 / float32 / int64 /
    /// uint64 / float64 / string).
    /// <para>
    /// Assigning through this property records the value as the OPERATOR's, so
    /// <see cref="ApplyRegisterClassDatatypeSuggestion"/> will never overwrite
    /// it. The wizard's own suggestions go through that method instead, which
    /// writes the backing field and leaves the row still marked as suggested.
    /// </para>
    /// </summary>
    public string? Datatype
    {
        get => _datatype;
        set
        {
            _datatype = value;
            DatatypeIsOperatorChosen = true;
        }
    }

    /// <summary>
    /// <see langword="true"/> once the datatype has been set by anything other
    /// than a wizard suggestion — the operator picking from the dropdown, or
    /// <see cref="ModbusSourceWizardModel.HydrateFromExisting"/> restoring a
    /// saved tag. A freshly added row starts <see langword="false"/>, carrying
    /// the seeded <c>uint16</c> as a suggestion the register class may revise.
    /// </summary>
    public bool DatatypeIsOperatorChosen { get; private set; }

    /// <summary>
    /// Re-suggest the datatype implied by the current
    /// <see cref="RegisterClass"/>. Called by the wizard when the operator
    /// changes the register class of a row.
    /// <para>
    /// SUGGEST, NEVER COERCE (the convention the S7 and EtherNet/IP wizards
    /// follow): a datatype the operator chose — or one hydrated from a saved
    /// configuration — is left exactly as it is. Only a value the wizard itself
    /// put there is revised.
    /// </para>
    /// <para>
    /// When the suggestion is <c>bool</c> the byte order is cleared with it: a
    /// coil or discrete input reads one bit, for which byte order is not
    /// applicable and the shared validator rejects it outright.
    /// </para>
    /// </summary>
    public void ApplyRegisterClassDatatypeSuggestion()
    {
        if (DatatypeIsOperatorChosen)
        {
            return;
        }

        if (ModbusSourceWizardModel.SuggestDatatypeForRegisterClass(RegisterClass) is not { } suggestion)
        {
            return;
        }

        _datatype = suggestion;

        if (string.Equals(suggestion, "bool", StringComparison.Ordinal))
        {
            ByteOrder = null;
            StringLength = null;
        }
    }

    /// <summary>
    /// Pin the current <see cref="Datatype"/> as the operator's own, so no
    /// later register-class change revises it. Used by
    /// <see cref="ModbusSourceWizardModel.HydrateFromExisting"/> to protect the
    /// value a saved tag was applied with — including an absent one.
    /// </summary>
    public void MarkDatatypeOperatorChosen() => DatatypeIsOperatorChosen = true;

    /// <summary>
    /// Byte order for multi-register datatypes (AB / ABCD / CDAB / etc.).
    /// Auto-cleared and disabled by the wizard UI when <see cref="Datatype"/>
    /// is <c>"string"</c> (the shared validator rejects byteOrder on
    /// strings; v2 keeps the operator out of that invalid state up front).
    /// </summary>
    public string? ByteOrder { get; set; }

    /// <summary>
    /// Character length for the <c>string</c> datatype (M.2b.6.2 v2).
    /// Required and must be positive when
    /// <see cref="Datatype"/> is <c>"string"</c>; ignored otherwise.
    /// The wizard composes <c>"string"</c> + this length into the
    /// canonical wire form <c>"stringN"</c> when emitting.
    /// </summary>
    public int? StringLength { get; set; }

    /// <summary>Optional linear scale factor: <c>scaled = raw * Scale + Offset</c>.</summary>
    public double? Scale { get; set; }

    /// <summary>Optional additive offset; see <see cref="Scale"/>.</summary>
    public double? Offset { get; set; }

    /// <summary>Optional engineering unit string copied to the canonical data point.</summary>
    public string? Unit { get; set; }
}
