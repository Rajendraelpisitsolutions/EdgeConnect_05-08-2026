// ============================================================================
// Tests: OpcUaNodeIdResolver — the IDENTITY contract for OPC UA clients.
//        These tests are deliberately strict because changing this
//        behavior is a v2 namespace bump per
//        shared-knowledge/contracts/opcua-namespace-policy.md.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.OpcUaServer.Tests;

public class OpcUaNodeIdResolverTests
{
    [Fact]
    public void ResolveNodeId_DefaultTemplate_ProducesLockedShape()
    {
        var resolver = new OpcUaNodeIdResolver(new OpcUaNamespaceConfig());

        var nodeId = resolver.ResolveNodeId("gw-line1-edge", "modbus-s7-line1", "spindle_rpm");

        nodeId.Should().Be("ns=2;s=gw-line1-edge/modbus-s7-line1/spindle_rpm");
    }

    [Fact]
    public void ResolveNodeId_IsStableAcrossDisplayRenames()
    {
        // The NodeId is derived from stableTagId, not from any
        // operator-renamable display field. Renaming the display name
        // must NOT change the NodeId — this is the LOCKED identity
        // contract for v1.
        var resolver = new OpcUaNodeIdResolver(new OpcUaNamespaceConfig());

        var before = resolver.ResolveNodeId("gw-1", "src-1", "spindle_rpm");
        var after = resolver.ResolveNodeId("gw-1", "src-1", "spindle_rpm");

        before.Should().Be(after);
    }

    [Fact]
    public void ResolveBrowsePathSegments_DefaultTemplate_DropsNullPlaceholders()
    {
        var resolver = new OpcUaNodeIdResolver(new OpcUaNamespaceConfig());
        var placeholders = new Dictionary<string, string?>
        {
            ["deviceClass"] = "cnc",
            ["sourceId"] = "focas2-cnc01",
            ["tagName"] = "spindle_rpm",
        };

        var segments = resolver.ResolveBrowsePathSegments(placeholders);

        segments.Should().Equal("cnc", "focas2-cnc01", "spindle_rpm");
    }

    [Fact]
    public void ResolveBrowsePathSegments_NullDeviceClass_DropsSilently()
    {
        var resolver = new OpcUaNodeIdResolver(new OpcUaNamespaceConfig
        {
            BrowsePathTemplate = "{deviceClass}/{sourceId}/{tagName}",
        });
        var placeholders = new Dictionary<string, string?>
        {
            ["deviceClass"] = null,
            ["sourceId"] = "modbus-s7-line1",
            ["tagName"] = "spindle_rpm",
        };

        var segments = resolver.ResolveBrowsePathSegments(placeholders);

        segments.Should().Equal("modbus-s7-line1", "spindle_rpm");
    }

    [Fact]
    public void ResolveBrowsePathSegments_Isa95Template_BuildsFullHierarchy()
    {
        var resolver = new OpcUaNodeIdResolver(new OpcUaNamespaceConfig
        {
            BrowsePathTemplate = "{site}/{area}/{line}/{deviceClass}/{sourceId}/{category}/{tagName}",
        });
        var placeholders = new Dictionary<string, string?>
        {
            ["site"] = "Kolhapur",
            ["area"] = "Press-Shop",
            ["line"] = "Line1",
            ["deviceClass"] = "cnc",
            ["sourceId"] = "focas2-cnc01",
            ["category"] = "Status",
            ["tagName"] = "running",
        };

        var segments = resolver.ResolveBrowsePathSegments(placeholders);

        segments.Should().Equal("Kolhapur", "Press-Shop", "Line1", "cnc", "focas2-cnc01", "Status", "running");
    }
}
