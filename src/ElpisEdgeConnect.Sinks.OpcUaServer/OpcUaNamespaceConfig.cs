// ============================================================================
// File: OpcUaNamespaceConfig.cs
// Purpose: Template-driven OPC UA namespace shape — the consumer-facing
//          contract that SCADA/MES clients depend on. Settling this
//          shape correctly before any code lands is the entire point of
//          Milestone H.0.
//
//          The CRITICAL invariants:
//            * NamespaceUri is versioned (`urn:elpis:edgeconnect:v1`).
//              Reserved `:v2` for future shape evolution.
//            * NodeId is STABLE: derived from {gatewayId}/{sourceId}/{StableTagId}.
//              Renaming a tag's display name does NOT change its NodeId.
//            * BrowsePath is TEMPLATE-DRIVEN: operators pick the hierarchy
//              levels in config (site, area, line, deviceClass, sourceId,
//              tagName, category). NOT hardcoded Factory/Site/Line.
//
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.0
//            shared-knowledge/contracts/opcua-namespace-policy.md (authoritative)
// ============================================================================

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Configures the OPC UA Server's address-space shape — what clients see
/// when they browse the endpoint and what NodeIds programmatic clients
/// pin against. See <c>shared-knowledge/contracts/opcua-namespace-policy.md</c>
/// for the authoritative consumer-facing contract, including the
/// stability and versioning guarantees.
/// </summary>
public sealed record OpcUaNamespaceConfig
{
    /// <summary>
    /// OPC UA NamespaceUri. Versioned — <c>urn:elpis:edgeconnect:v1</c>
    /// is the stable v1 identifier for the lifetime of v1. Future shape
    /// evolutions ship as <c>:v2</c> and run side-by-side during a
    /// migration window. Customers should NOT change this manually.
    /// </summary>
    public string NamespaceUri { get; init; } = "urn:elpis:edgeconnect:v1";

    /// <summary>
    /// Top-level folder under <c>Objects/</c>. Default <c>"EdgeConnect"</c>.
    /// Customer-facing branding can swap this (e.g. <c>"ElpisGateway"</c>)
    /// but the same value MUST be used consistently across deployments —
    /// renaming this breaks every client that browsed the old structure.
    /// </summary>
    public string RootFolder { get; init; } = "EdgeConnect";

    /// <summary>
    /// Operator-readable BrowsePath template. Placeholders are substituted
    /// from gateway / source / tag metadata at address-space build time:
    /// <list type="bullet">
    ///   <item><c>{site}</c> / <c>{area}</c> — from GatewaySettings</item>
    ///   <item><c>{line}</c> / <c>{assetClass}</c> / <c>{sourceId}</c> — from SourceConfiguration</item>
    ///   <item><c>{deviceClass}</c> — from SourceConfiguration.DeviceClass</item>
    ///   <item><c>{category}</c> — from TagSemantics.Category (when present)</item>
    ///   <item><c>{tagName}</c> — TagSemantics.Name</item>
    /// </list>
    /// Placeholders evaluating to null are silently dropped from the
    /// path. Default mirrors the per-tag MQTT topic shape so a customer
    /// browsing both endpoints sees the same hierarchy.
    /// </summary>
    public string BrowsePathTemplate { get; init; } = "{deviceClass}/{sourceId}/{tagName}";

    /// <summary>
    /// NodeId derivation template. Placeholders:
    /// <list type="bullet">
    ///   <item><c>{gatewayId}</c> — from GatewayConfiguration.Gateway.GatewayId</item>
    ///   <item><c>{sourceId}</c> — from SourceConfiguration.InstanceId</item>
    ///   <item><c>{stableTagId}</c> — from TagSemantics.StableTagId (NOT
    ///         the display Name — the rename-stable identifier)</item>
    /// </list>
    /// <para>
    /// <b>This template controls the IDENTITY contract for OPC UA clients.</b>
    /// Once a license-issued gateway is in production with SCADA / MES
    /// clients subscribed, this template MUST NOT change — clients pin
    /// against the resulting NodeIds. Renaming a tag's display name does
    /// NOT change its NodeId because <c>{stableTagId}</c> is decoupled
    /// from the display name (see Milestone G.6's TagSemantics).
    /// </para>
    /// </summary>
    public string NodeIdTemplate { get; init; } = "ns=2;s={gatewayId}/{sourceId}/{stableTagId}";
}
