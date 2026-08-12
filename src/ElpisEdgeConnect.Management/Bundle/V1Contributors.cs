// ============================================================================
// File: Bundle/V1Contributors.cs
// Purpose: The v1 diagnostic-bundle contributors (plan v2 §2): gateway identity,
//          redacted config, redacted history, audit log, and the route-inventory
//          summary. Each contributes content only — the ArchiveComposer applies
//          redaction and derives the manifest grouping. Fail-closed: a thrown
//          exception fails the whole bundle.
// Reference: docs/sessions/2026-05-31-adr0020-bundle-generation-plan-v2.md §1/§2.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Management.Bundle;

/// <summary>Shared JSON options for contributor-authored documents (camelCase, indented, deterministic).</summary>
internal static class BundleJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>Strip filesystem-unfriendly characters from a version id for a zip-entry name.</summary>
    public static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }
        return sb.ToString();
    }
}

/// <summary>Gateway identity (id + name + site/area labels). Non-secret — verbatim.</summary>
public sealed class GatewayIdentityContributor : IBundleContributor
{
    /// <inheritdoc />
    public string Name => "identity";

    /// <inheritdoc />
    public BundleCapability Capability => BundleCapability.Identity;

    /// <inheritdoc />
    public async Task ContributeAsync(IBundleWriter writer, BundleContext context, CancellationToken ct)
    {
        var gw = context.Configuration.Gateway;
        var identity = new
        {
            gatewayId = context.GatewayId,
            gatewayName = gw.GatewayName,
            site = gw.Site,
            siteId = gw.SiteId,
            area = gw.Area,
            areaId = gw.AreaId,
        };
        await writer.AddVerbatimAsync(
            "gateway-identity.json",
            JsonSerializer.Serialize(identity, BundleJson.Options),
            ct).ConfigureAwait(false);
    }
}

/// <summary>Topology summary — counts + enabled/disabled + route ids. Non-secret — verbatim.</summary>
public sealed class RouteInventoryContributor : IBundleContributor
{
    /// <inheritdoc />
    public string Name => "route-inventory";

    /// <inheritdoc />
    public BundleCapability Capability => BundleCapability.Inventory;

    /// <inheritdoc />
    public async Task ContributeAsync(IBundleWriter writer, BundleContext context, CancellationToken ct)
    {
        var cfg = context.Configuration;
        var enabled = 0;
        var disabled = 0;
        var routeIds = new List<string>(cfg.Routes.Count);
        foreach (var route in cfg.Routes)
        {
            if (route.Enabled)
            {
                enabled++;
            }
            else
            {
                disabled++;
            }
            routeIds.Add(route.RouteId);
        }

        var inventory = new
        {
            sources = cfg.Sources.Count,
            sinks = cfg.Sinks.Count,
            routes = cfg.Routes.Count,
            enabledRoutes = enabled,
            disabledRoutes = disabled,
            routeIds,
        };
        await writer.AddVerbatimAsync(
            "route-inventory.json",
            JsonSerializer.Serialize(inventory, BundleJson.Options),
            ct).ConfigureAwait(false);
    }
}

/// <summary>The active configuration, redacted through the engine.</summary>
public sealed class ConfigContributor : IBundleContributor
{
    /// <inheritdoc />
    public string Name => "config";

    /// <inheritdoc />
    public BundleCapability Capability => BundleCapability.Config;

    /// <inheritdoc />
    public async Task ContributeAsync(IBundleWriter writer, BundleContext context, CancellationToken ct)
    {
        var path = context.Layout.CurrentConfigPath;
        if (!File.Exists(path))
        {
            // Fail-closed: a bundle without the active config is operationally
            // meaningless and would be a silent gap.
            throw new InvalidOperationException(
                $"Cannot build a diagnostic bundle: no active configuration at '{path}'.");
        }

        var raw = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        await writer.AddRedactedJsonAsync("gateway.json", raw, ct).ConfigureAwait(false);
    }
}

/// <summary>Configuration history versions, each redacted through the engine.</summary>
public sealed class HistoryContributor : IBundleContributor
{
    /// <inheritdoc />
    public string Name => "history";

    /// <inheritdoc />
    public BundleCapability Capability => BundleCapability.History;

    /// <inheritdoc />
    public async Task ContributeAsync(IBundleWriter writer, BundleContext context, CancellationToken ct)
    {
        var history = await context.ConfigManager.GetHistoryAsync(ct).ConfigureAwait(false);
        for (var i = 0; i < history.Count; i++)
        {
            var entry = history[i];
            var historyPath = context.Layout.HistoryPath(entry.VersionId);
            if (!File.Exists(historyPath))
            {
                // Not a redaction failure — a referenced file is missing on disk.
                // Note it (visible in the manifest's EXCLUDE group) rather than
                // failing the whole bundle.
                writer.AddExcludedNote($"history/{entry.VersionId.Value}", "version file missing on disk");
                continue;
            }

            var raw = await File.ReadAllTextAsync(historyPath, ct).ConfigureAwait(false);
            var entryName = $"history/{(i + 1):D4}-{BundleJson.Sanitize(entry.VersionId.Value)}.json";
            await writer.AddRedactedJsonAsync(entryName, raw, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>The audit log, newline-delimited JSON. Ships verbatim (see BackupBuilder note on safety).</summary>
public sealed class AuditContributor : IBundleContributor
{
    /// <inheritdoc />
    public string Name => "audit";

    /// <inheritdoc />
    public BundleCapability Capability => BundleCapability.Audit;

    /// <inheritdoc />
    public async Task ContributeAsync(IBundleWriter writer, BundleContext context, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var entry in context.ConfigManager.GetAuditLogAsync(verifyChain: false, ct).ConfigureAwait(false))
        {
            sb.Append(JsonSerializer.Serialize(entry)).Append('\n');
        }
        await writer.AddVerbatimAsync("audit.log", sb.ToString(), ct).ConfigureAwait(false);
    }
}
