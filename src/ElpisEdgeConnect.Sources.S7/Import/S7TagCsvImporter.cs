// ============================================================================
// File: Import/S7TagCsvImporter.cs
// Purpose: Parse a CSV of Siemens S7 tag definitions into a validated list of
//          S7TagDefinitions. Mirrors ModbusTagCsvImporter's strict, all-errors-
//          at-once, side-effect-free semantics. Consumed by the S7 source
//          wizard's "Import CSV" affordance (M.2b.2 v1.1).
//
// Strict semantics:
//   - Any row-level error fails the whole file — no partial imports.
//   - All errors are reported in one run.
//   - Warnings (duplicate addresses) never fail the import.
//
// Schema (header row REQUIRED, fixed column names, case-insensitive):
//   Name, Address, Datatype, ScanRateMs, Unit, Scale, Offset
//   Required header columns: Name, Address
//   Required values:         Name (non-empty), Address (parses; not Timer/Counter)
//   Optional values:         Datatype (blank → derived from address width),
//                            ScanRateMs (blank → 1000), Unit, Scale, Offset
//
// Validation reuses the same Sources.S7 primitives the adapter uses
// (S7AddressParser, S7DatatypeParser, S7TagCompatibility) so a CSV the importer
// accepts produces tags the adapter accepts. Timer/Counter addresses are
// recognised but rejected (read path unproven — M.2b.2 v2 decision).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ElpisEdgeConnect.Sources.S7.Import;

/// <summary>
/// CSV → <see cref="S7TagDefinition"/> importer. Strict, deterministic,
/// side-effect-free. Produces an <see cref="S7TagCsvImportResult"/>.
/// </summary>
public static class S7TagCsvImporter
{
    private static readonly HashSet<string> KnownColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "address", "datatype", "scanRateMs", "unit", "scale", "offset",
    };

    private static readonly string[] RequiredColumns = ["name", "address"];

    /// <summary>Default scan rate when the ScanRateMs column/value is absent.</summary>
    private const int DefaultScanRateMs = 1000;

    /// <summary>Parse a CSV file's contents into S7 tag definitions.</summary>
    public static S7TagCsvImportResult Import(TextReader csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        var errors = new List<S7TagCsvIssue>();
        var warnings = new List<S7TagCsvIssue>();
        var tags = new List<S7TagDefinition>();

        var commentCount = 0;
        var blankCount = 0;
        var dataRowCount = 0;

        var rows = ReadRows(csv);
        if (rows.Count == 0)
        {
            errors.Add(Issue(0, null, null, S7ImportErrors.Empty, "CSV file is empty."));
            return Fail(errors, warnings, 0, 0, 0);
        }

        Row? headerRow = null;
        foreach (var row in rows)
        {
            if (row.IsBlank) { blankCount++; continue; }
            if (row.IsComment) { commentCount++; continue; }
            headerRow = row;
            break;
        }

        if (headerRow is null)
        {
            errors.Add(Issue(1, null, null, S7ImportErrors.NoHeader, "CSV has no header row (only blanks / comments)."));
            return Fail(errors, warnings, 0, commentCount, blankCount);
        }

        var columnNames = headerRow.Fields.Select(f => f.Trim()).ToList();
        ValidateHeader(headerRow.LineNumber, columnNames, errors);
        if (errors.Count > 0)
        {
            return Fail(errors, warnings, 0, commentCount, blankCount);
        }
        var columnIndex = BuildColumnIndex(columnNames);

        var headerLine = headerRow.LineNumber;
        foreach (var row in rows)
        {
            if (row.LineNumber <= headerLine) continue;
            if (row.IsBlank) { blankCount++; continue; }
            if (row.IsComment) { commentCount++; continue; }

            dataRowCount++;
            var parsed = ParseRow(row, columnIndex, columnNames.Count, errors);
            if (parsed is not null)
            {
                tags.Add(parsed);
            }
        }

        if (dataRowCount == 0 && errors.Count == 0)
        {
            errors.Add(Issue(headerLine, null, null, S7ImportErrors.Empty, "CSV has a header but no data rows."));
            return Fail(errors, warnings, 0, commentCount, blankCount);
        }

        DetectDuplicateNames(tags, errors);
        DetectDuplicateAddresses(tags, warnings);

        if (errors.Count > 0)
        {
            return Fail(errors, warnings, dataRowCount, commentCount, blankCount);
        }

        return new S7TagCsvImportResult
        {
            IsSuccess = true,
            Tags = tags,
            Errors = errors,
            Warnings = warnings,
            Summary = new S7TagCsvSummary
            {
                RowsRead = dataRowCount,
                TagsProduced = tags.Count,
                CommentRowsIgnored = commentCount,
                BlankRowsIgnored = blankCount,
            },
        };
    }

    // ── header ───────────────────────────────────────────────────────────

    private static void ValidateHeader(int headerLine, List<string> columns, List<S7TagCsvIssue> errors)
    {
        foreach (var name in columns)
        {
            if (!KnownColumns.Contains(name))
            {
                errors.Add(Issue(headerLine, name, name, S7ImportErrors.UnknownColumn,
                    $"Unknown column '{name}'. Accepted: {string.Join(", ", KnownColumns.OrderBy(s => s, StringComparer.Ordinal))}."));
            }
        }

        var present = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredColumns)
        {
            if (!present.Contains(required))
            {
                errors.Add(Issue(headerLine, required, null, S7ImportErrors.MissingColumn,
                    $"Required column '{required}' is missing from the header."));
            }
        }
    }

    private static Dictionary<string, int> BuildColumnIndex(List<string> columnNames)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columnNames.Count; i++)
        {
            map[columnNames[i]] = i;
        }
        return map;
    }

    // ── row parsing ────────────────────────────────────────────────────

    private static S7TagDefinition? ParseRow(
        Row row, Dictionary<string, int> colIndex, int expectedColumnCount, List<S7TagCsvIssue> errors)
    {
        if (row.Fields.Count < expectedColumnCount)
        {
            errors.Add(Issue(row.LineNumber, null, string.Join(",", row.Fields), S7ImportErrors.ShortRow,
                $"Row has {row.Fields.Count} field(s) but header declared {expectedColumnCount}."));
            return null;
        }

        var before = errors.Count;

        var name = GetField(row, colIndex, "name").Trim();
        var addressRaw = GetField(row, colIndex, "address").Trim();
        var datatypeRaw = GetField(row, colIndex, "datatype").Trim();
        var scanRaw = GetField(row, colIndex, "scanRateMs").Trim();
        var unit = GetField(row, colIndex, "unit").Trim();
        var scaleRaw = GetField(row, colIndex, "scale").Trim();
        var offsetRaw = GetField(row, colIndex, "offset").Trim();

        if (string.IsNullOrEmpty(name))
        {
            errors.Add(Issue(row.LineNumber, "name", name, S7ImportErrors.BadField, "Tag name is required."));
        }

        var address = ParseAddress(row.LineNumber, addressRaw, errors);
        var datatype = ParseDatatype(row.LineNumber, datatypeRaw, errors);
        var scanRateMs = ParseScanRate(row.LineNumber, scanRaw, errors);
        var scale = ParseOptionalDouble(row.LineNumber, "scale", scaleRaw, errors);
        var offset = ParseOptionalDouble(row.LineNumber, "offset", offsetRaw, errors);

        // Compatibility — only when both the address parsed and a datatype was
        // explicitly given. A blank datatype is derived from the address width
        // by the adapter and is always compatible.
        if (address is { } addr && datatype.Spec is { } spec)
        {
            var verdict = S7TagCompatibility.Check(addr, spec);
            if (verdict.BlocksSave)
            {
                errors.Add(Issue(row.LineNumber, "datatype", datatypeRaw, S7ImportErrors.IncompatibleDatatype, verdict.Message!));
            }
        }

        if (errors.Count != before)
        {
            return null;
        }

        return new S7TagDefinition
        {
            Name = name,
            Address = addressRaw,
            Datatype = datatype.Raw,
            ScanRateMs = scanRateMs!.Value,
            Unit = string.IsNullOrEmpty(unit) ? null : unit,
            Scale = scale,
            Offset = offset,
        };
    }

    private static string GetField(Row row, Dictionary<string, int> idx, string column)
    {
        if (!idx.TryGetValue(column, out var i))
        {
            return string.Empty;
        }
        return i < row.Fields.Count ? row.Fields[i] : string.Empty;
    }

    /// <summary>Parse + range-check the S7 address; Timer/Counter rejected as unsupported.</summary>
    private static S7Address? ParseAddress(int line, string raw, List<S7TagCsvIssue> errors)
    {
        if (string.IsNullOrEmpty(raw))
        {
            errors.Add(Issue(line, "address", raw, S7ImportErrors.BadField, "Address is required."));
            return null;
        }

        S7Address parsed;
        try
        {
            parsed = S7AddressParser.Parse(raw);
        }
        catch (ArgumentException)
        {
            errors.Add(Issue(line, "address", raw, S7ImportErrors.BadAddress,
                $"'{raw}' is not a valid S7 address. Examples: DB1.DBW0, DB1.DBX0.0, M10.2, IW4, Q0.1."));
            return null;
        }

        if (parsed.Area is S7MemoryArea.Timer or S7MemoryArea.Counter)
        {
            var areaWord = parsed.Area == S7MemoryArea.Timer ? "Timer" : "Counter";
            errors.Add(Issue(line, "address", raw, S7ImportErrors.UnsupportedAddress,
                $"{areaWord} addresses are recognized but not supported in this release. Use DB, M, I, or Q addresses."));
            return null;
        }

        return parsed;
    }

    /// <summary>Validate the optional datatype token; returns the raw string + resolved spec.</summary>
    private static (string? Raw, S7DatatypeSpec? Spec) ParseDatatype(int line, string raw, List<S7TagCsvIssue> errors)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return (null, null);
        }
        try
        {
            var spec = S7DatatypeParser.Parse(raw, defaultSpec: default);
            return (raw, spec);
        }
        catch (ArgumentException ex)
        {
            errors.Add(Issue(line, "datatype", raw, S7ImportErrors.BadField, ex.Message));
            return (raw, null);
        }
    }

    private static int? ParseScanRate(int line, string raw, List<S7TagCsvIssue> errors)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return DefaultScanRateMs;
        }
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            errors.Add(Issue(line, "scanRateMs", raw, S7ImportErrors.BadField, $"'{raw}' is not a valid integer."));
            return null;
        }
        if (v <= 0)
        {
            errors.Add(Issue(line, "scanRateMs", raw, S7ImportErrors.BadField, "Scan rate must be a positive number of milliseconds."));
            return null;
        }
        return v;
    }

    private static double? ParseOptionalDouble(int line, string column, string raw, List<S7TagCsvIssue> errors)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        errors.Add(Issue(line, column, raw, S7ImportErrors.BadField, $"'{raw}' is not a valid number."));
        return null;
    }

    // ── cross-row ────────────────────────────────────────────────────────

    private static void DetectDuplicateNames(List<S7TagDefinition> tags, List<S7TagCsvIssue> errors)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < tags.Count; i++)
        {
            var name = tags[i].Name;
            if (seen.TryGetValue(name, out var firstIdx))
            {
                errors.Add(Issue(i + 1, "name", name, S7ImportErrors.DuplicateName,
                    $"Tag name '{name}' appears in more than one row (first seen at data row {firstIdx + 1})."));
            }
            else
            {
                seen[name] = i;
            }
        }
    }

    private static void DetectDuplicateAddresses(List<S7TagDefinition> tags, List<S7TagCsvIssue> warnings)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < tags.Count; i++)
        {
            string normalized;
            try { normalized = S7AddressParser.Parse(tags[i].Address).ToString(); }
            catch (ArgumentException) { continue; }

            if (seen.TryGetValue(normalized, out var firstIdx))
            {
                warnings.Add(Issue(i + 1, "address", normalized, S7ImportErrors.DuplicateAddress,
                    $"Address '{normalized}' is read by more than one tag (first at data row {firstIdx + 1}). Allowed, but usually unintended."));
            }
            else
            {
                seen[normalized] = i;
            }
        }
    }

    private static S7TagCsvIssue Issue(int line, string? column, string? value, string code, string message)
        => new() { Line = line, Column = column, RawValue = value, Code = code, Message = message };

    // ── CSV tokenizer (RFC 4180 subset) ───────────────────────────────────

    private sealed class Row
    {
        public required int LineNumber { get; init; }
        public required IReadOnlyList<string> Fields { get; init; }
        public required bool IsComment { get; init; }
        public required bool IsBlank { get; init; }
    }

    private static List<Row> ReadRows(TextReader reader)
    {
        var raw = reader.ReadToEnd();
        if (raw.Length == 0)
        {
            return new List<Row>();
        }
        if (raw[0] == '﻿')
        {
            raw = raw[1..];
        }

        var rows = new List<Row>();
        var field = new StringBuilder();
        var fields = new List<string>();
        var inQuote = false;
        var lineNumber = 1;

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (inQuote)
            {
                if (c == '"')
                {
                    if (i + 1 < raw.Length && raw[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuote = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                switch (c)
                {
                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        if (i + 1 < raw.Length && raw[i + 1] == '\n') i++;
                        goto NEWLINE;
                    case '\n':
                        NEWLINE:
                        fields.Add(field.ToString());
                        field.Clear();
                        rows.Add(BuildRow(lineNumber, fields));
                        fields = new List<string>();
                        lineNumber++;
                        break;
                    case '"':
                        if (field.Length == 0)
                        {
                            inQuote = true;
                        }
                        else
                        {
                            field.Append(c);
                        }
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(BuildRow(lineNumber, fields));
        }

        return rows;
    }

    private static Row BuildRow(int lineNumber, List<string> fields)
    {
        var allBlank = fields.All(f => string.IsNullOrWhiteSpace(f));
        var firstNonBlank = fields.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f))?.TrimStart();
        var isComment = firstNonBlank is not null && firstNonBlank.StartsWith('#');
        return new Row
        {
            LineNumber = lineNumber,
            Fields = fields.ToArray(),
            IsBlank = allBlank && !isComment,
            IsComment = isComment,
        };
    }

    private static S7TagCsvImportResult Fail(
        List<S7TagCsvIssue> errors, List<S7TagCsvIssue> warnings, int rowsRead, int comments, int blanks)
        => new()
        {
            IsSuccess = false,
            Tags = Array.Empty<S7TagDefinition>(),
            Errors = errors,
            Warnings = warnings,
            Summary = new S7TagCsvSummary
            {
                RowsRead = rowsRead,
                TagsProduced = 0,
                CommentRowsIgnored = comments,
                BlankRowsIgnored = blanks,
            },
        };
}
