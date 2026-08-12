// ============================================================================
// File: EthernetIpErrors.cs
// Purpose: EtherNet/IP-specific error code constants following the locked
//          convention {PROTOCOL}.{CATEGORY_SUBCATEGORY}.
// Reference: ARCHITECTURE_BLUEPRINT.md §13.3; Errors/AdapterError.cs
// ============================================================================

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// Stable error codes for the EtherNet/IP source adapter. All codes follow the
/// locked convention <c>ETHERNETIP.{CATEGORY_SUBCATEGORY}</c>.
/// </summary>
internal static class EthernetIpErrors
{
    // ---- Network ----

    /// <summary>CIP session could not be established to the gateway.</summary>
    internal const string ConnectFailed = "ETHERNETIP.CONNECT_FAILED";

    /// <summary>A tag read exceeded the configured timeout.</summary>
    internal const string Timeout = "ETHERNETIP.TIMEOUT";

    /// <summary>Socket-level I/O error during a CIP operation.</summary>
    internal const string SocketError = "ETHERNETIP.SOCKET_ERROR";

    // ---- ResourceExhausted ----

    /// <summary>Skipping connect — exponential backoff window is still active.</summary>
    internal const string ConnectBackoff = "ETHERNETIP.CONNECT_BACKOFF";

    /// <summary>
    /// Circuit breaker is OPEN — too many consecutive failures. Connect
    /// attempts are suspended until the breaker's reset window elapses.
    /// </summary>
    internal const string CircuitBreakerOpen = "ETHERNETIP.CIRCUIT_BREAKER_OPEN";

    // ---- Configuration ----

    /// <summary>Configuration is not an <c>EthernetIpSourceConfiguration</c>.</summary>
    internal const string ConfigWrongType = "ETHERNETIP.CONFIG_WRONG_TYPE";

    /// <summary>Bad configuration (missing host, invalid path, unknown CPU family, etc.).</summary>
    internal const string ConfigInvalid = "ETHERNETIP.CONFIG_INVALID";

    /// <summary>A required configuration field is missing.</summary>
    internal const string ConfigMissingField = "ETHERNETIP.CONFIG_MISSING_FIELD";

    /// <summary>A numeric field is outside its allowed range.</summary>
    internal const string ConfigOutOfRange = "ETHERNETIP.CONFIG_OUT_OF_RANGE";

    // ---- Protocol ----

    /// <summary>The controller does not contain the requested tag (CIP 0x04 / 0x05).</summary>
    internal const string TagNotFound = "ETHERNETIP.TAG_NOT_FOUND";

    /// <summary>The tag's actual type does not match the configured element type.</summary>
    internal const string TypeMismatch = "ETHERNETIP.TYPE_MISMATCH";

    /// <summary>
    /// The read request could not be BUILT, so the controller was never
    /// contacted — a malformed address (stray whitespace, invalid characters)
    /// or a symbol the controller does not publish.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="TypeMismatch"/> on purpose. ErrorBadParam used
    /// to be reported as a type mismatch, on the stated premise that "the
    /// controller answered, but the request was bad". Measurement against a
    /// Micro820 disproved that: ErrorBadParam comes back in 0-30ms where a real
    /// round trip to that controller is 12-60ms and a genuine timeout is the
    /// full 3000ms. Nothing about the tag's actual type has been observed,
    /// because no request left the machine — so telling an operator to check
    /// their data type sent them looking in the wrong place entirely.
    /// </remarks>
    internal const string TagDefinitionInvalid = "ETHERNETIP.TAG_DEFINITION_INVALID";

    /// <summary>A read transaction failed for a non-fatal reason.</summary>
    internal const string ReadFailed = "ETHERNETIP.READ_FAILED";

    /// <summary>Failed to decode the raw CIP payload into the configured type.</summary>
    internal const string DecodeFailed = "ETHERNETIP.DECODE_FAILED";

    // ---- DeviceState ----

    /// <summary>The CIP connection to the controller was lost mid-session.</summary>
    internal const string ConnectionLost = "ETHERNETIP.CONNECTION_LOST";
}
