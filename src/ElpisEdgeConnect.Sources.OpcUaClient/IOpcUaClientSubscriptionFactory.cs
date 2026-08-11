// ============================================================================
// File: IOpcUaClientSubscriptionFactory.cs
// Purpose: Test seam for OPC UA Client subscription lifecycle. Wraps
//          the OPC stack's Subscription.Create / AddItems / ApplyChanges
//          sequence into a substitutable surface so adapter tests don't
//          fight the OPC Foundation stack's not-friendly-to-mocking
//          Subscription class.
//
//          Pattern matches IOpcUaClientConnectionEstablisher from PR 2:
//          adapter takes this interface (default = real impl), tests
//          substitute the whole factory and verify the call interactions.
//
//          The pure-logic Planner + Builder are tested in isolation
//          (no Session needed) so the real factory's batching + per-item
//          construction logic gets covered without integration tests.
//
// LOCKED design rules:
//   * Caller owns Subscription lifetime — adapter Dispose chain ends
//     with subscription.Dispose() per OPC stack ownership semantics
//   * Factory mutates the session (sets MinPublishRequestCount,
//     calls session.AddSubscription) — production impl is OK with this
//     because the factory is constructed per StartAsync call
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5, §5.1
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Create the subscriptions for a configured OPC UA Client source.
/// </summary>
internal interface IOpcUaClientSubscriptionFactory
{
    /// <summary>
    /// Plan and create subscriptions on the supplied <paramref name="session"/>
    /// for all monitored items in <paramref name="config"/>. Sets
    /// <c>session.MinPublishRequestCount = subscriptions + 2</c> per
    /// v2.1 §2.5 LockedTuningKnobs guidance before subscriptions are
    /// activated.
    /// </summary>
    Task<IReadOnlyList<Subscription>> CreateSubscriptionsAsync(
        ISession session,
        OpcUaClientSourceConfiguration config,
        CancellationToken ct);
}
