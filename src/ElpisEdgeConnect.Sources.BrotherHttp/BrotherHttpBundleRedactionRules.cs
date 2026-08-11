// ============================================================================
// File: BrotherHttpBundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the Brother HTTP source
//          connection block (ADR-0020 Amendment 1, A1.2). No credentials in
//          the connection block — every key is INCLUDE. Metadata only.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Redaction rules for the Brother HTTP source. The base URL, timeouts,
/// backoff, and data-point selection carry no secret — every key is
/// <see cref="BundleTier.Include"/>.
/// </summary>
public sealed class BrotherHttpBundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => BrotherHttpSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        BrotherHttpConnectionKeys.All.ToDictionary(k => k, _ => BundleTier.Include);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
