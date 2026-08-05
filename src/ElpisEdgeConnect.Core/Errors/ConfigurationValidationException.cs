// ============================================================================
// File: Errors/ConfigurationValidationException.cs
// Purpose: Specialized ConfigurationException that carries a ValidationResult.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Core.Errors;

/// <summary>
/// Thrown when configuration validation fails inside a code path that
/// expects success (e.g., loading <c>current.json</c> at startup or
/// applying a previously-validated draft that has gone stale).
/// </summary>
/// <remarks>
/// Most of B2's APIs return a <see cref="ValidationResult"/> rather than
/// throwing; this exception is reserved for boundary cases where a return
/// channel is not available, such as constructor and initialization paths.
/// </remarks>
public sealed class ConfigurationValidationException : ConfigurationException
{
    /// <summary>
    /// Create a new <see cref="ConfigurationValidationException"/> wrapping
    /// a failed <see cref="ValidationResult"/>.
    /// </summary>
    /// <param name="code">Stable error code, typically <c>CORE.CONFIG_VALIDATION_FAILED</c>.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="validationResult">The failing validation result. Must not be null.</param>
    public ConfigurationValidationException(string code, string message, ValidationResult validationResult)
        : base(code, message)
    {
        ValidationResult = validationResult ?? throw new System.ArgumentNullException(nameof(validationResult));
    }

    /// <summary>The validation result that triggered this exception.</summary>
    public ValidationResult ValidationResult { get; }
}
