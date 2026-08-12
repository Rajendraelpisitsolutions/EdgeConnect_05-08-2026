// ============================================================================
// File: Adapters/ISourceAdapter.cs
// Purpose: THE source adapter contract. Every protocol module that reads data
//          from devices implements this interface.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2 (LOCKED)
//
// LOCKED: This interface is an architectural commitment. Changes require
//         blueprint revision. Adding optional members that do not break
//         existing implementations is acceptable.
//
// Method order and parameter names match blueprint §4.2 exactly. Do not
// reorder, rename parameters, or change signatures without a blueprint
// revision.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Contract implemented by every source protocol adapter (FOCAS2, MT-LINKi,
/// MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, custom drivers, etc.).
/// </summary>
/// <remarks>
/// <para>
/// Implementations MUST:
/// </para>
/// <list type="bullet">
///   <item>Be isolated from other adapters — an exception in one adapter
///   must never propagate to another.</item>
///   <item>Wrap all outbound errors in <see cref="Errors.AdapterException"/>
///   with a well-formed error code.</item>
///   <item>Emit data only as <see cref="CanonicalDataPoint"/>, constructed
///   via <see cref="CanonicalDataPointFactory"/>.</item>
///   <item>Honor cancellation tokens in all async methods.</item>
///   <item>Respect the declared <see cref="Capabilities"/> — do not accept
///   calls to capabilities you do not support.</item>
///   <item>Update <see cref="State"/> via the transitions allowed by
///   <see cref="AdapterStateTransitions"/>.</item>
/// </list>
/// </remarks>
public interface ISourceAdapter : System.IAsyncDisposable
{
    /// <summary>Stable identifier for this source connector instance.</summary>
    string InstanceId { get; }

    /// <summary>Protocol module name (e.g., "focas2", "mtlinki", "modbus").</summary>
    string ProtocolName { get; }

    /// <summary>Capabilities this adapter supports.</summary>
    SourceCapabilities Capabilities { get; }

    /// <summary>Current lifecycle state.</summary>
    AdapterState State { get; }

    /// <summary>
    /// Initialize the adapter with a validated configuration. Must not begin
    /// data acquisition; <see cref="StartAsync"/> does that.
    /// </summary>
    Task InitializeAsync(SourceConfiguration config, CancellationToken ct);

    /// <summary>
    /// Start data acquisition. After this returns, the adapter is in the
    /// <see cref="AdapterState.Running"/> state and either accepts
    /// <see cref="PollAsync"/> calls (for polling adapters) or is streaming
    /// via <see cref="SubscribeAsync"/> (for subscription adapters).
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Stop data acquisition cleanly. Graceful shutdown must complete within
    /// a reasonable bound (typically 10 seconds).
    /// </summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Return a health snapshot without blocking running operations.
    /// </summary>
    Task<AdapterHealth> CheckHealthAsync(CancellationToken ct);

    /// <summary>
    /// Poll the device once. Returns the canonical points read. Only valid
    /// when <see cref="Capabilities"/> includes <see cref="SourceCapabilities.Polling"/>.
    /// </summary>
    Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct);

    /// <summary>
    /// Open a subscription to the device and stream canonical points as they
    /// arrive. Only valid when <see cref="Capabilities"/> includes
    /// <see cref="SourceCapabilities.Subscription"/>.
    /// </summary>
    IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CancellationToken ct);

    /// <summary>
    /// Enumerate the tags available on this device. Only valid when
    /// <see cref="Capabilities"/> includes <see cref="SourceCapabilities.Browse"/>.
    /// </summary>
    Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct);

    /// <summary>
    /// Validate a configuration without starting the adapter. Called by the
    /// config manager during draft validation and by the Configuration Copilot
    /// when guiding a user through setup.
    /// </summary>
    Task<ValidationResult> ValidateConfigAsync(
        SourceConfiguration config,
        CancellationToken ct);

    /// <summary>
    /// Hot-reconfigure the adapter with a new configuration. Honours the
    /// ADR-0015 Rule 11 wizard contract — Edit-mode changes go through
    /// <c>ReconfigureAsync</c> rather than full <see cref="StopAsync"/> +
    /// <see cref="InitializeAsync"/> + <see cref="StartAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default implementation:</b> safe fallback that calls
    /// <see cref="StopAsync"/> → <see cref="InitializeAsync"/> →
    /// <see cref="StartAsync"/> in sequence. This is the same behaviour
    /// every existing adapter had before <c>ReconfigureAsync</c> existed;
    /// no behavioural regression for adapters that don't override.
    /// </para>
    /// <para>
    /// <b>Override contract</b> (per multi-protocol pilot expansion plan
    /// v2.1 §1.3.5):
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     <b>Active-set snapshot at batch boundary.</b> Atomically swap the
    ///     adapter's active subscription / polling set at the next natural
    ///     batch boundary. Mid-batch reconfigures don't take effect until
    ///     the next batch.
    ///   </description></item>
    ///   <item><description>
    ///     <b>No session tear-down.</b> Preserve underlying transport state
    ///     (e.g. OPC UA <c>Session</c>, libplctag connection). Use the
    ///     protocol's native add/remove APIs (e.g. UA
    ///     <c>Subscription.AddItems</c> / <c>RemoveItems</c>).
    ///   </description></item>
    ///   <item><description>
    ///     <b>Tag-removal in-flight tolerance.</b> Already-enqueued data
    ///     points for removed tags continue through the pipeline; only new
    ///     batches drop them.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Reconfigure-during-reconfigure throws.</b> Second concurrent
    ///     call throws <see cref="System.InvalidOperationException"/> with
    ///     a protocol-specific <c>RECONFIGURE_IN_PROGRESS</c> error code so
    ///     the wizard surfaces a clear error.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Validation BEFORE active-set swap.</b> Call
    ///     <see cref="ValidateConfigAsync"/> first; on failure leave the
    ///     active set unchanged. The operator sees the validation error;
    ///     nothing breaks.
    ///   </description></item>
    /// </list>
    /// <para>
    /// New browse-capable adapters (OPC UA Client, EtherNet/IP) MUST
    /// override per ADR-0015 Rule 11. Existing adapters (FOCAS2, Brother
    /// HTTP, Modbus TCP, MTConnect, S7) inherit the safe default until
    /// they elect to override.
    /// </para>
    /// </remarks>
    async Task ReconfigureAsync(SourceConfiguration newConfig, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(newConfig);
        await StopAsync(ct).ConfigureAwait(false);
        await InitializeAsync(newConfig, ct).ConfigureAwait(false);
        await StartAsync(ct).ConfigureAwait(false);
    }
}
