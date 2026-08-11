// ============================================================================
// File: Routing/IRouteBufferFactory.cs
// Purpose: Factory seam that the routing engine uses to construct a per-route
//          IMessageBuffer. Decouples the engine from the choice between
//          InMemoryBuffer (BufferMode.InMemory) and SqliteBuffer
//          (BufferMode.StoreAndForward), and lets tests substitute a
//          fast in-memory buffer regardless of policy.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.3, PHASE1_EXECUTION_PLAN.md C3
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Constructs a per-route <see cref="IMessageBuffer"/> from a runtime
/// <see cref="BufferPolicy"/>. The default implementation chooses
/// <c>SqliteBuffer</c> for <c>StoreAndForward</c> and <c>InMemoryBuffer</c>
/// otherwise; tests inject a stub that always returns <c>InMemoryBuffer</c>
/// for speed.
/// </summary>
public interface IRouteBufferFactory
{
    /// <summary>
    /// Create the buffer for a route. The implementation owns the buffer's
    /// lifetime through the returned reference; the routing engine
    /// disposes it via <see cref="System.IAsyncDisposable.DisposeAsync"/>.
    /// </summary>
    /// <param name="routeId">The route id (used as the buffer id and filename).</param>
    /// <param name="policy">Runtime buffer policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IMessageBuffer> CreateAsync(
        string routeId,
        BufferPolicy policy,
        CancellationToken cancellationToken);
}
