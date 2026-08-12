// ============================================================================
// File: Api/OpcUaClientTestConnectionService.cs
// Purpose: Drives the OPC UA Client Test Connection probe behind
//          POST /api/v1/sources/test-connection/opcua-client. Builds a
//          throwaway OpcUaClientSourceAdapter, invokes its TestConnectAsync
//          (PR 2 — read-only, idempotent, no draft mutation per ADR-0015
//          Rule 6 / Rule 11.1), and surfaces a categorised result.
//
// LOCKED behaviour pinned by tests (PR 7c-2 plan + amendments,
// user lock 2026-05-29):
//
//   1. License-gated by `source-opcua-client` (mirrors Focas2BrowseService's
//      `source-focas2` gate)
//   2. Single-flight per endpoint URL (mirrors MqttTestConnectionService's
//      per-broker lease — protects the OPC server from a "click Test 5 times"
//      hammer)
//   3. Fixed 15s probe budget (matches Focas2BrowseService's budget — OPC UA
//      session create + ReadAsync on ServerStatus is similar order to FOCAS2
//      connect + sysinfo read)
//   4. Correlation ProbeId on every log line + in the DTO
//   5. NEVER calls anything beyond TestConnectAsync — no subscriptions,
//      no monitored items, no writes (the adapter's own implementation
//      enforces this; the service just calls the surface)
//
// Reference: PR 7c-2 plan + amendments (user lock 2026-05-29)
//            docs/decisions/0015-wizard-contract.md Rule 6, Rule 11.1
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.OpcUaClient;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Executes a one-shot OPC UA Client Test Connection probe — used by the
/// wizard's "Test Connection" button to verify endpoint reachability +
/// security + auth before the operator commits a draft.
/// </summary>
public sealed class OpcUaClientTestConnectionService
{
    private const string LicenseModuleKey = OpcUaClientSourceConfiguration.LicenseModuleKey;

    /// <summary>Fixed probe budget — matches the FOCAS2 Browse service.</summary>
    internal static readonly TimeSpan DefaultProbeBudget = TimeSpan.FromSeconds(15);

    private readonly Func<string, IOpcUaClientTestConnectionProber> _proberFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<OpcUaClientTestConnectionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _probeBudget;

    /// <summary>
    /// Production constructor — wraps a throwaway
    /// <see cref="OpcUaClientSourceAdapter"/> per probe, gated by the
    /// supplied <see cref="ILicenseManager"/>. When the license manager is
    /// null or no license is loaded, the gate is permissive (matches the
    /// existing Focas2BrowseService dev-mode semantic).
    /// </summary>
    public OpcUaClientTestConnectionService(
        ILoggerFactory loggerFactory,
        ILicenseManager? license = null)
        : this(
            proberFactory: instanceId => new RealOpcUaClientTestConnectionProber(
                instanceId, loggerFactory),
            isModuleEnabled: BuildLicenseGate(license),
            loggerFactory: loggerFactory,
            probeBudget: DefaultProbeBudget)
    {
    }

    /// <summary>
    /// Test constructor — injects a custom prober factory + license gate +
    /// probe budget so unit tests can pin the contract without standing
    /// up a real OPC stack endpoint.
    /// </summary>
    internal OpcUaClientTestConnectionService(
        Func<string, IOpcUaClientTestConnectionProber> proberFactory,
        Func<string, bool> isModuleEnabled,
        ILoggerFactory loggerFactory,
        TimeSpan probeBudget)
    {
        ArgumentNullException.ThrowIfNull(proberFactory);
        ArgumentNullException.ThrowIfNull(isModuleEnabled);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _proberFactory = proberFactory;
        _isModuleEnabled = isModuleEnabled;
        _logger = loggerFactory.CreateLogger<OpcUaClientTestConnectionService>();
        _probeBudget = probeBudget;
    }

    /// <summary>
    /// Build the license-gate delegate used by the production constructor.
    /// </summary>
    /// <remarks>
    /// Internal so a regression test can pin the dev-mode permissive
    /// behaviour — when the operator hasn't installed a license file, the
    /// first Test Connection click should NOT surface a spurious
    /// LICENSE.MODULE_DISABLED.
    /// </remarks>
    internal static Func<string, bool> BuildLicenseGate(ILicenseManager? license) =>
        moduleKey =>
            license is null
            || license.Current is null
            || license.IsModuleEnabled(moduleKey);

    /// <summary>
    /// Run the probe. Returns an outcome describing success or a
    /// categorised failure.
    /// </summary>
    public async Task<OpcUaClientTestConnectionOutcome> ProbeAsync(
        OpcUaClientTestConnectionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var probeId = GenerateProbeId();
        var sw = Stopwatch.StartNew();

        // ── License gate ──────────────────────────────────────────────
        if (!_isModuleEnabled(LicenseModuleKey))
        {
            _logger.LogInformation(
                "OPC UA Client probe {ProbeId} blocked: license module '{Module}' disabled.",
                probeId, LicenseModuleKey);
            return new OpcUaClientTestConnectionOutcome(
                OpcUaClientTestConnectionStatus.LicenseDisabled,
                new OpcUaClientTestConnectionResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    EndpointUrl = request.EndpointUrl ?? string.Empty,
                    ErrorCode = "OPCUA.PROBE_LICENSE_DISABLED",
                    ErrorMessage = "OPC UA Client source module is not licensed for this gateway.",
                    RemediationHint = "Contact your administrator to enable the source-opcua-client license module.",
                });
        }

        // ── Schema / required-field check ─────────────────────────────
        if (string.IsNullOrWhiteSpace(request.EndpointUrl))
        {
            return new OpcUaClientTestConnectionOutcome(
                OpcUaClientTestConnectionStatus.ConfigInvalid,
                new OpcUaClientTestConnectionResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    EndpointUrl = request.EndpointUrl ?? string.Empty,
                    ErrorCode = "OPCUA.PROBE_CONFIG_INVALID",
                    ErrorMessage = "EndpointUrl is required.",
                    RemediationHint = "Supply a complete opc.tcp:// endpoint URL before testing the connection.",
                });
        }

        // ── Single-flight per endpoint URL ────────────────────────────
        var lease = _leases.GetOrAdd(request.EndpointUrl, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "OPC UA Client probe {ProbeId} busy: another probe is in flight against {Endpoint}.",
                probeId, request.EndpointUrl);
            return new OpcUaClientTestConnectionOutcome(
                OpcUaClientTestConnectionStatus.Busy,
                new OpcUaClientTestConnectionResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    EndpointUrl = request.EndpointUrl,
                    ErrorCode = "OPCUA.PROBE_BUSY",
                    ErrorMessage = $"Another Test Connection probe is in flight against {request.EndpointUrl}.",
                    RemediationHint = "Wait a few seconds and try again.",
                });
        }

        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);

            // Build a config from the request. Use an ephemeral instance
            // id — the probe never persists anything and shouldn't collide
            // with a live adapter's instance id.
            var probeConfig = BuildProbeConfig(request, probeId);

            // Throwaway prober: opens its own session, reads ServerStatus.State,
            // closes. No subscriptions, no monitored items, no writes — the
            // adapter's own TestConnectAsync enforces this.
            var prober = _proberFactory(probeConfig.InstanceId);

            _logger.LogInformation(
                "OPC UA Client probe {ProbeId} → TestConnectAsync(endpoint={Endpoint}, security={Security}, auth={Auth}).",
                probeId, request.EndpointUrl, request.SecurityMode, request.AuthMode);

            TestConnectResult adapterResult;
            try
            {
                adapterResult = await prober.TestConnectAsync(probeConfig, probeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (probeCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return Fail(probeId, sw, request.EndpointUrl,
                    "OPCUA.PROBE_TIMEOUT",
                    $"Endpoint {request.EndpointUrl} did not respond within {_probeBudget.TotalSeconds:F0}s.",
                    "Verify the OPC UA server is reachable from this gateway (hostname, port, firewall).");
            }

            if (adapterResult.Success)
            {
                _logger.LogInformation(
                    "OPC UA Client probe {ProbeId} succeeded in {Elapsed}ms (serverState={State}).",
                    probeId, sw.ElapsedMilliseconds, adapterResult.ServerState);
                return new OpcUaClientTestConnectionOutcome(
                    OpcUaClientTestConnectionStatus.Success,
                    new OpcUaClientTestConnectionResultDto
                    {
                        ProbeId = probeId,
                        Success = true,
                        ElapsedMs = (int)sw.ElapsedMilliseconds,
                        EndpointUrl = request.EndpointUrl,
                        ServerState = adapterResult.ServerState,
                    });
            }

            // Adapter reported failure — pass through the classified error
            // code and add a remediation hint based on the category.
            var hint = RemediationHintFor(adapterResult.ErrorCode);
            _logger.LogInformation(
                "OPC UA Client probe {ProbeId} failed: {ErrorCode} — {Message}",
                probeId, adapterResult.ErrorCode, adapterResult.Message);
            return new OpcUaClientTestConnectionOutcome(
                OpcUaClientTestConnectionStatus.Failure,
                new OpcUaClientTestConnectionResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    EndpointUrl = request.EndpointUrl,
                    ErrorCode = adapterResult.ErrorCode ?? "OPCUA.PROBE_FAILED",
                    ErrorMessage = adapterResult.Message,
                    RemediationHint = hint,
                });
        }
        finally
        {
            lease.Release();
        }
    }

    private static OpcUaClientSourceConfiguration BuildProbeConfig(
        OpcUaClientTestConnectionRequest request, string probeId)
    {
        var instanceId = $"opcua-probe-{probeId}";
        OpcUaClientCredentials? credentials = null;
        if (request.AuthMode == OpcUaAuthMode.UserName
            && !string.IsNullOrEmpty(request.Username))
        {
            credentials = new OpcUaClientCredentials
            {
                Username = request.Username,
                Password = request.Password,
            };
        }
        else if (request.AuthMode == OpcUaAuthMode.Certificate
            && !string.IsNullOrEmpty(request.CertificatePath))
        {
            credentials = new OpcUaClientCredentials
            {
                CertificatePath = request.CertificatePath,
            };
        }

        return new OpcUaClientSourceConfiguration
        {
            InstanceId = instanceId,
            ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
            DeviceId = "probe-device",
            EndpointUrl = request.EndpointUrl!,
            ApplicationUri = string.IsNullOrWhiteSpace(request.ApplicationUri)
                ? $"urn:elpis:edgeconnect:probe:{probeId}"
                : request.ApplicationUri!,
            SecurityMode = request.SecurityMode,
            AuthMode = request.AuthMode,
            Credentials = credentials,
            AutoAcceptUntrustedServerCertificate = true,
        };
    }

    private static string RemediationHintFor(string? errorCode) => errorCode switch
    {
        "OPCUA.SERVER_CERT_UNTRUSTED" =>
            "The server's certificate is not trusted by this gateway. Import it into the OPC UA client trust list, or have the server present a certificate signed by a trusted CA.",
        "OPCUA.SERVER_CERT_HOSTNAME_INVALID" =>
            "The server's certificate does not list the hostname you connected to. Connect using the hostname in the certificate's SAN, or re-issue the certificate.",
        "OPCUA.SECURITY_CHECKS_FAILED" =>
            "Security negotiation failed. Verify SecurityMode and SecurityPolicyUri match what the server's endpoint advertises.",
        "OPCUA.AUTH_DENIED" or "OPCUA.AUTH_TOKEN_INVALID" =>
            "Authentication was rejected. Double-check the username/password or certificate, and that the user is authorised on the server.",
        "OPCUA.CONNECTION_REJECTED" =>
            "The server is reachable but refused the connection. Check that the OPC UA service is running and that the endpoint URL is correct.",
        "OPCUA.CONNECT_NETWORK_ERROR" =>
            "A network-level error prevented the connect. Verify hostname/IP, port, and firewall rules.",
        "OPCUA.CONNECT_TIMEOUT" =>
            "The server did not respond before the probe budget elapsed. Verify the server is reachable and not under heavy load.",
        _ =>
            "Review the endpoint URL, security mode, and credentials. If the issue persists, check the OPC UA server's diagnostic log.",
    };

    private OpcUaClientTestConnectionOutcome Fail(
        string probeId, Stopwatch sw, string endpoint,
        string errorCode, string errorMessage, string remediation)
    {
        _logger.LogWarning(
            "OPC UA Client probe {ProbeId} failed: {ErrorCode} — {Message}",
            probeId, errorCode, errorMessage);
        return new OpcUaClientTestConnectionOutcome(
            OpcUaClientTestConnectionStatus.Failure,
            new OpcUaClientTestConnectionResultDto
            {
                ProbeId = probeId,
                Success = false,
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                EndpointUrl = endpoint,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                RemediationHint = remediation,
            });
    }

    private static string GenerateProbeId() =>
        string.Concat("probe-", Guid.NewGuid().ToString("N").AsSpan(0, 8));
}

/// <summary>Service-level status that drives the HTTP status code in the API layer.</summary>
public enum OpcUaClientTestConnectionStatus
{
    /// <summary>Probe succeeded — HTTP 200.</summary>
    Success,

    /// <summary>Probe ran but the server rejected / timed out — HTTP 200 (Success=false body).</summary>
    Failure,

    /// <summary>Required fields missing in the request — HTTP 400.</summary>
    ConfigInvalid,

    /// <summary>License gate refused the probe — HTTP 403.</summary>
    LicenseDisabled,

    /// <summary>Single-flight lease busy — HTTP 409.</summary>
    Busy,
}

/// <summary>Service-layer outcome carrying the DTO plus the status enum used for HTTP mapping.</summary>
public sealed record OpcUaClientTestConnectionOutcome(
    OpcUaClientTestConnectionStatus Status,
    OpcUaClientTestConnectionResultDto Result);

/// <summary>
/// Input parameters for the OPC UA Client probe. The wizard projects its
/// in-progress draft into this shape and POSTs it to
/// <c>/api/v1/sources/test-connection/opcua-client</c>.
/// </summary>
public sealed record OpcUaClientTestConnectionRequest
{
    /// <summary>OPC UA endpoint URL (e.g. <c>opc.tcp://server.local:4840</c>).</summary>
    public string? EndpointUrl { get; init; }

    /// <summary>Client-side application URI (defaults to a per-probe URN when null/empty).</summary>
    public string? ApplicationUri { get; init; }

    /// <summary>Endpoint security mode.</summary>
    public OpcUaSecurityMode SecurityMode { get; init; } = OpcUaSecurityMode.None;

    /// <summary>User token mode.</summary>
    public OpcUaAuthMode AuthMode { get; init; } = OpcUaAuthMode.Anonymous;

    /// <summary>Username for <see cref="OpcUaAuthMode.UserName"/> auth.</summary>
    public string? Username { get; init; }

    /// <summary>Password for <see cref="OpcUaAuthMode.UserName"/> auth.</summary>
    public string? Password { get; init; }

    /// <summary>Certificate path for <see cref="OpcUaAuthMode.Certificate"/> auth.</summary>
    public string? CertificatePath { get; init; }
}

/// <summary>
/// Test-seam interface wrapping the call into the throwaway adapter.
/// Production wires <see cref="RealOpcUaClientTestConnectionProber"/>;
/// tests substitute to verify license / single-flight / budget plumbing
/// without standing up an OPC stack endpoint.
/// </summary>
public interface IOpcUaClientTestConnectionProber
{
    /// <summary>
    /// Probe the endpoint described by <paramref name="config"/>. The
    /// adapter's own <c>TestConnectAsync</c> is read-only and idempotent
    /// per ADR-0015 Rule 6 / Rule 11.1.
    /// </summary>
    Task<TestConnectResult> TestConnectAsync(
        OpcUaClientSourceConfiguration config,
        CancellationToken ct);
}

/// <summary>
/// Production prober — constructs a throwaway
/// <see cref="OpcUaClientSourceAdapter"/> per probe and invokes its
/// <c>TestConnectAsync</c>.
/// </summary>
internal sealed class RealOpcUaClientTestConnectionProber : IOpcUaClientTestConnectionProber
{
    private readonly string _instanceId;
    private readonly ILoggerFactory _loggerFactory;

    public RealOpcUaClientTestConnectionProber(string instanceId, ILoggerFactory loggerFactory)
    {
        _instanceId = instanceId;
        _loggerFactory = loggerFactory;
    }

    public async Task<TestConnectResult> TestConnectAsync(
        OpcUaClientSourceConfiguration config,
        CancellationToken ct)
    {
        await using var adapter = new OpcUaClientSourceAdapter(
            _instanceId,
            _loggerFactory.CreateLogger<OpcUaClientSourceAdapter>());
        return await adapter.TestConnectAsync(config, ct).ConfigureAwait(false);
    }
}
