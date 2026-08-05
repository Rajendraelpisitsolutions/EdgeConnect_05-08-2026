// ============================================================================
// File: Focas2BundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the FOCAS2 source connection
//          block (ADR-0020 Amendment 1, A1.2). FOCAS2 has no authentication in
//          its connection block — every key is INCLUDE. Metadata only.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Redaction rules for the FOCAS2 source. The connection block (IP, port,
/// timeouts, data-point selection, backoff) holds no secret — every key is
/// <see cref="BundleTier.Include"/>.
/// </summary>
public sealed class Focas2BundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => Focas2SourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        Focas2ConnectionKeys.All.ToDictionary(k => k, _ => BundleTier.Include);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
