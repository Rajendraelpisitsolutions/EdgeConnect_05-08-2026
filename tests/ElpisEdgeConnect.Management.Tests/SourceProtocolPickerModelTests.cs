// ============================================================================
// Tests: SourceProtocolPickerModel — pins the tile catalogue rendered by
//        ChooseSourceProtocol.razor. The Razor is a thin shell over this
//        POCO (mirrors ReloadOutcomePanelModel pattern), so unit-testing
//        the model gives bUnit-equivalent regression coverage without a
//        new test framework dependency.
//
//        Locked R (M.2b.3 plan v3): "Catches accidental 'broke the tile'
//        regressions" — the headline guard is that Modbus + FOCAS2 stay
//        Available with correct hrefs. (MTConnect went Available in M.2b.4;
//        S7 in M.2b.2/M4 — every tile is now Available.)
// Reference: docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md §8.3
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class SourceProtocolPickerModelTests
{
    [Fact]
    public void Tiles_HasExpectedEntries_InExpectedOrderAndKeys()
    {
        // Order is operator-facing — pin it. M.P2.4 added the brother-http tile
        // between focas2 and s7 (grouping CNC protocols visually). PR 7c-4
        // (multi-protocol pilot Week 1) added opcua-client after brother-http;
        // the EtherNet/IP MVP slice appended ethernet-ip after mtconnect;
        // the MELSEC UI slice appended melsec after ethernet-ip.
        var keys = SourceProtocolPickerModel.Tiles.Select(t => t.Key).ToArray();
        // ADR-0033 split Modbus into TCP + RTU tiles (modbus-rtu after modbus).
        keys.Should().Equal(new[] { "modbus", "modbus-rtu", "focas2", "brother-http", "opcua-client", "s7", "mtconnect", "ethernet-ip", "melsec" },
            "the tile catalogue is operator-facing layout; new tiles append, never reorder.");
    }

    [Fact]
    public void EthernetIpTile_IsAvailable_WithExpectedTargetHref()
    {
        var eip = SourceProtocolPickerModel.Tiles.Single(t => t.Key == "ethernet-ip");
        eip.Status.Should().Be(SourceProtocolTileStatus.Available);
        eip.TargetHref.Should().Be("/sources/new/ethernet-ip");
        eip.PendingMilestone.Should().BeNull();
    }

    [Fact]
    public void BrotherHttpTile_IsAvailable_WithExpectedTargetHref()
    {
        // M.P2.4 step 11 — Brother HTTP tile flipped to Available alongside
        // the new wizard at /sources/new/brother-http.
        var brother = SourceProtocolPickerModel.Tiles.Single(t => t.Key == "brother-http");
        brother.Status.Should().Be(SourceProtocolTileStatus.Available);
        brother.TargetHref.Should().Be("/sources/new/brother-http");
        brother.PendingMilestone.Should().BeNull();
    }

    [Fact]
    public void ModbusAndFocas2Tiles_AreAvailable_WithExpectedTargetHrefs()
    {
        // Headline test for Locked R — the regression case that motivated
        // pulling the catalogue into a POCO. If either tile slips back to
        // Pending or loses its target href, this fails loudly.
        var modbus = SourceProtocolPickerModel.Tiles.Single(t => t.Key == "modbus");
        modbus.Status.Should().Be(SourceProtocolTileStatus.Available);
        modbus.TargetHref.Should().Be("/sources/new/modbus");
        modbus.DisplayName.Should().Be("Modbus TCP");
        modbus.PendingMilestone.Should().BeNull();

        // ADR-0033 — Modbus RTU is its own tile/wizard route.
        var modbusRtu = SourceProtocolPickerModel.Tiles.Single(t => t.Key == "modbus-rtu");
        modbusRtu.Status.Should().Be(SourceProtocolTileStatus.Available);
        modbusRtu.TargetHref.Should().Be("/sources/new/modbus-rtu");
        modbusRtu.DisplayName.Should().Be("Modbus RTU");
        modbusRtu.PendingMilestone.Should().BeNull();

        var focas2 = SourceProtocolPickerModel.Tiles.Single(t => t.Key == "focas2");
        focas2.Status.Should().Be(SourceProtocolTileStatus.Available);
        focas2.TargetHref.Should().Be("/sources/new/focas2");
        focas2.PendingMilestone.Should().BeNull();
    }

    [Fact]
    public void MTConnectTile_IsAvailable_WithExpectedTargetHref()
    {
        // M.2b.4 — MTConnect flipped to Available once its wizard shipped.
        var mtconnect = SourceProtocolPickerModel.Tiles.Single(t => t.Key == "mtconnect");
        mtconnect.Status.Should().Be(SourceProtocolTileStatus.Available);
        mtconnect.TargetHref.Should().Be("/sources/new/mtconnect");
        mtconnect.PendingMilestone.Should().BeNull();
    }

    [Fact]
    public void S7Tile_IsAvailable_WithExpectedTargetHref()
    {
        // M.2b.2 / M4 — S7 flipped to Available once its manual tag-editor
        // wizard shipped at /sources/new/s7 (M3) and the required non-hardware
        // verification path was defined (plan v2 §M5).
        var s7 = SourceProtocolPickerModel.Tiles.Single(t => t.Key == "s7");
        s7.Status.Should().Be(SourceProtocolTileStatus.Available);
        s7.TargetHref.Should().Be("/sources/new/s7");
        s7.PendingMilestone.Should().BeNull();
    }

    [Fact]
    public void MelsecTile_IsAvailableByDefault_WithHrefAndLicenseModule()
    {
        var melsec = SourceProtocolPickerModel.Tiles.Single(t => t.Key == "melsec");
        melsec.Status.Should().Be(SourceProtocolTileStatus.Available);
        melsec.TargetHref.Should().Be("/sources/new/melsec");
        melsec.RequiredLicenseModule.Should().Be("source-melsec");
    }

    [Fact]
    public void Resolve_WhenMelsecModuleDisabled_TileIsRequiresLicense_OthersUnaffected()
    {
        var tiles = SourceProtocolPickerModel.Resolve(module => module != "source-melsec");

        var melsec = tiles.Single(t => t.Key == "melsec");
        melsec.Status.Should().Be(SourceProtocolTileStatus.RequiresLicense);
        melsec.TargetHref.Should().BeNull();
        tiles.Single(t => t.Key == "s7").Status.Should().Be(SourceProtocolTileStatus.Available);
    }

    [Fact]
    public void Resolve_WhenMelsecModuleEnabled_TileIsAvailable()
    {
        var tiles = SourceProtocolPickerModel.Resolve(_ => true);
        tiles.Single(t => t.Key == "melsec").Status.Should().Be(SourceProtocolTileStatus.Available);
    }

    [Fact]
    public void Resolve_NullLicense_ReturnsStaticTilesUnchanged()
    {
        SourceProtocolPickerModel.Resolve(null).Should().BeSameAs(SourceProtocolPickerModel.Tiles);
    }

    [Fact]
    public void Resolve_StarterEdition_OffersOnlyModbusTcp()
    {
        // Starter offers exactly ONE source: Modbus TCP. Every other tile —
        // including Modbus RTU (which shares the module) — is hidden entirely.
        var tiles = SourceProtocolPickerModel.Resolve(_ => true, LicenseEdition.Starter);

        tiles.Select(t => t.Key).Should().Equal("modbus");
        tiles.Single().Status.Should().Be(SourceProtocolTileStatus.Available);
    }

    [Fact]
    public void Resolve_StarterEdition_HidesModbusRtuAndAllOthers()
    {
        var tiles = SourceProtocolPickerModel.Resolve(_ => true, LicenseEdition.Starter);

        tiles.Should().NotContain(t => t.Key == "modbus-rtu");
        tiles.Should().NotContain(t => t.Key == "focas2");
        tiles.Should().NotContain(t => t.Key == "melsec");
    }

    [Fact]
    public void Resolve_ProfessionalEdition_OffersFullCatalogue()
    {
        var tiles = SourceProtocolPickerModel.Resolve(_ => true, LicenseEdition.Professional);
        tiles.Should().HaveCount(SourceProtocolPickerModel.Tiles.Count);
    }

    [Fact]
    public void Resolve_StarterEdition_NullGate_StillHidesNonOfferedTiles()
    {
        // Even with a permissive (null) gate, a Starter edition must restrict.
        var tiles = SourceProtocolPickerModel.Resolve(null, LicenseEdition.Starter);
        tiles.Select(t => t.Key).Should().Equal("modbus");
    }
}
