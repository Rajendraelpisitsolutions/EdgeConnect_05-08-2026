// ============================================================================
// File: Api/BulkSourceMerge/BulkSourceMergeCsvParser.cs
// Purpose: Minimal RFC-4180 CSV reader for the bulk-source-merge inputs.
//
//          Scope is intentionally narrow:
//            1. Decode UTF-8 bytes (no BOM, with-BOM both accepted).
//            2. Enforce the size + row-count caps from v3.1 §8 (1 MB / 1000 rows).
//            3. Validate the header against the protocol's required column
//               shape per v3.1 §8 (strict: missing OR extra rejects).
//            4. Return per-row, line-numbered, name-keyed cell dictionaries
//               for downstream validators and the substitution engine.
//
//          Per-cell semantic validation (deviceId regex, baseUrl scheme,
//          enabled value, duplicate detection) is NOT this file's job — it
//          lives in the row validators and BulkSourceMergeService.
//
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md §8
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;

namespace ElpisEdgeConnect.Management.Api.BulkSourceMerge;

/// <summary>
/// One parsed CSV row. <see cref="LineNumber"/> is 1-based and matches the
/// physical file line number (header is line 1; the first data row is
/// line 2). <see cref="Cells"/> is keyed by header column name.
/// </summary>
public sealed record ParsedCsvRow
{
    /// <summary>1-based physical line number in the source CSV.</summary>
    public required int LineNumber { get; init; }

    /// <summary>Cell values keyed by header column name. Empty values are preserved.</summary>
    public required IReadOnlyDictionary<string, string> Cells { get; init; }
}

/// <summary>
/// Parse outcome. When the parse fails structurally
/// (size/row caps, malformed UTF-8, header mismatch), <see cref="Rows"/> is
/// empty and the explanation lives in <see cref="Findings"/>.
/// </summary>
public sealed record CsvParseResult
{
    /// <summary>Successfully read rows in original CSV order.</summary>
    public required IReadOnlyList<ParsedCsvRow> Rows { get; init; }

    /// <summary>
    /// Structural findings explaining why the result is empty or partial.
    /// Always populated when <see cref="Rows"/> is empty for an
    /// otherwise-non-empty input.
    /// </summary>
    public required IReadOnlyList<BulkSourceMergeFinding> Findings { get; init; }
}

/// <summary>
/// Stateless CSV parser shared by preview + submit. Per v3.1 §1 the submit
/// path re-parses the original bytes, so this method MUST be deterministic
/// and side-effect-free.
/// </summary>
public static class BulkSourceMergeCsvParser
{
    /// <summary>Maximum CSV body size per v3.1 §8 (1 MB).</summary>
    public const int MaxBytes = 1 * 1024 * 1024;

    /// <summary>Maximum number of data rows per v3.1 §8.</summary>
    public const int MaxDataRows = 1000;

    /// <summary>
    /// Parse <paramref name="csvBytes"/> (UTF-8, with or without BOM) and
    /// validate the header against <paramref name="requiredColumns"/>.
    /// </summary>
    public static CsvParseResult Parse(
        byte[] csvBytes,
        IReadOnlyList<string> requiredColumns)
    {
        ArgumentNullException.ThrowIfNull(csvBytes);
        ArgumentNullException.ThrowIfNull(requiredColumns);
        if (requiredColumns.Count == 0)
        {
            throw new ArgumentException("requiredColumns must be non-empty.", nameof(requiredColumns));
        }

        if (csvBytes.Length > MaxBytes)
        {
            return Fail(BulkSourceMergeErrorCode.CsvParseFailed,
                $"CSV size {csvBytes.Length} bytes exceeds the {MaxBytes}-byte limit.");
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(StripBom(csvBytes));
        }
        catch (DecoderFallbackException ex)
        {
            return Fail(BulkSourceMergeErrorCode.CsvParseFailed,
                $"CSV body is not valid UTF-8: {ex.Message}.");
        }

        List<string[]> records;
        try
        {
            records = ReadAllRecords(text);
        }
        catch (FormatException ex)
        {
            return Fail(BulkSourceMergeErrorCode.CsvParseFailed,
                $"CSV could not be parsed: {ex.Message}.");
        }

        if (records.Count == 0)
        {
            return Fail(BulkSourceMergeErrorCode.CsvParseFailed,
                "CSV body is empty. Expected a header row followed by at least one data row.");
        }

        var header = records[0];
        var headerFinding = ValidateHeader(header, requiredColumns);
        if (headerFinding is not null)
        {
            return new CsvParseResult
            {
                Rows = Array.Empty<ParsedCsvRow>(),
                Findings = new[] { headerFinding },
            };
        }

        var dataRowCount = records.Count - 1;
        if (dataRowCount > MaxDataRows)
        {
            return Fail(BulkSourceMergeErrorCode.CsvTooManyRows,
                $"CSV contains {dataRowCount} data rows; the limit is {MaxDataRows}.");
        }

        var rows = new List<ParsedCsvRow>(dataRowCount);
        var findings = new List<BulkSourceMergeFinding>();
        for (var i = 1; i < records.Count; i++)
        {
            var cells = records[i];
            var lineNumber = i + 1;
            if (cells.Length != header.Length)
            {
                findings.Add(new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.CsvParseFailed,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = $"Row has {cells.Length} cell(s); expected {header.Length} to match the header.",
                    CsvRow = lineNumber,
                });
                continue;
            }
            var dict = new Dictionary<string, string>(header.Length, StringComparer.Ordinal);
            for (var c = 0; c < header.Length; c++)
            {
                dict[header[c]] = cells[c];
            }
            rows.Add(new ParsedCsvRow { LineNumber = lineNumber, Cells = dict });
        }

        return new CsvParseResult { Rows = rows, Findings = findings };
    }

    /// <summary>
    /// Validate header column shape against <paramref name="requiredColumns"/>.
    /// Strict: extras and missing both reject (v3.1 §8). Order does NOT
    /// matter — operators may rearrange columns. Returns <c>null</c> on success.
    /// </summary>
    private static BulkSourceMergeFinding? ValidateHeader(
        IReadOnlyList<string> header,
        IReadOnlyList<string> requiredColumns)
    {
        var headerSet = new HashSet<string>(header, StringComparer.Ordinal);
        var requiredSet = new HashSet<string>(requiredColumns, StringComparer.Ordinal);

        if (headerSet.Count != header.Count)
        {
            return new BulkSourceMergeFinding
            {
                Code = BulkSourceMergeErrorCode.CsvHeaderShapeInvalid,
                Severity = BulkSourceMergeSeverity.Error,
                Message = "CSV header contains duplicate column names. Each column must be unique.",
            };
        }

        var missing = requiredSet.Except(headerSet).ToArray();
        var extra = headerSet.Except(requiredSet).ToArray();
        if (missing.Length > 0 || extra.Length > 0)
        {
            var parts = new List<string>(2);
            if (missing.Length > 0)
            {
                parts.Add("missing: " + string.Join(", ", missing.OrderBy(s => s, StringComparer.Ordinal)));
            }
            if (extra.Length > 0)
            {
                parts.Add("unexpected: " + string.Join(", ", extra.OrderBy(s => s, StringComparer.Ordinal)));
            }
            return new BulkSourceMergeFinding
            {
                Code = BulkSourceMergeErrorCode.CsvHeaderShapeInvalid,
                Severity = BulkSourceMergeSeverity.Error,
                Message = $"CSV header does not match the required column shape ({string.Join("; ", parts)}). Required exactly: {string.Join(", ", requiredColumns)}.",
            };
        }
        return null;
    }

    private static CsvParseResult Fail(string code, string message) => new()
    {
        Rows = Array.Empty<ParsedCsvRow>(),
        Findings = new[]
        {
            new BulkSourceMergeFinding
            {
                Code = code,
                Severity = BulkSourceMergeSeverity.Error,
                Message = message,
            },
        },
    };

    private static byte[] StripBom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            var copy = new byte[bytes.Length - 3];
            Array.Copy(bytes, 3, copy, 0, copy.Length);
            return copy;
        }
        return bytes;
    }

    /// <summary>
    /// Minimal RFC-4180 record reader. Supports quoted fields with embedded
    /// commas, line breaks, and escaped quotes (<c>""</c>). Does NOT support
    /// non-comma delimiters, non-double-quote quoting, or backslash escapes —
    /// none of which appear in the wizard's documented input shape.
    /// </summary>
    private static List<string[]> ReadAllRecords(string text)
    {
        var records = new List<string[]>();
        using var reader = new StringReader(text);
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var seenAnyChar = false;
        int next;
        while ((next = reader.Read()) != -1)
        {
            var c = (char)next;
            seenAnyChar = true;
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        inQuotes = false;
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
                    case '"':
                        if (field.Length > 0)
                        {
                            throw new FormatException(
                                FormattableString.Invariant(
                                    $"unexpected quote inside an unquoted field near record {records.Count + 1}"));
                        }
                        inQuotes = true;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        // Swallow CRLF; treat lone CR as a record terminator too.
                        if (reader.Peek() == '\n')
                        {
                            reader.Read();
                        }
                        fields.Add(field.ToString());
                        field.Clear();
                        records.Add(fields.ToArray());
                        fields.Clear();
                        break;
                    case '\n':
                        fields.Add(field.ToString());
                        field.Clear();
                        records.Add(fields.ToArray());
                        fields.Clear();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }
        }
        if (inQuotes)
        {
            throw new FormatException(
                FormattableString.Invariant(
                    $"unterminated quoted field at record {records.Count + 1}"));
        }
        // Flush trailing record if the file doesn't end with a newline.
        if (seenAnyChar && (field.Length > 0 || fields.Count > 0))
        {
            fields.Add(field.ToString());
            records.Add(fields.ToArray());
        }
        return records;
    }
}
