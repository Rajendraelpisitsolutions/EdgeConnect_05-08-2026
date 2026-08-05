// ============================================================================
// File: Api/BulkSourceMerge/TemplateRegistry.cs
// Purpose: Static per-protocol template + placeholder spec catalog driving
//          BulkSourceMergeService's per-row substitution.
//
//          The Studio bulk-import wizard merges N new sources into the CURRENT
//          gateway config (v3 architecture amendment) — it does NOT generate
//          whole-gateway JSON files like the offline `tools/bulk-provision/`
//          generator. So these service-internal templates are narrower than
//          the offline templates: just the per-source body and the per-route
//          body, with placeholder sets shifted accordingly.
//
//          Per v3.1 §4, the wizard's RouteId convention is `route-{deviceId}`
//          (deviceId-derived, stable across re-runs) — differs from the
//          offline generator's `route-{protocol}-{gatewayProvisioningId}`
//          (provisioning-run-scoped). The route template here reflects the
//          wizard's convention.
//
//          For MTConnect the operator-supplied column is `baseUrl` (full
//          Agent URL), not `host` — per templates/MANIFEST.md "Offline-vs-
//          Studio CSV column convention" section.
//
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md §1, §2, §4, §5, §8
//            tools/bulk-provision/templates/MANIFEST.md (offline-vs-Studio convention)
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;

namespace ElpisEdgeConnect.Management.Api.BulkSourceMerge;

/// <summary>
/// One per-protocol entry. Carries the source template + placeholder spec,
/// the (protocol-agnostic) route template + placeholder spec, the required
/// CSV column shape per v3.1 §8, and the CSV column name that supplies the
/// connection address (differs for MTConnect — see file header).
/// </summary>
public sealed record TemplateRegistryEntry
{
    /// <summary>Protocol id this entry applies to.</summary>
    public required BulkSourceMergeProtocol Protocol { get; init; }

    /// <summary>
    /// Canonical wire protocol name written into <c>SourceInstanceConfig.ProtocolName</c>.
    /// Must match the adapter's declared <c>ISourceAdapter.ProtocolName</c>.
    /// </summary>
    public required string ProtocolName { get; init; }

    /// <summary>
    /// Per-row source body template containing <c>{{ name }}</c> placeholders.
    /// After substitution it deserializes into <c>Core.Configuration.SourceInstanceConfig</c>.
    /// </summary>
    public required string SourceTemplate { get; init; }

    /// <summary>
    /// Placeholders the source template requires. Drives <see cref="TemplateSubstitutionEngine"/>.
    /// </summary>
    public required IReadOnlyList<PlaceholderSpec> SourcePlaceholders { get; init; }

    /// <summary>
    /// Per-row route body template containing <c>{{ name }}</c> placeholders.
    /// After substitution it deserializes into <c>Core.Configuration.RouteConfig</c>.
    /// </summary>
    public required string RouteTemplate { get; init; }

    /// <summary>
    /// Placeholders the route template requires. The route template is protocol-
    /// agnostic, but each entry carries its own spec list so the engine has a
    /// single registry to query per direction.
    /// </summary>
    public required IReadOnlyList<PlaceholderSpec> RoutePlaceholders { get; init; }

    /// <summary>
    /// Required CSV column shape. Per v3.1 §8 strict required-column validation
    /// rejects extras AND missing. Order in this list is the expected header
    /// order so the wizard can show operators the canonical layout.
    /// </summary>
    public required IReadOnlyList<string> RequiredCsvColumns { get; init; }

    /// <summary>
    /// CSV column name carrying the connection address — usually <c>host</c>,
    /// but <c>baseUrl</c> for MTConnect per Studio convention.
    /// </summary>
    public required string AddressColumnName { get; init; }

    /// <summary>
    /// Placeholder name in the source template that receives the address value.
    /// Matches the placeholder in <see cref="SourcePlaceholders"/>.
    /// </summary>
    public required string AddressPlaceholderName { get; init; }
}

/// <summary>
/// Compile-time per-protocol registry. Templates are embedded as raw string
/// literals so the assembly is self-contained — no template file IO at
/// runtime — and operator-readable by a maintainer alongside the spec.
/// </summary>
public static class TemplateRegistry
{
    // ── Shared route template (protocol-agnostic) ─────────────────────────────
    // Buffer + Delivery values mirror the offline generator's defaults so
    // wizard-created routes match offline-generated routes byte-for-byte
    // modulo the placeholder slots filled per row.
    private const string SharedRouteTemplate = """
{
  "RouteId": "{{ routeId }}",
  "Name": "{{ routeName }}",
  "SourceInstanceId": "{{ instanceId }}",
  "SinkInstanceIds": [ "{{ sinkInstanceId }}" ],
  "Enabled": true,
  "Buffer": {
    "Mode": "StoreAndForward",
    "MaxDepth": 100000,
    "MaxAgeDays": 7,
    "OnOverflow": "DropOldest"
  },
  "Delivery": {
    "Mode": "AtLeastOnce",
    "MaxRetries": 100,
    "InitialBackoffMs": 100,
    "MaxBackoffMs": 30000,
    "BackoffMultiplier": 2.0,
    "JitterPercent": 10
  }
}
""";

    private static readonly IReadOnlyList<PlaceholderSpec> SharedRoutePlaceholders = new[]
    {
        new PlaceholderSpec { Name = "routeId",        Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "routeName",      Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "instanceId",     Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "sinkInstanceId", Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
    };

    // ── Source placeholder specs ──────────────────────────────────────────────
    // Three of the four protocols share the same Source placeholder shape
    // (host-as-IP). MTConnect uses baseUrl. The 'enabled' raw-position
    // placeholder appears once per source body.
    private static readonly IReadOnlyList<PlaceholderSpec> HostBasedSourcePlaceholders = new[]
    {
        new PlaceholderSpec { Name = "instanceId", Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "deviceId",   Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "deviceName", Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "enabled",    Position = PlaceholderPosition.RawBoolean,  ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "host",       Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
    };

    private static readonly IReadOnlyList<PlaceholderSpec> MtConnectSourcePlaceholders = new[]
    {
        new PlaceholderSpec { Name = "instanceId", Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "deviceId",   Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "deviceName", Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "enabled",    Position = PlaceholderPosition.RawBoolean,  ExpectedOccurrences = 1 },
        new PlaceholderSpec { Name = "baseUrl",    Position = PlaceholderPosition.StringValue, ExpectedOccurrences = 1 },
    };

    // ── FOCAS2 source template ────────────────────────────────────────────────
    // dataPoints matches the customer 10-path production baseline (PR I-0).
    private const string Focas2SourceTemplate = """
{
  "InstanceId": "{{ instanceId }}",
  "ProtocolName": "focas2",
  "DeviceId": "{{ deviceId }}",
  "DeviceName": "{{ deviceName }}",
  "DeviceClass": "cnc",
  "Enabled": {{ enabled }},
  "Polling": {
    "IntervalMs": 3000
  },
  "Connection": {
    "ipAddress": "{{ host }}",
    "port": 8193,
    "timeoutSeconds": 10,
    "keepAlive": true,
    "initialBackoffMs": 5000,
    "maxBackoffMs": 120000,
    "backoffMultiplier": 2.0,
    "maxConnectRetries": 5,
    "dataPoints": [
      "Program/MainProgram",
      "Status/RunState",
      "Axes/Position/Absolute",
      "Axes/Position/Machine",
      "Axes/FeedRate",
      "Spindle/Speed",
      "Spindle/Load",
      "Alarms/Active",
      "CycleTime",
      "PartsCount"
    ]
  }
}
""";

    // ── Brother HTTP source template ──────────────────────────────────────────
    // Template prepends "http://" so the operator-supplied host is just the
    // IP / hostname (mirrors the offline template). dataPoints baseline
    // matches the offline brother template's prefix-based bucket list (see
    // brother template-v1 file under tools/bulk-provision/templates/ — kept
    // byte-equivalent so the wizard and offline generator produce identical
    // sources).
    private const string BrotherHttpSourceTemplate = """
{
  "InstanceId": "{{ instanceId }}",
  "ProtocolName": "brother-http",
  "DeviceId": "{{ deviceId }}",
  "DeviceName": "{{ deviceName }}",
  "DeviceClass": "cnc",
  "Enabled": {{ enabled }},
  "Polling": {
    "IntervalMs": 3000
  },
  "Connection": {
    "baseUrl": "http://{{ host }}",
    "timeoutSeconds": 10,
    "initialBackoffMs": 5000,
    "maxBackoffMs": 120000,
    "backoffMultiplier": 2.0,
    "maxConnectRetries": 5,
    "dataPoints": [
      "MachineInfo/",
      "Status/",
      "Program/",
      "CycleTime/",
      "Production/",
      "Alarms/",
      "Maintenance/",
      "Tools/"
    ]
  }
}
""";

    // ── Modbus TCP source template ────────────────────────────────────────────
    // Per-tag definitions are operator-driven (see chip-3 v2 §1.3 + the
    // separate per-tag CSV importer); Connection.tags starts empty here just
    // like the offline template.
    private const string ModbusTcpSourceTemplate = """
{
  "InstanceId": "{{ instanceId }}",
  "ProtocolName": "modbus-tcp",
  "DeviceId": "{{ deviceId }}",
  "DeviceName": "{{ deviceName }}",
  "DeviceClass": "plc",
  "Enabled": {{ enabled }},
  "Polling": {
    "IntervalMs": 1000
  },
  "Connection": {
    "host": "{{ host }}",
    "port": 502,
    "unitId": 1,
    "timeoutMs": 5000,
    "initialBackoffMs": 5000,
    "maxBackoffMs": 120000,
    "backoffMultiplier": 2.0,
    "maxConnectRetries": 5,
    "tags": []
  }
}
""";

    // ── MTConnect source template ─────────────────────────────────────────────
    // dataPoints matches the customer 10-path production baseline (same as
    // FOCAS2 — adapter parity per MTConnectSemanticMap.cs:7).
    private const string MtConnectSourceTemplate = """
{
  "InstanceId": "{{ instanceId }}",
  "ProtocolName": "mtconnect",
  "DeviceId": "{{ deviceId }}",
  "DeviceName": "{{ deviceName }}",
  "DeviceClass": "cnc",
  "Enabled": {{ enabled }},
  "Polling": {
    "IntervalMs": 3000
  },
  "Connection": {
    "agentBaseUrl": "{{ baseUrl }}",
    "timeoutSeconds": 10,
    "initialBackoffMs": 2000,
    "maxBackoffMs": 60000,
    "backoffMultiplier": 2.0,
    "degradeAfterConsecutiveFailures": 3,
    "dataPoints": [
      "Program/MainProgram",
      "Status/RunState",
      "Axes/Position/Absolute",
      "Axes/Position/Machine",
      "Axes/FeedRate",
      "Spindle/Speed",
      "Spindle/Load",
      "Alarms/Active",
      "CycleTime",
      "PartsCount"
    ]
  }
}
""";

    private static readonly Dictionary<BulkSourceMergeProtocol, TemplateRegistryEntry> _entries =
        new Dictionary<BulkSourceMergeProtocol, TemplateRegistryEntry>
        {
            [BulkSourceMergeProtocol.Focas2] = new TemplateRegistryEntry
            {
                Protocol = BulkSourceMergeProtocol.Focas2,
                ProtocolName = "focas2",
                SourceTemplate = Focas2SourceTemplate,
                SourcePlaceholders = HostBasedSourcePlaceholders,
                RouteTemplate = SharedRouteTemplate,
                RoutePlaceholders = SharedRoutePlaceholders,
                RequiredCsvColumns = new[] { "deviceId", "deviceName", "host", "enabled" },
                AddressColumnName = "host",
                AddressPlaceholderName = "host",
            },
            [BulkSourceMergeProtocol.BrotherHttp] = new TemplateRegistryEntry
            {
                Protocol = BulkSourceMergeProtocol.BrotherHttp,
                ProtocolName = "brother-http",
                SourceTemplate = BrotherHttpSourceTemplate,
                SourcePlaceholders = HostBasedSourcePlaceholders,
                RouteTemplate = SharedRouteTemplate,
                RoutePlaceholders = SharedRoutePlaceholders,
                RequiredCsvColumns = new[] { "deviceId", "deviceName", "host", "enabled" },
                AddressColumnName = "host",
                AddressPlaceholderName = "host",
            },
            [BulkSourceMergeProtocol.ModbusTcp] = new TemplateRegistryEntry
            {
                Protocol = BulkSourceMergeProtocol.ModbusTcp,
                ProtocolName = "modbus-tcp",
                SourceTemplate = ModbusTcpSourceTemplate,
                SourcePlaceholders = HostBasedSourcePlaceholders,
                RouteTemplate = SharedRouteTemplate,
                RoutePlaceholders = SharedRoutePlaceholders,
                RequiredCsvColumns = new[] { "deviceId", "deviceName", "host", "enabled" },
                AddressColumnName = "host",
                AddressPlaceholderName = "host",
            },
            [BulkSourceMergeProtocol.Mtconnect] = new TemplateRegistryEntry
            {
                Protocol = BulkSourceMergeProtocol.Mtconnect,
                ProtocolName = "mtconnect",
                SourceTemplate = MtConnectSourceTemplate,
                SourcePlaceholders = MtConnectSourcePlaceholders,
                RouteTemplate = SharedRouteTemplate,
                RoutePlaceholders = SharedRoutePlaceholders,
                RequiredCsvColumns = new[] { "deviceId", "deviceName", "baseUrl", "enabled" },
                AddressColumnName = "baseUrl",
                AddressPlaceholderName = "baseUrl",
            },
        };

    /// <summary>Lookup map keyed by protocol id.</summary>
    public static IReadOnlyDictionary<BulkSourceMergeProtocol, TemplateRegistryEntry> Entries => _entries;

    /// <summary>
    /// Resolve the registry entry for <paramref name="protocol"/>. Throws
    /// when the protocol is unknown (defensive — the DTO layer's enum
    /// validation should have caught it first).
    /// </summary>
    public static TemplateRegistryEntry Get(BulkSourceMergeProtocol protocol)
    {
        if (_entries.TryGetValue(protocol, out var entry))
        {
            return entry;
        }
        throw new ArgumentOutOfRangeException(
            nameof(protocol),
            protocol,
            $"Unknown bulk-source-merge protocol id: {protocol}. Update TemplateRegistry when adding a new protocol.");
    }
}
