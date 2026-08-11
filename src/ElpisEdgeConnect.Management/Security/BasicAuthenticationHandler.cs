// ============================================================================
// File: Security/BasicAuthenticationHandler.cs
// Purpose: Minimal HTTP Basic auth handler for the management host.
//          Only activated when ManagementOptions.Auth.Mode == Basic;
//          otherwise the host runs with AllowAnonymous on every
//          endpoint (the localhost-default scenario).
//
//          Compares against the configured user list with a
//          constant-time password compare (same pattern as
//          OpcUaCredential from K). Returns 401 + WWW-Authenticate
//          header on failure so browsers prompt for credentials.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1a
// ============================================================================

using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElpisEdgeConnect.Management.Security;

/// <summary>Scheme constant for the management Basic auth handler.</summary>
public static class ManagementAuthSchemes
{
    /// <summary>Name of the Basic auth scheme registered with ASP.NET Core.</summary>
    public const string Basic = "ManagementBasic";
}

/// <summary>
/// HTTP Basic auth handler against
/// <see cref="ManagementOptions.Auth"/>. Returns 401 with a
/// <c>WWW-Authenticate: Basic realm="..."</c> header on failure.
/// </summary>
public sealed class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ManagementOptions _options;

    /// <inheritdoc/>
    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ManagementOptions managementOptions)
        : base(schemeOptions, logger, encoder)
    {
        ArgumentNullException.ThrowIfNull(managementOptions);
        _options = managementOptions;
    }

    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values) || values.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var header = values[0];
        if (string.IsNullOrWhiteSpace(header)
            || !AuthenticationHeaderValue.TryParse(header, out var auth)
            || !string.Equals(auth.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(auth.Parameter))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization header."));
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter));
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Authorization header is not valid Base64."));
        }

        var separator = decoded.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Authorization header missing ':' separator."));
        }
        var username = decoded[..separator];
        var password = decoded[(separator + 1)..];

        foreach (var u in _options.Auth.Users)
        {
            if (!string.Equals(u.Username, username, StringComparison.Ordinal))
            {
                continue;
            }
            if (ConstantTimeEquals(u.Password, password))
            {
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, u.Username),
                }, ManagementAuthSchemes.Basic);
                var principal = new ClaimsPrincipal(identity);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(principal, ManagementAuthSchemes.Basic)));
            }
            // Username found but password mismatched — break out so
            // we don't leak via timing how many credentials we checked.
            break;
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid credentials."));
    }

    /// <inheritdoc/>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers["WWW-Authenticate"] = "Basic realm=\"EdgeConnect Connectivity Studio\"";
        return base.HandleChallengeAsync(properties);
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }
}
