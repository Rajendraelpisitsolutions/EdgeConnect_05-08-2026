// ============================================================================
// File: Licensing/AllowAllLicenseGate.cs
// Purpose: Default ILicenseGate implementation used in B2 (and in tests).
//          Permits every configuration. Replaced by a real signed-license-
//          validating implementation in B3.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// <see cref="ILicenseGate"/> that always permits the configuration.
/// Used in B2 (and in tests that do not exercise license behaviour) so
/// that the configuration validator pipeline can be wired now without
/// waiting for B3's real license manager.
/// </summary>
/// <remarks>
/// Singleton via <see cref="Instance"/>; thread-safe and stateless.
/// </remarks>
public sealed class AllowAllLicenseGate : ILicenseGate
{
    /// <summary>Shared singleton instance.</summary>
    public static AllowAllLicenseGate Instance { get; } = new();

    private AllowAllLicenseGate()
    {
    }

    /// <inheritdoc/>
    public ValueTask<LicenseGateResult> EvaluateAsync(
        GatewayConfiguration configuration,
        CancellationToken cancellationToken)
    {
        // Configuration is intentionally not inspected. Replaced in B3.
        _ = configuration;
        return new ValueTask<LicenseGateResult>(LicenseGateResult.AllowAll);
    }
}
