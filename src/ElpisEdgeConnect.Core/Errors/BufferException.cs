// ============================================================================
// File: Errors/BufferException.cs
// Purpose: Thrown by the store-and-forward buffer subsystem.
// ============================================================================

namespace ElpisEdgeConnect.Core.Errors;

/// <summary>
/// Thrown for buffer-level failures: storage I/O errors, corruption,
/// capacity exhaustion, and cursor consistency violations.
/// </summary>
public sealed class BufferException : AdapterException
{
    /// <summary>
    /// Create a new <see cref="BufferException"/> with a stable error code
    /// and message. Buffer failures default to <see cref="ErrorCategory.Internal"/>
    /// and non-retryable because they usually indicate a disk, serialization,
    /// or consistency problem that requires investigation rather than blind
    /// retry.
    /// </summary>
    /// <param name="code">
    /// Stable error code, typically one of the <c>Buffer*</c> constants in
    /// <see cref="CoreErrors"/>.
    /// </param>
    /// <param name="message">Human-readable description of the buffer failure.</param>
    public BufferException(string code, string message)
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
    /// Create a new <see cref="BufferException"/> that wraps a lower level
    /// exception (e.g., a <see cref="System.IO.IOException"/> from SQLite).
    /// The inner exception is preserved for logging.
    /// </summary>
    /// <param name="code">Stable error code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <param name="inner">The underlying exception.</param>
    public BufferException(string code, string message, System.Exception inner)
        : base(
            new AdapterError
            {
                Code = code,
                Category = ErrorCategory.Internal,
                Message = message,
                Retryable = false,
            },
            inner)
    {
    }
}
