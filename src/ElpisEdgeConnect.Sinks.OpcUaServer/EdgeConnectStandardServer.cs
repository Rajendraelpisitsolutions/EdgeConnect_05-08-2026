// ============================================================================
// File: EdgeConnectStandardServer.cs
// Purpose: Thin StandardServer subclass that injects EdgeConnect's
//          custom node manager during the master-node-manager build.
//
//          Kept as small as possible — all interesting behavior lives
//          in EdgeConnectNodeManager. The server subclass is here only
//          because the OPC UA Foundation library's extension point for
//          custom node managers is a virtual method on StandardServer.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.1
// ============================================================================

using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Server;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// OPC UA <see cref="StandardServer"/> subclass that owns
/// EdgeConnect's <see cref="EdgeConnectNodeManager"/>. The node
/// manager handle is exposed after server start so the sink adapter
/// can hand canonical data points to it.
/// </summary>
internal sealed class EdgeConnectStandardServer : StandardServer
{
    private readonly OpcUaNamespaceConfig _nsConfig;

    /// <summary>The custom node manager created during <see cref="CreateMasterNodeManager"/>.</summary>
    public EdgeConnectNodeManager? NodeManager { get; private set; }

    public EdgeConnectStandardServer(OpcUaNamespaceConfig nsConfig)
    {
        _nsConfig = nsConfig;
    }

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        var customNodeManager = new EdgeConnectNodeManager(server, configuration, _nsConfig);
        NodeManager = customNodeManager;
        return new MasterNodeManager(server, configuration, dynamicNamespaceUri: null, new INodeManager[]
        {
            customNodeManager,
        });
    }
}
