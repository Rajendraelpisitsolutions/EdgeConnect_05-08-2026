// ============================================================================
// File: Contracts/SinkListItemDto.cs
// Purpose: List-view + detail-view DTO for /api/v1/sinks. Pairs each
//          sink's diagnostic summary with the routes it serves, its
//          sink kind (resolved from gateway.json so the UI can render
//          mqtt/opcua-server branches), and — for OPC UA Server sinks
//          — its live active-session list.
//
//          Same shape served from the list endpoint and the detail
//          endpoint. The list payload growth from including Sessions
//          everywhere is negligible at commissioning scale (<50 sinks,
//          <100 sessions across them).
//
// M.P2.1 phase 3b — Sinks page becomes config-driven (mirrors the
// M.2b.1.1 Sources rewire). Implications for this contract:
//
//   * `RouteId: string` → `RouteIds: IReadOnlyList<string>`. A sink
//     instance can be referenced by multiple routes (true 1-to-many,
//     unlike sources). Earlier shape only carried a single id because
//     the diagnostics-driven projection emitted one row per (route,
//     sink) tuple; config-driven inventory emits one row per sink.
//   * Only consumers of the shape are the in-process Sinks.razor and
//     SinkDetail.razor pages — no external HTTP consumers exist yet.
//     The breaking rename is captured here in the file header.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1b.3 + M.P2.1
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// One row in the Destinations page's DataGrid, also the response shape for
/// <c>GET /api/v1/sinks/{sinkInstanceId}</c>. Wraps a
/// <see cref="RouteSinkSummaryDto"/> with route context, sink kind
/// (the protocol module name from gateway.json — mqtt / opcua-server
/// / …), and the optional live session list.
/// </summary>
public sealed record SinkListItemDto
{
    /// <summary>
    /// Every route that references this sink in <c>gateway.json</c>.
    /// Empty when the sink is configured but no route currently uses
    /// it (a legal but rare commissioning state — operator added the
    /// destination without wiring it up yet).
    /// </summary>
    public IReadOnlyList<string> RouteIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Sink protocol kind — the <c>ProtocolName</c> from the sink's
    /// <c>gateway.json</c> entry (e.g., <c>"mqtt"</c>, <c>"opcua-server"</c>).
    /// <c>"unknown"</c> when the diagnostics surface reports a sink that
    /// the configuration manager hasn't yet loaded (typically a startup race).
    /// The Razor surface keys off this field to choose between the OPC UA
    /// session panel and the MQTT publishing card.
    /// </summary>
    public required string SinkKind { get; init; }

    /// <summary>Sink-side summary fields (instance id, state, counters, last error).</summary>
    public required RouteSinkSummaryDto Sink { get; init; }

    /// <summary>
    /// Live active-client-sessions list, populated for session-tracking sinks
    /// (today: OPC UA Server). Null when the sink doesn't support session
    /// tracking; empty when the sink is tracked but no clients are connected.
    /// </summary>
    public IReadOnlyList<SinkSessionSummaryDto>? Sessions { get; init; }
}
