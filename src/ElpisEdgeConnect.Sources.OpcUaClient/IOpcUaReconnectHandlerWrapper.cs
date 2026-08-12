// ============================================================================
// File: IOpcUaReconnectHandlerWrapper.cs
// Purpose: Test seam wrapping the OPC Foundation stack's
//          SessionReconnectHandler. The stack's class is event-driven
//          with private state — substituting it directly is impractical.
//          This wrapper exposes a thin reactive surface that the
//          OpcUaReconnectCoordinator drives.
//
// LOCKED design rules (same pattern as PR 2/4/5 seams):
//   * Tests substitute this whole interface to verify the coordinator
//     drives state transitions / counters correctly without standing
//     up a real OPC stack
//   * Production impl (DefaultOpcUaReconnectHandlerWrapper) is a thin
//     wrapper — covered by integration tests in PR 7 against UA Sample
//     Server
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §1.3, §2.5
//            OPC UA Part 4 §6.7 — Re-establishing connections
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Reactive wrapper around <see cref="SessionReconnectHandler"/>. The
/// coordinator drives the wrapper; the wrapper notifies the coordinator
/// of reconnect outcomes via <see cref="ReconnectCompleted"/>.
/// </summary>
internal interface IOpcUaReconnectHandlerWrapper : IAsyncDisposable
{
    /// <summary>
    /// Raised by the wrapper when a reconnect attempt finishes — either
    /// success (Transfer or Recreate) or terminal failure. The
    /// coordinator inspects the <see cref="ReconnectResult"/> and emits
    /// state transitions accordingly.
    /// </summary>
    event Action<ReconnectResult> ReconnectCompleted;

    /// <summary>
    /// Begin a reconnect attempt against the supplied
    /// <paramref name="lostSession"/>. The wrapper invokes the OPC
    /// stack's <c>SessionReconnectHandler.BeginReconnect</c> with the
    /// configured retry period. Returns immediately; outcome surfaces
    /// via <see cref="ReconnectCompleted"/>.
    /// </summary>
    Task BeginReconnectAsync(ISession lostSession, CancellationToken ct);
}

/// <summary>
/// Discriminates the path the stack took when the reconnect succeeded —
/// or "Failed" when the retry budget was exhausted.
/// </summary>
public enum ReconnectMode
{
    /// <summary>
    /// Default — no reconnect has occurred yet (or coordinator has been
    /// reset). Surfaces as <c>lastReconnectMode = "Unknown"</c> in
    /// health metrics.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Stack called <c>TransferSubscriptions</c> successfully — server-
    /// side subscription state was preserved across the reconnect.
    /// No notification loss expected.
    /// </summary>
    Transfer = 1,

    /// <summary>
    /// Stack fell back to recreating subscriptions client-side because
    /// the server didn't preserve them (or returned
    /// <c>BadNotImplemented</c> for <c>TransferSubscriptions</c>). Some
    /// notification loss IS possible — operator sees the
    /// <c>reconnectsViaRecreate</c> counter tick.
    /// </summary>
    Recreate = 2,

    /// <summary>
    /// Reconnect attempts exhausted the retry budget. Adapter
    /// transitions to <see cref="Core.Adapters.AdapterState.Failed"/>.
    /// </summary>
    Failed = 3,
}

/// <summary>
/// Payload of <see cref="IOpcUaReconnectHandlerWrapper.ReconnectCompleted"/>.
/// </summary>
public sealed record ReconnectResult
{
    /// <summary>
    /// Path the stack took. <see cref="ReconnectMode.Failed"/> means
    /// <see cref="NewSession"/> is null and <see cref="Error"/> describes
    /// the terminal failure.
    /// </summary>
    public required ReconnectMode Mode { get; init; }

    /// <summary>
    /// The new (reconnected) session. <see langword="null"/> when
    /// <see cref="Mode"/> is <see cref="ReconnectMode.Failed"/>.
    /// </summary>
    public ISession? NewSession { get; init; }

    /// <summary>
    /// Terminal error when <see cref="Mode"/> is
    /// <see cref="ReconnectMode.Failed"/>. <see langword="null"/> on
    /// success.
    /// </summary>
    public Exception? Error { get; init; }
}
