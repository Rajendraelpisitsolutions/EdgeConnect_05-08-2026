// ============================================================================
// Tests: DestinationProtocolPickerModel — pins the tile catalogue rendered
//        by ChooseDestinationProtocol.razor. Same Razor-shell-over-POCO
//        pattern as SourceProtocolPickerModel: testing the model gives
//        bUnit-equivalent regression coverage without a new framework.
//
//        Headline guard: MQTT + OPC UA Server stay Available with correct
//        hrefs; HTTP + TCP stay Pending. Order is operator-facing per UX
//        mockups §2.1 (MQTT first).
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class DestinationProtocolPickerModelTests
{
    [Fact]
    public void Tiles_HasFourEntries_InExpectedOrderAndKeys()
    {
        // Order is operator-facing per UX mockups §2.1 — MQTT first,
        // OPC UA Server second, pending tiles trail.
        var keys = DestinationProtocolPickerModel.Tiles.Select(t => t.Key).ToArray();
        keys.Should().Equal(new[] { "mqtt", "opcua-server", "http-webhook", "tcp-socket" },
            "the tile catalogue is operator-facing layout; new tiles append, never reorder.");
    }

    [Fact]
    public void MqttAndOpcUaServerTiles_AreAvailable_WithExpectedTargetHrefs()
    {
        var mqtt = DestinationProtocolPickerModel.Tiles.Single(t => t.Key == "mqtt");
        mqtt.Status.Should().Be(DestinationProtocolTileStatus.Available);
        mqtt.TargetHref.Should().Be("/destinations/new/mqtt");
        mqtt.PendingMilestone.Should().BeNull();

        var opcua = DestinationProtocolPickerModel.Tiles.Single(t => t.Key == "opcua-server");
        opcua.Status.Should().Be(DestinationProtocolTileStatus.Available);
        opcua.TargetHref.Should().Be("/destinations/new/opcua");
        opcua.PendingMilestone.Should().BeNull();
    }

    [Fact]
    public void HttpAndTcpTiles_RemainPending_WithMilestoneLabel()
    {
        // Inverse regression guard — a pending tile accidentally promoted to
        // Available without its sink adapter would route operators to a
        // wizard that doesn't exist.
        var http = DestinationProtocolPickerModel.Tiles.Single(t => t.Key == "http-webhook");
        http.Status.Should().Be(DestinationProtocolTileStatus.Pending);
        http.TargetHref.Should().BeNull();
        http.PendingMilestone.Should().Be("M.2b.7");

        var tcp = DestinationProtocolPickerModel.Tiles.Single(t => t.Key == "tcp-socket");
        tcp.Status.Should().Be(DestinationProtocolTileStatus.Pending);
        tcp.TargetHref.Should().BeNull();
        tcp.PendingMilestone.Should().Be("M.2b.8");
    }

    [Fact]
    public void Resolve_StarterEdition_OffersOnlyMqtt()
    {
        // Starter offers exactly ONE destination: MQTT. OPC UA Server and the
        // pending HTTP/TCP tiles are all hidden.
        var tiles = DestinationProtocolPickerModel.Resolve(_ => true, LicenseEdition.Starter);

        tiles.Select(t => t.Key).Should().Equal("mqtt");
        tiles.Single().Status.Should().Be(DestinationProtocolTileStatus.Available);
    }

    [Fact]
    public void Resolve_ProfessionalEdition_OffersOnlyMqtt()
    {
        // Professional destinations are MQTT-only (OPC UA Server is Enterprise).
        var tiles = DestinationProtocolPickerModel.Resolve(_ => true, LicenseEdition.Professional);

        tiles.Select(t => t.Key).Should().Equal("mqtt");
        tiles.Single().Status.Should().Be(DestinationProtocolTileStatus.Available);
    }

    [Fact]
    public void Resolve_CncProEdition_OffersOnlyMqtt()
    {
        // CNC Pro destinations are MQTT-only (OPC UA Server is Enterprise).
        var tiles = DestinationProtocolPickerModel.Resolve(_ => true, LicenseEdition.CncPro);

        tiles.Select(t => t.Key).Should().Equal("mqtt");
        tiles.Single().Status.Should().Be(DestinationProtocolTileStatus.Available);
    }

    [Fact]
    public void Resolve_TrialPeriodEdition_OffersMqttAndOpcUaServer()
    {
        // Trial offers every destination; MQTT + OPC UA Server are Available,
        // the HTTP/TCP tiles stay Pending (no adapter yet).
        var tiles = DestinationProtocolPickerModel.Resolve(_ => true, LicenseEdition.TrialPeriod);

        tiles.Single(t => t.Key == "mqtt").Status.Should().Be(DestinationProtocolTileStatus.Available);
        tiles.Single(t => t.Key == "opcua-server").Status.Should().Be(DestinationProtocolTileStatus.Available);
    }

    [Fact]
    public void Resolve_EnterpriseEdition_OffersMqttAndOpcUaServer()
    {
        // Enterprise adds OPC UA Server; the pending HTTP/TCP tiles stay hidden.
        var tiles = DestinationProtocolPickerModel.Resolve(_ => true, LicenseEdition.Enterprise);

        tiles.Select(t => t.Key).Should().Equal("mqtt", "opcua-server");
        tiles.Should().OnlyContain(t => t.Status == DestinationProtocolTileStatus.Available);
    }

    [Fact]
    public void Resolve_EnterpriseEdition_WithOpcUaServerModuleDisabled_LocksThatTile()
    {
        // Offered by the edition, but the license module isn't enabled → locked.
        var tiles = DestinationProtocolPickerModel.Resolve(
            module => module != "sink-opc-ua-server", LicenseEdition.Enterprise);

        tiles.Single(t => t.Key == "mqtt").Status.Should().Be(DestinationProtocolTileStatus.Available);
        tiles.Single(t => t.Key == "opcua-server").Status.Should().Be(DestinationProtocolTileStatus.RequiresLicense);
    }

    [Fact]
    public void Resolve_NullLicense_ReturnsStaticTilesUnchanged()
    {
        DestinationProtocolPickerModel.Resolve(null).Should().BeSameAs(DestinationProtocolPickerModel.Tiles);
    }
}
