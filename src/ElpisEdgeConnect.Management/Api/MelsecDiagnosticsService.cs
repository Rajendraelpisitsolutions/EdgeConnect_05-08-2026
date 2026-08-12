// ============================================================================
// File: Api/MelsecDiagnosticsService.cs
// Purpose: Management-side bridge that resolves the RUNNING MELSEC adapter by
//          source id (via ISupervisedSourceRegistry) and returns its
//          observational diagnostics snapshot. Read-only: it only calls the
//          IMelsecDiagnosticsProvider capability, never mutating adapter state.
//          Returns a typed "unavailable" DTO (never throws) when the source is
//          missing / stopped / unlicensed (not supervised) or is not a MELSEC
//          source. Carries NO raw request/response payloads.
// Reference: docs/sessions/2026-07-01-melsec-wizard-ui-plan-v2.md (§Δ3, Δ9)
// ============================================================================

using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.Melsec.Diagnostics;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Availability envelope around a <see cref="MelsecDiagnosticsSnapshot"/>.</summary>
public sealed record MelsecDiagnosticsDto
{
    /// <summary>True when a live MELSEC snapshot was resolved.</summary>
    public required bool Available { get; init; }

    /// <summary>Why diagnostics are unavailable (null when <see cref="Available"/>).</summary>
    public string? Reason { get; init; }

    /// <summary>The observational snapshot when available (no raw payloads).</summary>
    public MelsecDiagnosticsSnapshot? Snapshot { get; init; }

    /// <summary>Build a typed unavailable response.</summary>
    public static MelsecDiagnosticsDto Unavailable(string reason) => new() { Available = false, Reason = reason };
}

/// <summary>Resolves the running MELSEC adapter and returns its observational diagnostics.</summary>
public sealed class MelsecDiagnosticsService
{
    private readonly ISupervisedSourceRegistry? _registry;

    /// <summary>Construct the service. A null registry degrades gracefully to "unavailable".</summary>
    public MelsecDiagnosticsService(ISupervisedSourceRegistry? registry = null)
    {
        _registry = registry;
    }

    /// <summary>Return observational diagnostics for the given source, or a typed unavailable response.</summary>
    public MelsecDiagnosticsDto GetDiagnostics(string sourceInstanceId)
    {
        if (string.IsNullOrWhiteSpace(sourceInstanceId))
        {
            return MelsecDiagnosticsDto.Unavailable("Source id is required.");
        }
        if (_registry is null)
        {
            return MelsecDiagnosticsDto.Unavailable("Runtime source registry is unavailable.");
        }

        var adapter = _registry.GetAdapter(sourceInstanceId);
        if (adapter is null)
        {
            return MelsecDiagnosticsDto.Unavailable($"Source '{sourceInstanceId}' is not running (missing, stopped, or unlicensed).");
        }
        if (adapter is not IMelsecDiagnosticsProvider provider)
        {
            return MelsecDiagnosticsDto.Unavailable($"Source '{sourceInstanceId}' is not a MELSEC source.");
        }

        return new MelsecDiagnosticsDto { Available = true, Snapshot = provider.GetDiagnosticsSnapshot() };
    }
}
