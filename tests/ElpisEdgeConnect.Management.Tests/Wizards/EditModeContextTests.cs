// ============================================================================
// Tests: EditModeContextTests — pins the M.2d.1 v2 §5.6 mode-context type.
//
// Pure type; pure xUnit (no bUnit).
//
// Verified contracts:
//   * Add() yields Mode=Add, ExistingInstanceId=null.
//   * Edit("id") yields Mode=Edit, ExistingInstanceId="id".
//   * Edit("") / Edit(null) / Edit("   ") throw ArgumentException.
//   * IsEdit returns true iff Mode=Edit AND id is non-empty.
//   * FindSource / FindSink / FindRoute return null in Add mode.
//   * FindSource / FindSink / FindRoute return the matching entity in
//     Edit mode by InstanceId / RouteId.
//   * FindSource / FindSink / FindRoute return null in Edit mode when no
//     matching entity exists.
//   * FindXxx(null config) throws ArgumentNullException.
//
// Reference: docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md §5.6
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Wizards;

public sealed class EditModeContextTests
{
    [Fact]
    public void Add_YieldsAddModeAndNullExistingId()
    {
        var ctx = EditModeContext.Add();

        ctx.Mode.Should().Be(WizardMode.Add);
        ctx.ExistingInstanceId.Should().BeNull();
        ctx.IsEdit.Should().BeFalse();
    }

    [Fact]
    public void Edit_WithNonEmptyId_YieldsEditModeAndPopulatedId()
    {
        var ctx = EditModeContext.Edit("source-1");

        ctx.Mode.Should().Be(WizardMode.Edit);
        ctx.ExistingInstanceId.Should().Be("source-1");
        ctx.IsEdit.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Edit_WithEmptyOrWhitespaceId_Throws(string id)
    {
        var act = () => EditModeContext.Edit(id);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("existingInstanceId");
    }

    [Fact]
    public void Edit_WithNullId_Throws()
    {
        var act = () => EditModeContext.Edit(null!);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("existingInstanceId");
    }

    [Fact]
    public void FindSource_InAddMode_ReturnsNull()
    {
        var config = SampleConfig();
        var ctx = EditModeContext.Add();

        ctx.FindSource(config).Should().BeNull();
    }

    [Fact]
    public void FindSource_InEditMode_ReturnsMatchingSource()
    {
        var config = SampleConfig();
        var ctx = EditModeContext.Edit("source-A");

        var found = ctx.FindSource(config);
        found.Should().NotBeNull();
        found!.InstanceId.Should().Be("source-A");
    }

    [Fact]
    public void FindSource_InEditMode_NoMatch_ReturnsNull()
    {
        var config = SampleConfig();
        var ctx = EditModeContext.Edit("does-not-exist");

        ctx.FindSource(config).Should().BeNull();
    }

    [Fact]
    public void FindSink_InEditMode_ReturnsMatchingSink()
    {
        var config = SampleConfig();
        var ctx = EditModeContext.Edit("sink-X");

        var found = ctx.FindSink(config);
        found.Should().NotBeNull();
        found!.InstanceId.Should().Be("sink-X");
    }

    [Fact]
    public void FindRoute_InEditMode_ReturnsMatchingRoute_ByRouteId()
    {
        var config = SampleConfig();
        // Note: ExistingInstanceId carries the route id in this context;
        // the field name reflects the Add/Edit concept, not the route shape.
        var ctx = EditModeContext.Edit("route-1");

        var found = ctx.FindRoute(config);
        found.Should().NotBeNull();
        found!.RouteId.Should().Be("route-1");
    }

    [Fact]
    public void FindSource_NullConfig_Throws()
    {
        var ctx = EditModeContext.Add();
        var act = () => ctx.FindSource(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FindSink_NullConfig_Throws()
    {
        var ctx = EditModeContext.Add();
        var act = () => ctx.FindSink(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FindRoute_NullConfig_Throws()
    {
        var ctx = EditModeContext.Add();
        var act = () => ctx.FindRoute(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RecordEquality_TwoContextsWithSameModeAndId_AreEqual()
    {
        var a = EditModeContext.Edit("source-A");
        var b = EditModeContext.Edit("source-A");

        a.Should().Be(b);
    }

    [Fact]
    public void RecordEquality_DifferentMode_AreNotEqual()
    {
        var addCtx = EditModeContext.Add();
        var editCtx = EditModeContext.Edit("source-A");

        addCtx.Should().NotBe(editCtx);
    }

    // ─── BaseVersionId (M.2d.2 §5.5 optimistic-concurrency) ──────────────

    [Fact]
    public void Add_HasNullBaseVersionId()
    {
        var ctx = EditModeContext.Add();
        ctx.BaseVersionId.Should().BeNull();
    }

    [Fact]
    public void Edit_SingleArg_HasNullBaseVersionId()
    {
        var ctx = EditModeContext.Edit("source-A");
        ctx.BaseVersionId.Should().BeNull();
    }

    [Fact]
    public void Edit_WithBaseVersionId_CapturesIt()
    {
        var ctx = EditModeContext.Edit("source-A", "2026-05-22T08-00-00-001Z-042");

        ctx.Mode.Should().Be(WizardMode.Edit);
        ctx.ExistingInstanceId.Should().Be("source-A");
        ctx.BaseVersionId.Should().Be("2026-05-22T08-00-00-001Z-042");
        ctx.IsEdit.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Edit_WithBaseVersionId_EmptyOrWhitespaceInstanceId_Throws(string id)
    {
        var act = () => EditModeContext.Edit(id, "v-123");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("existingInstanceId");
    }

    [Fact]
    public void Edit_WithBaseVersionId_NullInstanceId_Throws()
    {
        var act = () => EditModeContext.Edit(null!, "v-123");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("existingInstanceId");
    }

    [Fact]
    public void RecordEquality_SameIdDifferentBaseVersionId_AreNotEqual()
    {
        // Two edit sessions that captured different base versions are NOT
        // equal — the version token is part of identity for save-time
        // collision detection.
        var a = EditModeContext.Edit("source-A", "v-1");
        var b = EditModeContext.Edit("source-A", "v-2");

        a.Should().NotBe(b);
    }

    [Fact]
    public void RecordEquality_SameIdSameBaseVersionId_AreEqual()
    {
        var a = EditModeContext.Edit("source-A", "v-1");
        var b = EditModeContext.Edit("source-A", "v-1");

        a.Should().Be(b);
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static GatewayConfiguration SampleConfig() => new()
    {
        Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Test" },
        Sources = new[]
        {
            new SourceInstanceConfig
            {
                InstanceId = "source-A",
                ProtocolName = "focas2",
                DeviceId = "device-A",
                DeviceName = "Device A",
            },
            new SourceInstanceConfig
            {
                InstanceId = "source-B",
                ProtocolName = "brother-http",
                DeviceId = "device-B",
                DeviceName = "Device B",
            },
        },
        Sinks = new[]
        {
            new SinkInstanceConfig
            {
                InstanceId = "sink-X",
                ProtocolName = "mqtt",
            },
        },
        Routes = new[]
        {
            new RouteConfig
            {
                RouteId = "route-1",
                Name = "Route 1",
                SourceInstanceId = "source-A",
                SinkInstanceIds = new[] { "sink-X" },
            },
        },
    };
}
