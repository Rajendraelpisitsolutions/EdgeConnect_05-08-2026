// ============================================================================
// File: Errors/LicenseException.cs
// Purpose: Thrown when a license check fails.
// ============================================================================

namespace ElpisEdgeConnect.Core.Errors;

/// <summary>
/// Thrown when license enforcement rejects an operation — including module
/// activation, instance count limits, expiration, and signature failures.
/// </summary>
public sealed class LicenseException : AdapterException
{
    /// <summary>
    /// Create a new <see cref="LicenseException"/> with a stable error code
    /// and message. The resulting <see cref="AdapterError"/> is categorised
    /// as <see cref="ErrorCategory.License"/> and marked non-retryable,
    /// because license issues are resolved by updating the license file,
    /// not by retrying the operation.
    /// </summary>
    /// <param name="code">
    /// Stable error code, typically one of the <c>License*</c> constants in
    /// <see cref="CoreErrors"/>.
    /// </param>
    /// <param name="message">
    /// Human-readable explanation of the license violation (e.g., which
    /// module is not licensed, how many instances are configured vs. allowed,
    /// when the license expired).
    /// </param>
    public LicenseException(string code, string message)
        : base(new AdapterError
        {
            Code = code,
            Category = ErrorCategory.License,
            Message = message,
            Retryable = false,
        })
    {
    }
}
