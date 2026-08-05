// ============================================================================
// File: DefaultOpcUaClientConnectionEstablisher.cs
// Purpose: Production implementation of IOpcUaClientConnectionEstablisher.
//          Runs the full real connect pipeline against a live OPC UA
//          server. Used by OpcUaClientSourceAdapter in production wiring.
//
// LOCKED steps (must run in this exact order):
//   1. Build OPC UA ApplicationConfiguration via the builder
//   2. Validate it against ApplicationType.Client
//   3. Ensure the application instance certificate exists (cert mgr)
//   4. Select the endpoint (CoreClientUtils.SelectEndpoint)
//   5. Build the user identity (Anonymous / UserName / Certificate)
//   6. Hand off to IOpcUaClientSessionFactory for the actual Session.Create
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §5.1
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Production <see cref="IOpcUaClientConnectionEstablisher"/>. Performs
/// real I/O — cert store creation, endpoint discovery, session create.
/// </summary>
internal sealed class DefaultOpcUaClientConnectionEstablisher : IOpcUaClientConnectionEstablisher
{
    private readonly IOpcUaClientSessionFactory _sessionFactory;

    public DefaultOpcUaClientConnectionEstablisher(IOpcUaClientSessionFactory? sessionFactory = null)
    {
        _sessionFactory = sessionFactory ?? new DefaultOpcUaClientSessionFactory();
    }

    public async Task<ISession> EstablishAsync(
        OpcUaClientSourceConfiguration config,
        string sessionName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        var certManager = new OpcUaClientCertManager(config);

        // 1. Build + validate the application configuration.
        var builder = new OpcUaClientApplicationConfigurationBuilder(config, certManager);
        var appConfig = await builder.BuildAndValidateAsync().ConfigureAwait(false);

        // 2. Ensure application instance certificate exists.
        var applicationInstance = new ApplicationInstance(appConfig);
        await certManager.EnsureApplicationCertificateAsync(applicationInstance, ct).ConfigureAwait(false);

        // 3. Discover + select the endpoint.
        //
        // We honour BOTH config.SecurityMode AND config.SecurityPolicyUri
        // when selecting from the server's advertised endpoints. The
        // earlier implementation passed only `useSecurity: bool` to
        // CoreClientUtils.SelectEndpoint, which let the stack pick the
        // highest-security endpoint the server advertises — making
        // config.SecurityPolicyUri effectively decoration. Real symptom
        // caught in live testing: operator's config said Basic256Sha256
        // but the actual negotiated policy was a higher-numbered Aes*
        // policy, and the operator's client cert was trusted server-side
        // only for the Basic256Sha256 slot, so Browse succeeded (cert
        // exchange completes for SecureChannel open) but subscription
        // operations were silently rejected. See task #55 + the
        // 2026-05-30 follow-ups doc.
        var endpointDescription = SelectEndpointMatching(appConfig, config);
        var endpoint = new ConfiguredEndpoint(
            collection: null,
            description: endpointDescription,
            configuration: EndpointConfiguration.Create(appConfig));

        // 4. Build the user identity.
        var userIdentity = BuildUserIdentity(config);

        // 5. Hand off to the session factory.
        return await _sessionFactory.CreateAsync(
            applicationConfiguration: appConfig,
            endpoint: endpoint,
            userIdentity: userIdentity,
            sessionTimeoutMs: config.SessionTimeoutMs,
            sessionName: sessionName,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Discover the endpoints the server advertises and pick the one
    /// matching BOTH the requested SecurityMode AND the requested
    /// SecurityPolicyUri. When no exact match exists, throw a
    /// classified error listing what the server actually offers so the
    /// operator can correct their config.
    /// </summary>
    /// <remarks>
    /// For <see cref="OpcUaSecurityMode.None"/> we fall through to
    /// <see cref="CoreClientUtils.SelectEndpoint(ApplicationConfiguration, string, bool)"/>
    /// with <c>useSecurity=false</c> — the policy URI is irrelevant when
    /// there's no security to negotiate.
    /// </remarks>
    private static EndpointDescription SelectEndpointMatching(
        ApplicationConfiguration appConfig,
        OpcUaClientSourceConfiguration config)
    {
        // None mode: no policy to match. Just use the stack helper.
        if (config.SecurityMode == OpcUaSecurityMode.None)
        {
            return CoreClientUtils.SelectEndpoint(appConfig, config.EndpointUrl, useSecurity: false);
        }

        var requestedMode = config.SecurityMode switch
        {
            OpcUaSecurityMode.Sign => MessageSecurityMode.Sign,
            OpcUaSecurityMode.SignAndEncrypt => MessageSecurityMode.SignAndEncrypt,
            _ => MessageSecurityMode.None,
        };

        var discoveryEndpoint = new Uri(config.EndpointUrl);
        var endpointConfiguration = EndpointConfiguration.Create(appConfig);
        endpointConfiguration.OperationTimeout = 10_000;

        using var discoveryClient = DiscoveryClient.Create(discoveryEndpoint, endpointConfiguration);
        var endpoints = discoveryClient.GetEndpoints(profileUris: null);

        // Exact match on (mode, policy URI).
        foreach (var ep in endpoints)
        {
            if (ep.SecurityMode == requestedMode
                && string.Equals(ep.SecurityPolicyUri, config.SecurityPolicyUri, StringComparison.Ordinal))
            {
                return ep;
            }
        }

        // No exact match — surface the available combinations so the
        // operator can correct their config rather than guess.
        var availablePolicies = string.Join(", ",
            endpoints
                .Where(ep => ep.SecurityMode == requestedMode)
                .Select(ep => ep.SecurityPolicyUri)
                .Distinct());

        if (string.IsNullOrEmpty(availablePolicies))
        {
            throw new InvalidOperationException(
                $"OPCUA.NO_MATCHING_SECURITY_ENDPOINT: server at {config.EndpointUrl} does not "
                + $"advertise any endpoint for SecurityMode={config.SecurityMode}. Try a different "
                + "SecurityMode or verify the server is configured to expose the requested mode.");
        }

        throw new InvalidOperationException(
            $"OPCUA.NO_MATCHING_SECURITY_ENDPOINT: server at {config.EndpointUrl} does not advertise "
            + $"SecurityMode={config.SecurityMode} + SecurityPolicyUri={config.SecurityPolicyUri}. "
            + $"For this SecurityMode, the server offers these policies: [{availablePolicies}]. "
            + "Update SecurityPolicyUri in the source config to one of the listed values.");
    }

    private static UserIdentity BuildUserIdentity(OpcUaClientSourceConfiguration config) => config.AuthMode switch
    {
        OpcUaAuthMode.Anonymous => new UserIdentity(new AnonymousIdentityToken()),
        OpcUaAuthMode.UserName => new UserIdentity(
            config.Credentials?.Username ?? throw new InvalidOperationException(
                "OPCUA.USERNAME_CREDENTIALS_MISSING: AuthMode is UserName but Credentials.Username is null."),
            config.Credentials?.Password ?? throw new InvalidOperationException(
                "OPCUA.USERNAME_CREDENTIALS_MISSING: AuthMode is UserName but Credentials.Password is null.")),
        OpcUaAuthMode.Certificate => throw new NotSupportedException(
            "OPCUA.AUTH_CERTIFICATE_NOT_YET_WIRED: Certificate auth lands in a follow-up PR; "
            + "see plan v2.1 §5.1 — the cert manager's user-token-policy wiring is scoped separately."),
        _ => throw new InvalidOperationException($"OPCUA.UNKNOWN_AUTH_MODE: '{config.AuthMode}'."),
    };
}
