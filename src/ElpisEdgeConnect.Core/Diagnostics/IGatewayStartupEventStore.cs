// ============================================================================
// File: Diagnostics/IGatewayStartupEventStore.cs
// Purpose: Append-only, in-memory, process-lifetime store of gateway-scoped
//          boot-time signals. The seam between Host (which emits at startup
//          via EdgeConnectComposition.ConfigureRuntimeAsync) and the
//          Management layer (which projects entries into DiagnosticsEventDto
//          via DiagnosticsEventAggregator).
//
//          DELIBERATELY NARROW: not a general event bus. Bounded retention
//          via BoundedEventLog mirrors the existing diagnostics-cap pattern.
//          No persistence, no audit semantics, no route/source coupling.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md §1
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Append-only store of <see cref="GatewayStartupEvent"/>s. Bounded retention
/// inherited from <see cref="BoundedEventLog{T}"/>; oldest entries are
/// evicted when the cap is reached (drops counted but not exposed on this
/// narrow surface — pull from the underlying log if diagnostics drift is
/// suspected, but for boot-time signals the cap should never be hit).
/// </summary>
public interface IGatewayStartupEventStore
{
    /// <summary>
    /// Append a new boot-time signal. Thread-safe. Always succeeds; on
    /// capacity overflow the oldest entry is evicted.
    /// </summary>
    void Append(GatewayStartupEvent ev);

    /// <summary>
    /// Return every retained event in chronological order (oldest first).
    /// Returns a fresh collection on every call — safe to iterate without
    /// holding any lock.
    /// </summary>
    IReadOnlyList<GatewayStartupEvent> GetAll();
}
