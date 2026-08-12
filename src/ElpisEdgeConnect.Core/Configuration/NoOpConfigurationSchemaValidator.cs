// ============================================================================
// File: Configuration/NoOpConfigurationSchemaValidator.cs
// Purpose: Default IConfigurationSchemaValidator that always returns success.
//          Used in B2 (and in tests) so Core has no external schema-library
//          dependency. Replaced by NJsonSchemaConfigurationValidator from
//          the ElpisEdgeConnect.SchemaValidation project at host wire-up time.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// <see cref="IConfigurationSchemaValidator"/> implementation that performs
/// no schema checks. Always returns <see cref="ValidationResult.Success"/>.
/// </summary>
/// <remarks>
/// Singleton via <see cref="Instance"/>; thread-safe and stateless.
/// Production hosts replace this with the NJsonSchema-based implementation
/// from the SchemaValidation project. Tests use this directly when schema
/// behaviour is not being exercised.
/// </remarks>
public sealed class NoOpConfigurationSchemaValidator : IConfigurationSchemaValidator
{
    /// <summary>Shared singleton instance.</summary>
    public static NoOpConfigurationSchemaValidator Instance { get; } = new();

    private NoOpConfigurationSchemaValidator()
    {
    }

    /// <inheritdoc/>
    public ValueTask<ValidationResult> ValidateAsync(string json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        return new ValueTask<ValidationResult>(ValidationResult.Success());
    }
}
