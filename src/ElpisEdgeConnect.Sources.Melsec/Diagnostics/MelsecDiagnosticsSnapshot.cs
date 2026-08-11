// ============================================================================
// File: Diagnostics/MelsecDiagnosticsSnapshot.cs
// Purpose: Read-only, copy-on-read diagnostics surface for the MELSEC source
//          adapter. OBSERVATIONAL ONLY — reading a snapshot never affects
//          polling, backoff, quality, or retirement (platform principle P1).
//          Exposed via IMelsecDiagnosticsProvider so the common ISourceAdapter
//          contract is unchanged. Contains NO raw request/response payloads —
//          raw bytes stay probe-only / Advanced-only.
// Reference: docs/sessions/2026-07-01-melsec-wizard-ui-plan-v2.md (§Δ3, Δ6)
// ============================================================================

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Sources.Melsec.Diagnostics;

/// <summary>Last observed result of one planned scan block.</summary>
/// <remarks>
/// Serialized as a stable string over the diagnostics API (matching the
/// codebase convention for wire-facing enums) so consumers parse a name, not
/// an ordinal that could shift.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MelsecBlockResult
{
    /// <summary>Not polled since the adapter started.</summary>
    NotYetPolled,

    /// <summary>Last read succeeded.</summary>
    Good,

    /// <summary>Last read returned a non-zero MELSEC end code.</summary>
    ProtocolError,

    /// <summary>Last read failed at the transport (socket / timeout / malformed).</summary>
    CommunicationError,
}

/// <summary>3E route header fields (diagnostic view).</summary>
/// <param name="NetworkNo">Network number.</param>
/// <param name="PcNo">PC / station number.</param>
/// <param name="RequestDestModuleIoNo">Request destination module I/O number.</param>
/// <param name="RequestDestModuleStationNo">Request destination module station number.</param>
public sealed record MelsecRouteDiagnostics(int NetworkNo, int PcNo, int RequestDestModuleIoNo, int RequestDestModuleStationNo);

/// <summary>Diagnostic view of one planned scan block plus its last result.</summary>
public sealed record MelsecScanBlockDiagnostics
{
    /// <summary>Device symbol (e.g. <c>D</c>).</summary>
    public required string DeviceSymbol { get; init; }
    /// <summary>Head device number.</summary>
    public required int HeadDeviceNumber { get; init; }
    /// <summary>Returned word count.</summary>
    public required int WordCount { get; init; }
    /// <summary>Scan-rate bucket (ms).</summary>
    public required int ScanRateMs { get; init; }
    /// <summary>Tags satisfied by this block.</summary>
    public required IReadOnlyList<string> TagNames { get; init; }
    /// <summary>Last observed result for this block.</summary>
    public required MelsecBlockResult LastResult { get; init; }
    /// <summary>MELSEC end code on the last protocol failure.</summary>
    public ushort? LastEndCode { get; init; }
    /// <summary>Diagnostic message on the last failure.</summary>
    public string? LastMessage { get; init; }
}

/// <summary>Per-tag quality summary (derived from its block's last result).</summary>
public sealed record MelsecTagQuality
{
    /// <summary>Tag name.</summary>
    public required string TagName { get; init; }
    /// <summary>Resolved address.</summary>
    public required string Address { get; init; }
    /// <summary>"Good", "Bad", or "Unknown" (not yet polled).</summary>
    public required string Quality { get; init; }
    /// <summary>Reason when Bad.</summary>
    public string? Reason { get; init; }
}

/// <summary>An immutable, observational diagnostics snapshot for a MELSEC source.</summary>
public sealed record MelsecDiagnosticsSnapshot
{
    /// <summary>Source instance id.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Display name of the family profile this source runs (A-2O); null on older snapshots.</summary>
    public string? ProfileDisplayName { get; init; }
    /// <summary>Route header fields.</summary>
    public required MelsecRouteDiagnostics Route { get; init; }
    /// <summary>Planned scan blocks with their last result.</summary>
    public required IReadOnlyList<MelsecScanBlockDiagnostics> ScanBlocks { get; init; }
    /// <summary>Most recent non-zero MELSEC end code seen on any block.</summary>
    public ushort? LastEndCode { get; init; }
    /// <summary>Description of <see cref="LastEndCode"/>.</summary>
    public string? LastEndCodeDescription { get; init; }
    /// <summary>Tags belonging to blocks whose last result was a failure.</summary>
    public required IReadOnlyList<string> AffectedTags { get; init; }
    /// <summary>Per-tag quality summary.</summary>
    public required IReadOnlyList<MelsecTagQuality> TagQuality { get; init; }
    /// <summary>Last request latency in ms (null before the first read).</summary>
    public int? LastRequestLatencyMs { get; init; }
    /// <summary>Whether the connection is currently established.</summary>
    public required bool Connected { get; init; }
    /// <summary>Circuit-breaker state (Closed / Open / HalfOpen / Unknown).</summary>
    public required string BreakerState { get; init; }
    /// <summary>Consecutive connect/transport failures since the last success.</summary>
    public required int ConsecutiveFailures { get; init; }
}

/// <summary>
/// Opt-in capability: a source adapter that can produce an observational MELSEC
/// diagnostics snapshot. Kept separate from <c>ISourceAdapter</c> so the common
/// contract is unchanged (mirrors how retirement is a separate capability).
/// </summary>
public interface IMelsecDiagnosticsProvider
{
    /// <summary>Return a copy-on-read snapshot of current MELSEC diagnostics. Never mutates adapter state.</summary>
    MelsecDiagnosticsSnapshot GetDiagnosticsSnapshot();
}
