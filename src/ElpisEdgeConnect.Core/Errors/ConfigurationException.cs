// ============================================================================
// File: Errors/ConfigurationException.cs
// Purpose: Thrown during configuration loading, validation, or application.
// ============================================================================

namespace ElpisEdgeConnect.Core.Errors;

/// <summary>
/// Thrown when a configuration operation fails — including schema validation,
/// semantic validation, license enforcement, and apply-time consistency checks.
/// </summary>
public class ConfigurationException : AdapterException
{
    /// <summary>
    /// Create a new <see cref="ConfigurationException"/> with a stable error
    /// code and message. The resulting <see cref="AdapterError"/> is categorised
    /// as <see cref="ErrorCategory.Configuration"/> and marked non-retryable,
    /// because configuration problems require explicit user intervention.
    /// </summary>
    /// <param name="code">
    /// Stable error code following the <c>MODULE.CATEGORY_SUBCATEGORY</c>
    /// naming convention (see <see cref="CoreErrors"/> for examples).
    /// </param>
    /// <param name="message">
    /// Human-readable description of what is wrong with the configuration.
    /// Must not contain credentials or PII.
    /// </param>
    public ConfigurationException(string code, string message)
        : base(new AdapterError
        {
            Code = code,
            Category = ErrorCategory.Configuration,
            Message = message,
            Retryable = false,
        })
    {
    }

    /// <summary>
    /// Create a new <see cref="ConfigurationException"/> that wraps a lower
    /// level exception (e.g., a <see cref="System.IO.FileNotFoundException"/>
    /// while loading a config file). The inner exception is preserved for
    /// logging but not exposed to end users.
    /// </summary>
    /// <param name="code">
    /// Stable error code following the <c>MODULE.CATEGORY_SUBCATEGORY</c>
    /// naming convention.
    /// </param>
    /// <param name="message">Human-readable description of the failure.</param>
    /// <param name="inner">The underlying exception that triggered the failure.</param>
    public ConfigurationException(string code, string message, System.Exception inner)
        : base(
            new AdapterError
            {
                Code = code,
                Category = ErrorCategory.Configuration,
                Message = message,
                Retryable = false,
            },
            inner)
    {
    }
}
