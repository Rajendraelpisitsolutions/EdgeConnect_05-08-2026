// ============================================================================
// File: Adapters/ISinkAdapter.cs
// Purpose: THE sink adapter contract. Every protocol module that delivers data
//          to a destination implements this interface.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.3 (LOCKED)
//
// LOCKED: This interface is an architectural commitment. Changes require
//         blueprint revision. The contract is designed to support both push
//         sinks (MQTT, HTTP, TCP) and pull sinks (OPC UA Server) from day one
//         so that forward-compatible adapters do not require a later rework.
//
// Method order and parameter names match blueprint §4.3 exactly. Do not
// reorder, rename parameters, or change signatures without a blueprint
// revision.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Contract implemented by every sink protocol adapter (MQTT, HTTP, TCP,
/// OPC UA Server, database writers, etc.).
/// </summary>
/// <remarks>
/// <para>
/// Sinks come in two flavors:
/// </para>
/// <list type="bullet">
///   <item><b>Push sinks</b> (MQTT, HTTP, TCP) — Declare
///   <see cref="SinkCapabilities.Push"/>. The routing engine calls
///   <see cref="PublishAsync"/> with batches of canonical points. Each call
///   returns a <see cref="PublishResult"/> which drives per-sink cursor
///   advancement in the route buffer.</item>
///   <item><b>Pull sinks</b> (OPC UA Server) — Declare
///   <see cref="SinkCapabilities.Pull"/>. The routing engine calls
///   <see cref="UpdateCurrentValuesAsync"/> to refresh the exposed current
///   value set. External clients (SCADA, HMI) then read those values on
///   their own schedule.</item>
/// </list>
/// <para>
/// A single sink may support both modes. Implementations MUST honor the
/// isolation, error handling, and cancellation rules described on
/// <see cref="ISourceAdapter"/>.
/// </para>
/// </remarks>
public interface ISinkAdapter : System.IAsyncDisposable
{
    /// <summary>Stable identifier for this sink connector instance.</summary>
    string InstanceId { get; }

    /// <summary>Protocol module name (e.g., "mqtt", "http", "tcp", "opcua-server").</summary>
    string ProtocolName { get; }

    /// <summary>Capabilities this adapter supports.</summary>
    SinkCapabilities Capabilities { get; }

    /// <summary>Current lifecycle state.</summary>
    AdapterState State { get; }

    /// <summary>
    /// Initialize the adapter with a validated configuration. Must not begin
    /// publishing; <see cref="StartAsync"/> does that.
    /// </summary>
    Task InitializeAsync(SinkConfiguration config, CancellationToken ct);

    /// <summary>Start publishing. After this returns, the adapter is in
    /// <see cref="AdapterState.Running"/>.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Stop publishing cleanly. Pending batches should flush if possible.</summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>Return a health snapshot without blocking running operations.</summary>
    Task<AdapterHealth> CheckHealthAsync(CancellationToken ct);

    /// <summary>
    /// Publish a batch of canonical points. Only valid when
    /// <see cref="Capabilities"/> includes <see cref="SinkCapabilities.Push"/>.
    /// The returned <see cref="PublishResult"/> drives cursor advancement in
    /// the route buffer per blueprint Section 19.4.
    /// </summary>
    Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct);

    /// <summary>
    /// Update the exposed current value set for pull-mode sinks. Only valid
    /// when <see cref="Capabilities"/> includes <see cref="SinkCapabilities.Pull"/>.
    /// </summary>
    Task UpdateCurrentValuesAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct);

    /// <summary>
    /// Validate a configuration without starting the adapter.
    /// </summary>
    Task<ValidationResult> ValidateConfigAsync(
        SinkConfiguration config,
        CancellationToken ct);
}
