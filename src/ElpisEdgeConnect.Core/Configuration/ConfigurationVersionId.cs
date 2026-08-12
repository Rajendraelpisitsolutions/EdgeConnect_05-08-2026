// ============================================================================
// File: Configuration/ConfigurationVersionId.cs
// Purpose: Strongly-typed identifier for an applied configuration version.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2
// ============================================================================

using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Identifier for a single applied configuration version. Used as the file
/// name in the history directory and as the parameter to
/// <c>IConfigurationManager.RollbackAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Format: <c>yyyy-MM-ddTHH-mm-ss-fffZ-NNN</c> where the timestamp portion
/// is UTC with colons replaced by hyphens for filesystem safety, and the
/// 3-digit suffix disambiguates applies that land within the same
/// millisecond. The format sorts chronologically as plain text, which is
/// what the history retention policy and history listing rely on.
/// </para>
/// <para>
/// JSON-serializes as a plain string via
/// <see cref="ConfigurationVersionIdJsonConverter"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(ConfigurationVersionIdJsonConverter))]
public readonly record struct ConfigurationVersionId
{
    private static long _counter;

    private readonly string _value;

    /// <summary>Construct a version id from an existing string value (e.g., when reading from disk).</summary>
    public ConfigurationVersionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Version id cannot be null, empty, or whitespace.", nameof(value));
        }
        _value = value;
    }

    /// <summary>The string representation of the version id.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>True if this is the default (uninitialized) version id.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>
    /// Generate a fresh version id from the current UTC time plus an
    /// in-process counter. The counter ensures uniqueness even when two
    /// applies happen within the same millisecond.
    /// </summary>
    public static ConfigurationVersionId NewId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss-fffZ", CultureInfo.InvariantCulture);
        var seq = (Interlocked.Increment(ref _counter) % 1000).ToString("D3", CultureInfo.InvariantCulture);
        return new ConfigurationVersionId($"{timestamp}-{seq}");
    }

    /// <summary>The synthetic version id used when a config has been loaded without any history entries.</summary>
    public static ConfigurationVersionId Initial => new("initial-0000-00-00T00-00-00-000Z-000");

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// JSON converter that serializes <see cref="ConfigurationVersionId"/> as
/// a plain string.
/// </summary>
public sealed class ConfigurationVersionIdJsonConverter : JsonConverter<ConfigurationVersionId>
{
    /// <inheritdoc/>
    public override ConfigurationVersionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }
        var value = reader.GetString();
        return string.IsNullOrEmpty(value) ? default : new ConfigurationVersionId(value);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ConfigurationVersionId value, JsonSerializerOptions options)
    {
        if (value.IsEmpty)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
