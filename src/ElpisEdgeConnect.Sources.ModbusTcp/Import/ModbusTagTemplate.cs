// ============================================================================
// File: Import/ModbusTagTemplate.cs
// Purpose: Load the built-in reference templates embedded in this assembly.
//          Two templates ship with F4 — a plant-floor PLC shape and a
//          CNC-via-Modbus-gateway shape. Both are plain CSVs that the
//          user copies and edits; the loader exists so tests and tools
//          can round-trip them without grabbing the file from disk.
// Reference: docs/PHASE3_EXECUTION_PLAN.md F4
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Import;

/// <summary>
/// Accessor for reference CSV templates shipped inside the Modbus assembly.
/// </summary>
public static class ModbusTagTemplate
{
    /// <summary>Name of the generic plant-floor PLC template.</summary>
    public const string GenericPlc = "generic-plc";

    /// <summary>Name of the CNC-via-Modbus-gateway template.</summary>
    public const string CncViaModbusGateway = "cnc-via-modbus-gateway";

    /// <summary>Every template name shipped with this assembly.</summary>
    public static IReadOnlyList<string> Available { get; } = new[]
    {
        GenericPlc,
        CncViaModbusGateway,
    };

    /// <summary>
    /// Return the raw CSV text for the named template.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="name"/> is not in <see cref="Available"/>.
    /// </exception>
    public static string LoadCsv(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Array.IndexOf(AvailableArray, name) < 0)
        {
            throw new ArgumentException(
                $"Unknown Modbus template '{name}'. Available: {string.Join(", ", Available)}.",
                nameof(name));
        }

        var resource = $"ElpisEdgeConnect.Sources.ModbusTcp.Templates.{name}.csv";
        var assembly = typeof(ModbusTagTemplate).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resource}' not found. Check .csproj EmbeddedResource wiring.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Load the named template and run it through the CSV importer.
    /// Returns the same result shape as any user-supplied CSV — warnings
    /// and errors are reported the same way.
    /// </summary>
    public static ModbusTagCsvImportResult Load(string name)
    {
        var csv = LoadCsv(name);
        using var reader = new StringReader(csv);
        return ModbusTagCsvImporter.Import(reader);
    }

    // Cached array for the fast IndexOf lookup above — saves a LINQ alloc per call.
    private static readonly string[] AvailableArray =
    {
        GenericPlc,
        CncViaModbusGateway,
    };
}
