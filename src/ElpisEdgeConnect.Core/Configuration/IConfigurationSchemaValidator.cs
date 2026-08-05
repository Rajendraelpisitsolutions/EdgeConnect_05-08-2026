// ============================================================================
// File: Configuration/IConfigurationSchemaValidator.cs
// Purpose: Abstraction over JSON Schema validation of raw configuration files.
//          The real implementation lives in the ElpisEdgeConnect.SchemaValidation
//          project (which depends on NJsonSchema). Core ships only the
//          interface plus a no-op default so the runtime stays free of
//          external package dependencies.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2 (Q1 design decision)
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Validates raw configuration JSON against the canonical schema.
/// </summary>
/// <remarks>
/// <para>
/// The configuration manager calls this when loading <c>current.json</c>
/// or a history file from disk — i.e., at the boundary where raw JSON
/// becomes a <see cref="GatewayConfiguration"/>. Once the JSON has been
/// deserialized into typed records, the typed validator
/// (<c>ConfigurationValidator</c>) takes over with DataAnnotations,
/// cross-record, and license stages.
/// </para>
/// <para>
/// The default implementation in Core is <see cref="NoOpConfigurationSchemaValidator"/>,
/// which always returns success. The real implementation lives in
/// <c>ElpisEdgeConnect.SchemaValidation.NJsonSchemaConfigurationValidator</c>
/// and is wired by the host project.
/// </para>
/// </remarks>
public interface IConfigurationSchemaValidator
{
    /// <summary>
    /// Validate the given raw JSON text against the gateway configuration
    /// schema. Returns a <see cref="ValidationResult"/> describing any
    /// schema-level errors found.
    /// </summary>
    /// <param name="json">The raw JSON text. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ValidationResult> ValidateAsync(string json, CancellationToken cancellationToken);
}
