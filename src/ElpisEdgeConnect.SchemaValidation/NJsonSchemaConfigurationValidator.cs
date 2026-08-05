// ============================================================================
// File: NJsonSchemaConfigurationValidator.cs
// Purpose: Production IConfigurationSchemaValidator implementation backed by
//          NJsonSchema. Lives in the SchemaValidation project so the Core
//          assembly stays free of external package dependencies.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2 (Q1 design decision)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using NJsonSchema;

namespace ElpisEdgeConnect.SchemaValidation;

/// <summary>
/// <see cref="IConfigurationSchemaValidator"/> implementation that validates
/// raw JSON against a freshly generated <see cref="GatewayConfiguration"/>
/// schema using NJsonSchema. The schema is generated once per instance and
/// reused for every validation call.
/// </summary>
/// <remarks>
/// <para>
/// Generation uses <see cref="ConfigurationSchemaFactory.CreateGenerator"/>
/// — the same factory used by the SchemaGen CLI tool — so the validator
/// behaviour is bit-for-bit consistent with the schemas committed to
/// <c>docs/config-schemas/</c>.
/// </para>
/// <para>
/// Thread-safe: the underlying <see cref="JsonSchema"/> is immutable after
/// construction, and validation calls are reentrant.
/// </para>
/// </remarks>
public sealed class NJsonSchemaConfigurationValidator : IConfigurationSchemaValidator
{
    private readonly JsonSchema _schema;

    /// <summary>
    /// Construct a new validator. The gateway configuration schema is
    /// generated once during construction and cached.
    /// </summary>
    public NJsonSchemaConfigurationValidator()
    {
        var generator = ConfigurationSchemaFactory.CreateGenerator();
        _schema = generator.Generate(typeof(GatewayConfiguration));
    }

    /// <inheritdoc/>
    public ValueTask<ValidationResult> ValidateAsync(string json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = _schema.Validate(json);
        if (errors.Count == 0)
        {
            return new ValueTask<ValidationResult>(ValidationResult.Success());
        }

        var issues = errors
            .Select(e => new ValidationIssue
            {
                Code = CoreErrors.ConfigValidationFailed,
                Message = e.ToString(),
                Path = e.Path,
            })
            .ToList();

        var result = new ValidationResult
        {
            IsValid = false,
            Errors = issues,
        };

        return new ValueTask<ValidationResult>(result);
    }
}
