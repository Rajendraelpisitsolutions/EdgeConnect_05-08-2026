// ============================================================================
// File: Api/MqttTestConnectionService.cs
// Purpose: Drives the MQTT Test Connection probe behind
//          POST /api/v1/sinks/test-connection/mqtt — performs a throwaway
//          ConnectAsync + DisconnectAsync against the operator-supplied
//          broker config and surfaces the result. Locked H: NEVER calls
//          PublishAsync — the probe is a verification, not a write, and
//          must not pollute the broker with one-off test topics.
//
// LOCKED behaviour pinned by tests (M.2b.6 v2 Locked H + v3 Locked N):
//   * Locked H — never invokes PublishAsync (asserted by a counter test).
//   * License-gated by `sink-mqtt` module key (mirrors Focas2BrowseService's
//     `source-focas2` gate).
//   * Single-flight per host:port (mirrors Focas2BrowseService's per-IpPort
//     lease — protects the broker from a "click Test 5 times" hammer).
//   * Fixed 5s probe budget — broker connect latency on a healthy LAN is
//     under 200ms; 5s gives plenty of headroom while preventing a stuck
//     probe from hanging the wizard.
//   * Correlation ProbeId on every log line + in the DTO.
//
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
//            docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v2.md §3 (Locked H)
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sinks.Mqtt;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Executes a one-shot MQTT Test Connection probe — used by the
/// destination wizard's "Test Connection" button to verify broker
/// reachability before the operator commits a draft.
/// </summary>
/// <remarks>
/// Locked H: this service NEVER calls <c>PublishAsync</c>. The probe is
/// CONNECT + DISCONNECT only. <c>MqttTestConnectionServiceTests</c>
/// pins this with a counter on an injected fake client.
/// </remarks>
public sealed class MqttTestConnectionService
{
    private const string LicenseModuleKey = MqttSinkConfiguration.LicenseModuleKey;

    /// <summary>Fixed probe budget — 5s is plenty for a healthy broker on a LAN.</summary>
    internal static readonly TimeSpan DefaultProbeBudget = TimeSpan.FromSeconds(5);

    private readonly Func<IMqttProbeClient> _clientFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<MqttTestConnectionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _probeBudget;

    /// <summary>
    /// Production constructor — builds a real <see cref="IMqttClient"/>
    /// per probe via <see cref="MqttFactory"/>, gated by the supplied
    /// <see cref="ILicenseManager"/>. When <paramref name="license"/> is
    /// null OR no license loaded, the gate is permissive (matches the
    /// existing registration semantic in <c>MqttRegistrationExtensions</c>).
    /// </summary>
    public MqttTestConnectionService(
        ILoggerFactory loggerFactory,
        ILicenseManager? license = null)
        : this(
            clientFactory: () => new RealMqttProbeClient(),
            isModuleEnabled: BuildLicenseGate(license),
            loggerFactory: loggerFactory,
            probeBudget: DefaultProbeBudget)
    {
    }

    /// <summary>
    /// Test-only constructor — injects a custom client factory + license
    /// gate + probe budget so unit tests can pin the Locked H "no Publish"
    /// invariant against a fake <see cref="IMqttProbeClient"/> implementation.
    /// </summary>
    internal MqttTestConnectionService(
        Func<IMqttProbeClient> clientFactory,
        Func<string, bool> isModuleEnabled,
        ILoggerFactory loggerFactory,
        TimeSpan probeBudget)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(isModuleEnabled);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _clientFactory = clientFactory;
        _isModuleEnabled = isModuleEnabled;
        _logger = loggerFactory.CreateLogger<MqttTestConnectionService>();
        _probeBudget = probeBudget;
    }

    /// <summary>
    /// Build the license-gate delegate used by the production constructor.
    /// Internal-visible so a regression test can pin the dev-mode (no
    /// license loaded) permissive behaviour that earlier returned a
    /// spurious MQTT.PROBE_LICENSE_DISABLED on the first Test Connection
    /// click after <c>dotnet run</c>.
    /// </summary>
    internal static Func<string, bool> BuildLicenseGate(ILicenseManager? license)
    {
        // Match Focas2BrowseService's gate semantics exactly:
        //   * license manager null       → permissive (no DI registration)
        //   * license manager but Current is null → permissive (dev / sales)
        //   * license loaded             → consult IsModuleEnabled
        // The Current-is-null branch is the one that fires in dev runs
        // where the operator hasn't installed a license file.
        return moduleKey =>
            license is null
            || license.Current is null
            || license.IsModuleEnabled(moduleKey);
    }

    /// <summary>
    /// Run the probe. Returns a <see cref="MqttTestConnectionResultDto"/>
    /// describing success or the categorised failure.
    /// </summary>
    /// <param name="request">Operator-supplied broker config (host, port, auth, TLS).</param>
    /// <param name="ct">Cancellation supplied by the HTTP request scope.</param>
    public async Task<MqttTestConnectionProbeOutcome> ProbeAsync(
        MqttTestConnectionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var probeId = GenerateProbeId();
        var sw = Stopwatch.StartNew();

        // ── License gate ──────────────────────────────────────────────
        if (!_isModuleEnabled(LicenseModuleKey))
        {
            _logger.LogInformation("MQTT probe {ProbeId} blocked: license module '{Module}' disabled.",
                probeId, LicenseModuleKey);
            return new MqttTestConnectionProbeOutcome(
                Status: MqttTestConnectionStatus.LicenseDisabled,
                Result: new MqttTestConnectionResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ErrorCode = "MQTT.PROBE_LICENSE_DISABLED",
                    ErrorMessage = "MQTT sink module is not licensed for this gateway.",
                    RemediationHint = "Contact your administrator to enable the sink-mqtt license module.",
                });
        }

        // ── Single-flight per host:port ───────────────────────────────
        // Protects the broker from a "click Test 5 times" hammer and gives
        // each probe a clean adapter lifecycle. If another probe holds
        // the lease, we return Busy immediately rather than queueing.
        var leaseKey = $"{request.BrokerHost}:{request.BrokerPort}";
        var lease = _leases.GetOrAdd(leaseKey, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("MQTT probe {ProbeId} busy: {Key} has another probe in flight.",
                probeId, leaseKey);
            return new MqttTestConnectionProbeOutcome(
                Status: MqttTestConnectionStatus.Busy,
                Result: new MqttTestConnectionResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ErrorCode = "MQTT.PROBE_BUSY",
                    ErrorMessage = $"Another Test Connection probe is in flight against {leaseKey}.",
                    RemediationHint = "Wait a few seconds and try again.",
                });
        }

        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);

            using var client = _clientFactory();
            {
                var options = BuildOptions(request, probeId);

                _logger.LogInformation(
                    "MQTT probe {ProbeId} → ConnectAsync(broker={Host}:{Port}, tls={Tls}, auth={Auth}).",
                    probeId, request.BrokerHost, request.BrokerPort, request.UseTls, request.HasCredentials);

                MqttProbeConnectResult connectResult;
                try
                {
                    connectResult = await client.ConnectAsync(options, probeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (probeCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    return Fail(probeId, sw, "MQTT.PROBE_TIMEOUT",
                        $"Broker {request.BrokerHost}:{request.BrokerPort} did not respond within {_probeBudget.TotalSeconds:F0}s.",
                        "Verify the broker is reachable from this gateway (firewall, hostname, port).");
                }
                catch (Exception ex)
                {
                    return Fail(probeId, sw, "MQTT.PROBE_REFUSED",
                        $"Broker refused the connection: {ex.Message}",
                        "Check the broker URL and firewall rules. If using TLS, verify the broker presents a valid certificate.");
                }

                if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                {
                    var code = MapConnectResultCode(connectResult.ResultCode);
                    return Fail(probeId, sw, code.ErrorCode, code.ErrorMessage, code.Hint);
                }

                // ── Disconnect — Locked H: no Publish in between ──────
                try
                {
                    await client.DisconnectAsync(probeCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Disconnect failures don't fail the probe — CONNECT
                    // succeeded which is the meaningful signal. Log and
                    // continue.
                    _logger.LogDebug(ex,
                        "MQTT probe {ProbeId} disconnect raised an exception; probe still considered successful.",
                        probeId);
                }

                _logger.LogInformation("MQTT probe {ProbeId} succeeded in {Elapsed}ms.",
                    probeId, sw.ElapsedMilliseconds);

                return new MqttTestConnectionProbeOutcome(
                    Status: MqttTestConnectionStatus.Success,
                    Result: new MqttTestConnectionResultDto
                    {
                        ProbeId = probeId,
                        Success = true,
                        ElapsedMs = (int)sw.ElapsedMilliseconds,
                    });
            }
        }
        finally
        {
            lease.Release();
        }
    }

    private static MqttClientOptions BuildOptions(MqttTestConnectionRequest request, string probeId)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(request.BrokerHost, request.BrokerPort)
            .WithClientId($"edgeconnect-probe-{probeId}")
            .WithCleanSession(true)
            .WithTimeout(TimeSpan.FromSeconds(4));

        if (request.UseTls)
        {
            builder = builder.WithTlsOptions(o => { });
        }

        if (request.HasCredentials)
        {
            builder = builder.WithCredentials(request.Username, request.Password);
        }

        return builder.Build();
    }

    private static (string ErrorCode, string ErrorMessage, string Hint) MapConnectResultCode(MqttClientConnectResultCode code) =>
        code switch
        {
            MqttClientConnectResultCode.BadUserNameOrPassword or MqttClientConnectResultCode.NotAuthorized =>
                ("MQTT.PROBE_AUTH_FAILED",
                 $"Broker rejected credentials ({code}).",
                 "Double-check the username and password. Some brokers require ACL setup before a new client can connect."),
            MqttClientConnectResultCode.ServerUnavailable or MqttClientConnectResultCode.ServerBusy =>
                ("MQTT.PROBE_BROKER_UNAVAILABLE",
                 $"Broker reported unavailable ({code}).",
                 "The broker is reachable but signalled it is overloaded or down for maintenance. Try again in a moment."),
            MqttClientConnectResultCode.Banned =>
                ("MQTT.PROBE_BANNED",
                 "Broker banned this client (Banned).",
                 "The broker has banned this client id or source IP. Check broker access logs."),
            MqttClientConnectResultCode.UnsupportedProtocolVersion =>
                ("MQTT.PROBE_PROTOCOL_VERSION",
                 "Broker rejected the MQTT protocol version.",
                 "EdgeConnect uses MQTT 3.1.1. Configure the broker to accept it, or check whether the broker is actually an MQTT broker."),
            _ =>
                ($"MQTT.PROBE_REJECTED_{code}",
                 $"Broker rejected the CONNECT frame ({code}).",
                 "Check the broker logs for details — the broker reported a non-success result code."),
        };

    private MqttTestConnectionProbeOutcome Fail(
        string probeId, Stopwatch sw, string errorCode, string errorMessage, string remediation)
    {
        _logger.LogWarning("MQTT probe {ProbeId} failed: {ErrorCode} — {Message}",
            probeId, errorCode, errorMessage);
        return new MqttTestConnectionProbeOutcome(
            Status: MqttTestConnectionStatus.Failure,
            Result: new MqttTestConnectionResultDto
            {
                ProbeId = probeId,
                Success = false,
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                RemediationHint = remediation,
            });
    }

    private static string GenerateProbeId() =>
        string.Concat("probe-", Guid.NewGuid().ToString("N").AsSpan(0, 8));
}

/// <summary>Service-level status that drives the HTTP status code in the API layer.</summary>
public enum MqttTestConnectionStatus
{
    /// <summary>Probe succeeded — HTTP 200.</summary>
    Success,

    /// <summary>Probe ran but the broker rejected or timed out — HTTP 400.</summary>
    Failure,

    /// <summary>License gate refused the probe — HTTP 403.</summary>
    LicenseDisabled,

    /// <summary>Single-flight lease busy — HTTP 409.</summary>
    Busy,
}

/// <summary>Service-layer outcome — DTO plus the status enum that drives HTTP status mapping.</summary>
public sealed record MqttTestConnectionProbeOutcome(
    MqttTestConnectionStatus Status,
    MqttTestConnectionResultDto Result);

/// <summary>
/// Minimal MQTT-client abstraction the probe uses. Defined locally so
/// the test fake can pin Locked H ("no PublishAsync ever") without
/// having to implement every method of MQTTnet's full
/// <c>IMqttClient</c> surface (which evolves across minor versions).
/// </summary>
/// <remarks>
/// <c>PublishAsync</c> is INTENTIONALLY part of this interface even
/// though the production probe must never call it — the test fake's
/// call counter on <see cref="PublishAsync"/> is the load-bearing
/// assertion for Locked H. If a future refactor of
/// <see cref="MqttTestConnectionService"/> ever calls <c>PublishAsync</c>,
/// the test catches it.
/// </remarks>
public interface IMqttProbeClient : IDisposable
{
    /// <summary>Connect to the broker described by <paramref name="options"/>.</summary>
    Task<MqttProbeConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken);

    /// <summary>Disconnect cleanly. Failures are non-fatal to the probe outcome.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Locked H: the production probe NEVER calls this. The test fake
    /// counts invocations to prove the production service stays clean.
    /// </summary>
    Task PublishAsync(MqttApplicationMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Probe-specific connect result. Exists because MQTTnet's
/// <c>MqttClientConnectResult.ResultCode</c> is a get-only property
/// that's awkward to construct from a test fake. The production wrapper
/// translates the MQTTnet result into this type; the fake produces it
/// directly.
/// </summary>
public sealed record MqttProbeConnectResult(MqttClientConnectResultCode ResultCode);

/// <summary>
/// Production wrapper around MQTTnet's <c>IMqttClient</c>. Created by
/// the default factory; not exposed for direct test use.
/// </summary>
internal sealed class RealMqttProbeClient : IMqttProbeClient
{
    private readonly IMqttClient _client = new MqttFactory().CreateMqttClient();

    public async Task<MqttProbeConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken)
    {
        var result = await _client.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        return new MqttProbeConnectResult(result.ResultCode);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);

    public Task PublishAsync(MqttApplicationMessage message, CancellationToken cancellationToken) =>
        _client.PublishAsync(message, cancellationToken);

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Input parameters for the MQTT probe. The wire DTO (deserialised by
/// the API endpoint) projects into this record; tests instantiate it
/// directly.
/// </summary>
public sealed record MqttTestConnectionRequest
{
    /// <summary>Broker hostname or IP.</summary>
    public required string BrokerHost { get; init; }

    /// <summary>Broker TCP port.</summary>
    public required int BrokerPort { get; init; }

    /// <summary>Whether to use TLS.</summary>
    public bool UseTls { get; init; }

    /// <summary>Optional username — paired with <see cref="Password"/>.</summary>
    public string? Username { get; init; }

    /// <summary>Optional password.</summary>
    public string? Password { get; init; }

    /// <summary>Whether the request carries a username/password pair.</summary>
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Username) && Password is not null;
}
