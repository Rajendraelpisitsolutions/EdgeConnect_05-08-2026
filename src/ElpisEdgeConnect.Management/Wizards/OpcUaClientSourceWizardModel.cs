// ============================================================================
// File: Wizards/OpcUaClientSourceWizardModel.cs
// Purpose: In-memory state for the Add/Edit OPC UA Client source wizard.
//          Razor two-way-binds every form field to this POCO; on Save
//          the wizard calls BuildSourceInstance() to produce a canonical
//          SourceInstanceConfig that OpcUaClientSourceConfiguration
//          .FromSourceInstance (PR 7c-3) parses back on the host side.
//
//          Kept wire-shape friendly (string enums for security/auth
//          modes) so MudSelect can bind directly. The conversion to
//          canonical typed values happens in BuildSourceInstance via
//          enum parsing.
//
//          AMENDMENTS folded (per PR 7c plan, user lock 2026-05-29):
//
//          #1  Cancelled probe skips cooldown — implemented in the
//              companion OpcUaTestConnectionCooldownFsm class.
//          #2  NodeId canonicalisation — manual tag-entry uses
//              OpcUaNodeIdCanonicalizer (PR 7c-1) for de-dup matching
//              so an operator typing "ns=2;i=00000001" while the same
//              node already exists as "ns=2;i=1" doesn't create a
//              duplicate row.
//          #3  Live Browse-step selection summary — SelectionSummary
//              computed property exposes the tag count + expected
//              subscriptions + profile alignment label so the wizard's
//              sticky panel re-renders without recomputing on every
//              binding cycle.
//          #5  Tag-count Blocked → CanSave=false. The Review-step
//              guardrail.
//
// Reference: PR 7c plan + amendments (user lock 2026-05-29)
//            docs/decisions/0015-wizard-contract.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.OpcUaClient;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Wizard state for adding / editing an OPC UA Client source.
/// </summary>
public sealed class OpcUaClientSourceWizardModel
{
    // ─── Identity ────────────────────────────────────────────────────

    /// <summary>Stable instance id (e.g. <c>"opcua-factorytalk-east"</c>).</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device identifier.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Operator-readable device display name.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Device class — typically <c>"scada"</c> or <c>"plc"</c>.</summary>
    public string DeviceClass { get; set; } = "scada";

    /// <summary>Whether the source is enabled when the new draft is applied.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Top-level "polling interval" in milliseconds. OPC UA is a
    /// subscription-mode adapter so polling isn't used at runtime; the
    /// canonical <c>SourceInstanceConfig</c> still carries the field, so
    /// we surface it for UI completeness with a sensible default.
    /// </summary>
    public int PollIntervalMs { get; set; } = 1000;

    // ─── Connection ──────────────────────────────────────────────────

    /// <summary>OPC UA endpoint URL (e.g. <c>opc.tcp://factorytalk.local:4840</c>).</summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>Client-side application URI. Defaults to the EdgeConnect URN.</summary>
    public string ApplicationUri { get; set; } = "urn:elpis:edgeconnect:opcua-client";

    /// <summary>Operator-readable application name surfaced to the server.</summary>
    public string ApplicationName { get; set; } = "Elpis EdgeConnect";

    /// <summary>Session timeout (ms) negotiated with the server.</summary>
    public uint SessionTimeoutMs { get; set; } = 60_000;

    // ─── Security ────────────────────────────────────────────────────

    /// <summary>Security mode — bound to MudSelect as the enum value.</summary>
    public OpcUaSecurityMode SecurityMode { get; set; } = OpcUaSecurityMode.SignAndEncrypt;

    /// <summary>Security policy URI.</summary>
    public string SecurityPolicyUri { get; set; } =
        "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256";

    /// <summary>Whether to auto-accept untrusted server certificates (dev/pilot mode).</summary>
    public bool AutoAcceptUntrustedServerCertificate { get; set; } = true;

    /// <summary>Optional file-system store path for the client application certificate.</summary>
    public string? ApplicationCertificateStorePath { get; set; }

    // ─── Auth ────────────────────────────────────────────────────────

    /// <summary>User token mode — Anonymous / UserName / Certificate.</summary>
    public OpcUaAuthMode AuthMode { get; set; } = OpcUaAuthMode.Anonymous;

    /// <summary>Username for <see cref="OpcUaAuthMode.UserName"/>.</summary>
    public string? Username { get; set; }

    /// <summary>Password for <see cref="OpcUaAuthMode.UserName"/>.</summary>
    public string? Password { get; set; }

    /// <summary>Certificate path for <see cref="OpcUaAuthMode.Certificate"/>.</summary>
    public string? CertificatePath { get; set; }

    // ─── Tuning (v2.1 §2.5 locked defaults; Advanced toggle unlocks) ──

    /// <summary>Subscription publishing interval (ms). v2.1 §2.5 lock: 50.</summary>
    public int PublishingIntervalMs { get; set; } = 50;

    /// <summary>Default sampling interval (ms). v2.1 §2.5 lock: 50.</summary>
    public int SamplingIntervalMs { get; set; } = 50;

    /// <summary>Keep-alive count. v2.1 §2.5 lock: 20.</summary>
    public uint KeepAliveCount { get; set; } = 20;

    /// <summary>Subscription lifetime count. v2.1 §2.5 lock: 60.</summary>
    public uint LifetimeCount { get; set; } = 60;

    /// <summary>Max notifications per publish. v2.1 §2.5 lock: 1000.</summary>
    public uint MaxNotificationsPerPublish { get; set; } = 1_000;

    /// <summary>Default analog queue size. v2.1 §2.5 lock: 2.</summary>
    public uint DefaultAnalogQueueSize { get; set; } = 2;

    /// <summary>Default discrete queue size. v2.1 §2.5 lock: 10.</summary>
    public uint DefaultDiscreteQueueSize { get; set; } = 10;

    /// <summary>
    /// Notification dispatcher channel capacity. Operator-tunable in
    /// <see cref="OpcUaClientSourceConfiguration.MinimumNotificationChannelCapacity"/>
    /// …<see cref="OpcUaClientSourceConfiguration.MaximumNotificationChannelCapacity"/>.
    /// </summary>
    public int NotificationChannelCapacity { get; set; } =
        OpcUaClientSourceConfiguration.DefaultNotificationChannelCapacity;

    /// <summary>
    /// Whether the operator has unlocked the Advanced section (v2.1 §2.5
    /// locked tuning knobs). Surfaces a warning banner when true.
    /// </summary>
    public bool AdvancedModeUnlocked { get; set; }

    // ─── Monitored items ─────────────────────────────────────────────

    /// <summary>
    /// Selected OPC UA nodes (populated via the Browse step or hand-
    /// entered on an advanced row).
    /// </summary>
    public List<MonitoredItemWizardRow> MonitoredItems { get; set; } = new();

    // ─── Derived properties for the UI ───────────────────────────────

    /// <summary>
    /// Live tag-count classification surfaced by amendment #3's sticky
    /// Browse-step summary panel + amendment #5's Review Operational
    /// Expectations section.
    /// </summary>
    public TagCountClassification SelectionSummary =>
        TagCountThresholds.Classify(MonitoredItems.Count);

    /// <summary>
    /// Whether Save is allowed. Tag-count <see cref="TagCountSeverity.Blocked"/>
    /// disables Save per amendment #3 — exceeds the per-session cap.
    /// </summary>
    public bool CanSave =>
        !string.IsNullOrWhiteSpace(InstanceId)
        && !string.IsNullOrWhiteSpace(EndpointUrl)
        && EndpointUrl.StartsWith("opc.tcp://", StringComparison.OrdinalIgnoreCase)
        && !SelectionSummary.BlocksSave
        && !ValidateCoherenceIssues().Any();

    /// <summary>
    /// Add a monitored item from a Browse-step selection or hand-entered
    /// row. Per amendment #2, semantically-equal NodeIds (via
    /// <see cref="OpcUaNodeIdCanonicalizer"/>) collapse to the existing
    /// row rather than creating a duplicate.
    /// </summary>
    public bool TryAddMonitoredItem(string nodeId, string displayName)
    {
        var canonical = OpcUaNodeIdCanonicalizer.Canonicalize(nodeId);
        if (string.IsNullOrEmpty(canonical)) return false;

        foreach (var existing in MonitoredItems)
        {
            if (OpcUaNodeIdCanonicalizer.Canonicalize(existing.NodeId) == canonical)
            {
                // Amendment #2 — de-dup via semantic canonicalisation.
                return false;
            }
        }

        MonitoredItems.Add(new MonitoredItemWizardRow
        {
            NodeId = nodeId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? nodeId : displayName,
        });
        return true;
    }

    /// <summary>
    /// Remove a monitored item by index. Defensive bounds-check so the
    /// razor's row-delete button can't crash the wizard.
    /// </summary>
    public void RemoveMonitoredItemAt(int index)
    {
        if (index < 0 || index >= MonitoredItems.Count) return;
        MonitoredItems.RemoveAt(index);
    }

    // ─── Coherence validation ────────────────────────────────────────

    /// <summary>
    /// Cross-field coherence issues surfaced via the wizard's validation
    /// banner. Mirrors the adapter's runtime <c>ValidateConfigAsync</c>
    /// rules so operators get the same feedback at draft time.
    /// </summary>
    public IReadOnlyList<ValidationIssue> ValidateCoherenceIssues()
    {
        var issues = new List<ValidationIssue>();

        if (AuthMode == OpcUaAuthMode.UserName && SecurityMode == OpcUaSecurityMode.None)
        {
            issues.Add(new ValidationIssue
            {
                Code = "OPCUA.UNSAFE_USERNAME_OVER_NONE",
                Message = "UserName authentication requires SecurityMode != None — credentials would "
                    + "be transmitted in cleartext.",
                Path = "$.SecurityMode",
            });
        }

        if (AuthMode == OpcUaAuthMode.UserName
            && (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password)))
        {
            issues.Add(new ValidationIssue
            {
                Code = "OPCUA.USERNAME_CREDENTIALS_MISSING",
                Message = "UserName auth requires both Username and Password.",
                Path = "$.Credentials",
            });
        }

        if (AuthMode == OpcUaAuthMode.Certificate && string.IsNullOrEmpty(CertificatePath))
        {
            issues.Add(new ValidationIssue
            {
                Code = "OPCUA.CERT_PATH_MISSING",
                Message = "Certificate auth requires CertificatePath to be set.",
                Path = "$.CertificatePath",
            });
        }

        if (LifetimeCount < KeepAliveCount * 3)
        {
            issues.Add(new ValidationIssue
            {
                Code = "OPCUA.CONFIG_LIFETIME_TOO_SHORT",
                Message = $"LifetimeCount ({LifetimeCount}) must be ≥ 3× KeepAliveCount ({KeepAliveCount}).",
                Path = "$.LifetimeCount",
            });
        }

        if (NotificationChannelCapacity < OpcUaClientSourceConfiguration.MinimumNotificationChannelCapacity
            || NotificationChannelCapacity > OpcUaClientSourceConfiguration.MaximumNotificationChannelCapacity)
        {
            issues.Add(new ValidationIssue
            {
                Code = "OPCUA.CONFIG_CHANNEL_CAPACITY_OUT_OF_RANGE",
                Message = $"NotificationChannelCapacity ({NotificationChannelCapacity}) must lie within "
                    + $"[{OpcUaClientSourceConfiguration.MinimumNotificationChannelCapacity}, "
                    + $"{OpcUaClientSourceConfiguration.MaximumNotificationChannelCapacity}].",
                Path = "$.NotificationChannelCapacity",
            });
        }

        return issues;
    }

    // ─── Wire-shape conversion ───────────────────────────────────────

    /// <summary>
    /// Project the wizard state into a canonical
    /// <see cref="SourceInstanceConfig"/>.
    /// <see cref="OpcUaClientSourceConfiguration.FromSourceInstance"/>
    /// (PR 7c-3) is the inverse — round-trip is byte-equivalent.
    /// </summary>
    public SourceInstanceConfig BuildSourceInstance()
    {
        var connection = new JsonObject
        {
            ["endpointUrl"] = EndpointUrl,
            ["applicationName"] = ApplicationName,
            ["applicationUri"] = ApplicationUri,
            ["sessionTimeoutMs"] = SessionTimeoutMs,
            ["securityMode"] = SecurityMode.ToString(),
            ["securityPolicyUri"] = SecurityPolicyUri,
            ["authMode"] = AuthMode.ToString(),
            ["autoAcceptUntrustedServerCertificate"] = AutoAcceptUntrustedServerCertificate,
            ["publishingIntervalMs"] = PublishingIntervalMs,
            ["samplingIntervalMs"] = SamplingIntervalMs,
            ["keepAliveCount"] = KeepAliveCount,
            ["lifetimeCount"] = LifetimeCount,
            ["maxNotificationsPerPublish"] = MaxNotificationsPerPublish,
            ["defaultAnalogQueueSize"] = DefaultAnalogQueueSize,
            ["defaultDiscreteQueueSize"] = DefaultDiscreteQueueSize,
            ["notificationChannelCapacity"] = NotificationChannelCapacity,
        };

        if (!string.IsNullOrWhiteSpace(ApplicationCertificateStorePath))
        {
            connection["applicationCertificateStorePath"] = ApplicationCertificateStorePath;
        }

        if (AuthMode != OpcUaAuthMode.Anonymous)
        {
            var creds = new JsonObject();
            if (!string.IsNullOrWhiteSpace(Username)) creds["username"] = Username;
            if (!string.IsNullOrWhiteSpace(Password)) creds["password"] = Password;
            if (!string.IsNullOrWhiteSpace(CertificatePath)) creds["certificatePath"] = CertificatePath;
            connection["credentials"] = creds;
        }

        var items = new JsonArray();
        foreach (var row in MonitoredItems)
        {
            var item = new JsonObject
            {
                ["nodeId"] = row.NodeId,
                ["displayName"] = string.IsNullOrWhiteSpace(row.DisplayName) ? row.NodeId : row.DisplayName,
            };
            if (row.SamplingIntervalMs is { } sampling) item["samplingIntervalMs"] = sampling;
            if (row.QueueSize is { } queue) item["queueSize"] = queue;
            if (row.DeadbandPercent is { } deadband) item["deadbandPercent"] = deadband;
            items.Add(item);
        }
        connection["monitoredItems"] = items;

        var json = connection.ToJsonString();
        using var doc = JsonDocument.Parse(json);
        return new SourceInstanceConfig
        {
            InstanceId = InstanceId,
            ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
            DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? InstanceId : DeviceId,
            DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? InstanceId : DeviceName,
            DeviceClass = DeviceClass,
            Enabled = Enabled,
            Polling = new PollingSettings { IntervalMs = PollIntervalMs },
            Connection = doc.RootElement.Clone(),
        };
    }

    /// <summary>
    /// Inverse of <see cref="BuildSourceInstance"/> — populate a fresh
    /// wizard model from a canonical <see cref="SourceInstanceConfig"/>.
    /// Used by Edit-mode routing to hydrate the form with the source's
    /// current settings. MonitoredItems order is preserved exactly.
    /// </summary>
    /// <remarks>
    /// Round-trip invariant: build → hydrate → build again produces a
    /// byte-equivalent <see cref="SourceInstanceConfig"/>. Amendment #5's
    /// idempotent edit-mode save depends on this — when the operator
    /// changes nothing and presses Save, the diff against the live
    /// adapter is empty.
    /// </remarks>
    public static OpcUaClientSourceWizardModel BuildFromExisting(SourceInstanceConfig instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var typed = OpcUaClientSourceConfiguration.FromSourceInstance(instance);

        return new OpcUaClientSourceWizardModel
        {
            InstanceId = typed.InstanceId,
            DeviceId = typed.DeviceId ?? string.Empty,
            DeviceName = instance.DeviceName ?? string.Empty,
            DeviceClass = instance.DeviceClass ?? "scada",
            Enabled = typed.Enabled,
            PollIntervalMs = instance.Polling.IntervalMs,

            EndpointUrl = typed.EndpointUrl,
            ApplicationName = typed.ApplicationName,
            ApplicationUri = typed.ApplicationUri,
            SessionTimeoutMs = typed.SessionTimeoutMs,

            SecurityMode = typed.SecurityMode,
            SecurityPolicyUri = typed.SecurityPolicyUri,
            AutoAcceptUntrustedServerCertificate = typed.AutoAcceptUntrustedServerCertificate,
            ApplicationCertificateStorePath = typed.ApplicationCertificateStorePath,

            AuthMode = typed.AuthMode,
            Username = typed.Credentials?.Username,
            Password = typed.Credentials?.Password,
            CertificatePath = typed.Credentials?.CertificatePath,

            PublishingIntervalMs = typed.PublishingIntervalMs,
            SamplingIntervalMs = typed.SamplingIntervalMs,
            KeepAliveCount = typed.KeepAliveCount,
            LifetimeCount = typed.LifetimeCount,
            MaxNotificationsPerPublish = typed.MaxNotificationsPerPublish,
            DefaultAnalogQueueSize = typed.DefaultAnalogQueueSize,
            DefaultDiscreteQueueSize = typed.DefaultDiscreteQueueSize,
            NotificationChannelCapacity = typed.NotificationChannelCapacity,

            MonitoredItems = typed.MonitoredItems.Select(mi => new MonitoredItemWizardRow
            {
                NodeId = mi.NodeId,
                DisplayName = mi.DisplayName,
                SamplingIntervalMs = mi.SamplingIntervalMs,
                QueueSize = mi.QueueSize,
                DeadbandPercent = mi.DeadbandPercent,
            }).ToList(),
        };
    }
}

/// <summary>
/// One row of the wizard's MonitoredItems table. Two-way bound to MudGrid
/// rows on the Browse-step results panel.
/// </summary>
public sealed class MonitoredItemWizardRow
{
    /// <summary>Protocol-native OPC UA NodeId string.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Operator-facing display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional per-item sampling-interval override (milliseconds).</summary>
    public int? SamplingIntervalMs { get; set; }

    /// <summary>Optional per-item queue-size override.</summary>
    public uint? QueueSize { get; set; }

    /// <summary>Optional per-tag percent deadband.</summary>
    public double? DeadbandPercent { get; set; }
}
