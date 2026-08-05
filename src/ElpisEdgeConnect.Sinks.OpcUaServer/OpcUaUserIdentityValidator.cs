// ============================================================================
// File: OpcUaUserIdentityValidator.cs
// Purpose: Validates OPC UA session user-identity tokens against the
//          configured policy + credential list:
//             - Anonymous: accepted iff the policy is enabled.
//             - UserName: matched against OpcUaSecurityConfig.Credentials
//                         (constant-time password compare).
//             - Certificate: deferred to the OPC UA library's trust-list
//                            validation; we accept any cert the library
//                            considered trusted (i.e. present in the
//                            TrustedClientsPath store).
//
//          Invoked by EdgeConnectStandardServer through the standard
//          SessionManager.ImpersonateUser event seam. On any failure
//          the validator throws ServiceResultException with
//          BadIdentityTokenRejected, which the library propagates as
//          the OPC UA-standard error response.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone K
//            shared-knowledge/contracts/opcua-namespace-policy.md
//                (§ Sessions, § Cert management)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Wires into a <see cref="StandardServer"/>'s SessionManager and
/// validates every <see cref="UserIdentityToken"/> the server is
/// asked to impersonate. The validator is stateless across sessions
/// — it captures the configured policy + credentials at construction
/// time so policy changes require a gateway restart (matching the
/// rest of the OPC UA Server lifecycle).
/// </summary>
public sealed class OpcUaUserIdentityValidator
{
    private readonly OpcUaSecurityConfig _security;
    private readonly ILogger _logger;
    private readonly HashSet<OpcUaUserTokenPolicy> _enabledPolicies;
    private SessionManager? _sessionManager;

    /// <summary>Construct a validator around an immutable security config.</summary>
    public OpcUaUserIdentityValidator(OpcUaSecurityConfig security, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(logger);
        _security = security;
        _logger = logger;
        _enabledPolicies = new HashSet<OpcUaUserTokenPolicy>(security.UserTokenPolicies);
    }

    /// <summary>Attach to a started server's session manager. Idempotent.</summary>
    public void Attach(StandardServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (_sessionManager is not null)
        {
            return;
        }
        _sessionManager = (SessionManager)server.CurrentInstance.SessionManager;
        _sessionManager.ImpersonateUser += OnImpersonateUser;
    }

    /// <summary>Detach. Safe to call multiple times.</summary>
    public void Detach()
    {
        if (_sessionManager is null)
        {
            return;
        }
        _sessionManager.ImpersonateUser -= OnImpersonateUser;
        _sessionManager = null;
    }

    private void OnImpersonateUser(Session session, ImpersonateEventArgs args)
    {
        try
        {
            ValidateOrThrow(args);
            args.Identity = BuildAcceptedIdentity(args);
            args.IdentityValidationError = null;
        }
        catch (ServiceResultException ex)
        {
            // Standard OPC UA rejection — the library propagates this
            // to the client as the appropriate BadIdentity* error code.
            _logger.LogWarning(
                "OPC UA session {SessionId} rejected: {Code} {Message}",
                session?.Id, ex.StatusCode, ex.Message);
            args.IdentityValidationError = new ServiceResult(ex.StatusCode, ex.Message);
        }
    }

    private void ValidateOrThrow(ImpersonateEventArgs args)
    {
        switch (args.NewIdentity)
        {
            case AnonymousIdentityToken:
                EnsureEnabled(OpcUaUserTokenPolicy.Anonymous);
                break;

            case UserNameIdentityToken un:
                EnsureEnabled(OpcUaUserTokenPolicy.UserName);
                ValidateUserNameToken(un);
                break;

            case X509IdentityToken cert:
                EnsureEnabled(OpcUaUserTokenPolicy.Certificate);
                ValidateCertificateToken(cert);
                break;

            default:
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    $"Unsupported identity token type: {args.NewIdentity?.GetType().Name ?? "(null)"}.");
        }
    }

    private void EnsureEnabled(OpcUaUserTokenPolicy policy)
    {
        if (!_enabledPolicies.Contains(policy))
        {
            throw new ServiceResultException(
                StatusCodes.BadIdentityTokenRejected,
                $"User token policy '{policy}' is not enabled on this endpoint.");
        }
    }

    private void ValidateUserNameToken(UserNameIdentityToken token)
    {
        if (string.IsNullOrEmpty(token.UserName))
        {
            throw new ServiceResultException(
                StatusCodes.BadIdentityTokenInvalid,
                "UserName token has no UserName.");
        }
        if (_security.Credentials.Count == 0)
        {
            throw new ServiceResultException(
                StatusCodes.BadIdentityTokenRejected,
                "Server has no configured credentials — UserName authentication is unavailable.");
        }

        // Token.DecryptedPassword is the standard property OPC UA's
        // server-side machinery exposes after the secure-channel layer
        // has decrypted the password under the session's security
        // policy. For SecurityMode=None it's the plain-text bytes;
        // for Sign/SignAndEncrypt it's been decrypted by the channel.
        var password = token.DecryptedPassword;

        var match = _security.Credentials.FirstOrDefault(c =>
            string.Equals(c.Username, token.UserName, StringComparison.Ordinal));
        if (match is null || !match.MatchesPassword(password))
        {
            throw new ServiceResultException(
                StatusCodes.BadUserAccessDenied,
                $"Invalid credentials for user '{token.UserName}'.");
        }
    }

    private void ValidateCertificateToken(X509IdentityToken token)
    {
        if (token.Certificate is null)
        {
            throw new ServiceResultException(
                StatusCodes.BadIdentityTokenInvalid,
                "X509 token has no Certificate.");
        }
        // The OPC UA library already validates the client's cert
        // against TrustedPeerCertificates during session activation
        // (operators promote certs from the rejected/ folder into
        // trusted/). We accept anything the library considered
        // trusted. K' can add per-cert authorization (subject DN
        // → role mapping) once Customer B's MES requires it.
    }

    private static UserIdentity BuildAcceptedIdentity(ImpersonateEventArgs args) =>
        args.NewIdentity switch
        {
            UserNameIdentityToken un => new UserIdentity(un),
            X509IdentityToken cert => new UserIdentity(cert),
            _ => new UserIdentity(),
        };
}
