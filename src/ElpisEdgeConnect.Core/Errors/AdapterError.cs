// ============================================================================
// File: Errors/AdapterError.cs
// Purpose: Structured error record carried by AdapterException.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 13.3
//
// Code naming convention (locked):
//   {PROTOCOL}.{CATEGORY_SUBCATEGORY}
//   Examples:
//     FOCAS2.HANDLE_EXHAUSTED
//     MODBUS.TIMEOUT
//     MQTT.AUTH_REJECTED
//     CORE.CONFIG_INVALID
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Errors;

/// <summary>
/// Structured error record used throughout the runtime. Every failure with
/// observable impact is expressed as an <see cref="AdapterError"/> so that
/// diagnostics, logs, and AI agents can reason about it uniformly.
/// </summary>
public sealed record AdapterError
{
    /// <summary>
    /// Stable error code in the form <c>PROTOCOL.CATEGORY_SUBCATEGORY</c>
    /// (e.g., <c>FOCAS2.HANDLE_EXHAUSTED</c>, <c>MODBUS.TIMEOUT</c>,
    /// <c>CORE.CONFIG_INVALID</c>).
    /// </summary>
    public required string Code { get; init; }

    /// <summary>High-level error category driving retry policy and rollups.</summary>
    public required ErrorCategory Category { get; init; }

    /// <summary>Human-readable message. Should not include PII or credentials.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// True if the runtime should retry this operation according to the
    /// delivery policy. False for configuration, auth, and license errors.
    /// </summary>
    public bool Retryable { get; init; }

    /// <summary>
    /// Optional recommended backoff before the next retry attempt. The retry
    /// state machine uses this as a floor if provided; otherwise it applies
    /// the configured exponential backoff.
    /// </summary>
    public TimeSpan? SuggestedBackoff { get; init; }

    /// <summary>
    /// Optional additional context (request id, device response, etc.).
    /// Safe to log but not displayed to end users.
    /// </summary>
    public string? Context { get; init; }
}
