// ============================================================================
// File: Wizards/OpcUaServerSinkWizardModel.cs
// Purpose: In-memory state for the Add-OPC-UA-Server-Destination wizard
//          (M.2b.6). Razor two-way-binds every form field to this POCO;
//          on Save the wizard calls BuildSinkInstance() to produce a
//          canonical SinkInstanceConfig (with the OPC UA-specific block
//          packed into the opaque Connection JsonElement, exactly as
//          OpcUaServerConfiguration.FromSinkInstance expects).
//
//          KEY DECISIONS (M.2b.6 v3 §1 Locked M + Locked O):
//             * Locked M: consumes canonical OpcUaServerConfiguration
//               (NOT OpcUaServerSinkConfiguration). All field names
//               match the record exactly.
//             * Locked O: full security schema is wizard-visible. Mode
//               radio (None / Sign / SignAndEncrypt) reflects Core's
//               actual surface today. SignAndEncrypt carries the
//               Recommended chip (the most secure value Core honors at
//               the schema level — runtime enforcement of non-None modes
//               lands in Milestone K, which is a release prerequisite).
//             * Forward-compat note: when Core adds explicit security-
//               policy URIs (Basic256Sha256 vs Aes128/Aes256 variants
//               named in the v3 plan), the wizard adds a policy dropdown
//               then. Today Core hardcodes Basic256Sha256 for non-None
//               modes in OpcUaServerSinkAdapter.BuildServerSecurityPolicies.
//
//          KEY UX DECISIONS (UX mockups §2.3):
//             * No Test Connection panel — server is an acceptor; the
//               equivalent verification is "can we bind?" which has
//               side effects. Caption explains this.
//             * Custom-cert-path behind Advanced ▾ expansion panel.
//             * Auto-generate is the default (operator just clicks Save).
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §1, §5
//            docs/sessions/2026-05-18-mp2b5-mp2b6-ux-mockups.md §2.3
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Components.Shared;
using ElpisEdgeConnect.Sinks.OpcUaServer;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Wizard state for adding a new OPC UA Server sink. Razor binds form
/// fields directly to these properties; on Save, <see cref="BuildSinkInstance"/>
/// produces a canonical <see cref="SinkInstanceConfig"/> whose Connection
/// JsonElement is round-trip-readable by
/// <see cref="OpcUaServerConfiguration.FromSinkInstance"/>.
/// </summary>
public sealed class OpcUaServerSinkWizardModel
{
    // ─── Identity ────────────────────────────────────────────────────────

    /// <summary>Stable sink instance id (e.g. <c>"opcua-edge"</c>). Regex enforced by Core.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Whether the sink is enabled when the new draft is applied.</summary>
    public bool Enabled { get; set; } = true;

    // ─── Endpoint / application identity ─────────────────────────────────

    /// <summary>OPC UA endpoint URL clients connect to.</summary>
    public string EndpointUrl { get; set; } = "opc.tcp://0.0.0.0:4840/edgeconnect";

    /// <summary>OPC UA ApplicationUri identifying this server instance.</summary>
    public string ApplicationUri { get; set; } = "urn:elpis:edgeconnect";

    /// <summary>Human-readable application name shown during endpoint discovery.</summary>
    public string ApplicationName { get; set; } = "Elpis EdgeConnect OPC UA Server";

    // ─── Namespace shape ────────────────────────────────────────────────

    /// <summary>OPC UA NamespaceUri — versioned. Operators rarely change this.</summary>
    public string NamespaceUri { get; set; } = "urn:elpis:edgeconnect:v1";

    /// <summary>Top-level folder under <c>Objects/</c>.</summary>
    public string RootFolder { get; set; } = "EdgeConnect";

    /// <summary>Operator-readable BrowsePath template with placeholders.</summary>
    public string BrowsePathTemplate { get; set; } = "{deviceClass}/{sourceId}/{tagName}";

    /// <summary>Stable NodeId template with placeholders.</summary>
    public string NodeIdTemplate { get; set; } = "ns=2;s={gatewayId}/{sourceId}/{stableTagId}";

    // ─── Security ────────────────────────────────────────────────────────

    /// <summary>
    /// Endpoint security mode. Locked O: the full schema is visible
    /// even though only <see cref="OpcUaSecurityMode.None"/> is runtime-
    /// enforced today. SignAndEncrypt is the wizard's "Recommended"
    /// option for the K-and-after release.
    /// </summary>
    public OpcUaSecurityMode SecurityMode { get; set; } = OpcUaSecurityMode.None;

    /// <summary>User-authentication policies offered by the endpoint.</summary>
    public HashSet<OpcUaUserTokenPolicy> UserTokenPolicies { get; } = new()
    {
        OpcUaUserTokenPolicy.Anonymous,
    };

    /// <summary>
    /// Path to the server's application certificate. Null/empty → adapter
    /// auto-generates a self-signed certificate on first start (the
    /// wizard default).
    /// </summary>
    public string ApplicationCertificatePath { get; set; } = string.Empty;

    /// <summary>Directory of trusted client certificates (Milestone K+ honored).</summary>
    public string TrustedClientsPath { get; set; } = string.Empty;

    /// <summary>Directory where rejected client certs land (Milestone K+ honored).</summary>
    public string RejectedClientsPath { get; set; } = string.Empty;

    // ─── Capacity ────────────────────────────────────────────────────────

    /// <summary>Maximum concurrent client sessions.</summary>
    public int MaxSessions { get; set; } = 10;

    /// <summary>Maximum monitored items per session.</summary>
    public int MaxMonitoredItemsPerSession { get; set; } = 1_000;

    /// <summary>Minimum publishing interval (ms) the server honors from clients.</summary>
    public int MinPublishingIntervalMs { get; set; } = 100;

    // ─── Hydration (Edit mode) ───────────────────────────────────────────

    /// <summary>
    /// Reverse of <see cref="BuildSinkInstance"/>. Populates a new
    /// <see cref="OpcUaServerSinkWizardModel"/> from an existing
    /// <see cref="SinkInstanceConfig"/> so the Edit-mode wizard opens
    /// pre-filled with the applied values.
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="existing"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// When <paramref name="existing"/> is not an OPC UA Server sink.
    /// </exception>
    public static OpcUaServerSinkWizardModel HydrateFromExisting(SinkInstanceConfig existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (!string.Equals(existing.ProtocolName, OpcUaServerConfiguration.ProtocolNameConstant, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Cannot hydrate OpcUaServerSinkWizardModel from a '{existing.ProtocolName}' sink. " +
                $"Expected protocol name '{OpcUaServerConfiguration.ProtocolNameConstant}'.",
                nameof(existing));
        }

        var model = new OpcUaServerSinkWizardModel
        {
            InstanceId = existing.InstanceId,
            Enabled = existing.Enabled,
        };

        if (existing.Connection is not { } conn) return model;

        if (conn.TryGetProperty("endpointUrl", out var ep)) model.EndpointUrl = ep.GetString() ?? model.EndpointUrl;
        if (conn.TryGetProperty("applicationUri", out var appUri)) model.ApplicationUri = appUri.GetString() ?? model.ApplicationUri;
        if (conn.TryGetProperty("applicationName", out var appName)) model.ApplicationName = appName.GetString() ?? model.ApplicationName;
        if (conn.TryGetProperty("maxSessions", out var maxSess)) model.MaxSessions = maxSess.GetInt32();
        if (conn.TryGetProperty("maxMonitoredItemsPerSession", out var maxMon)) model.MaxMonitoredItemsPerSession = maxMon.GetInt32();
        if (conn.TryGetProperty("minPublishingIntervalMs", out var minPub)) model.MinPublishingIntervalMs = minPub.GetInt32();

        if (conn.TryGetProperty("namespace", out var ns))
        {
            if (ns.TryGetProperty("namespaceUri", out var nsUri)) model.NamespaceUri = nsUri.GetString() ?? model.NamespaceUri;
            if (ns.TryGetProperty("rootFolder", out var root)) model.RootFolder = root.GetString() ?? model.RootFolder;
            if (ns.TryGetProperty("browsePathTemplate", out var bpt)) model.BrowsePathTemplate = bpt.GetString() ?? model.BrowsePathTemplate;
            if (ns.TryGetProperty("nodeIdTemplate", out var nit)) model.NodeIdTemplate = nit.GetString() ?? model.NodeIdTemplate;
        }

        if (conn.TryGetProperty("security", out var sec))
        {
            if (sec.TryGetProperty("mode", out var mode) &&
                Enum.TryParse<OpcUaSecurityMode>(mode.GetString(), out var sm))
                model.SecurityMode = sm;

            if (sec.TryGetProperty("userTokenPolicies", out var policies) &&
                policies.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                model.UserTokenPolicies.Clear();
                foreach (var policy in policies.EnumerateArray())
                {
                    if (Enum.TryParse<OpcUaUserTokenPolicy>(policy.GetString(), out var utp))
                        model.UserTokenPolicies.Add(utp);
                }
            }

            if (sec.TryGetProperty("applicationCertificatePath", out var certPath))
                model.ApplicationCertificatePath = certPath.GetString() ?? string.Empty;
            if (sec.TryGetProperty("trustedClientsPath", out var trusted))
                model.TrustedClientsPath = trusted.GetString() ?? string.Empty;
            if (sec.TryGetProperty("rejectedClientsPath", out var rejected))
                model.RejectedClientsPath = rejected.GetString() ?? string.Empty;
        }

        return model;
    }

    // ─── Eager validation helpers ───────────────────────────────────────

    /// <summary>Validate the endpoint URL — must start with opc.tcp:// (the OPC UA TCP scheme).</summary>
    public string? ValidateEndpointUrl()
    {
        if (string.IsNullOrWhiteSpace(EndpointUrl))
        {
            return "Endpoint URL is required.";
        }
        if (!EndpointUrl.StartsWith("opc.tcp://", StringComparison.OrdinalIgnoreCase))
        {
            return "Endpoint URL must start with 'opc.tcp://'.";
        }
        return null;
    }

    /// <summary>Validate the ApplicationUri — must be non-empty (clients expect a unique URI).</summary>
    public string? ValidateApplicationUri()
    {
        if (string.IsNullOrWhiteSpace(ApplicationUri))
        {
            return "Application URI is required (e.g. 'urn:elpis:edgeconnect').";
        }
        return null;
    }

    /// <summary>Validate the namespace block — URI + browse template + node-id template all non-empty.</summary>
    public string? ValidateNamespace()
    {
        if (string.IsNullOrWhiteSpace(NamespaceUri))
        {
            return "Namespace URI is required.";
        }
        if (string.IsNullOrWhiteSpace(BrowsePathTemplate))
        {
            return "BrowsePath template is required.";
        }
        if (string.IsNullOrWhiteSpace(NodeIdTemplate))
        {
            return "NodeId template is required.";
        }
        return null;
    }

    /// <summary>Validate capacity numerics — all positive.</summary>
    public string? ValidateCapacity()
    {
        if (MaxSessions <= 0)
        {
            return "Max sessions must be greater than zero.";
        }
        if (MaxMonitoredItemsPerSession <= 0)
        {
            return "Max monitored items per session must be greater than zero.";
        }
        if (MinPublishingIntervalMs <= 0)
        {
            return "Min publishing interval must be greater than zero.";
        }
        return null;
    }

    /// <summary>Validate the user-token policy set is non-empty.</summary>
    public string? ValidateUserTokenPolicies()
    {
        if (UserTokenPolicies.Count == 0)
        {
            return "At least one user-token policy must be enabled (Anonymous is the MVP default).";
        }
        return null;
    }

    /// <summary>
    /// Aggregate all per-field validation results into a list of
    /// <see cref="WizardValidationMessage"/> suitable for the shared
    /// <c>WizardValidationBanner</c>. Empty list means no errors.
    /// FieldAnchor convention per ADR-0015 Rule 3 — kebab-case CSS
    /// selectors prefixed with <c>#field-</c>.
    /// </summary>
    public IReadOnlyList<WizardValidationMessage> Validate()
    {
        var messages = new List<WizardValidationMessage>();

        if (string.IsNullOrWhiteSpace(InstanceId))
        {
            messages.Add(new WizardValidationMessage(
                "opcua.instance-id.empty",
                "InstanceId",
                "Instance id is required.",
                "#field-instance-id"));
        }

        var endpointError = ValidateEndpointUrl();
        if (endpointError is not null)
        {
            messages.Add(new WizardValidationMessage(
                "opcua.endpoint-url.invalid",
                "Endpoint.Url",
                endpointError,
                "#field-endpoint.url"));
        }

        var appUriError = ValidateApplicationUri();
        if (appUriError is not null)
        {
            messages.Add(new WizardValidationMessage(
                "opcua.application-uri.empty",
                "Endpoint.ApplicationUri",
                appUriError,
                "#field-endpoint.application-uri"));
        }

        // ValidateNamespace lumps three field checks into one error. Map
        // the resulting message to the most-specific anchor we can infer.
        var nsError = ValidateNamespace();
        if (nsError is not null)
        {
            var anchor = "#field-namespace.uri";
            if (string.IsNullOrWhiteSpace(BrowsePathTemplate))
            {
                anchor = "#field-namespace.browse-path";
            }
            else if (string.IsNullOrWhiteSpace(NodeIdTemplate))
            {
                anchor = "#field-namespace.node-id";
            }
            messages.Add(new WizardValidationMessage(
                "opcua.namespace.invalid",
                "Namespace",
                nsError,
                anchor));
        }

        // ValidateCapacity lumps three numeric checks into one error.
        var capError = ValidateCapacity();
        if (capError is not null)
        {
            var anchor = "#field-capacity.max-sessions";
            if (MaxMonitoredItemsPerSession <= 0)
            {
                anchor = "#field-capacity.max-monitored";
            }
            else if (MinPublishingIntervalMs <= 0)
            {
                anchor = "#field-capacity.min-publishing-interval";
            }
            messages.Add(new WizardValidationMessage(
                "opcua.capacity.invalid",
                "Capacity",
                capError,
                anchor));
        }

        var policyError = ValidateUserTokenPolicies();
        if (policyError is not null)
        {
            messages.Add(new WizardValidationMessage(
                "opcua.user-token-policies.empty",
                "Security.UserTokenPolicies",
                policyError,
                "#field-security.user-tokens"));
        }

        return messages;
    }

    // ─── Projection ──────────────────────────────────────────────────────

    /// <summary>
    /// Project the wizard state into a canonical
    /// <see cref="SinkInstanceConfig"/>. The OPC UA-specific fields land
    /// in the opaque <c>Connection</c> <see cref="JsonElement"/> in the
    /// shape <see cref="OpcUaServerConfiguration.FromSinkInstance"/>
    /// expects.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a required field is blank or numeric ranges are
    /// violated. Defensive guard mirroring the MQTT wizard.
    /// </exception>
    public SinkInstanceConfig BuildSinkInstance()
    {
        var endpointError = ValidateEndpointUrl();
        if (endpointError is not null) throw new InvalidOperationException(endpointError);
        var appUriError = ValidateApplicationUri();
        if (appUriError is not null) throw new InvalidOperationException(appUriError);
        var nsError = ValidateNamespace();
        if (nsError is not null) throw new InvalidOperationException(nsError);
        var capError = ValidateCapacity();
        if (capError is not null) throw new InvalidOperationException(capError);
        var policyError = ValidateUserTokenPolicies();
        if (policyError is not null) throw new InvalidOperationException(policyError);

        var namespaceBlock = new JsonObject
        {
            ["namespaceUri"] = NamespaceUri,
            ["rootFolder"] = RootFolder,
            ["browsePathTemplate"] = BrowsePathTemplate,
            ["nodeIdTemplate"] = NodeIdTemplate,
        };

        var tokenArray = new JsonArray();
        foreach (var policy in UserTokenPolicies)
        {
            tokenArray.Add(policy.ToString());
        }

        var securityBlock = new JsonObject
        {
            ["mode"] = SecurityMode.ToString(),
            ["userTokenPolicies"] = tokenArray,
        };
        if (!string.IsNullOrWhiteSpace(ApplicationCertificatePath))
        {
            securityBlock["applicationCertificatePath"] = ApplicationCertificatePath;
        }
        if (!string.IsNullOrWhiteSpace(TrustedClientsPath))
        {
            securityBlock["trustedClientsPath"] = TrustedClientsPath;
        }
        if (!string.IsNullOrWhiteSpace(RejectedClientsPath))
        {
            securityBlock["rejectedClientsPath"] = RejectedClientsPath;
        }

        var connection = new JsonObject
        {
            ["endpointUrl"] = EndpointUrl,
            ["applicationUri"] = ApplicationUri,
            ["applicationName"] = ApplicationName,
            ["maxSessions"] = MaxSessions,
            ["maxMonitoredItemsPerSession"] = MaxMonitoredItemsPerSession,
            ["minPublishingIntervalMs"] = MinPublishingIntervalMs,
            ["namespace"] = namespaceBlock,
            ["security"] = securityBlock,
        };

        var json = connection.ToJsonString();
        using var doc = JsonDocument.Parse(json);

        return new SinkInstanceConfig
        {
            InstanceId = InstanceId,
            ProtocolName = OpcUaServerConfiguration.ProtocolNameConstant,
            Enabled = Enabled,
            Connection = doc.RootElement.Clone(),
        };
    }
}
