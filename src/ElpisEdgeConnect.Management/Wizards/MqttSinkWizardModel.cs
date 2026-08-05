// ============================================================================
// File: Wizards/MqttSinkWizardModel.cs
// Purpose: In-memory state for the Add-MQTT-Destination wizard (M.2b.6).
//          Razor two-way-binds every form field to this POCO; on Save the
//          wizard calls BuildSinkInstance() to produce a canonical
//          SinkInstanceConfig (with the MQTT-specific block packed into
//          the opaque Connection JsonElement, exactly as
//          MqttSinkConfiguration.FromSinkInstance expects).
//
//          KEY UX DECISIONS (M.2b.5/6 v2 §3 deltas + UX mockups §2.2):
//             * TLS as a single toggle (no cert upload UI in v1).
//             * Authentication = None / Username+Password only. mTLS
//               option HIDDEN entirely until a future milestone.
//             * Topic template gets a live preview row resolving
//               {gatewayId} from /api/v1/config (Razor-side; this model
//               just holds the raw template).
//             * PerTag mode default for EREMOS V2 compatibility — the
//               topic shape that the EREMOS V2 subscription expects:
//               eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}.
//
//          KEY VALIDATION DISCIPLINE (Locked N):
//             * Eager checks: required fields (host, port range,
//               username/password pair completeness, topic template
//               non-empty).
//             * Cross-record + runtime checks (dup sink-id, route
//               wiring constraints) deferred to merger + API.
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
//            docs/sessions/2026-05-18-mp2b5-mp2b6-ux-mockups.md §2.2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Components.Shared;
using ElpisEdgeConnect.Sinks.Mqtt;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Operator's choice of MQTT authentication mode in the wizard.
/// </summary>
public enum MqttAuthenticationMode
{
    /// <summary>Connect anonymously — no broker-side credentials.</summary>
    None,

    /// <summary>Username + password supplied on the CONNECT frame.</summary>
    UsernamePassword,
}

/// <summary>
/// Wizard state for adding a new MQTT sink. Razor binds form fields
/// directly to these properties; on Save, <see cref="BuildSinkInstance"/>
/// produces a canonical <see cref="SinkInstanceConfig"/>.
/// </summary>
public sealed class MqttSinkWizardModel
{
    // ─── Identity ────────────────────────────────────────────────────────

    /// <summary>Stable sink instance id (e.g. <c>"mqtt-eremos"</c>). Regex enforced by Core.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Whether the sink is enabled when the new draft is applied.</summary>
    public bool Enabled { get; set; } = true;

    // ─── Broker connection ───────────────────────────────────────────────

    /// <summary>Broker hostname or IP (required).</summary>
    public string BrokerHost { get; set; } = string.Empty;

    /// <summary>Broker TCP port. Default 1883 (plain) or 8883 (TLS).</summary>
    public int BrokerPort { get; set; } = 1883;

    /// <summary>Enable TLS (server-cert validation only — mTLS deferred).</summary>
    public bool UseTls { get; set; }

    /// <summary>MQTT keep-alive interval in seconds.</summary>
    public int KeepAliveSeconds { get; set; } = 30;

    /// <summary>
    /// Optional client id. Empty → adapter auto-generates
    /// <c>"edgeconnect-{InstanceId}"</c>.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    // ─── Authentication ──────────────────────────────────────────────────

    /// <summary>Authentication strategy.</summary>
    public MqttAuthenticationMode Authentication { get; set; } = MqttAuthenticationMode.None;

    /// <summary>Username — only used when <see cref="Authentication"/> is <see cref="MqttAuthenticationMode.UsernamePassword"/>.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password — only used when <see cref="Authentication"/> is <see cref="MqttAuthenticationMode.UsernamePassword"/>.</summary>
    public string Password { get; set; } = string.Empty;

    // ─── Topic / publish policy ──────────────────────────────────────────

    /// <summary>Publish strategy. Default <see cref="MqttPublishMode.PerTag"/> for EREMOS V2 compatibility.</summary>
    public MqttPublishMode PublishMode { get; set; } = MqttPublishMode.PerTag;

    /// <summary>Batch-mode topic template. Resolved at runtime with {gatewayId}/{sourceId}/{routeId}.</summary>
    public string BatchTopicTemplate { get; set; } = "edgeconnect/{gatewayId}/data";

    /// <summary>PerTag-mode topic template. EREMOS V2 default subscription shape.</summary>
    public string PerTagTopicTemplate { get; set; } = "eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}";

    /// <summary>MQTT QoS level (0 = at-most-once, 1 = at-least-once).</summary>
    public int QosLevel { get; set; } = 1;

    /// <summary>Default retain flag for published messages.</summary>
    public bool RetainDefault { get; set; }

    // ─── Reconnect / backoff ─────────────────────────────────────────────

    /// <summary>Initial reconnect delay in ms.</summary>
    public int ReconnectDelayMs { get; set; } = 5_000;

    /// <summary>Maximum reconnect delay in ms (cap for exponential backoff).</summary>
    public int MaxReconnectDelayMs { get; set; } = 60_000;

    /// <summary>Exponential backoff multiplier for reconnect delay.</summary>
    public double ReconnectMultiplier { get; set; } = 2.0;

    // ─── Hydration (Edit mode) ───────────────────────────────────────────

    /// <summary>
    /// Reverse of <see cref="BuildSinkInstance"/>. Populates a new
    /// <see cref="MqttSinkWizardModel"/> from an existing
    /// <see cref="SinkInstanceConfig"/> so the Edit-mode wizard opens
    /// pre-filled with the applied values.
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="existing"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// When <paramref name="existing"/> is not an MQTT sink
    /// (<see cref="SinkInstanceConfig.ProtocolName"/> ≠ <c>"mqtt"</c>).
    /// </exception>
    public static MqttSinkWizardModel HydrateFromExisting(SinkInstanceConfig existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (!string.Equals(existing.ProtocolName, MqttSinkConfiguration.ProtocolNameConstant, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Cannot hydrate MqttSinkWizardModel from a '{existing.ProtocolName}' sink. " +
                $"Expected protocol name '{MqttSinkConfiguration.ProtocolNameConstant}'.",
                nameof(existing));
        }

        var model = new MqttSinkWizardModel
        {
            InstanceId = existing.InstanceId,
            Enabled = existing.Enabled,
        };

        if (existing.Connection is not { } conn) return model;

        if (conn.TryGetProperty("brokerHost", out var brokerHost))
            model.BrokerHost = brokerHost.GetString() ?? string.Empty;
        if (conn.TryGetProperty("brokerPort", out var brokerPort))
            model.BrokerPort = brokerPort.GetInt32();
        if (conn.TryGetProperty("useTls", out var useTls))
            model.UseTls = useTls.GetBoolean();
        if (conn.TryGetProperty("keepAliveSeconds", out var keepAlive))
            model.KeepAliveSeconds = keepAlive.GetInt32();
        if (conn.TryGetProperty("clientId", out var clientId))
            model.ClientId = clientId.GetString() ?? string.Empty;
        if (conn.TryGetProperty("publishMode", out var publishMode) &&
            Enum.TryParse<MqttPublishMode>(publishMode.GetString(), out var pm))
            model.PublishMode = pm;
        if (conn.TryGetProperty("topicTemplate", out var batchTopic))
            model.BatchTopicTemplate = batchTopic.GetString() ?? model.BatchTopicTemplate;
        if (conn.TryGetProperty("perTagTopicTemplate", out var perTagTopic))
            model.PerTagTopicTemplate = perTagTopic.GetString() ?? model.PerTagTopicTemplate;
        if (conn.TryGetProperty("qosLevel", out var qos))
            model.QosLevel = qos.GetInt32();
        if (conn.TryGetProperty("retainDefault", out var retain))
            model.RetainDefault = retain.GetBoolean();
        if (conn.TryGetProperty("reconnectDelayMs", out var reconnect))
            model.ReconnectDelayMs = reconnect.GetInt32();
        if (conn.TryGetProperty("maxReconnectDelayMs", out var maxReconnect))
            model.MaxReconnectDelayMs = maxReconnect.GetInt32();
        if (conn.TryGetProperty("reconnectMultiplier", out var multiplier))
            model.ReconnectMultiplier = multiplier.GetDouble();

        // Authentication: presence of "username" key implies UsernamePassword mode.
        if (conn.TryGetProperty("username", out var username))
        {
            model.Authentication = MqttAuthenticationMode.UsernamePassword;
            model.Username = username.GetString() ?? string.Empty;
            if (conn.TryGetProperty("password", out var password))
                model.Password = password.GetString() ?? string.Empty;
        }

        return model;
    }

    // ─── Eager validation helpers ───────────────────────────────────────

    /// <summary>
    /// Validate the broker host. Returns null on success, an operator-facing
    /// error message on failure. Composes Core's port-range invariant.
    /// </summary>
    public string? ValidateBrokerHost()
    {
        if (string.IsNullOrWhiteSpace(BrokerHost))
        {
            return "Broker host is required.";
        }
        return null;
    }

    /// <summary>Validate the broker port (1..65535).</summary>
    public string? ValidateBrokerPort()
    {
        if (BrokerPort < 1 || BrokerPort > 65535)
        {
            return "Broker port must be between 1 and 65535.";
        }
        return null;
    }

    /// <summary>
    /// Validate the authentication block — when UsernamePassword is selected,
    /// both username and password must be present (matches
    /// <see cref="MqttSinkConfiguration"/>'s "partial auth is a config error"
    /// invariant).
    /// </summary>
    public string? ValidateAuthentication()
    {
        if (Authentication != MqttAuthenticationMode.UsernamePassword)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            return "Username is required when authentication is Username+Password.";
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            return "Password is required when authentication is Username+Password.";
        }
        return null;
    }

    /// <summary>Validate the active topic template for the current publish mode.</summary>
    public string? ValidateTopicTemplate()
    {
        var template = PublishMode == MqttPublishMode.PerTag ? PerTagTopicTemplate : BatchTopicTemplate;
        if (string.IsNullOrWhiteSpace(template))
        {
            return "Topic template is required.";
        }
        return null;
    }

    /// <summary>Validate the QoS level (0 or 1; QoS 2 is intentionally not supported in v1).</summary>
    public string? ValidateQosLevel()
    {
        if (QosLevel != 0 && QosLevel != 1)
        {
            return "QoS must be 0 (at most once) or 1 (at least once). QoS 2 is not supported in v1.";
        }
        return null;
    }

    /// <summary>
    /// Aggregate all per-field validation results into a list of
    /// <see cref="WizardValidationMessage"/> suitable for the shared
    /// <c>WizardValidationBanner</c>. Empty list means no errors —
    /// the banner renders zero DOM (ADR-0015 Rule 5: "absence is the
    /// success signal", no green success state).
    ///
    /// FieldAnchor convention is locked in ADR-0015 Rule 3: kebab-case
    /// CSS selector beginning with <c>#field-</c>. The razor template
    /// must declare matching <c>id</c> attributes on every field
    /// referenced here.
    /// </summary>
    public IReadOnlyList<WizardValidationMessage> Validate()
    {
        var messages = new List<WizardValidationMessage>();

        if (string.IsNullOrWhiteSpace(InstanceId))
        {
            messages.Add(new WizardValidationMessage(
                Code: "mqtt.instance-id.empty",
                Path: "InstanceId",
                Message: "Destination id is required.",
                FieldAnchor: "#field-instance-id"));
        }

        var hostError = ValidateBrokerHost();
        if (hostError is not null)
        {
            messages.Add(new WizardValidationMessage(
                "mqtt.broker-host.empty",
                "Connection.BrokerHost",
                hostError,
                "#field-connection.broker-host"));
        }

        var portError = ValidateBrokerPort();
        if (portError is not null)
        {
            messages.Add(new WizardValidationMessage(
                "mqtt.broker-port.out-of-range",
                "Connection.BrokerPort",
                portError,
                "#field-connection.broker-port"));
        }

        var authError = ValidateAuthentication();
        if (authError is not null)
        {
            // Point at whichever field is empty; if both are empty pick username.
            var anchor = string.IsNullOrWhiteSpace(Username)
                ? "#field-authentication.username"
                : "#field-authentication.password";
            messages.Add(new WizardValidationMessage(
                "mqtt.authentication.incomplete",
                "Authentication",
                authError,
                anchor));
        }

        var topicError = ValidateTopicTemplate();
        if (topicError is not null)
        {
            var anchor = PublishMode == MqttPublishMode.PerTag
                ? "#field-topic.per-tag-template"
                : "#field-topic.batch-template";
            messages.Add(new WizardValidationMessage(
                "mqtt.topic-template.empty",
                "Topic.Template",
                topicError,
                anchor));
        }

        var qosError = ValidateQosLevel();
        if (qosError is not null)
        {
            messages.Add(new WizardValidationMessage(
                "mqtt.qos-level.invalid",
                "Topic.QosLevel",
                qosError,
                "#field-topic.qos-level"));
        }

        return messages;
    }

    // ─── Projection ──────────────────────────────────────────────────────

    /// <summary>
    /// Project the wizard state into a canonical
    /// <see cref="SinkInstanceConfig"/>. The MQTT-specific fields land
    /// in the opaque <c>Connection</c> <see cref="JsonElement"/> — the
    /// canonical type stays protocol-agnostic.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a required field is blank or outside its valid range.
    /// Defensive — the wizard UI disables Save until each field validates,
    /// but the projection enforces invariants anyway so a programmatic
    /// caller can't construct an invalid sink.
    /// </exception>
    public SinkInstanceConfig BuildSinkInstance()
    {
        var hostError = ValidateBrokerHost();
        if (hostError is not null) throw new InvalidOperationException(hostError);
        var portError = ValidateBrokerPort();
        if (portError is not null) throw new InvalidOperationException(portError);
        var authError = ValidateAuthentication();
        if (authError is not null) throw new InvalidOperationException(authError);
        var topicError = ValidateTopicTemplate();
        if (topicError is not null) throw new InvalidOperationException(topicError);
        var qosError = ValidateQosLevel();
        if (qosError is not null) throw new InvalidOperationException(qosError);

        var connection = new JsonObject
        {
            ["brokerHost"] = BrokerHost,
            ["brokerPort"] = BrokerPort,
            ["useTls"] = UseTls,
            ["keepAliveSeconds"] = KeepAliveSeconds,
            ["publishMode"] = PublishMode.ToString(),
            ["topicTemplate"] = BatchTopicTemplate,
            ["perTagTopicTemplate"] = PerTagTopicTemplate,
            ["qosLevel"] = QosLevel,
            ["retainDefault"] = RetainDefault,
            ["reconnectDelayMs"] = ReconnectDelayMs,
            ["maxReconnectDelayMs"] = MaxReconnectDelayMs,
            ["reconnectMultiplier"] = ReconnectMultiplier,
        };

        if (!string.IsNullOrWhiteSpace(ClientId))
        {
            connection["clientId"] = ClientId;
        }

        if (Authentication == MqttAuthenticationMode.UsernamePassword)
        {
            connection["username"] = Username;
            connection["password"] = Password;
        }

        // Convert JsonNode tree to JsonElement (the opaque shape SinkInstanceConfig holds).
        var json = connection.ToJsonString();
        using var doc = JsonDocument.Parse(json);

        return new SinkInstanceConfig
        {
            InstanceId = InstanceId,
            ProtocolName = MqttSinkConfiguration.ProtocolNameConstant,
            Enabled = Enabled,
            Connection = doc.RootElement.Clone(),
        };
    }
}
