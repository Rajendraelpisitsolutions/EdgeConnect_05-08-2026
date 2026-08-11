// ============================================================================
// File: IOpcUaClientSessionFactory.cs
// Purpose: Test seam for OPC UA Session creation. The OPC Foundation
//          stack's Session.Create is a static method — impossible to
//          mock directly. This interface wraps it so adapter tests can
//          substitute a fake session factory (NSubstitute) for unit
//          tests, while the production path uses DefaultSessionFactory
//          underneath.
//
//          Returns ISession so consumers can swap implementations
//          (e.g. the standard Session vs a TraceableSession in tests).
//
// LOCKED design rules:
//   * NO defaults baked in here — every parameter is supplied per-call
//     by the adapter. The adapter is the source of truth for
//     useUpdateBeforeConnect / checkDomain / sessionName etc.
//   * The factory does NOT own session lifetime — callers Dispose().
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §5.1
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Test-substitutable Session factory. Production uses
/// <see cref="DefaultOpcUaClientSessionFactory"/>.
/// </summary>
internal interface IOpcUaClientSessionFactory
{
    /// <summary>
    /// Create an OPC UA Session against the supplied endpoint. Returns
    /// a connected, ready-to-use <see cref="ISession"/>; the caller owns
    /// its lifetime.
    /// </summary>
    Task<ISession> CreateAsync(
        ApplicationConfiguration applicationConfiguration,
        ConfiguredEndpoint endpoint,
        IUserIdentity userIdentity,
        uint sessionTimeoutMs,
        string sessionName,
        CancellationToken ct);
}

/// <summary>
/// Production session factory — wraps the OPC Foundation stack's
/// <see cref="DefaultSessionFactory"/>.
/// </summary>
internal sealed class DefaultOpcUaClientSessionFactory : IOpcUaClientSessionFactory
{
    public async Task<ISession> CreateAsync(
        ApplicationConfiguration applicationConfiguration,
        ConfiguredEndpoint endpoint,
        IUserIdentity userIdentity,
        uint sessionTimeoutMs,
        string sessionName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(applicationConfiguration);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(userIdentity);
        ct.ThrowIfCancellationRequested();

        var session = await DefaultSessionFactory.Instance.CreateAsync(
            configuration: applicationConfiguration,
            endpoint: endpoint,
            updateBeforeConnect: false,
            checkDomain: false,
            sessionName: sessionName,
            sessionTimeout: sessionTimeoutMs,
            identity: userIdentity,
            preferredLocales: null,
            ct: ct).ConfigureAwait(false);
        return session;
    }
}
