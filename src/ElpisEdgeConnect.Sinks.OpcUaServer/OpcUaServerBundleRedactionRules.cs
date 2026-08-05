// ============================================================================
// File: OpcUaServerBundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the OPC UA Server sink
//          connection block (ADR-0020 Amendment 1, A1.2). The only secret is
//          the per-user credential password (MASK). Certificate-store fields are
//          file-system PATHS, not inline key material, so they are INCLUDE.
//          Metadata only.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Redaction rules for the OPC UA Server sink. The server-side user
/// <see cref="OpcUaServerConnectionKeys.Password"/> is masked (re-enterable on
/// restore); endpoint, namespace, security mode, certificate-store <em>paths</em>,
/// and tuning are benign and included verbatim. There is no inline certificate
/// or private-key material in this connection block (only store paths), so no
/// <see cref="ExtraNameOverrides"/> are required.
/// </summary>
public sealed class OpcUaServerBundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => OpcUaServerConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        OpcUaServerConnectionKeys.All.ToDictionary(
            k => k,
            k => k == OpcUaServerConnectionKeys.Password ? BundleTier.Mask : BundleTier.Include);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
