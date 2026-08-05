// ============================================================================
// File: Wizards/DestinationProtocolPickerModel.cs
// Purpose: Tile catalogue for the `/destinations/new` protocol picker.
//          Sibling of SourceProtocolPickerModel — same Razor-shell-over-
//          testable-POCO pattern.
//
// MILESTONE TIE-INS:
//   * MQTT tile is Available — sink adapter shipped in M.P1 Phase 2.
//   * OPC UA Server tile is Available — sink adapter shipped in M.P1
//     phase 2. Note: the FULL security schema is wizard-visible per
//     M.2b.6 v3 Locked O, but only None+Anonymous is runtime-active
//     until Milestone K (release prerequisite).
//   * HTTP webhook + TCP socket tiles stay Pending until their sink
//     adapters ship (M.2b.7 / M.2b.8 placeholders per UX mockups §2.1).
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
//            docs/sessions/2026-05-18-mp2b5-mp2b6-ux-mockups.md §2.1
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Licensing;
using MudBlazor;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>Whether a destination tile is wizard-enabled or still pending its milestone.</summary>
public enum DestinationProtocolTileStatus
{
    /// <summary>Clickable; navigates to the wizard at <see cref="DestinationProtocolTile.TargetHref"/>.</summary>
    Available,

    /// <summary>Tooltip-disabled; surfaces the future milestone label only.</summary>
    Pending,

    /// <summary>Locked; the tile's <see cref="DestinationProtocolTile.RequiredLicenseModule"/> is not
    /// enabled. UI is explanatory only — the backend license gate remains authoritative.</summary>
    RequiresLicense,
}

/// <summary>
/// A single tile on the destination-protocol picker page. Mirrors
/// <see cref="SourceProtocolTile"/> in shape and intent; tests pin the
/// catalogue so accidental tile breakage doesn't slip through.
/// </summary>
public sealed record DestinationProtocolTile
{
    /// <summary>Stable identifier (e.g. <c>"mqtt"</c>); matches the sink's protocol-name constant.</summary>
    public required string Key { get; init; }

    /// <summary>Operator-facing label (e.g. <c>"MQTT"</c>).</summary>
    public required string DisplayName { get; init; }

    /// <summary>One-line description rendered beneath the title.</summary>
    public required string Description { get; init; }

    /// <summary>MudBlazor Material icon (an SVG path string).</summary>
    public required string IconSvg { get; init; }

    /// <summary>Whether this tile is wizard-enabled.</summary>
    public required DestinationProtocolTileStatus Status { get; init; }

    /// <summary>Wizard route when <see cref="Status"/> is <see cref="DestinationProtocolTileStatus.Available"/>.</summary>
    public string? TargetHref { get; init; }

    /// <summary>Future milestone label (e.g. <c>"M.2b.7"</c>) when <see cref="Status"/> is <see cref="DestinationProtocolTileStatus.Pending"/>.</summary>
    public string? PendingMilestone { get; init; }

    /// <summary>
    /// License module that gates this tile (e.g. <c>"sink-mqtt"</c>). When set and the module is not
    /// enabled, <see cref="DestinationProtocolPickerModel.Resolve"/> downgrades the tile to
    /// <see cref="DestinationProtocolTileStatus.RequiresLicense"/>. Null tiles are never license-gated in the UI.
    /// </summary>
    public string? RequiredLicenseModule { get; init; }
}

/// <summary>
/// Static catalogue of destination-protocol tiles. Order is operator-facing —
/// changing it changes the picker layout. MQTT first per UX mockups §2.1
/// (more common); OPC UA Server second; pending tiles trail.
/// </summary>
public static class DestinationProtocolPickerModel
{
    /// <summary>The full tile list in display order.</summary>
    public static readonly IReadOnlyList<DestinationProtocolTile> Tiles = new DestinationProtocolTile[]
    {
        new()
        {
            Key = "mqtt",
            DisplayName = "MQTT",
            Description = "Publish to a broker (Mosquitto, HiveMQ, AWS IoT, …).",
            IconSvg = Icons.Material.Filled.Cloud,
            Status = DestinationProtocolTileStatus.Available,
            TargetHref = "/destinations/new/mqtt",
            RequiredLicenseModule = "sink-mqtt",
        },
        new()
        {
            Key = "opcua-server",
            DisplayName = "OPC UA Server",
            Description = "Expose tags as an OPC UA server other tools subscribe to.",
            IconSvg = Icons.Material.Filled.Hub,
            Status = DestinationProtocolTileStatus.Available,
            TargetHref = "/destinations/new/opcua",
            RequiredLicenseModule = "sink-opc-ua-server",
        },
        new()
        {
            Key = "http-webhook",
            DisplayName = "HTTP webhook",
            Description = "POST events to a REST endpoint.",
            IconSvg = Icons.Material.Filled.Http,
            Status = DestinationProtocolTileStatus.Pending,
            PendingMilestone = "M.2b.7",
        },
        new()
        {
            Key = "tcp-socket",
            DisplayName = "TCP socket",
            Description = "Stream bytes to a raw TCP listener.",
            IconSvg = Icons.Material.Filled.SettingsEthernet,
            Status = DestinationProtocolTileStatus.Pending,
            PendingMilestone = "M.2b.8",
        },
    };

    /// <summary>
    /// Resolve the catalogue for a given license. Under an edition that restricts
    /// destinations (Starter / Professional → MQTT only; Enterprise → MQTT +
    /// OPC UA Server) only the offered tiles are returned — every other tile,
    /// including the Pending HTTP/TCP placeholders, is hidden. Of the surviving
    /// tiles, any Available one whose
    /// <see cref="DestinationProtocolTile.RequiredLicenseModule"/> is not enabled is downgraded to
    /// <see cref="DestinationProtocolTileStatus.RequiresLicense"/> (and loses its target href).
    /// A null <paramref name="isModuleEnabled"/> means no license is loaded (dev / permissive,
    /// matching the backend fallback). The UI is explanatory only; the backend gate remains
    /// authoritative on save.
    /// </summary>
    /// <param name="isModuleEnabled">Per-module gate, or null for permissive/dev.</param>
    /// <param name="edition">Current license edition; named editions hide non-offered tiles.</param>
    public static IReadOnlyList<DestinationProtocolTile> Resolve(
        Func<string, bool>? isModuleEnabled,
        LicenseEdition edition = LicenseEdition.Unknown)
    {
        var restricted = LicenseEditionCatalog.RestrictsSinks(edition);
        if (isModuleEnabled is null && !restricted)
        {
            return Tiles;
        }

        var resolved = new List<DestinationProtocolTile>(Tiles.Count);
        foreach (var tile in Tiles)
        {
            // A named edition offers only certain sinks; hide every other tile.
            // Pending tiles (no module) are never offered under a named edition.
            if (restricted
                && !(tile.RequiredLicenseModule is { } offered
                     && LicenseEditionCatalog.IsSinkModuleOffered(edition, offered)))
            {
                continue;
            }

            if (tile.Status == DestinationProtocolTileStatus.Available
                && isModuleEnabled is not null
                && tile.RequiredLicenseModule is { } module
                && !isModuleEnabled(module))
            {
                resolved.Add(tile with { Status = DestinationProtocolTileStatus.RequiresLicense, TargetHref = null });
            }
            else
            {
                resolved.Add(tile);
            }
        }
        return resolved;
    }
}
