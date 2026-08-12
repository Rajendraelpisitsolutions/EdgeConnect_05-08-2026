// ============================================================================
// File: EthernetIpBundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the EtherNet/IP source connection
//          block (ADR-0020 Amendment 1, A1.2). EtherNet/IP has no authentication
//          — every key (connection + per-tag) is INCLUDE. Metadata only.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// Redaction rules for the EtherNet/IP source. The connection block and per-tag
/// definitions (addresses, datatypes, scaling) hold no secret — every key is
/// <see cref="BundleTier.Include"/>.
/// </summary>
public sealed class EthernetIpBundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => EthernetIpSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        EthernetIpConnectionKeys.All.ToDictionary(k => k, _ => BundleTier.Include);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
