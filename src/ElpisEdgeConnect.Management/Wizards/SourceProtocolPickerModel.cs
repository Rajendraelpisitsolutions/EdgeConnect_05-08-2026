// ============================================================================
// File: Wizards/SourceProtocolPickerModel.cs
// Purpose: Tile catalogue for the `/sources/new` protocol picker. Extracted
//          from the Razor component so the "which tiles are clickable" state
//          can be pinned by unit tests without standing up bUnit. Mirrors
//          the pattern established by ReloadOutcomePanelModel — the Razor
//          becomes a thin shell over a testable POCO.
//
// MILESTONE TIE-INS:
//   * Modbus tile flipped to Available in M.2b.1.
//   * FOCAS2 tile flipped to Available in M.2b.3 (this milestone).
//   * S7 tile stays Pending until M.2b.2.
//   * MTConnect tile flipped to Available in M.2b.4.
// Reference: docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md §6.3, §8.3 (Locked R)
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Licensing;
using MudBlazor;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>Whether a protocol tile is wizard-enabled, pending its milestone, or license-locked.</summary>
public enum SourceProtocolTileStatus
{
    /// <summary>Clickable; navigates to the wizard at <see cref="SourceProtocolTile.TargetHref"/>.</summary>
    Available,

    /// <summary>Tooltip-disabled; surfaces the future milestone label only.</summary>
    Pending,

    /// <summary>Locked; the tile's <see cref="SourceProtocolTile.RequiredLicenseModule"/> is not enabled.
    /// UI is explanatory only — the backend license gate remains authoritative.</summary>
    RequiresLicense,
}

/// <summary>
/// A single tile on the source-protocol picker page. The Razor renders
/// directly from this record; tests pin the catalogue to guard against
/// accidental tile breakage.
/// </summary>
public sealed record SourceProtocolTile
{
    /// <summary>Stable identifier (e.g. <c>"focas2"</c>); matches the protocol-name constant.</summary>
    public required string Key { get; init; }

    /// <summary>Operator-facing label (e.g. <c>"FANUC FOCAS2"</c>).</summary>
    public required string DisplayName { get; init; }

    /// <summary>One-line description rendered beneath the title.</summary>
    public required string Description { get; init; }

    /// <summary>MudBlazor Material icon (an SVG path string like <c>Icons.Material.Filled.PrecisionManufacturing</c>).</summary>
    public required string IconSvg { get; init; }

    /// <summary>Whether this tile is wizard-enabled.</summary>
    public required SourceProtocolTileStatus Status { get; init; }

    /// <summary>Wizard route when <see cref="Status"/> is <see cref="SourceProtocolTileStatus.Available"/>.</summary>
    public string? TargetHref { get; init; }

    /// <summary>Future milestone label (e.g. <c>"M.2b.2"</c>) when <see cref="Status"/> is <see cref="SourceProtocolTileStatus.Pending"/>.</summary>
    public string? PendingMilestone { get; init; }

    /// <summary>
    /// License module that gates this tile (e.g. <c>"source-melsec"</c>). When set and the module is
    /// not enabled, <see cref="SourceProtocolPickerModel.Resolve"/> downgrades the tile to
    /// <see cref="SourceProtocolTileStatus.RequiresLicense"/>. Null tiles are never license-gated in the UI.
    /// </summary>
    public string? RequiredLicenseModule { get; init; }
}

/// <summary>
/// Static catalogue of source-protocol tiles. Order is operator-facing —
/// changing it changes the picker layout.
/// </summary>
public static class SourceProtocolPickerModel
{
    /// <summary>The full tile list in display order.</summary>
    public static readonly IReadOnlyList<SourceProtocolTile> Tiles = new SourceProtocolTile[]
    {
        new()
        {
            Key = "modbus",
            DisplayName = "Modbus TCP",
            Description = "Connect to PLCs, energy meters, and DAQ over native Modbus TCP.",
            IconSvg = Icons.Material.Filled.Memory,
            Status = SourceProtocolTileStatus.Available,
            TargetHref = "/sources/new/modbus",
            RequiredLicenseModule = "source-modbus-tcp",
        },
        new()
        {
            Key = "modbus-rtu",
            DisplayName = "Modbus RTU",
            Description = "Connect to serial RTU devices (RS-485 / RS-232) or RTU-over-TCP gateways.",
            IconSvg = Icons.Material.Filled.SettingsInputComponent,
            Status = SourceProtocolTileStatus.Available,
            TargetHref = "/sources/new/modbus-rtu",
            RequiredLicenseModule = "source-modbus-tcp",
        },
        new()
        {
            Key = "focas2",
            DisplayName = "FANUC FOCAS2",
            Description = "FANUC CNCs via the FOCAS2 library.",
            IconSvg = Icons.Material.Filled.PrecisionManufacturing,
            Status = SourceProtocolTileStatus.Available,
            TargetHref = "/sources/new/focas2",
            RequiredLicenseModule = "source-focas2",
        },
        new()
        {
            Key = "brother-http",
            DisplayName = "Brother HTTP",
            Description = "Brother Speedio and other Brother CNCs via the built-in port-80 web-monitoring interface.",
            IconSvg = Icons.Material.Filled.Build,
            Status = SourceProtocolTileStatus.Available,
            TargetHref = "/sources/new/brother-http",
            RequiredLicenseModule = "source-brother-http",
        },
        new()
        {
            Key = "opcua-client",
            DisplayName = "OPC UA Client",
            Description = "FactoryTalk SCADA, Kepware, vendor PLCs, and other OPC UA servers via subscription-mode reads.",
            IconSvg = Icons.Material.Filled.Hub,
            Status = SourceProtocolTileStatus.Available,
            TargetHref = "/sources/new/opcua-client",
            RequiredLicenseModule = "source-opcua-client",
        },
        new()
        {
            Key = "s7",
            DisplayName = "Siemens S7",
            Description = "S7-300, S7-400, S7-1200, S7-1500.",
            IconSvg = Icons.Material.Filled.DeveloperBoard,
            Status = SourceProtocolTileStatus.Available,
            TargetHref = "/sources/new/s7",
            RequiredLicenseModule = "source-s7",
        },
        new()
        {
            Key = "mtconnect",
            DisplayName = "MTConnect",
            Description = "MTConnect agents over HTTP.",
            IconSvg = Icons.Material.Filled.Cable,
            Status = SourceProtocolTileStatus.Available,
            TargetHref = "/sources/new/mtconnect",
            RequiredLicenseModule = "source-mtconnect",
        },
        new()
        {
            Key = "ethernet-ip",
            DisplayName = "EtherNet/IP",
            Description = "Allen-Bradley ControlLogix / CompactLogix / GuardLogix / MicroLogix / Micro800 via EtherNet/IP.",
            IconSvg = Icons.Material.Filled.Lan,
            Status = SourceProtocolTileStatus.Available,
            TargetHref = "/sources/new/ethernet-ip",
            RequiredLicenseModule = "source-ethernet-ip",
        },
        new()
        {
            Key = "melsec",
            DisplayName = "Mitsubishi MELSEC",
            Description = "iQ-R / iQ-F / Q / L via SLMP / MC 3E binary (TCP).",
            IconSvg = Icons.Material.Filled.DeveloperBoard,
            Status = SourceProtocolTileStatus.Available,
            TargetHref = "/sources/new/melsec",
            RequiredLicenseModule = LicenseModuleKeys.SourceMelsec,
        },
    };

    /// <summary>
    /// Source tiles OFFERED under a Starter license — every other tile is hidden
    /// entirely (not merely locked). Mirrors
    /// <see cref="LicenseEditionCatalog.StarterSourceModuleKey"/> (Modbus TCP);
    /// the <c>modbus-rtu</c> tile shares that module but is a distinct variant
    /// Starter does not surface, so only <c>modbus</c> is listed here.
    /// </summary>
    private static readonly HashSet<string> StarterSourceTileKeys =
        new(StringComparer.Ordinal) { "modbus" };

    /// <summary>
    /// Resolve the catalogue for a given license. Under a restricted edition
    /// (Starter) only the offered source tiles are returned — all others are
    /// hidden. Of the remaining tiles, any whose
    /// <see cref="SourceProtocolTile.RequiredLicenseModule"/> is not enabled are
    /// downgraded to <see cref="SourceProtocolTileStatus.RequiresLicense"/> (and
    /// lose their target href). A null <paramref name="isModuleEnabled"/> means no
    /// license is loaded (dev / permissive, matching the backend fallback). The
    /// UI is explanatory only; the backend gate remains authoritative on save.
    /// </summary>
    /// <param name="isModuleEnabled">Per-module gate, or null for permissive/dev.</param>
    /// <param name="edition">Current license edition; Starter hides non-offered tiles.</param>
    public static IReadOnlyList<SourceProtocolTile> Resolve(
        Func<string, bool>? isModuleEnabled,
        LicenseEdition edition = LicenseEdition.Unknown)
    {
        var restricted = LicenseEditionCatalog.RestrictsSources(edition);
        if (isModuleEnabled is null && !restricted)
        {
            return Tiles;
        }

        var resolved = new List<SourceProtocolTile>(Tiles.Count);
        foreach (var tile in Tiles)
        {
            // Starter: hide every source tile except the ones it offers.
            if (restricted && !StarterSourceTileKeys.Contains(tile.Key))
            {
                continue;
            }

            if (isModuleEnabled is not null
                && tile.RequiredLicenseModule is { } module && !isModuleEnabled(module))
            {
                resolved.Add(tile with { Status = SourceProtocolTileStatus.RequiresLicense, TargetHref = null });
            }
            else
            {
                resolved.Add(tile);
            }
        }
        return resolved;
    }
}
