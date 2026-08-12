// ============================================================================
// File: ModbusErrors.cs
// Purpose: Modbus-specific error code constants following the locked
//          convention {PROTOCOL}.{CATEGORY_SUBCATEGORY}.
// Reference: ARCHITECTURE_BLUEPRINT.md §13.3; Errors/AdapterError.cs
// ============================================================================

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Stable error codes for the Modbus TCP source adapter. All codes follow the
/// locked convention <c>MODBUS.{CATEGORY_SUBCATEGORY}</c>.
/// </summary>
internal static class ModbusErrors
{
    // ---- Network ----

    /// <summary>TCP connect failed after all retries.</summary>
    internal const string ConnectFailed = "MODBUS.CONNECT_FAILED";

    /// <summary>Request exceeded the configured response timeout.</summary>
    internal const string Timeout = "MODBUS.TIMEOUT";

    /// <summary>Socket-level I/O error during a transaction.</summary>
    internal const string SocketError = "MODBUS.SOCKET_ERROR";

    // ---- ResourceExhausted ----

    /// <summary>Skipping connect — exponential backoff window is still active.</summary>
    internal const string ConnectBackoff = "MODBUS.CONNECT_BACKOFF";

    /// <summary>
    /// Circuit breaker is OPEN — too many consecutive failures. Connect
    /// attempts are suspended until the breaker's reset window elapses.
    /// </summary>
    internal const string CircuitBreakerOpen = "MODBUS.CIRCUIT_BREAKER_OPEN";

    // ---- Configuration ----

    /// <summary>Configuration is not a <c>ModbusTcpSourceConfiguration</c>.</summary>
    internal const string ConfigWrongType = "MODBUS.CONFIG_WRONG_TYPE";

    /// <summary>Bad configuration (missing host, bad port, invalid unit id, etc.).</summary>
    internal const string ConfigInvalid = "MODBUS.CONFIG_INVALID";

    /// <summary>A required configuration field is missing.</summary>
    internal const string ConfigMissingField = "MODBUS.CONFIG_MISSING_FIELD";

    /// <summary>A numeric field is outside its allowed range.</summary>
    internal const string ConfigOutOfRange = "MODBUS.CONFIG_OUT_OF_RANGE";

    // ---- Protocol ----

    /// <summary>
    /// A Modbus exception response was returned by the slave
    /// (e.g. Illegal Function 0x01, Illegal Data Address 0x02).
    /// </summary>
    internal const string SlaveException = "MODBUS.SLAVE_EXCEPTION";

    /// <summary>Unexpected protocol framing — CRC/MBAP mismatch or bad PDU length.</summary>
    internal const string BadFraming = "MODBUS.BAD_FRAMING";

    /// <summary>
    /// Requested read size exceeds the function-code limit
    /// (FC01/02 max 2000 bits, FC03/04 max 125 registers).
    /// </summary>
    internal const string RequestTooLarge = "MODBUS.REQUEST_TOO_LARGE";

    /// <summary>Generic transaction failure (non-fatal).</summary>
    internal const string TransactionFailed = "MODBUS.TRANSACTION_FAILED";

    // ---- DeviceState ----

    /// <summary>
    /// Slave returned GatewayPathUnavailable / GatewayTargetNoResponse
    /// (0x0A / 0x0B) — RTU gateway cannot reach the downstream device.
    /// </summary>
    internal const string GatewayUnreachable = "MODBUS.GATEWAY_UNREACHABLE";

    /// <summary>Slave returned SlaveDeviceBusy (0x06).</summary>
    internal const string SlaveBusy = "MODBUS.SLAVE_BUSY";

    // ---- CSV Import (F4) ----

    /// <summary>CSV file had no header row.</summary>
    internal const string ImportNoHeader = "MODBUS.IMPORT_NO_HEADER";

    /// <summary>A required column is missing from the CSV header.</summary>
    internal const string ImportMissingColumn = "MODBUS.IMPORT_MISSING_COLUMN";

    /// <summary>The CSV header contains a column name the importer does not recognize.</summary>
    internal const string ImportUnknownColumn = "MODBUS.IMPORT_UNKNOWN_COLUMN";

    /// <summary>A row has fewer fields than the header declared.</summary>
    internal const string ImportShortRow = "MODBUS.IMPORT_SHORT_ROW";

    /// <summary>A row field could not be parsed (bad number, unknown enum value, etc.).</summary>
    internal const string ImportBadField = "MODBUS.IMPORT_BAD_FIELD";

    /// <summary>
    /// The <c>address</c> value looks like a legacy vendor offset
    /// (30001 / 40001 / …) rather than a zero-based logical address.
    /// </summary>
    internal const string ImportLegacyAddress = "MODBUS.IMPORT_LEGACY_ADDRESS";

    /// <summary>The CSV contains two rows with the same tag name.</summary>
    internal const string ImportDuplicateName = "MODBUS.IMPORT_DUPLICATE_NAME";

    /// <summary>CSV opened but produced no tag rows (blank / comments only).</summary>
    internal const string ImportEmpty = "MODBUS.IMPORT_EMPTY";

    // ---- CSV Import warnings (non-fatal) ----

    /// <summary>
    /// Two or more tags share a register range. Allowed (e.g. raw + bitfield
    /// overlay), but surfaced as a warning so users who didn't intend the
    /// overlap can fix it.
    /// </summary>
    internal const string ImportOverlappingRange = "MODBUS.IMPORT_OVERLAPPING_RANGE";
}
