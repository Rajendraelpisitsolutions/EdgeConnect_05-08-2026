// ============================================================================
// File: Errors/AdapterException.cs
// Purpose: Base exception for all adapter-emitted failures. Carries a
//          structured AdapterError payload.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 13.3
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Errors;

/// <summary>
/// Exception thrown by source and sink adapters (and re-thrown by Core
/// subsystems) to carry a structured <see cref="AdapterError"/> payload.
/// </summary>
/// <remarks>
/// Adapters should never throw raw <see cref="Exception"/> or protocol-specific
/// exception types past their own boundary. All outbound errors must be
/// wrapped in <see cref="AdapterException"/> with a well-formed
/// <see cref="Error"/> code so the runtime can classify, retry, and report
/// consistently.
/// </remarks>
public class AdapterException : Exception
{
    /// <summary>The structured error payload carried by this exception.</summary>
    public AdapterError Error { get; }

    /// <summary>
    /// Create a new <see cref="AdapterException"/> wrapping a structured
    /// <see cref="AdapterError"/> payload. The exception's <see cref="Exception.Message"/>
    /// is taken from <see cref="AdapterError.Message"/>.
    /// </summary>
    /// <param name="error">The structured error payload. Must not be null.</param>
    public AdapterException(AdapterError error)
        : base(error?.Message ?? "Adapter error")
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    /// <summary>
    /// Create a new <see cref="AdapterException"/> wrapping a structured
    /// <see cref="AdapterError"/> payload and a lower-level inner exception.
    /// The inner exception is preserved for logging but not exposed to end
    /// users.
    /// </summary>
    /// <param name="error">The structured error payload. Must not be null.</param>
    /// <param name="innerException">The underlying exception that triggered this failure.</param>
    public AdapterException(AdapterError error, Exception innerException)
        : base(error?.Message ?? "Adapter error", innerException)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    /// <summary>Convenience factory for Configuration-category errors.</summary>
    public static AdapterException Configuration(string code, string message) =>
        new(new AdapterError
        {
            Code = code,
            Category = ErrorCategory.Configuration,
            Message = message,
            Retryable = false,
        });

    /// <summary>Convenience factory for Network-category (retryable) errors.</summary>
    public static AdapterException Network(string code, string message, Exception? inner = null)
    {
        var err = new AdapterError
        {
            Code = code,
            Category = ErrorCategory.Network,
            Message = message,
            Retryable = true,
        };
        return inner is null ? new AdapterException(err) : new AdapterException(err, inner);
    }

    /// <summary>Convenience factory for License-category (non-retryable) errors.</summary>
    public static AdapterException License(string code, string message) =>
        new(new AdapterError
        {
            Code = code,
            Category = ErrorCategory.License,
            Message = message,
            Retryable = false,
        });
}
