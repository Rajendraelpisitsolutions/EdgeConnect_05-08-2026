// ============================================================================
// File: Import/ModbusTagCsvImportResult.cs
// Purpose: Result types returned by ModbusTagCsvImporter. Strict semantics:
//          IsSuccess=false ⇒ Tags is empty and Errors is non-empty.
//          Warnings are returned regardless of outcome.
//
//          Shape chosen so the F4 CLI and the Phase 4 management API
//          (`/api/sources/{id}/import-tags`) both consume the same contract.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Import;

/// <summary>
/// The outcome of a single CSV import. On a strict importer run,
/// <see cref="IsSuccess"/> is true only if every row parsed AND every tag
/// passed <see cref="ModbusTagValidator"/>. Warnings do not fail the import.
/// </summary>
public sealed record ModbusTagCsvImportResult
{
    /// <summary>Overall outcome. <c>false</c> ⇒ <see cref="Tags"/> is empty.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Parsed tags. Empty when <see cref="IsSuccess"/> is false.</summary>
    public required IReadOnlyList<ModbusTagDefinition> Tags { get; init; }

    /// <summary>Fatal per-row errors. Non-empty ⇒ <see cref="IsSuccess"/> is false.</summary>
    public required IReadOnlyList<ModbusTagCsvIssue> Errors { get; init; }

    /// <summary>Non-fatal notices (e.g. overlapping address ranges). Always returned.</summary>
    public required IReadOnlyList<ModbusTagCsvIssue> Warnings { get; init; }

    /// <summary>Per-file counts — diagnostics + CLI output.</summary>
    public required ModbusTagCsvSummary Summary { get; init; }
}

/// <summary>
/// One issue detected by the importer. Used for both errors and warnings —
/// the enclosing list on the result is what makes an issue fatal.
/// </summary>
public sealed record ModbusTagCsvIssue
{
    /// <summary>1-based line number in the source CSV (header is line 1).</summary>
    public required int Line { get; init; }

    /// <summary>Column name the issue pertains to, or <see langword="null"/> for row-level issues.</summary>
    public string? Column { get; init; }

    /// <summary>The raw cell contents that triggered the issue, or <see langword="null"/> for structural issues.</summary>
    public string? RawValue { get; init; }

    /// <summary>Stable machine-readable code (e.g. <c>MODBUS.IMPORT_BAD_FIELD</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable explanation. Safe to log + print.</summary>
    public required string Message { get; init; }
}

/// <summary>Per-file counts emitted on every import.</summary>
public sealed record ModbusTagCsvSummary
{
    /// <summary>Total non-blank, non-comment data rows encountered.</summary>
    public required int RowsRead { get; init; }

    /// <summary>How many rows produced a valid tag (equal to <c>Tags.Count</c> on success).</summary>
    public required int TagsProduced { get; init; }

    /// <summary>Number of full-line <c>#</c> comment rows the importer ignored.</summary>
    public required int CommentRowsIgnored { get; init; }

    /// <summary>Number of blank rows ignored.</summary>
    public required int BlankRowsIgnored { get; init; }
}
