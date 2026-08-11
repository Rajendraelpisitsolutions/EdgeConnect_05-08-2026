// ============================================================================
// File: Contracts/SourceLogDto.cs
// Purpose: Wire shape for GET /api/v1/sources/{id}/log — the recent lines of a
//          source's diagnostic log plus whether logging is currently enabled.
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>Recent diagnostic-log lines for one source, with the enable state.</summary>
public sealed record SourceLogDto
{
    /// <summary>Source instance id the log belongs to.</summary>
    public required string SourceInstanceId { get; init; }

    /// <summary>True when opt-in logging is currently enabled for this source.</summary>
    public bool Enabled { get; init; }

    /// <summary>Most recent log lines (oldest first); empty when no file exists yet.</summary>
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
}
