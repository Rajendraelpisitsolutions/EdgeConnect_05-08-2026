// ============================================================================
// File: Contracts/ConfigurationFaultDto.cs
// Purpose: Wire-shape projection of Core's ConfigurationFault for the
//          new /api/v1/diagnostics/configuration-faults endpoint. The
//          /diagnostics page's Configuration-faults panel binds to this
//          shape; Sources/Destinations/Routes list pages surface the
//          ErrorCode + Message inline on the existing Last-error fields
//          of their own DTOs (RouteSourceSummaryDto, etc.) rather than
//          embedding this DTO.
//
//          Decoupling from Core.Diagnostics.ConfigurationFault keeps
//          the wire shape evolvable independently of Core's in-process
//          record shape.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.P2.1 phase 3
// ============================================================================

using System;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// One row in the Configuration-faults surface. Mirrors Core's
/// <c>ConfigurationFault</c> with the <c>Kind</c> enum serialised as a
/// string (Source / Sink / Route) so external clients don't need to
/// reference Core types.
/// </summary>
public sealed record ConfigurationFaultDto
{
    /// <summary>"Source", "Sink", or "Route".</summary>
    public required string Kind { get; init; }

    /// <summary>Id of the faulted instance.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Structured error code (e.g. "CONFIG.SOURCE_WITHOUT_ROUTE").</summary>
    public required string ErrorCode { get; init; }

    /// <summary>Operator-readable description of the fault.</summary>
    public required string Message { get; init; }

    /// <summary>UTC timestamp the fault was first observed.</summary>
    public required DateTime ObservedAtUtc { get; init; }
}
