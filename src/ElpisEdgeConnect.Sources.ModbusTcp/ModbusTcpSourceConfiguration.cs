// ============================================================================
// File: ModbusTcpSourceConfiguration.cs
// Purpose: Configuration record for a Modbus TCP source adapter instance.
//          Derives from SourceConfiguration. Includes FromSourceInstance(…)
//          factory that parses the opaque SourceInstanceConfig.Connection
//          JSON into a typed config — same bridge pattern used by FOCAS2.
// Reference: ARCHITECTURE_BLUEPRINT.md §8; PHASE3_EXECUTION_PLAN.md §5
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Configuration for a single Modbus TCP source connection.
/// </summary>
public sealed record ModbusTcpSourceConfiguration : SourceConfiguration
{
    /// <summary>Protocol id for native Modbus TCP sources (encapsulation = Tcp).</summary>
    public const string ProtocolNameConstant = "modbustcp";

    /// <summary>
    /// Protocol id for Modbus RTU sources (encapsulation = SerialRtu or
    /// RtuOverTcp). Shares this adapter/config with <see cref="ProtocolNameConstant"/>
    /// but is a distinct operator-facing protocol (ADR-0033).
    /// </summary>
    public const string RtuProtocolNameConstant = "modbusrtu";

    /// <summary>True if <paramref name="protocolName"/> is either Modbus protocol id.</summary>
    public static bool IsModbusProtocolName(string? protocolName) =>
        string.Equals(protocolName, ProtocolNameConstant, StringComparison.OrdinalIgnoreCase)
        || string.Equals(protocolName, RtuProtocolNameConstant, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// License module key that gates registration of this protocol's
    /// source adapters. Mirrors <c>LicenseModuleKeys.SourceModbusTcp</c>;
    /// duplicated as a local <c>const</c> so per-protocol projects don't
    /// need a Core reference solely to expose the key string. Stable
    /// identifier — see <c>docs/licensing/module-catalog.md</c>.
    /// </summary>
    public const string LicenseModuleKey = "source-modbus-tcp";

    // ---- Connection ----

    /// <summary>
    /// Slave / gateway IP address or hostname (e.g., "192.168.1.50"). Required
    /// for <see cref="ModbusEncapsulation.Tcp"/> and
    /// <see cref="ModbusEncapsulation.RtuOverTcp"/>; unused (empty) for
    /// <see cref="ModbusEncapsulation.SerialRtu"/>.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>TCP port. Default 502 (IANA-assigned for Modbus TCP). Unused for serial.</summary>
    public ushort Port { get; init; } = 502;

    /// <summary>Wire encapsulation / transport — native Modbus TCP, RTU-over-TCP, or serial RTU.</summary>
    public ModbusEncapsulation Encapsulation { get; init; } = ModbusEncapsulation.Tcp;

    // ---- Serial (SerialRtu encapsulation only) ----

    /// <summary>
    /// Serial port name for <see cref="ModbusEncapsulation.SerialRtu"/>
    /// (e.g. <c>COM3</c> on Windows, <c>/dev/ttyUSB0</c> on Linux). Required
    /// when the encapsulation is serial; ignored otherwise.
    /// </summary>
    public string? SerialPort { get; init; }

    /// <summary>Serial baud rate (default 9600). Modbus RTU is always 8 data bits.</summary>
    public int BaudRate { get; init; } = 9600;

    /// <summary>Serial parity. Default <see cref="System.IO.Ports.Parity.None"/>.</summary>
    public Parity SerialParity { get; init; } = Parity.None;

    /// <summary>Serial stop bits. Default <see cref="System.IO.Ports.StopBits.One"/>.</summary>
    public StopBits SerialStopBits { get; init; } = StopBits.One;

    /// <summary>Serial flow control. Default <see cref="System.IO.Ports.Handshake.None"/>.</summary>
    public Handshake SerialHandshake { get; init; } = Handshake.None;

    /// <summary>
    /// Default slave unit id used when a tag does not carry an explicit
    /// unitId. Direct-connect slaves typically ignore this (some libraries
    /// send 0xFF or 0x00); RTU gateways route by it. Range 0..247.
    /// </summary>
    public byte DefaultUnitId { get; init; } = 1;

    /// <summary>TCP handshake timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; init; } = 2000;

    /// <summary>Per-request read/write timeout in milliseconds.</summary>
    public int RequestTimeoutMs { get; init; } = 1000;

    /// <summary>
    /// Keep the TCP socket open across polls (typical and recommended). When
    /// <see langword="false"/>, the adapter reconnects for every poll cycle
    /// — useful for intermittent slaves that do not tolerate long sessions.
    /// </summary>
    public bool KeepAlive { get; init; } = true;

    // ---- Retry / Backoff ----

    /// <summary>Max per-transaction retries before surfacing a failure.</summary>
    public int MaxTransactionRetries { get; init; } = 2;

    /// <summary>Initial backoff in ms after a connect failure.</summary>
    public int InitialBackoffMs { get; init; } = 2000;

    /// <summary>Maximum backoff in ms — caps exponential growth.</summary>
    public int MaxBackoffMs { get; init; } = 60_000;

    /// <summary>Exponential multiplier between consecutive connect failures.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Number of consecutive connect failures that flip the circuit breaker
    /// to OPEN. The breaker blocks new connect attempts for
    /// <see cref="CircuitBreakerResetMs"/> milliseconds before allowing a
    /// single probe (HALF-OPEN).
    /// </summary>
    public int CircuitBreakerThreshold { get; init; } = 5;

    /// <summary>
    /// Breaker cool-down window in ms before the next probe. Kept short (10s)
    /// so a device that recovers reconnects quickly — this is the dominant
    /// delay an operator sees after a source comes back online. Still long
    /// enough not to hammer a genuinely down device.
    /// </summary>
    public int CircuitBreakerResetMs { get; init; } = 10_000;

    // ---- Scan-group planner (F2) ----

    /// <summary>
    /// Maximum gap (in registers for FC03/FC04, in bits for FC01/FC02) that the
    /// scan-group planner will tolerate when coalescing adjacent tags into a
    /// single block. Default 8: reading one 20-register block is cheaper than
    /// two 8+9-register blocks on almost any network, so small gaps are worth
    /// bridging. Set to 0 to disable coalescing and read each tag with its
    /// own transaction (useful when the slave charges per-register).
    /// </summary>
    public int MaxGapRegisters { get; init; } = 8;

    // ---- Addressing ----

    /// <summary>
    /// Notation the operator used for <see cref="ModbusTagDefinition.Address"/>
    /// values in this source's config. Addresses are converted to zero-based
    /// logical addresses ONCE, in <see cref="FromSourceInstance"/>, so every
    /// consumer downstream (planner, executor, wire) sees zero-based addresses
    /// regardless of what the operator typed.
    /// <para>
    /// Defaults to <see cref="ModbusAddressBase.ZeroBased"/>, which is a no-op —
    /// every configuration written before this option existed behaves exactly
    /// as before.
    /// </para>
    /// </summary>
    public ModbusAddressBase AddressBase { get; init; } = ModbusAddressBase.ZeroBased;

    // ---- Tag list (F3/F4 populate this via CSV import / template profiles) ----

    /// <summary>
    /// Per-tag definitions. Empty in F1; F4 populates this from CSV imports
    /// or template profiles. The transaction executor is fed from here by
    /// F2's scan-group planner, so getting the shape right early matters
    /// even though F1 does not consume any entries.
    /// </summary>
    public IReadOnlyList<ModbusTagDefinition> TagDefinitions { get; init; } = [];

    /// <summary>
    /// Translate a loaded <see cref="SourceInstanceConfig"/> DTO into a
    /// typed <see cref="ModbusTcpSourceConfiguration"/>. Reads Modbus-specific
    /// fields from the opaque <c>Connection</c> JSON block.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="instance"/>'s protocol is not
    /// <c>"modbustcp"</c>, when <see cref="SourceInstanceConfig.Connection"/>
    /// is missing, or when <c>host</c> is absent.
    /// </exception>
    public static ModbusTcpSourceConfiguration FromSourceInstance(SourceInstanceConfig instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!IsModbusProtocolName(instance.ProtocolName))
        {
            throw new ArgumentException(
                $"Expected protocolName '{ProtocolNameConstant}' or '{RtuProtocolNameConstant}', got '{instance.ProtocolName}'.",
                nameof(instance));
        }

        // RTU sources default to serial; TCP sources default to native TCP. The
        // explicit encapsulation key (if present) still wins via ReadEncapsulation.
        var isRtuProtocol = string.Equals(
            instance.ProtocolName, RtuProtocolNameConstant, StringComparison.OrdinalIgnoreCase);
        var defaultEncapsulation = isRtuProtocol
            ? ModbusEncapsulation.SerialRtu
            : ModbusEncapsulation.Tcp;
        if (instance.Connection is null || instance.Connection.Value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"SourceInstanceConfig '{instance.InstanceId}' is missing the " +
                "required Modbus Connection object (must contain at least 'host').",
                nameof(instance));
        }

        var conn = instance.Connection.Value;
        // Keys named via ModbusTcpConnectionKeys so they cannot be parsed
        // without a redaction tier (ADR-0020 M-B Q-B2); the drift guard asserts
        // ModbusTcpConnectionKeys.All == KnownKeys.
        var encapsulation = ReadEncapsulation(conn, ModbusTcpConnectionKeys.Encapsulation, defaultEncapsulation);

        // Addressing notation the operator used. Tag addresses are normalised to
        // zero-based logical addresses while reading tagDefinitions below, so
        // nothing downstream needs to know this was ever anything else.
        var addressBase = ModbusAddressBaseExtensions.Parse(
            ReadString(conn, ModbusTcpConnectionKeys.AddressBase), ModbusAddressBase.ZeroBased);

        // Host is required for the TCP-based encapsulations; serial RTU uses a
        // serial port instead, so host is optional (and empty) there.
        var host = ReadString(conn, ModbusTcpConnectionKeys.Host);
        if (host is null && encapsulation != ModbusEncapsulation.SerialRtu)
        {
            throw new ArgumentException(
                $"Modbus Connection for source '{instance.InstanceId}' is missing the required 'host' field.",
                nameof(instance));
        }

        return new ModbusTcpSourceConfiguration
        {
            // Base SourceConfiguration fields
            InstanceId = instance.InstanceId,
            ProtocolName = instance.ProtocolName,
            DisplayName = instance.DeviceName,
            Enabled = instance.Enabled,
            PollIntervalMs = instance.Polling.IntervalMs,
            DeviceId = instance.DeviceId,
            DeviceName = instance.DeviceName,
            DeviceClass = instance.DeviceClass,
            Tags = instance.Tags,

            // Modbus-specific fields
            Host = host ?? string.Empty,
            Port = ReadUShort(conn, ModbusTcpConnectionKeys.Port, defaultValue: 502),
            Encapsulation = encapsulation,
            // Serial (SerialRtu only; harmless defaults otherwise)
            SerialPort = ReadString(conn, ModbusTcpConnectionKeys.SerialPort),
            BaudRate = ReadInt(conn, ModbusTcpConnectionKeys.BaudRate, defaultValue: 9600),
            SerialParity = ParseParity(ReadString(conn, ModbusTcpConnectionKeys.Parity)),
            SerialStopBits = ParseStopBits(ReadString(conn, ModbusTcpConnectionKeys.StopBits)),
            SerialHandshake = ParseHandshake(ReadString(conn, ModbusTcpConnectionKeys.Handshake)),
            DefaultUnitId = ReadByte(conn, ModbusTcpConnectionKeys.DefaultUnitId, defaultValue: 1),
            ConnectTimeoutMs = ReadInt(conn, ModbusTcpConnectionKeys.ConnectTimeoutMs, defaultValue: 2000),
            RequestTimeoutMs = ReadInt(conn, ModbusTcpConnectionKeys.RequestTimeoutMs, defaultValue: 1000),
            KeepAlive = ReadBool(conn, ModbusTcpConnectionKeys.KeepAlive, defaultValue: true),
            MaxTransactionRetries = ReadInt(conn, ModbusTcpConnectionKeys.MaxTransactionRetries, defaultValue: 2),
            InitialBackoffMs = ReadInt(conn, ModbusTcpConnectionKeys.InitialBackoffMs, defaultValue: 2000),
            MaxBackoffMs = ReadInt(conn, ModbusTcpConnectionKeys.MaxBackoffMs, defaultValue: 60_000),
            BackoffMultiplier = ReadDouble(conn, ModbusTcpConnectionKeys.BackoffMultiplier, defaultValue: 2.0),
            CircuitBreakerThreshold = ReadInt(conn, ModbusTcpConnectionKeys.CircuitBreakerThreshold, defaultValue: 5),
            CircuitBreakerResetMs = ReadInt(conn, ModbusTcpConnectionKeys.CircuitBreakerResetMs, defaultValue: 10_000),
            MaxGapRegisters = ReadInt(conn, ModbusTcpConnectionKeys.MaxGapRegisters, defaultValue: 8),
            AddressBase = addressBase,
            TagDefinitions = ReadTagDefinitions(conn, ModbusTcpConnectionKeys.TagDefinitions, instance.InstanceId, addressBase),
        };
    }

    /// <summary>
    /// Parse the optional <c>tagDefinitions</c> array from the connection
    /// JSON. Matches the shape the F4 CLI (<c>ModbusCsvImport</c>) emits
    /// so a copy-paste-edit round-trip just works.
    /// </summary>
    private static List<ModbusTagDefinition> ReadTagDefinitions(
        JsonElement conn, string name, string instanceId, ModbusAddressBase addressBase)
    {
        if (!conn.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tags = new List<ModbusTagDefinition>(v.GetArrayLength());
        var i = 0;
        foreach (var element in v.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    $"Modbus source '{instanceId}' tagDefinitions[{i}] must be a JSON object.",
                    nameof(conn));
            }

            var tagName = ReadString(element, ModbusTcpConnectionKeys.TagName)
                ?? throw new ArgumentException(
                    $"Modbus source '{instanceId}' tagDefinitions[{i}] is missing required field 'name'.");
            var rcRaw = ReadString(element, ModbusTcpConnectionKeys.RegisterClass)
                ?? throw new ArgumentException(
                    $"Modbus source '{instanceId}' tagDefinitions[{i}] ('{tagName}') is missing required field 'registerClass'.");
            if (!Enum.TryParse<ModbusRegisterClass>(rcRaw, ignoreCase: true, out var rc))
            {
                throw new ArgumentException(
                    $"Modbus source '{instanceId}' tagDefinitions[{i}] ('{tagName}') has invalid registerClass '{rcRaw}'. " +
                    "Accepted: Coil, DiscreteInput, HoldingRegister, InputRegister.");
            }

            // Normalise the operator-entered address to a zero-based logical
            // address. For the default ZeroBased notation this is a no-op; for
            // OneBased / Modicon the class prefix is subtracted here so the
            // planner, executor and wire only ever see zero-based addresses.
            var enteredAddress = ReadInt(element, ModbusTcpConnectionKeys.Address, defaultValue: 0);
            if (!addressBase.TryToZeroBased(enteredAddress, rc, out var wireAddress))
            {
                throw new ArgumentException(
                    $"Modbus source '{instanceId}' tagDefinitions[{i}] ('{tagName}') has address {enteredAddress}, " +
                    $"which is not valid for addressBase '{addressBase}' and register class '{rc}'. " +
                    (addressBase == ModbusAddressBase.Modicon
                        ? $"Modicon {rc} addresses start at {ModbusAddressBaseExtensions.ModiconOffset(rc)}."
                        : "The resulting zero-based address must be between 0 and 65535."),
                    nameof(conn));
            }

            tags.Add(new ModbusTagDefinition
            {
                Name = tagName,
                UnitId = ReadByte(element, ModbusTcpConnectionKeys.TagUnitId, defaultValue: 1),
                RegisterClass = rc,
                Address = (ushort)wireAddress,
                ScanRateMs = ReadInt(element, ModbusTcpConnectionKeys.ScanRateMs, defaultValue: 1000),
                Datatype = ReadString(element, ModbusTcpConnectionKeys.Datatype),
                ByteOrder = ModbusByteOrderExtensions.ParseOrNull(ReadString(element, ModbusTcpConnectionKeys.ByteOrder)),
                Scale = ReadOptionalDouble(element, ModbusTcpConnectionKeys.Scale),
                Offset = ReadOptionalDouble(element, ModbusTcpConnectionKeys.Offset),
                Unit = ReadString(element, ModbusTcpConnectionKeys.Unit),
            });

            i++;
        }
        return tags;
    }

    private static double? ReadOptionalDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

    // ---- JSON helpers (mirror FOCAS2's style for consistency) ----

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int ReadInt(JsonElement obj, string name, int defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : defaultValue;

    private static ushort ReadUShort(JsonElement obj, string name, ushort defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? (ushort)v.GetInt32()
            : defaultValue;

    private static byte ReadByte(JsonElement obj, string name, byte defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? (byte)v.GetInt32()
            : defaultValue;

    private static bool ReadBool(JsonElement obj, string name, bool defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : defaultValue;

    private static double ReadDouble(JsonElement obj, string name, double defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : defaultValue;

    private static ModbusEncapsulation ReadEncapsulation(JsonElement obj, string name, ModbusEncapsulation defaultValue)
    {
        var s = ReadString(obj, name)?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return defaultValue;
        }
        if (s.Equals("rtuOverTcp", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("rtu-over-tcp", StringComparison.OrdinalIgnoreCase))
        {
            return ModbusEncapsulation.RtuOverTcp;
        }
        if (s.Equals("serialRtu", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("serial-rtu", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("serial", StringComparison.OrdinalIgnoreCase))
        {
            return ModbusEncapsulation.SerialRtu;
        }
        return ModbusEncapsulation.Tcp;
    }

    private static Parity ParseParity(string? s) => (s?.Trim().ToLowerInvariant()) switch
    {
        "odd" => Parity.Odd,
        "even" => Parity.Even,
        "mark" => Parity.Mark,
        "space" => Parity.Space,
        _ => Parity.None,
    };

    private static StopBits ParseStopBits(string? s) => (s?.Trim().ToLowerInvariant()) switch
    {
        "two" or "2" => StopBits.Two,
        "onepointfive" or "1.5" => StopBits.OnePointFive,
        _ => StopBits.One,
    };

    private static Handshake ParseHandshake(string? s) => (s?.Trim().ToLowerInvariant()) switch
    {
        "xonxoff" => Handshake.XOnXOff,
        "requesttosend" or "rts" => Handshake.RequestToSend,
        "requesttosendxonxoff" => Handshake.RequestToSendXOnXOff,
        _ => Handshake.None,
    };
}

/// <summary>
/// Configuration for a single Modbus tag. F2's planner groups these by
/// <c>(ScanRateMs, UnitId, RegisterClass)</c>; F3's decoder interprets raw
/// registers according to <c>Datatype</c> and byte-order. F1 leaves the
/// list empty and does not consume it yet.
/// </summary>
public sealed record ModbusTagDefinition
{
    /// <summary>Canonical tag name emitted on the pipeline (e.g. <c>"spindle_load"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Slave unit id behind the TCP gateway. Range 0..247.</summary>
    public byte UnitId { get; init; } = 1;

    /// <summary>Register class (coil / discrete input / input register / holding register).</summary>
    public required ModbusRegisterClass RegisterClass { get; init; }

    /// <summary>Zero-based register/coil address.</summary>
    public required ushort Address { get; init; }

    /// <summary>Scan period in milliseconds (e.g. 100, 1000, 10000).</summary>
    public int ScanRateMs { get; init; } = 1000;

    /// <summary>
    /// Datatype hint consumed by F3's decoder. Accepted values:
    /// <c>bool</c>, <c>int16</c>, <c>uint16</c>, <c>int32</c>, <c>uint32</c>,
    /// <c>float32</c>, <c>int64</c>, <c>uint64</c>, <c>float64</c>,
    /// <c>stringN</c> (N chars, e.g. <c>string16</c>). Case-insensitive;
    /// <see cref="ModbusTcpSourceAdapter.ValidateConfigAsync"/> rejects
    /// unknown values rather than letting them surface at poll time.
    /// </summary>
    public string? Datatype { get; init; }

    /// <summary>
    /// Byte-order for multi-register values. Defaults to <see langword="null"/>,
    /// in which case the decoder picks big-endian at the resolved width
    /// (AB / ABCD / ABCDEFGH). Must match the datatype's byte count —
    /// ValidateConfigAsync rejects mismatches (e.g. <c>float32</c> with
    /// <c>AB</c>).
    /// </summary>
    public ModbusByteOrder? ByteOrder { get; init; }

    /// <summary>
    /// Optional linear scale factor applied to the decoded numeric value:
    /// <c>scaled = (raw * Scale) + Offset</c>. Rejected for <c>bool</c>
    /// and <c>stringN</c> datatypes — see the adapter's validator.
    /// </summary>
    public double? Scale { get; init; }

    /// <summary>Optional additive offset. See <see cref="Scale"/>.</summary>
    public double? Offset { get; init; }

    /// <summary>Optional engineering unit string copied to <c>CanonicalDataPoint.Unit</c>.</summary>
    public string? Unit { get; init; }
}
