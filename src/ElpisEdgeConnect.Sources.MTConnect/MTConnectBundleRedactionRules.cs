// ============================================================================
// File: MTConnectBundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the MTConnect source connection
//          block (ADR-0020 Amendment 1, A1.2). No credentials in the connection
//          block — every key is INCLUDE. Metadata only.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>
/// Redaction rules for the MTConnect source. The Agent base URL, device name,
/// timeouts, and backoff carry no secret — every key is
/// <see cref="BundleTier.Include"/>.
/// </summary>
public sealed class MTConnectBundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => MTConnectSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        MTConnectConnectionKeys.All.ToDictionary(k => k, _ => BundleTier.Include);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
