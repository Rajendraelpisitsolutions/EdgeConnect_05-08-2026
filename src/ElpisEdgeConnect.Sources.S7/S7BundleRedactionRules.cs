// ============================================================================
// File: S7BundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the Siemens S7 source connection
//          block (ADR-0020 Amendment 1, A1.2). S7comm carries no credentials in
//          this connection block — every key (connection + per-tag) is INCLUDE.
//          Metadata only.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Redaction rules for the Siemens S7 source. The connection block (host, rack,
/// slot, timeouts) and per-tag definitions (S7 addresses, datatypes, scaling)
/// hold no secret — every key is <see cref="BundleTier.Include"/>.
/// </summary>
public sealed class S7BundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => S7SourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        S7ConnectionKeys.All.ToDictionary(k => k, _ => BundleTier.Include);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
