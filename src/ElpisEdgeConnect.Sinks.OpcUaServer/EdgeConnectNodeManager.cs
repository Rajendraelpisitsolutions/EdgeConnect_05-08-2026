// ============================================================================
// File: EdgeConnectNodeManager.cs
// Purpose: Custom OPC UA node manager that holds EdgeConnect's tag
//          address space. Created once per server instance, owns the
//          mapping from CanonicalDataPoint → OPC UA BaseDataVariableState.
//
//          Address-space build strategy (MVP H.1): DYNAMIC. The root
//          folder is created at CreateAddressSpace; per-tag nodes are
//          created lazily on first publish using the locked NodeId /
//          BrowsePath templates. A future H.x can introduce a static
//          tag registry consumed at startup once tag aggregation
//          across sources is built.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.1
//            shared-knowledge/contracts/opcua-namespace-policy.md
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Tags;
using Opc.Ua;
using Opc.Ua.Server;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Custom OPC UA node manager that hosts the EdgeConnect tag address
/// space. Variable nodes are created lazily on first publish.
/// </summary>
internal sealed class EdgeConnectNodeManager : CustomNodeManager2
{
    private readonly OpcUaNamespaceConfig _nsConfig;
    private readonly OpcUaNodeIdResolver _nodeIdResolver;
    private readonly object _addressSpaceLock = new();

    // tagKey ("gatewayId|sourceId|stableTagId") -> Variable node we keep
    // a strong reference to so updates avoid a dictionary lookup against
    // PredefinedNodes on the hot path.
    private readonly ConcurrentDictionary<string, BaseDataVariableState> _tagNodes = new(StringComparer.Ordinal);

    // browse-path-segment -> FolderState — cached so we don't recreate
    // intermediate folders for every tag that shares an ancestor.
    private readonly ConcurrentDictionary<string, FolderState> _folderCache = new(StringComparer.Ordinal);

    private FolderState? _rootFolder;

    public EdgeConnectNodeManager(IServerInternal server, ApplicationConfiguration appConfig, OpcUaNamespaceConfig nsConfig)
        : base(server, appConfig, nsConfig.NamespaceUri)
    {
        _nsConfig = nsConfig;
        _nodeIdResolver = new OpcUaNodeIdResolver(nsConfig);
    }

    /// <summary>
    /// Number of tag variable nodes currently held in the address
    /// space. Exposed for diagnostics.
    /// </summary>
    public int TagNodeCount => _tagNodes.Count;

    /// <summary>
    /// Build the root folder of the address space. Per-tag nodes are
    /// created lazily on first publish — see
    /// <see cref="UpsertTagValue"/>.
    /// </summary>
    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (_addressSpaceLock)
        {
            base.CreateAddressSpace(externalReferences);

            // Hang our root folder off the standard Objects folder.
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
            {
                references = new List<IReference>();
                externalReferences[ObjectIds.ObjectsFolder] = references;
            }

            _rootFolder = CreateFolder(parent: null, browseName: _nsConfig.RootFolder);
            _rootFolder.AddReference(ReferenceTypeIds.Organizes, isInverse: true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, isInverse: false, _rootFolder.NodeId));

            AddPredefinedNode(SystemContext, _rootFolder);
        }
    }

    /// <summary>
    /// Create-or-update the variable node for a canonical data point.
    /// Thread-safe; intended to be called from
    /// <c>OpcUaServerSinkAdapter.PublishAsync</c> and
    /// <c>OpcUaServerSinkAdapter.UpdateCurrentValuesAsync</c>.
    /// </summary>
    public void UpsertTagValue(
        CanonicalDataPoint point,
        TagSemantics? semantics,
        string stableTagId,
        IReadOnlyDictionary<string, string?> browsePathPlaceholders)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableTagId);

        var gatewayId = point.GatewayId;
        var sourceInstanceId = point.SourceInstanceId;
        var key = MakeTagKey(gatewayId, sourceInstanceId, stableTagId);

        var node = _tagNodes.GetOrAdd(key, _ =>
        {
            lock (_addressSpaceLock)
            {
                // Double-check inside the lock — another thread may have
                // raced us. ConcurrentDictionary.GetOrAdd does not guarantee
                // factory-once semantics.
                if (_tagNodes.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                return CreateVariableNode(
                    gatewayId,
                    sourceInstanceId,
                    stableTagId,
                    point.ValueType,
                    semantics,
                    browsePathPlaceholders);
            }
        });

        // Update the value + quality + timestamps under the server lock.
        var variant = OpcUaValueMapping.ToVariant(point.Value, point.ValueType);
        var statusCode = OpcUaQualityMapping.ToStatusCode(point.Quality);
        var sourceTimestamp = ToUtc(point.DeviceTimestamp);
        var serverTimestamp = ToUtc(point.GatewayTimestamp);

        lock (Lock)
        {
            node.Value = variant.Value;
            node.StatusCode = statusCode;
            node.Timestamp = sourceTimestamp;
            // ServerTimestamp is propagated automatically via the change
            // detector when monitored items are notified.
            node.ClearChangeMasks(SystemContext, includeChildren: false);
        }
    }

    private BaseDataVariableState CreateVariableNode(
        string gatewayId,
        string sourceInstanceId,
        string stableTagId,
        CanonicalValueType valueType,
        TagSemantics? semantics,
        IReadOnlyDictionary<string, string?> browsePathPlaceholders)
    {
        if (_rootFolder is null)
        {
            throw new InvalidOperationException(
                "CreateAddressSpace must run before UpsertTagValue. The OPC UA server lifecycle is misordered.");
        }

        // Build folder hierarchy from the browse-path template, dropping
        // segments whose placeholders resolved to null/empty per the
        // namespace policy.
        var segments = _nodeIdResolver.ResolveBrowsePathSegments(browsePathPlaceholders);

        var parent = _rootFolder;
        var pathSoFar = _rootFolder.BrowseName.Name;

        // The last segment is the leaf variable name; intermediate
        // segments become folders.
        for (var i = 0; i < segments.Count - 1; i++)
        {
            pathSoFar = $"{pathSoFar}/{segments[i]}";
            parent = _folderCache.GetOrAdd(pathSoFar, _ =>
            {
                var folder = CreateFolder(parent, segments[i]);
                parent.AddReference(ReferenceTypeIds.Organizes, isInverse: false, folder.NodeId);
                folder.AddReference(ReferenceTypeIds.Organizes, isInverse: true, parent.NodeId);
                AddPredefinedNode(SystemContext, folder);
                return folder;
            });
        }

        // Leaf variable.
        var leafBrowseName = segments.Count > 0
            ? segments[^1]
            : stableTagId;

        var nodeIdString = _nodeIdResolver.ResolveNodeId(gatewayId, sourceInstanceId, stableTagId);
        var nodeId = new NodeId(StripNodeIdPrefix(nodeIdString), NamespaceIndex);
        var displayName = semantics?.DisplayName ?? semantics?.Name ?? leafBrowseName;

        var variable = new BaseDataVariableState(parent)
        {
            NodeId = nodeId,
            BrowseName = new QualifiedName(leafBrowseName, NamespaceIndex),
            DisplayName = new LocalizedText("en", displayName),
            Description = semantics?.Description is { Length: > 0 } desc
                ? new LocalizedText("en", desc)
                : null,
            DataType = OpcUaValueMapping.ResolveDataTypeId(valueType),
            ValueRank = OpcUaValueMapping.ResolveValueRank(valueType),
            AccessLevel = semantics?.Writable == true ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead,
            UserAccessLevel = semantics?.Writable == true ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead,
            Historizing = false,
            StatusCode = StatusCodes.BadWaitingForInitialData,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
        };

        if (semantics is { EngineeringUnit: { Length: > 0 } unit })
        {
            // EU information is exposed as a child property per OPC UA
            // Part 8. The DA Server companion spec defines NodeIds for
            // EngineeringUnits and EURange; we publish EngineeringUnits as
            // a string for MVP. K can upgrade to the structured EUInformation.
            var euProperty = new PropertyState<string>(variable)
            {
                NodeId = new NodeId($"{nodeIdString}/eu", NamespaceIndex),
                BrowseName = BrowseNames.EngineeringUnits,
                DisplayName = BrowseNames.EngineeringUnits,
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = unit,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TypeDefinitionId = VariableTypeIds.PropertyType,
            };
            variable.AddChild(euProperty);
        }

        if (semantics is { HighLimit: { } high, LowLimit: { } low })
        {
            var rangeProperty = new PropertyState<Opc.Ua.Range>(variable)
            {
                NodeId = new NodeId($"{nodeIdString}/range", NamespaceIndex),
                BrowseName = BrowseNames.EURange,
                DisplayName = BrowseNames.EURange,
                DataType = DataTypeIds.Range,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = new Opc.Ua.Range(high, low),
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TypeDefinitionId = VariableTypeIds.PropertyType,
            };
            variable.AddChild(rangeProperty);
        }

        parent.AddReference(ReferenceTypeIds.Organizes, isInverse: false, variable.NodeId);
        variable.AddReference(ReferenceTypeIds.Organizes, isInverse: true, parent.NodeId);
        AddPredefinedNode(SystemContext, variable);

        return variable;
    }

    private FolderState CreateFolder(FolderState? parent, string browseName)
    {
        var folderNodeIdString = parent is null
            ? $"folder:/{browseName}"
            : $"{parent.NodeId.Identifier}/{browseName}";

        return new FolderState(parent)
        {
            NodeId = new NodeId(folderNodeIdString, NamespaceIndex),
            BrowseName = new QualifiedName(browseName, NamespaceIndex),
            DisplayName = new LocalizedText("en", browseName),
            TypeDefinitionId = ObjectTypeIds.FolderType,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            EventNotifier = EventNotifiers.None,
        };
    }

    private static string MakeTagKey(string gatewayId, string sourceInstanceId, string stableTagId) =>
        string.Create(
            gatewayId.Length + sourceInstanceId.Length + stableTagId.Length + 2,
            (gatewayId, sourceInstanceId, stableTagId),
            (span, parts) =>
            {
                var (g, s, t) = parts;
                g.AsSpan().CopyTo(span);
                span[g.Length] = '|';
                s.AsSpan().CopyTo(span[(g.Length + 1)..]);
                span[g.Length + 1 + s.Length] = '|';
                t.AsSpan().CopyTo(span[(g.Length + 2 + s.Length)..]);
            });

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    /// <summary>
    /// Strip the <c>ns=2;s=</c> prefix from a fully-qualified NodeId
    /// string to extract just the identifier portion. The
    /// <see cref="NodeId"/> ctor expects the identifier separately
    /// from the namespace index.
    /// </summary>
    private static string StripNodeIdPrefix(string nodeIdString)
    {
        var sIndex = nodeIdString.IndexOf(";s=", StringComparison.Ordinal);
        return sIndex >= 0 ? nodeIdString[(sIndex + 3)..] : nodeIdString;
    }
}
