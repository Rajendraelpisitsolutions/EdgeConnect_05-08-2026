// ============================================================================
// File: OpcUaClientApplicationConfigurationBuilder.cs
// Purpose: Translates an OpcUaClientSourceConfiguration into the OPC
//          Foundation stack's ApplicationConfiguration. Pure logic — no
//          network calls, no file I/O beyond what the cert manager
//          delegates to. Tests cover the shape of the produced
//          ApplicationConfiguration against the input config.
//
//          The 12 v2.1 §2.5 tuning knobs that LIVE on the
//          ApplicationConfiguration / ClientConfiguration (vs the
//          Subscription) are applied here:
//             * DefaultSessionTimeout
//             * MinSubscriptionLifetime
//             * (TransportQuotas.OperationTimeout already at 30s default)
//          Subscription-level knobs (PublishingInterval, KeepAliveCount,
//          MaxNotificationsPerPublish, etc.) live on the per-subscription
//          factory in PR 3.
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5
// ============================================================================

using System.Threading.Tasks;
using Opc.Ua;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Build an OPC Foundation <see cref="ApplicationConfiguration"/> from
/// an <see cref="OpcUaClientSourceConfiguration"/>.
/// </summary>
internal sealed class OpcUaClientApplicationConfigurationBuilder
{
    private readonly OpcUaClientSourceConfiguration _config;
    private readonly OpcUaClientCertManager _certManager;

    public OpcUaClientApplicationConfigurationBuilder(
        OpcUaClientSourceConfiguration config,
        OpcUaClientCertManager certManager)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(certManager);
        _config = config;
        _certManager = certManager;
    }

    /// <summary>
    /// Build and <see cref="ApplicationConfiguration.Validate"/> the
    /// configuration. The stack requires the validate step before any
    /// session-create call; baking it into the builder ensures every
    /// caller hits the same shape.
    /// </summary>
    public async Task<ApplicationConfiguration> BuildAndValidateAsync()
    {
        var appConfig = Build();
        await appConfig.Validate(ApplicationType.Client).ConfigureAwait(false);
        return appConfig;
    }

    /// <summary>
    /// Build the configuration WITHOUT calling Validate. Exposed for
    /// tests that want to inspect the shape before the stack's
    /// validator mutates it.
    /// </summary>
    internal ApplicationConfiguration Build() =>
        new()
        {
            ApplicationName = _config.ApplicationName,
            ApplicationUri = _config.ApplicationUri,
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = _certManager.BuildSecurityConfiguration(),
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 30_000,
                MaxStringLength = 1_048_576,
                MaxByteStringLength = 4_194_304,
                MaxArrayLength = 65_535,
                MaxMessageSize = 4_194_304,
                MaxBufferSize = 65_535,
                ChannelLifetime = 300_000,
                SecurityTokenLifetime = 3_600_000,
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = (int)_config.SessionTimeoutMs,
                MinSubscriptionLifetime = 10_000,
            },
            TraceConfiguration = new TraceConfiguration { TraceMasks = 0 },
        };
}
