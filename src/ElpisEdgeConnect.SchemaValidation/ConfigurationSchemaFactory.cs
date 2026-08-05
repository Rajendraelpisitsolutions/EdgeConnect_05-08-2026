// ============================================================================
// File: ConfigurationSchemaFactory.cs
// Purpose: Single source of truth for the JSON Schema generator settings used
//          to emit schemas for ElpisEdgeConnect.Core.Configuration records.
//          Both the SchemaGen CLI tool and the round-trip schema validation
//          tests in ElpisEdgeConnect.Core.Tests reference this factory so the
//          two enforcement layers cannot drift.
//
// Migrated from tools/SchemaGen/ConfigurationSchemaFactory.cs as part of B2.
// The factory now lives in a production library so it can be referenced by
// the schema validation runner without depending on a tools project.
//
// Key locked settings:
//   - JsonStringEnumConverter is registered on the serializer options so that
//     enum schemas use the canonical string representation (e.g.,
//     "StoreAndForward") instead of the underlying integer. Matches the
//     runtime System.Text.Json behaviour configured on the enum types.
//
//   - AlwaysAllowAdditionalObjectProperties = true so the schema honours the
//     forward-compatibility posture of the runtime: unknown JSON properties
//     are accepted at the runtime and must be accepted by the schema.
//     PublishingSettings's [JsonExtensionData] continues to work because
//     NJsonSchema recognises that attribute and emits the appropriate
//     additionalProperties shape for that record specifically.
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using NJsonSchema.Generation;

namespace ElpisEdgeConnect.SchemaValidation;

/// <summary>
/// Factory that constructs a configured <see cref="JsonSchemaGenerator"/>
/// for ElpisEdgeConnect's configuration types. Single source of truth shared
/// by the SchemaGen CLI, the schema validation runner, and the round-trip
/// validation tests.
/// </summary>
public static class ConfigurationSchemaFactory
{
    /// <summary>
    /// Build the locked-settings <see cref="SystemTextJsonSchemaGeneratorSettings"/>
    /// instance. Any change to these settings must be made here so every
    /// consumer (CLI, validator, tests) stays in lock-step.
    /// </summary>
    public static SystemTextJsonSchemaGeneratorSettings CreateSettings()
    {
        var serializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };

        return new SystemTextJsonSchemaGeneratorSettings
        {
            SerializerOptions = serializerOptions,
            FlattenInheritanceHierarchy = true,
            AlwaysAllowAdditionalObjectProperties = true,
        };
    }

    /// <summary>
    /// Build a fully configured <see cref="JsonSchemaGenerator"/> ready to
    /// emit schemas via <see cref="JsonSchemaGenerator.Generate(System.Type)"/>.
    /// </summary>
    public static JsonSchemaGenerator CreateGenerator() =>
        new(CreateSettings());
}
