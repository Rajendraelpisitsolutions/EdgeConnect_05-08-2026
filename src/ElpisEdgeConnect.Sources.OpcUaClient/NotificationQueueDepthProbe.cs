// ============================================================================
// File: NotificationQueueDepthProbe.cs
// Purpose: Reflection wrapper around Opc.Ua.Client.Subscription's private
//          notification cache. Per v2.1 §2.6, the OPC Foundation stack
//          does not expose notification-queue depth on its public surface,
//          so we read the m_messageCache field via reflection.
//
//          The probe is intentionally tolerant of stack drift — if the
//          private field name changes in a future stack version,
//          IsAvailable returns false and Depth(...) returns -1. The
//          adapter surfaces both as a paired metric so operators know
//          whether the absent reading is "empty queue" or "unsupported".
//
// LOCKED behaviour (per PR 4 plan + amendment #3, user lock 2026-05-29):
//   * Available capability flag distinguishes "supported but queue is
//     empty" from "reflection failed; metric unknown" — surfaced as a
//     separate notificationQueueDepthAvailable metric in PR 4b's adapter
//     CheckHealth
//   * FieldInfo cached once at construction (per probe instance) so
//     hot-path reads are O(1) reflection-free after init
//   * Depth(subscription) → -1 on any failure (locked field missing,
//     reflection throws, subscription is null/disposed)
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §2.6
//            PR 4 plan amendment #3 (user lock 2026-05-29)
//            OPCFoundation/UA-.NETStandard internal field (1.5.376.232 pinned)
// ============================================================================

using System;
using System.Collections;
using System.Reflection;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Reflection-based reader for the OPC stack's per-subscription
/// notification cache.
/// </summary>
internal sealed class NotificationQueueDepthProbe
{
    /// <summary>The locked field name in the OPC stack version we pin.</summary>
    public const string MessageCacheFieldName = "m_messageCache";

    private readonly FieldInfo? _cacheField;

    public NotificationQueueDepthProbe()
    {
        _cacheField = typeof(Subscription).GetField(
            MessageCacheFieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
    }

    /// <summary>
    /// True when the reflection probe successfully resolved the
    /// <c>m_messageCache</c> field on construction. False indicates the
    /// pinned field is missing (stack drift) — queue depth will read
    /// as -1 until the adapter is rebuilt against the new stack version.
    /// </summary>
    public bool IsAvailable => _cacheField is not null;

    /// <summary>
    /// Read the per-subscription notification cache depth. Returns
    /// <c>-1</c> when <see cref="IsAvailable"/> is false OR when the
    /// reflection read itself throws (subscription disposed, field
    /// changed type, etc.).
    /// </summary>
    public int Depth(Subscription subscription)
    {
        if (subscription is null || _cacheField is null)
        {
            return -1;
        }

        try
        {
            var cache = _cacheField.GetValue(subscription);
            return cache switch
            {
                ICollection collection => collection.Count,
                _ => -1,
            };
        }
        catch
        {
            // Defensive — the OPC stack may dispose internal state during
            // reconnect; the probe returns -1 rather than crashing the
            // health-check path.
            return -1;
        }
    }
}
