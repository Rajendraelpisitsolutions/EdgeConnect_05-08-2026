// ============================================================================
// File: Api/Focas2BrowseResultDto.cs
// Purpose: Response shape for POST /api/v1/sources/browse/focas2. Carries
//          everything the wizard's Browse Controller panel renders, plus a
//          correlation ProbeId for triage and a Warnings list for non-fatal
//          cleanup signals (e.g. FOCAS2.DISPOSE_TIMEOUT — Locked P).
// Reference: docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md §6.4
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Result of a Browse Controller probe against a FOCAS2 source instance.
/// </summary>
public sealed record Focas2BrowseResultDto
{
    /// <summary>
    /// 8-character correlation id (e.g. <c>"a1b2c3d4"</c>) for this probe.
    /// Logged at every phase boundary so customer-issue triage can pull
    /// the full lifecycle from a single id (Locked Q).
    /// </summary>
    public required string ProbeId { get; init; }

    /// <summary>True if the probe completed and tags were discovered.</summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Ordered axis names discovered on the controller (e.g.
    /// <c>["X", "Y", "Z"]</c>). Empty when <see cref="Success"/> is false.
    /// </summary>
    public IReadOnlyList<string> AxisNames { get; init; } = [];

    /// <summary>
    /// Tag definitions returned by <c>BrowseTagsAsync</c>. Empty when
    /// <see cref="Success"/> is false.
    /// </summary>
    public IReadOnlyList<TagDefinition> Tags { get; init; } = [];

    /// <summary>
    /// Convenience count of <see cref="Tags"/> for the result panel header
    /// ("Discovered N tags"). Derived; not separately wire-allocated.
    /// </summary>
    public int TagCount => Tags.Count;

    /// <summary>
    /// CNC series string from <c>cnc_sysinfo</c> (e.g. <c>"31i"</c>), when
    /// the controller reports it. Null on failure or when sysinfo wasn't read.
    /// </summary>
    public string? CncSeries { get; init; }

    /// <summary>
    /// CNC type from <c>cnc_sysinfo</c> (<c>"M"</c> = machining centre,
    /// <c>"T"</c> = lathe). Null on failure.
    /// </summary>
    public string? CncType { get; init; }

    /// <summary>
    /// Structured error code when <see cref="Success"/> is false. Examples:
    /// <c>LICENSE.MODULE_DISABLED</c>, <c>FOCAS2.CONNECT_FAILED</c>,
    /// <c>FOCAS2.BROWSE_TIMEOUT</c>, <c>FOCAS2.BROWSE_IN_FLIGHT</c>,
    /// <c>FOCAS2.NATIVE_LIBRARY_MISSING</c>, <c>FOCAS2.CONFIG_INVALID</c>.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>Operator-facing error message (paired with <see cref="ErrorCode"/>).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Validation issues when <see cref="ErrorCode"/> is <c>FOCAS2.CONFIG_INVALID</c>.
    /// Empty otherwise.
    /// </summary>
    public IReadOnlyList<ValidationIssue> ValidationErrors { get; init; } = [];

    /// <summary>
    /// Non-fatal warnings — for example, <c>FOCAS2.DISPOSE_TIMEOUT</c> if the
    /// cleanup phase hit the 12s bound (Locked P). Mutable so the service can
    /// attach warnings AFTER the success/failure path returns but BEFORE the
    /// result hits the caller; intentional given the <c>finally</c> ordering
    /// in the probe service.
    /// </summary>
    public IList<string> Warnings { get; init; } = new List<string>();

    /// <summary>End-to-end probe duration in milliseconds (includes cleanup).</summary>
    public required long ElapsedMs { get; init; }
}
