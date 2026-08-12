// ============================================================================
// File: Errors/RouteException.cs
// Purpose: Thrown by the routing engine for route-level failures.
// ============================================================================

namespace ElpisEdgeConnect.Core.Errors;

/// <summary>
/// Thrown for route-level failures: route definition errors, invalid source
/// or sink references, transform pipeline errors, and irrecoverable dispatch
/// failures.
/// </summary>
public sealed class RouteException : AdapterException
{
    /// <summary>
    /// Create a new <see cref="RouteException"/> with a stable error code
    /// and message. Defaults to <see cref="ErrorCategory.Internal"/> and
    /// non-retryable. For retryable route errors (e.g., transient sink
    /// failures surfaced by the routing engine), use the overload that takes
    /// an explicit category and retryable flag.
    /// </summary>
    /// <param name="code">
    /// Stable error code, typically one of the <c>Route*</c> constants in
    /// <see cref="CoreErrors"/>.
    /// </param>
    /// <param name="message">Human-readable description of the route failure.</param>
    public RouteException(string code, string message)
        : base(new AdapterError
        {
            Code = code,
            Category = ErrorCategory.Internal,
            Message = message,
            Retryable = false,
        })
    {
    }

    /// <summary>
    /// Create a new <see cref="RouteException"/> with explicit category and
    /// retryable flag. Use this overload when the route failure is caused by
    /// a downstream issue that should drive retry behaviour — for example,
    /// a network failure on one sink should be <see cref="ErrorCategory.Network"/>
    /// with <paramref name="retryable"/> set to true.
    /// </summary>
    /// <param name="code">Stable error code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <param name="category">Error category driving diagnostics rollups.</param>
    /// <param name="retryable">
    /// Whether the routing engine's retry state machine should schedule
    /// another attempt. See blueprint §19.4 for retry semantics.
    /// </param>
    public RouteException(string code, string message, ErrorCategory category, bool retryable)
        : base(new AdapterError
        {
            Code = code,
            Category = category,
            Message = message,
            Retryable = retryable,
        })
    {
    }
}
