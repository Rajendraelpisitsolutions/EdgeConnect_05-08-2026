// ============================================================================
// File: Import/ModbusTagCsvImporter.cs
// Purpose: Parse a CSV of Modbus tag definitions into a validated list of
//          ModbusTagDefinitions. Used by the tools/ModbusCsvImport CLI and
//          designed for Phase 4's `/api/sources/{id}/import-tags` to reuse.
//
// Strict semantics:
//   - Any row-level error fails the whole file — no partial imports.
//   - All errors are reported in one run so the user doesn't discover them
//     one at a time.
//   - Warnings (e.g. overlapping register ranges) never fail the import.
//
// Schema (header row REQUIRED, fixed column names, case-insensitive):
//   name, unitId, registerClass, address, datatype, byteOrder,
//   scanRateMs, scale, offset, unit, description
//
// Required fields:  name, unitId, registerClass, address, datatype, scanRateMs
// Optional fields:  byteOrder, scale, offset, unit, description
//
// Address contract (F4 lock):
//   `address` is the zero-based logical Modbus address. Under the default
//   ZeroBased notation, legacy vendor notation (30001, 40001, 10001, 00001)
//   is explicitly REJECTED — the importer emits a clear error pointing at the
//   exact row/column when it sees a legacy-looking value.
//   Callers may instead pass a ModbusAddressBase (OneBased / Modicon), in which
//   case the column is CONVERTED to zero-based on the way in. Either way the
//   tags this importer produces are always zero-based.
//   See docs/modbus-address-base-design.md.
//
// Reference: docs/PHASE3_EXECUTION_PLAN.md F4
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Import;

/// <summary>
/// CSV → <see cref="ModbusTagDefinition"/> importer. Strict, deterministic,
/// side-effect-free. Produces a <see cref="ModbusTagCsvImportResult"/>
/// carrying tags, errors, warnings, and a summary.
/// </summary>
public static class ModbusTagCsvImporter
{
    /// <summary>All accepted column names, lower-case for header lookup.</summary>
    private static readonly HashSet<string> KnownColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "unitId", "registerClass", "address", "datatype",
        "byteOrder", "scanRateMs", "scale", "offset", "unit", "description",
    };

    /// <summary>Columns a valid import file MUST declare in its header.</summary>
    private static readonly string[] RequiredColumns =
    [
        "name", "unitId", "registerClass", "address", "datatype", "scanRateMs",
    ];

    /// <summary>Parse a CSV file's contents into tag definitions.</summary>
    /// <param name="csv">
    /// The file content. A UTF-8 BOM on the first line is stripped
    /// automatically — that's the shape Windows Notepad produces.
    /// </param>
    /// <param name="addressBase">
    /// Notation used for the <c>address</c> column. The default
    /// <see cref="ModbusAddressBase.ZeroBased"/> keeps the historical contract
    /// (legacy 4xxxx/3xxxx/1xxxx values are rejected with a fix-it message);
    /// <see cref="ModbusAddressBase.Modicon"/> / <see cref="ModbusAddressBase.OneBased"/>
    /// convert the column to zero-based logical addresses instead.
    /// </param>
    public static ModbusTagCsvImportResult Import(
        TextReader csv,
        ModbusAddressBase addressBase = ModbusAddressBase.ZeroBased)
    {
        ArgumentNullException.ThrowIfNull(csv);

        var errors = new List<ModbusTagCsvIssue>();
        var warnings = new List<ModbusTagCsvIssue>();
        var tags = new List<ModbusTagDefinition>();

        var commentCount = 0;
        var blankCount = 0;
        var dataRowCount = 0;

        // --- tokenize into logical rows -----------------------------------
        var rows = ReadRows(csv);
        if (rows.Count == 0)
        {
            errors.Add(new ModbusTagCsvIssue
            {
                Line = 0,
                Column = null,
                RawValue = null,
                Code = ModbusErrors.ImportEmpty,
                Message = "CSV file is empty.",
            });
            return Fail(errors, warnings, rowsRead: 0, comments: 0, blanks: 0);
        }

        // --- find header row (first non-comment non-blank) ----------------
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
            errors.Add(new ModbusTagCsvIssue
            {
                Line = 1,
                Column = null,
                RawValue = null,
                Code = ModbusErrors.ImportNoHeader,
                Message = "CSV has no header row (only blanks / comments).",
            });
            return Fail(errors, warnings, rowsRead: 0, comments: commentCount, blanks: blankCount);
        }

        // --- validate header columns --------------------------------------
        var columnNames = headerRow.Fields.Select(f => f.Trim()).ToList();
        ValidateHeader(headerRow.LineNumber, columnNames, errors);
        if (errors.Count > 0)
        {
            // Header errors stop the file — parsing rows against a bad
            // header would produce cascading noise.
            return Fail(errors, warnings, rowsRead: 0, comments: commentCount, blanks: blankCount);
        }
        var columnIndex = BuildColumnIndex(columnNames);

        // --- parse data rows ----------------------------------------------
        var headerLine = headerRow.LineNumber;
        foreach (var row in rows)
        {
            if (row.LineNumber <= headerLine) continue;
            if (row.IsBlank) { blankCount++; continue; }
            if (row.IsComment) { commentCount++; continue; }

            dataRowCount++;
            var parsed = ParseRow(row, columnIndex, columnNames.Count, errors, addressBase);
            if (parsed is not null)
            {
                tags.Add(parsed);
            }
        }

        if (dataRowCount == 0 && errors.Count == 0)
        {
            errors.Add(new ModbusTagCsvIssue
            {
                Line = headerLine,
                Column = null,
                RawValue = null,
                Code = ModbusErrors.ImportEmpty,
                Message = "CSV has a header but no data rows.",
            });
            return Fail(errors, warnings, rowsRead: 0, comments: commentCount, blanks: blankCount);
        }

        // --- cross-row validation: duplicate names, ModbusTagValidator ----
        DetectDuplicateNames(tags, errors);
        RunValidator(tags, errors, addressBase);
        DetectOverlappingRanges(tags, warnings);

        if (errors.Count > 0)
        {
            return Fail(errors, warnings, rowsRead: dataRowCount, comments: commentCount, blanks: blankCount);
        }

        return new ModbusTagCsvImportResult
        {
            IsSuccess = true,
            Tags = tags,
            Errors = errors,
            Warnings = warnings,
            Summary = new ModbusTagCsvSummary
            {
                RowsRead = dataRowCount,
                TagsProduced = tags.Count,
                CommentRowsIgnored = commentCount,
                BlankRowsIgnored = blankCount,
            },
        };
    }

    // =========================================================================
    // PRIVATE — header
    // =========================================================================

    private static void ValidateHeader(int headerLine, List<string> columns, List<ModbusTagCsvIssue> errors)
    {
        // Unknown columns → strict reject.
        foreach (var name in columns)
        {
            if (!KnownColumns.Contains(name))
            {
                errors.Add(new ModbusTagCsvIssue
                {
                    Line = headerLine,
                    Column = name,
                    RawValue = name,
                    Code = ModbusErrors.ImportUnknownColumn,
                    Message = $"Unknown column '{name}'. Accepted: {string.Join(", ", KnownColumns.OrderBy(s => s, StringComparer.Ordinal))}.",
                });
            }
        }

        // Required columns present?
        var present = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredColumns)
        {
            if (!present.Contains(required))
            {
                errors.Add(new ModbusTagCsvIssue
                {
                    Line = headerLine,
                    Column = required,
                    RawValue = null,
                    Code = ModbusErrors.ImportMissingColumn,
                    Message = $"Required column '{required}' is missing from the header.",
                });
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

    // =========================================================================
    // PRIVATE — row parsing
    // =========================================================================

    private static ModbusTagDefinition? ParseRow(
        Row row,
        Dictionary<string, int> colIndex,
        int expectedColumnCount,
        List<ModbusTagCsvIssue> errors,
        ModbusAddressBase addressBase)
    {
        if (row.Fields.Length < expectedColumnCount)
        {
            errors.Add(new ModbusTagCsvIssue
            {
                Line = row.LineNumber,
                Column = null,
                RawValue = string.Join(",", row.Fields),
                Code = ModbusErrors.ImportShortRow,
                Message = $"Row has {row.Fields.Length} field(s) but header declared {expectedColumnCount}.",
            });
            return null;
        }

        var before = errors.Count;

        var name = GetField(row, colIndex, "name");
        var unitIdRaw = GetField(row, colIndex, "unitId");
        var rcRaw = GetField(row, colIndex, "registerClass");
        var addressRaw = GetField(row, colIndex, "address");
        var datatype = GetField(row, colIndex, "datatype");
        var byteOrderRaw = GetField(row, colIndex, "byteOrder");
        var scanRateRaw = GetField(row, colIndex, "scanRateMs");
        var scaleRaw = GetField(row, colIndex, "scale");
        var offsetRaw = GetField(row, colIndex, "offset");
        var unit = GetField(row, colIndex, "unit");
        var description = GetField(row, colIndex, "description");

        // Strip surrounding whitespace consistently across every field.
        name = (name ?? string.Empty).Trim();
        unitIdRaw = (unitIdRaw ?? string.Empty).Trim();
        rcRaw = (rcRaw ?? string.Empty).Trim();
        addressRaw = (addressRaw ?? string.Empty).Trim();
        datatype = (datatype ?? string.Empty).Trim();
        byteOrderRaw = (byteOrderRaw ?? string.Empty).Trim();
        scanRateRaw = (scanRateRaw ?? string.Empty).Trim();
        scaleRaw = (scaleRaw ?? string.Empty).Trim();
        offsetRaw = (offsetRaw ?? string.Empty).Trim();
        unit = unit?.Trim() ?? string.Empty;
        description = description?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(name))
        {
            errors.Add(Field(row.LineNumber, "name", name, ModbusErrors.ImportBadField, "Tag name is required."));
        }

        var unitId = ParseByte(row.LineNumber, "unitId", unitIdRaw, errors);
        var registerClass = ParseRegisterClass(row.LineNumber, "registerClass", rcRaw, errors);
        var address = ParseAddress(row.LineNumber, "address", addressRaw, errors, registerClass, addressBase);
        var scanRateMs = ParseInt(row.LineNumber, "scanRateMs", scanRateRaw, errors);
        ModbusByteOrder? byteOrder = ParseByteOrder(row.LineNumber, "byteOrder", byteOrderRaw, errors);
        var scale = ParseOptionalDouble(row.LineNumber, "scale", scaleRaw, errors);
        var offset = ParseOptionalDouble(row.LineNumber, "offset", offsetRaw, errors);

        // Datatype is validated in depth by ModbusTagValidator downstream;
        // we just forward whatever non-empty token the user supplied.
        var datatypeValue = string.IsNullOrEmpty(datatype) ? null : datatype;

        if (errors.Count != before)
        {
            // This row contributed one or more errors — skip constructing
            // a tag so we don't cascade validator errors on a broken row.
            return null;
        }

        return new ModbusTagDefinition
        {
            Name = name,
            UnitId = unitId!.Value,
            RegisterClass = registerClass!.Value,
            Address = address!.Value,
            ScanRateMs = scanRateMs!.Value,
            Datatype = datatypeValue,
            ByteOrder = byteOrder,
            Scale = scale,
            Offset = offset,
            Unit = string.IsNullOrEmpty(unit) ? null : unit,
        };
    }

    private static string GetField(Row row, Dictionary<string, int> idx, string column)
    {
        if (!idx.TryGetValue(column, out var i))
        {
            return string.Empty;
        }
        return i < row.Fields.Length ? row.Fields[i] : string.Empty;
    }

    private static byte? ParseByte(int line, string column, string raw, List<ModbusTagCsvIssue> errors)
    {
        if (string.IsNullOrEmpty(raw))
        {
            errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField, $"'{column}' is required."));
            return null;
        }
        if (byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField, $"'{raw}' is not a valid byte value (0-255)."));
        return null;
    }

    private static int? ParseInt(int line, string column, string raw, List<ModbusTagCsvIssue> errors)
    {
        if (string.IsNullOrEmpty(raw))
        {
            errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField, $"'{column}' is required."));
            return null;
        }
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField, $"'{raw}' is not a valid integer."));
        return null;
    }

    private static double? ParseOptionalDouble(int line, string column, string raw, List<ModbusTagCsvIssue> errors)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField, $"'{raw}' is not a valid number."));
        return null;
    }

    private static ModbusRegisterClass? ParseRegisterClass(int line, string column, string raw, List<ModbusTagCsvIssue> errors)
    {
        if (string.IsNullOrEmpty(raw))
        {
            errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField, $"'{column}' is required."));
            return null;
        }
        if (Enum.TryParse<ModbusRegisterClass>(raw, ignoreCase: true, out var rc))
        {
            return rc;
        }
        errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField,
            $"'{raw}' is not a valid register class. Accepted: Coil, DiscreteInput, HoldingRegister, InputRegister."));
        return null;
    }

    private static ushort? ParseAddress(
        int line,
        string column,
        string raw,
        List<ModbusTagCsvIssue> errors,
        ModbusRegisterClass? registerClass,
        ModbusAddressBase addressBase)
    {
        if (string.IsNullOrEmpty(raw))
        {
            errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField, $"'{column}' is required."));
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField, $"'{raw}' is not a valid integer address."));
            return null;
        }

        // Non-zero-based notation: convert to the zero-based logical address the
        // rest of EdgeConnect speaks. The register class drives the Modicon
        // prefix, so a row whose class failed to parse can't be converted.
        if (addressBase != ModbusAddressBase.ZeroBased)
        {
            if (registerClass is not { } rc)
            {
                // registerClass already produced its own error on this row.
                return null;
            }
            if (!addressBase.TryToZeroBased(v, rc, out var converted))
            {
                errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField,
                    $"Address '{raw}' is not valid for addressBase '{addressBase}' and register class '{rc}'. " +
                    (addressBase == ModbusAddressBase.Modicon
                        ? $"Modicon {rc} addresses start at {ModbusAddressBaseExtensions.ModiconOffset(rc)}."
                        : "The resulting zero-based address must be between 0 and 65535.")));
                return null;
            }
            return (ushort)converted;
        }

        // Zero-based (default): legacy vendor notation (30001 / 40001 / 10001)
        // is a valid integer but almost never what the user intended, so reject
        // it with the fix in the message rather than reading a bogus register.
        if (v >= 30001 && v <= 49999)
        {
            errors.Add(Field(line, column, raw, ModbusErrors.ImportLegacyAddress,
                $"Address '{raw}' looks like legacy vendor notation (30001/40001 series). " +
                "EdgeConnect uses zero-based logical addresses — subtract 30001 for input registers " +
                "or 40001 for holding registers before import, or set the source's addressBase to 'Modicon'."));
            return null;
        }
        if (v >= 10001 && v <= 19999)
        {
            errors.Add(Field(line, column, raw, ModbusErrors.ImportLegacyAddress,
                $"Address '{raw}' looks like legacy discrete-input notation (10001 series). " +
                "Subtract 10001 to convert to the zero-based logical address, " +
                "or set the source's addressBase to 'Modicon'."));
            return null;
        }

        if (v < 0 || v > ushort.MaxValue)
        {
            errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField,
                $"Address '{raw}' is outside the 16-bit Modbus address space (0..65535)."));
            return null;
        }
        return (ushort)v;
    }

    private static ModbusByteOrder? ParseByteOrder(int line, string column, string raw, List<ModbusTagCsvIssue> errors)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        try
        {
            return ModbusByteOrderExtensions.ParseOrNull(raw);
        }
        catch (ArgumentException ex)
        {
            errors.Add(Field(line, column, raw, ModbusErrors.ImportBadField, ex.Message));
            return null;
        }
    }

    private static ModbusTagCsvIssue Field(int line, string column, string? value, string code, string message)
        => new() { Line = line, Column = column, RawValue = value, Code = code, Message = message };

    // =========================================================================
    // PRIVATE — cross-row
    // =========================================================================

    private static void DetectDuplicateNames(List<ModbusTagDefinition> tags, List<ModbusTagCsvIssue> errors)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < tags.Count; i++)
        {
            var name = tags[i].Name;
            if (seen.TryGetValue(name, out var firstIdx))
            {
                errors.Add(new ModbusTagCsvIssue
                {
                    // We don't preserve original CSV line through this stage,
                    // so report the duplicate's position in the produced tag
                    // list (1-based), which maps 1:1 to data-row order.
                    Line = i + 1,
                    Column = "name",
                    RawValue = name,
                    Code = ModbusErrors.ImportDuplicateName,
                    Message = $"Tag name '{name}' appears in more than one row (first seen at data row {firstIdx + 1}).",
                });
            }
            else
            {
                seen[name] = i;
            }
        }
    }

    private static void RunValidator(
        List<ModbusTagDefinition> tags,
        List<ModbusTagCsvIssue> errors,
        ModbusAddressBase addressBase)
    {
        var validatorIssues = new List<ValidationIssue>();
        for (var i = 0; i < tags.Count; i++)
        {
            ModbusTagValidator.Validate(tags[i], pathPrefix: $"row {i + 1}", errors: validatorIssues, addressBase: addressBase);
        }
        foreach (var issue in validatorIssues)
        {
            errors.Add(new ModbusTagCsvIssue
            {
                Line = 0, // validator works on tag objects, not CSV lines
                Column = issue.Path,
                RawValue = null,
                Code = issue.Code,
                Message = issue.Message,
            });
        }
    }

    private static void DetectOverlappingRanges(List<ModbusTagDefinition> tags, List<ModbusTagCsvIssue> warnings)
    {
        // Group by (unitId, registerClass) — overlap across different slaves
        // or different register classes is not possible.
        var groups = tags
            .Select((t, i) => (tag: t, idx: i))
            .GroupBy(x => (x.tag.UnitId, x.tag.RegisterClass));

        foreach (var group in groups)
        {
            var ordered = group
                .Select(x => (x.tag, x.idx, width: SafeWidth(x.tag)))
                .Where(x => x.width > 0)
                .OrderBy(x => x.tag.Address)
                .ToList();

            for (var i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var cur = ordered[i];
                if (cur.tag.Address < prev.tag.Address + prev.width)
                {
                    warnings.Add(new ModbusTagCsvIssue
                    {
                        Line = cur.idx + 1,
                        Column = "address",
                        RawValue = cur.tag.Address.ToString(CultureInfo.InvariantCulture),
                        Code = ModbusErrors.ImportOverlappingRange,
                        Message = $"Tag '{cur.tag.Name}' overlaps with '{prev.tag.Name}' (shared unit {cur.tag.UnitId}, {cur.tag.RegisterClass}). " +
                                  $"Allowed — the scan planner will read both from one block — but make sure it's intentional.",
                    });
                }
            }
        }
    }

    private static int SafeWidth(ModbusTagDefinition tag)
    {
        try { return ModbusTagWidth.Resolve(tag); }
        catch { return 0; } // malformed datatype — the validator already flagged it
    }

    // =========================================================================
    // PRIVATE — CSV tokenizer (RFC 4180 subset)
    // =========================================================================

    private sealed class Row
    {
        public required int LineNumber { get; init; }
        public required string[] Fields { get; init; }
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

        // Strip a UTF-8 BOM that Notepad-saved CSVs ship with.
        if (raw[0] == '\uFEFF')
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
                    // Doubled "" → literal "; end-of-field otherwise.
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

        // Flush trailing content that didn't end with a newline.
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

    // =========================================================================
    // PRIVATE — fail helpers
    // =========================================================================

    private static ModbusTagCsvImportResult Fail(
        List<ModbusTagCsvIssue> errors,
        List<ModbusTagCsvIssue> warnings,
        int rowsRead,
        int comments,
        int blanks)
        => new()
        {
            IsSuccess = false,
            Tags = Array.Empty<ModbusTagDefinition>(),
            Errors = errors,
            Warnings = warnings,
            Summary = new ModbusTagCsvSummary
            {
                RowsRead = rowsRead,
                TagsProduced = 0,
                CommentRowsIgnored = comments,
                BlankRowsIgnored = blanks,
            },
        };
}
