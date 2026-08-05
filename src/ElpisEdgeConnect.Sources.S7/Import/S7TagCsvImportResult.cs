// ============================================================================
// File: Import/S7TagCsvImportResult.cs
// Purpose: Result types returned by S7TagCsvImporter. Mirrors
//          ModbusTagCsvImportResult. Strict semantics: IsSuccess=false ⇒ Tags
//          is empty and Errors is non-empty; Warnings are returned regardless.
// Reference: docs/sessions/2026-06-02-s7-source-wizard-plan-v2.md (deferred v1.1)
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.S7.Import;

/// <summary>
/// The outcome of a single S7 tag CSV import. <see cref="IsSuccess"/> is true
/// only if every row parsed and every tag passed validation. Warnings (e.g.
/// duplicate addresses) do not fail the import.
/// </summary>
public sealed record S7TagCsvImportResult
{
    /// <summary>Overall outcome. <c>false</c> ⇒ <see cref="Tags"/> is empty.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Parsed tags. Empty when <see cref="IsSuccess"/> is false.</summary>
    public required IReadOnlyList<S7TagDefinition> Tags { get; init; }

    /// <summary>Fatal per-row errors. Non-empty ⇒ <see cref="IsSuccess"/> is false.</summary>
    public required IReadOnlyList<S7TagCsvIssue> Errors { get; init; }

    /// <summary>Non-fatal notices (e.g. duplicate addresses). Always returned.</summary>
    public required IReadOnlyList<S7TagCsvIssue> Warnings { get; init; }

    /// <summary>Per-file counts — diagnostics + UI output.</summary>
    public required S7TagCsvSummary Summary { get; init; }
}

/// <summary>One issue detected by the importer. Used for both errors and warnings.</summary>
public sealed record S7TagCsvIssue
{
    /// <summary>1-based line number in the source CSV (header is line 1); 0 for whole-file issues.</summary>
    public required int Line { get; init; }

    /// <summary>Column name the issue pertains to, or <see langword="null"/> for row-level issues.</summary>
    public string? Column { get; init; }

    /// <summary>The raw cell contents that triggered the issue, or <see langword="null"/>.</summary>
    public string? RawValue { get; init; }

    /// <summary>Stable machine-readable code (e.g. <c>S7.IMPORT_BAD_FIELD</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable explanation. Safe to log + print.</summary>
    public required string Message { get; init; }
}

/// <summary>Per-file counts emitted on every import.</summary>
public sealed record S7TagCsvSummary
{
    /// <summary>Total non-blank, non-comment data rows encountered.</summary>
    public required int RowsRead { get; init; }

    /// <summary>How many rows produced a valid tag (equals <c>Tags.Count</c> on success).</summary>
    public required int TagsProduced { get; init; }

    /// <summary>Number of full-line <c>#</c> comment rows ignored.</summary>
    public required int CommentRowsIgnored { get; init; }

    /// <summary>Number of blank rows ignored.</summary>
    public required int BlankRowsIgnored { get; init; }
}

/// <summary>Stable error/warning codes emitted by <see cref="S7TagCsvImporter"/>.</summary>
public static class S7ImportErrors
{
    /// <summary>The CSV was empty or had a header but no data rows.</summary>
    public const string Empty = "S7.IMPORT_EMPTY";

    /// <summary>No header row (only blanks / comments).</summary>
    public const string NoHeader = "S7.IMPORT_NO_HEADER";

    /// <summary>A required header column is missing.</summary>
    public const string MissingColumn = "S7.IMPORT_MISSING_COLUMN";

    /// <summary>An unrecognised header column.</summary>
    public const string UnknownColumn = "S7.IMPORT_UNKNOWN_COLUMN";

    /// <summary>A data row has fewer fields than the header declared.</summary>
    public const string ShortRow = "S7.IMPORT_SHORT_ROW";

    /// <summary>A field value failed to parse (bad number, missing required, etc.).</summary>
    public const string BadField = "S7.IMPORT_BAD_FIELD";

    /// <summary>The S7 address is malformed.</summary>
    public const string BadAddress = "S7.IMPORT_BAD_ADDRESS";

    /// <summary>A Timer/Counter address — recognised but unsupported this release.</summary>
    public const string UnsupportedAddress = "S7.IMPORT_UNSUPPORTED_ADDRESS";

    /// <summary>Datatype is incompatible with the address width (planner would reject).</summary>
    public const string IncompatibleDatatype = "S7.IMPORT_INCOMPATIBLE_DATATYPE";

    /// <summary>A tag name appears in more than one row.</summary>
    public const string DuplicateName = "S7.IMPORT_DUPLICATE_NAME";

    /// <summary>(Warning) Two tags read the same normalized address.</summary>
    public const string DuplicateAddress = "S7.IMPORT_DUPLICATE_ADDRESS";
}
