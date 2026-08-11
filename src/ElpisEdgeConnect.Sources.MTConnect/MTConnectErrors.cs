// ============================================================================
// File: MTConnectErrors.cs
// Purpose: Error-code catalogue for the MTConnect adapter. Codes follow the
//          project-wide MODULE.CATEGORY_SUBCATEGORY shape.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.2
// ============================================================================

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>
/// Stable error codes surfaced by the MTConnect adapter. Every code starts
/// with <c>MTCONNECT.</c> so downstream consumers (diagnostics, alerts)
/// can filter to this adapter.
/// </summary>
public static class MTConnectErrors
{
    /// <summary>Configuration provided is the wrong concrete type.</summary>
    public const string ConfigWrongType = "MTCONNECT.CONFIG_WRONG_TYPE";

    /// <summary>Required configuration field missing or invalid.</summary>
    public const string ConfigInvalid = "MTCONNECT.CONFIG_INVALID";

    /// <summary>HTTP request to the Agent failed (connection refused, timeout, etc.).</summary>
    public const string HttpRequestFailed = "MTCONNECT.HTTP_REQUEST_FAILED";

    /// <summary>Agent returned a non-success status code.</summary>
    public const string HttpStatus = "MTCONNECT.HTTP_STATUS";

    /// <summary>Response body could not be parsed as an MTConnect XML document.</summary>
    public const string XmlParseFailed = "MTCONNECT.XML_PARSE_FAILED";

    /// <summary>Agent responded but the response contained no recognizable device stream.</summary>
    public const string NoDeviceStream = "MTCONNECT.NO_DEVICE_STREAM";

    /// <summary>Unexpected error during collection that wasn't network or parse related.</summary>
    public const string CollectError = "MTCONNECT.COLLECT_ERROR";
}
