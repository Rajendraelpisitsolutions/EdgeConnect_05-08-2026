// ============================================================================
// File: OpcUaServerConfiguration.cs
// Purpose: Top-level OPC UA Server sink configuration. The forward-compat
//          schema lands in Milestone H.0; the adapter implementation
//          that consumes it lands in H.1; security enforcement of the
//          full schema lands in K.
//
//          Critical design property: this schema is STABLE across H, K,
//          and beyond. Customers can author production configs today
//          (forward-compatibly referencing Sign+Encrypt + Username) and
//          those configs will simply start working when K ships, without
//          a schema migration.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H
//            shared-knowledge/contracts/opcua-namespace-policy.md
//            docs/licensing/module-catalog.md (sink-opc-ua-server)
// ============================================================================

using System;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Configuration record for an OPC UA Server sink instance. Derives from
/// <see cref="SinkConfiguration"/> to participate in the Core configuration
/// system. The namespace block (<see cref="Namespace"/>) and security
/// block (<see cref="Security"/>) are stable forward-compatible schemas
/// — operator-authored configs valid today will remain valid when
/// Milestone K's full security implementation lands.
/// </summary>
public sealed record OpcUaServerConfiguration : SinkConfiguration
{
    /// <summary>The protocol-name constant carried in <c>SinkInstanceConfig.ProtocolName</c>.</summary>
    public const string ProtocolNameConstant = "opcua-server";

    /// <summary>
    /// License module key that gates registration of this sink. Mirrors
    /// <c>LicenseModuleKeys.SinkOpcUaServer</c>; see
    /// <c>docs/licensing/module-catalog.md</c>.
    /// </summary>
    public const string LicenseModuleKey = "sink-opc-ua-server";

    /// <summary>
    /// OPC UA endpoint URL clients connect to. Default
    /// <c>opc.tcp://0.0.0.0:4840/edgeconnect</c> — listens on every
    /// interface, port 4840 (the OPC UA standard). Override for
    /// multi-tenant deployments where two gateways share a host
    /// (use different ports), or to bind to a specific interface
    /// (replace 0.0.0.0 with an IP).
    /// </summary>
    public string EndpointUrl { get; init; } = "opc.tcp://0.0.0.0:4840/edgeconnect";

    /// <summary>
    /// OPC UA ApplicationUri identifying THIS server instance to clients.
    /// Default <c>urn:elpis:edgeconnect</c>. Per the OPC UA spec, this
    /// MUST match the SubjectAlternativeName URI in the server's
    /// application certificate when Security.Mode != None (enforced in
    /// Milestone K).
    /// </summary>
    public string ApplicationUri { get; init; } = "urn:elpis:edgeconnect";

    /// <summary>
    /// Human-readable application name shown to clients during endpoint
    /// discovery.
    /// </summary>
    public string ApplicationName { get; init; } = "Elpis EdgeConnect OPC UA Server";

    /// <summary>
    /// Namespace shape — BrowsePath template, NodeId template,
    /// NamespaceUri. See <see cref="OpcUaNamespaceConfig"/> for the
    /// stability contract and customer-facing implications.
    /// </summary>
    public OpcUaNamespaceConfig Namespace { get; init; } = new();

    /// <summary>
    /// Security and user-authentication configuration. MVP honors None +
    /// Anonymous; other modes are accepted in config and rejected at
    /// adapter Initialize with <c>OPCUA.SECURITY_NOT_YET_IMPLEMENTED</c>
    /// until Milestone K.
    /// </summary>
    public OpcUaSecurityConfig Security { get; init; } = new();

    /// <summary>
    /// Maximum concurrent client sessions. Default 10 (the v1 MVP target —
    /// see Phase 4 plan §5 scale targets). Sessions over this limit
    /// receive BadTooManySessions from the OPC UA stack.
    /// </summary>
    public int MaxSessions { get; init; } = 10;

    /// <summary>
    /// Maximum monitored items per session. Default 1,000. Combined with
    /// MaxSessions=10 this caps the server at 10,000 monitored items
    /// total — matching the v1 MVP scale-target ceiling.
    /// </summary>
    public int MaxMonitoredItemsPerSession { get; init; } = 1_000;

    /// <summary>
    /// Minimum publishing interval (milliseconds) the server will honor
    /// from clients. Lower than this and the server rounds up. Default
    /// 100 ms — fine-grained enough for SCADA refresh, slow enough not
    /// to swamp the OPC UA stack.
    /// </summary>
    public int MinPublishingIntervalMs { get; init; } = 100;

    /// <summary>
    /// Project a <see cref="SinkInstanceConfig"/> read from gateway.json
    /// into a typed <see cref="OpcUaServerConfiguration"/>. Mirrors the
    /// MQTT sink's <c>FromSinkInstance</c> pattern.
    /// </summary>
    public static OpcUaServerConfiguration FromSinkInstance(SinkInstanceConfig instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!string.Equals(instance.ProtocolName, ProtocolNameConstant, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected protocolName '{ProtocolNameConstant}', got '{instance.ProtocolName}'.",
                nameof(instance));
        }

        var conn = instance.Connection;
        var hasConn = conn is { ValueKind: JsonValueKind.Object };

        return new OpcUaServerConfiguration
        {
            InstanceId = instance.InstanceId,
            ProtocolName = instance.ProtocolName,
            Enabled = instance.Enabled,

            // Keys named via OpcUaServerConnectionKeys so they cannot be parsed
            // without a redaction tier (ADR-0020 M-B Q-B2); the drift guard
            // asserts OpcUaServerConnectionKeys.All == KnownKeys.
            EndpointUrl = hasConn
                ? ReadString(conn!.Value, OpcUaServerConnectionKeys.EndpointUrl) ?? "opc.tcp://0.0.0.0:4840/edgeconnect"
                : "opc.tcp://0.0.0.0:4840/edgeconnect",
            ApplicationUri = hasConn
                ? ReadString(conn!.Value, OpcUaServerConnectionKeys.ApplicationUri) ?? "urn:elpis:edgeconnect"
                : "urn:elpis:edgeconnect",
            ApplicationName = hasConn
                ? ReadString(conn!.Value, OpcUaServerConnectionKeys.ApplicationName) ?? "Elpis EdgeConnect OPC UA Server"
                : "Elpis EdgeConnect OPC UA Server",
            MaxSessions = hasConn
                ? ReadInt(conn!.Value, OpcUaServerConnectionKeys.MaxSessions, defaultValue: 10)
                : 10,
            MaxMonitoredItemsPerSession = hasConn
                ? ReadInt(conn!.Value, OpcUaServerConnectionKeys.MaxMonitoredItemsPerSession, defaultValue: 1000)
                : 1000,
            MinPublishingIntervalMs = hasConn
                ? ReadInt(conn!.Value, OpcUaServerConnectionKeys.MinPublishingIntervalMs, defaultValue: 100)
                : 100,

            Namespace = ReadNamespace(hasConn ? conn!.Value : default),
            Security = ReadSecurity(hasConn ? conn!.Value : default),
        };
    }

    private static OpcUaNamespaceConfig ReadNamespace(JsonElement parent)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(OpcUaServerConnectionKeys.Namespace, out var nsEl)
            || nsEl.ValueKind != JsonValueKind.Object)
        {
            return new OpcUaNamespaceConfig();
        }

        return new OpcUaNamespaceConfig
        {
            NamespaceUri = ReadString(nsEl, OpcUaServerConnectionKeys.NamespaceUri) ?? "urn:elpis:edgeconnect:v1",
            RootFolder = ReadString(nsEl, OpcUaServerConnectionKeys.RootFolder) ?? "EdgeConnect",
            BrowsePathTemplate = ReadString(nsEl, OpcUaServerConnectionKeys.BrowsePathTemplate) ?? "{deviceClass}/{sourceId}/{tagName}",
            NodeIdTemplate = ReadString(nsEl, OpcUaServerConnectionKeys.NodeIdTemplate) ?? "ns=2;s={gatewayId}/{sourceId}/{stableTagId}",
        };
    }

    private static OpcUaSecurityConfig ReadSecurity(JsonElement parent)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(OpcUaServerConnectionKeys.Security, out var secEl)
            || secEl.ValueKind != JsonValueKind.Object)
        {
            return new OpcUaSecurityConfig();
        }

        var modeRaw = ReadString(secEl, OpcUaServerConnectionKeys.Mode) ?? "None";
        Enum.TryParse<OpcUaSecurityMode>(modeRaw, ignoreCase: true, out var mode);

        return new OpcUaSecurityConfig
        {
            Mode = mode,
            ApplicationCertificatePath = ReadString(secEl, OpcUaServerConnectionKeys.ApplicationCertificatePath),
            TrustedClientsPath = ReadString(secEl, OpcUaServerConnectionKeys.TrustedClientsPath),
            RejectedClientsPath = ReadString(secEl, OpcUaServerConnectionKeys.RejectedClientsPath),
            UserTokenPolicies = ReadUserTokenPolicies(secEl),
            Credentials = ReadCredentials(secEl),
        };
    }

    private static System.Collections.Generic.IReadOnlyList<OpcUaCredential> ReadCredentials(JsonElement secEl)
    {
        if (!secEl.TryGetProperty(OpcUaServerConnectionKeys.Credentials, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<OpcUaCredential>();
        }
        var result = new System.Collections.Generic.List<OpcUaCredential>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var username = ReadString(item, OpcUaServerConnectionKeys.Username);
            var password = ReadString(item, OpcUaServerConnectionKeys.Password);
            if (string.IsNullOrWhiteSpace(username) || password is null) continue;
            result.Add(new OpcUaCredential
            {
                Username = username,
                Password = password,
                DisplayName = ReadString(item, OpcUaServerConnectionKeys.DisplayName),
            });
        }
        return result;
    }

    private static System.Collections.Generic.IReadOnlyList<OpcUaUserTokenPolicy> ReadUserTokenPolicies(JsonElement secEl)
    {
        if (!secEl.TryGetProperty(OpcUaServerConnectionKeys.UserTokenPolicies, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return new[] { OpcUaUserTokenPolicy.Anonymous };
        }

        var result = new System.Collections.Generic.List<OpcUaUserTokenPolicy>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String
                && Enum.TryParse<OpcUaUserTokenPolicy>(item.GetString(), ignoreCase: true, out var parsed))
            {
                result.Add(parsed);
            }
        }

        return result.Count > 0 ? result : new[] { OpcUaUserTokenPolicy.Anonymous };
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int ReadInt(JsonElement obj, string name, int defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : defaultValue;
}
