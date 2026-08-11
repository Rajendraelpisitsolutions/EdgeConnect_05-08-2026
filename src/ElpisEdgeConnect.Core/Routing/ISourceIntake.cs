// ============================================================================
// File: Routing/ISourceIntake.cs
// Purpose: Test seam decoupling the routing engine from real ISourceAdapter
//          calls. The host wraps an adapter (PollAsync / SubscribeAsync)
//          into one of these and hands it to RoutingEngine.RegisterRouteAsync.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.1, PHASE1_EXECUTION_PLAN.md C3
// ============================================================================

using System.Threading.Channels;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// A source-side intake the routing engine reads canonical points from.
/// The host (D1) is responsible for constructing one of these per source
/// instance and pumping it from the underlying
/// <see cref="Adapters.ISourceAdapter"/>. The routing engine never calls
/// <c>PollAsync</c> / <c>SubscribeAsync</c> directly — that decoupling is
/// what makes <c>RoutingEngineTests</c> able to use mock sources.
/// </summary>
public interface ISourceIntake
{
    /// <summary>Identifier of the source instance this intake represents.</summary>
    string SourceInstanceId { get; }

    /// <summary>
    /// The channel reader the routing engine consumes points from. Closing
    /// the writer side signals end-of-source to the route worker.
    /// </summary>
    ChannelReader<CanonicalDataPoint> Reader { get; }
}
