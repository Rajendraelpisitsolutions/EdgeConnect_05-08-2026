// ============================================================================
// File: OpcUaServerConnectionKeys.cs
// Purpose: Single source of truth for the JSON key names in an OPC UA Server
//          sink's opaque connection block, including the keys nested under
//          `namespace`, `security`, and `security.credentials[]`. Factory +
//          redaction rules name keys only via these constants (ADR-0020 M-B
//          Q-B2, plan §3a).
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>JSON key-name constants for the OPC UA Server sink connection block.</summary>
public static class OpcUaServerConnectionKeys
{
    // ---- Top-level ----
    /// <summary>Server endpoint URL.</summary>
    public const string EndpointUrl = "endpointUrl";
    /// <summary>Server application URI.</summary>
    public const string ApplicationUri = "applicationUri";
    /// <summary>Server application name.</summary>
    public const string ApplicationName = "applicationName";
    /// <summary>Max concurrent sessions.</summary>
    public const string MaxSessions = "maxSessions";
    /// <summary>Max monitored items per session.</summary>
    public const string MaxMonitoredItemsPerSession = "maxMonitoredItemsPerSession";
    /// <summary>Minimum publishing interval in ms.</summary>
    public const string MinPublishingIntervalMs = "minPublishingIntervalMs";
    /// <summary>Namespace config object.</summary>
    public const string Namespace = "namespace";
    /// <summary>Security config object.</summary>
    public const string Security = "security";

    // ---- Namespace (nested under `namespace`) ----
    /// <summary>Namespace URI.</summary>
    public const string NamespaceUri = "namespaceUri";
    /// <summary>Root folder name.</summary>
    public const string RootFolder = "rootFolder";
    /// <summary>Browse-path template.</summary>
    public const string BrowsePathTemplate = "browsePathTemplate";
    /// <summary>NodeId template.</summary>
    public const string NodeIdTemplate = "nodeIdTemplate";

    // ---- Security (nested under `security`) ----
    /// <summary>Security mode.</summary>
    public const string Mode = "mode";
    /// <summary>Application certificate store PATH (not key material).</summary>
    public const string ApplicationCertificatePath = "applicationCertificatePath";
    /// <summary>Trusted-clients store PATH.</summary>
    public const string TrustedClientsPath = "trustedClientsPath";
    /// <summary>Rejected-clients store PATH.</summary>
    public const string RejectedClientsPath = "rejectedClientsPath";
    /// <summary>User token policies array.</summary>
    public const string UserTokenPolicies = "userTokenPolicies";
    /// <summary>Credentials array.</summary>
    public const string Credentials = "credentials";

    // ---- Credential (nested under `security.credentials[]`) ----
    /// <summary>User name (nested).</summary>
    public const string Username = "username";
    /// <summary>User password (nested, secret).</summary>
    public const string Password = "password";
    /// <summary>Display name (nested).</summary>
    public const string DisplayName = "displayName";

    /// <summary>Ordered list of every connection key the OPC UA Server factory reads.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        EndpointUrl,
        ApplicationUri,
        ApplicationName,
        MaxSessions,
        MaxMonitoredItemsPerSession,
        MinPublishingIntervalMs,
        Namespace,
        Security,
        NamespaceUri,
        RootFolder,
        BrowsePathTemplate,
        NodeIdTemplate,
        Mode,
        ApplicationCertificatePath,
        TrustedClientsPath,
        RejectedClientsPath,
        UserTokenPolicies,
        Credentials,
        Username,
        Password,
        DisplayName,
    };
}
