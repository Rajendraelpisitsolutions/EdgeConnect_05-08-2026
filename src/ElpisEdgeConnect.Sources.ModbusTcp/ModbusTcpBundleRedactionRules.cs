// ============================================================================
// File: ModbusTcpBundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the Modbus TCP source connection
//          block (ADR-0020 Amendment 1, A1.2). Modbus TCP has no authentication
//          — every key (connection + per-tag) is INCLUDE. Metadata only.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Redaction rules for the Modbus TCP source. The connection block and per-tag
/// definitions (addresses, datatypes, scaling) hold no secret — every key is
/// <see cref="BundleTier.Include"/>.
/// </summary>
public sealed class ModbusTcpBundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => ModbusTcpSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        ModbusTcpConnectionKeys.All.ToDictionary(k => k, _ => BundleTier.Include);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
