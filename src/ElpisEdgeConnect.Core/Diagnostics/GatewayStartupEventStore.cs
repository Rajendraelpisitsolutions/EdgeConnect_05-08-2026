// ============================================================================
// File: Diagnostics/GatewayStartupEventStore.cs
// Purpose: Default IGatewayStartupEventStore — a thin wrapper around
//          BoundedEventLog<GatewayStartupEvent>. The wrapping is deliberate:
//          this surface only exposes Append + GetAll, not the underlying
//          drop accounting / capacity API. Future callers should NOT be
//          tempted to use this as a general event bus.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md §1
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// In-memory, process-lifetime store of boot-time signals. Backed by
/// <see cref="BoundedEventLog{T}"/> for retention bookkeeping. Boot-time
/// signal volume is expected to be in the low single digits; the 256-entry
/// default capacity is generous and intentionally well above realistic
/// production volume.
/// </summary>
public sealed class GatewayStartupEventStore : IGatewayStartupEventStore
{
    /// <summary>Default capacity — well above realistic boot-signal volume.</summary>
    public const int DefaultCapacity = 256;

    private readonly BoundedEventLog<GatewayStartupEvent> _log;

    /// <summary>Construct with the default capacity (<see cref="DefaultCapacity"/>).</summary>
    public GatewayStartupEventStore()
        : this(DefaultCapacity)
    {
    }

    /// <summary>
    /// Construct with an explicit capacity. Internal so tests can verify
    /// eviction behaviour at small caps without affecting the production
    /// default.
    /// </summary>
    internal GatewayStartupEventStore(int capacity)
    {
        _log = new BoundedEventLog<GatewayStartupEvent>(capacity);
    }

    /// <inheritdoc/>
    public void Append(GatewayStartupEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        _log.Add(ev);
    }

    /// <inheritdoc/>
    public IReadOnlyList<GatewayStartupEvent> GetAll() => _log.Snapshot();
}
