// ============================================================================
// File: MelsecBundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the MELSEC source connection
//          block (ADR-0020 Amendment 1). MC 3E binary has no credential
//          exchange in this connection block — host, port, route header,
//          timeouts, and per-tag device addresses hold no secret, so every
//          key is INCLUDE. Metadata only.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>
/// Redaction rules for the MELSEC source. The connection block (host, port,
/// route header, timeouts) and per-tag definitions (device addresses, datatypes,
/// scaling) hold no secret — every key is <see cref="BundleTier.Include"/>.
/// </summary>
public sealed class MelsecBundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => MelsecSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        MelsecConnectionKeys.All.ToDictionary(k => k, _ => BundleTier.Include);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
