// ============================================================================
// File: Program.cs
// Purpose: CLI entry point for F4's Modbus tag-import tool. Reads a CSV of
//          tag definitions, validates them, and emits a JSON fragment that
//          pastes straight under `connection.tagDefinitions` in a gateway
//          config. Pure text transform — does NOT mutate any existing
//          config file.
//
// Usage:
//   ModbusCsvImport --csv <path> [--output <path>|-] [--pretty]
//
// Exit codes:
//   0  success
//   1  validation / parse errors
//   2  argument / IO error
// Reference: docs/PHASE3_EXECUTION_PLAN.md F4
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ElpisEdgeConnect.Sources.ModbusTcp;
using ElpisEdgeConnect.Sources.ModbusTcp.Import;

namespace ElpisEdgeConnect.Tools.ModbusCsvImport;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitValidation = 1;
    private const int ExitUsage = 2;

    public static int Main(string[] args)
    {
        string? csvPath = null;
        string? outputArg = null;
        var pretty = false;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--csv":
                        csvPath = RequireValue(args, ref i, "--csv");
                        break;
                    case "--output":
                    case "-o":
                        outputArg = RequireValue(args, ref i, "--output");
                        break;
                    case "--pretty":
                        pretty = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        return ExitOk;
                    default:
                        Console.Error.WriteLine($"Unknown argument: '{args[i]}'");
                        PrintUsage();
                        return ExitUsage;
                }
            }

            if (csvPath is null)
            {
                Console.Error.WriteLine("Missing required argument: --csv <path>");
                PrintUsage();
                return ExitUsage;
            }
            if (!File.Exists(csvPath))
            {
                Console.Error.WriteLine($"CSV file not found: {csvPath}");
                return ExitUsage;
            }

            ModbusTagCsvImportResult result;
            using (var reader = new StreamReader(csvPath))
            {
                result = ModbusTagCsvImporter.Import(reader);
            }

            // Always surface warnings — they don't fail the run but users
            // should see them in both success and failure paths.
            foreach (var warn in result.Warnings)
            {
                Console.Error.WriteLine(FormatIssue("WARN", warn));
            }

            if (!result.IsSuccess)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Import FAILED. {result.Errors.Count} error(s):");
                foreach (var err in result.Errors)
                {
                    Console.Error.WriteLine(FormatIssue("ERROR", err));
                }
                return ExitValidation;
            }

            var json = SerializeTags(result.Tags, pretty);
            var outputPath = ResolveOutputPath(outputArg, csvPath);

            if (outputPath == "-")
            {
                Console.Out.Write(json);
                if (!json.EndsWith('\n')) Console.Out.WriteLine();
            }
            else
            {
                File.WriteAllText(outputPath, json);
            }

            // Summary line on stderr so stdout stays clean for stdout-only
            // scripting use.
            var summary = result.Summary;
            Console.Error.WriteLine(
                $"[OK] Imported {summary.TagsProduced} tag(s) from {summary.RowsRead} row(s) " +
                $"({summary.CommentRowsIgnored} comment, {summary.BlankRowsIgnored} blank, " +
                $"{result.Warnings.Count} warning(s)).");
            if (outputPath != "-")
            {
                Console.Error.WriteLine($"Wrote tagDefinitions fragment to: {outputPath}");
                Console.Error.WriteLine("Paste it into your gateway config under sources[].connection.tagDefinitions.");
            }
            return ExitOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal: {ex.Message}");
            return ExitUsage;
        }
    }

    // =========================================================================
    // PRIVATE
    // =========================================================================

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{flag} requires a value.");
        }
        return args[++i];
    }

    private static string ResolveOutputPath(string? outputArg, string csvPath)
    {
        if (outputArg == "-") return "-";
        if (!string.IsNullOrEmpty(outputArg)) return outputArg;

        // Default: alongside the input CSV with a .tags.json suffix.
        var dir = Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(csvPath);
        return Path.Combine(dir, $"{stem}.tags.json");
    }

    private static string SerializeTags(IReadOnlyList<ModbusTagDefinition> tags, bool pretty)
    {
        // Emit in a stable field order so the output is diff-friendly. This
        // matches what FromSourceInstance reads back on config load.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = pretty }))
        {
            writer.WriteStartArray();
            foreach (var t in tags)
            {
                writer.WriteStartObject();
                writer.WriteString("name", t.Name);
                writer.WriteNumber("unitId", t.UnitId);
                writer.WriteString("registerClass", t.RegisterClass.ToString());
                writer.WriteNumber("address", t.Address);
                writer.WriteString("datatype", t.Datatype ?? string.Empty);
                if (t.ByteOrder is { } order)
                {
                    writer.WriteString("byteOrder", order.ToString());
                }
                writer.WriteNumber("scanRateMs", t.ScanRateMs);
                if (t.Scale is { } scale)
                {
                    writer.WriteNumber("scale", scale);
                }
                if (t.Offset is { } offset)
                {
                    writer.WriteNumber("offset", offset);
                }
                if (!string.IsNullOrEmpty(t.Unit))
                {
                    writer.WriteString("unit", t.Unit);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string FormatIssue(string kind, ModbusTagCsvIssue issue)
    {
        var parts = new List<string> { kind };
        if (issue.Line > 0) parts.Add($"line {issue.Line}");
        if (!string.IsNullOrEmpty(issue.Column)) parts.Add($"column '{issue.Column}'");
        if (!string.IsNullOrEmpty(issue.RawValue)) parts.Add($"value '{issue.RawValue}'");
        parts.Add(issue.Code);
        parts.Add(issue.Message);
        return string.Join(": ", parts);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            ModbusCsvImport — Modbus tag-import CLI (EdgeConnect F4)

            Usage:
              ModbusCsvImport --csv <path> [--output <path>|-] [--pretty]

            Options:
              --csv <path>      (required) Input CSV file.
              --output, -o      Output path. '-' for stdout. Default:
                                <csv-stem>.tags.json next to the input.
              --pretty          Emit indented JSON (default is compact).
              --help, -h        Show this message.

            Exit codes:
              0  success
              1  validation errors (printed to stderr)
              2  argument / I/O error
            """);
    }
}
