// ============================================================================
// File: OpcUaServerSinkAdapter.cs
// Purpose: ISinkAdapter implementation that hosts an OPC UA Server
//          endpoint and exposes EdgeConnect's canonical tag set to
//          external clients (SCADA, MES, HMI).
//
//          Push and Pull capabilities are both declared:
//            * PublishAsync                — drives address-space
//                                            updates on the regular
//                                            data-flow path.
//            * UpdateCurrentValuesAsync   — drives address-space
//                                            updates from "latest-only"
//                                            sources (used when the
//                                            sink is configured as pull
//                                            in the route).
//          Both paths converge on EdgeConnectNodeManager.UpsertTagValue.
//
//          MVP security: None + Anonymous only. Sign / SignAndEncrypt
//          modes and UserName / Certificate user tokens are rejected
//          at Initialize with OPCUA.SECURITY_NOT_YET_IMPLEMENTED.
//          Milestone K lifts that rejection.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.1
//            shared-knowledge/contracts/opcua-namespace-policy.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Tags;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// OPC UA Server sink adapter. Hosts an OPC UA endpoint and updates
/// its address space from canonical data points.
/// </summary>
public sealed class OpcUaServerSinkAdapter : ISinkAdapter, ISessionTrackingSink
{
    private readonly ILogger<OpcUaServerSinkAdapter> _logger;
    private readonly object _stateLock = new();

    private OpcUaServerConfiguration _config = null!;
    private ApplicationInstance? _appInstance;
    private EdgeConnectStandardServer? _server;
    private OpcUaSessionTracker? _sessionTracker;
    private OpcUaCertManager? _certManager;
    private OpcUaUserIdentityValidator? _userValidator;

    // Tag semantics override registry — populated by host wiring
    // (future). For MVP this is empty and the adapter falls back to
    // the CanonicalDataPoint's TagName.
    private readonly Dictionary<string, TagSemantics> _tagSemanticsByStableTagId = new(StringComparer.Ordinal);

    private long _updateCalls;
    private long _updatedPoints;
    private long _droppedPoints;
    private DateTime? _lastUpdateAtUtc;
    private AdapterError? _lastError;

    /// <inheritdoc/>
    public string InstanceId { get; }

    /// <inheritdoc/>
    public string ProtocolName => OpcUaServerConfiguration.ProtocolNameConstant;

    /// <inheritdoc/>
    public SinkCapabilities Capabilities =>
        SinkCapabilities.Push
        | SinkCapabilities.Pull
        | SinkCapabilities.Browse
        | SinkCapabilities.Subscription
        | SinkCapabilities.SessionTracking;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    /// <summary>
    /// Build a new OPC UA Server sink adapter. <paramref name="instanceId"/>
    /// must match the <c>InstanceId</c> on the configuration passed to
    /// <see cref="InitializeAsync"/>.
    /// </summary>
    public OpcUaServerSinkAdapter(string instanceId, ILogger<OpcUaServerSinkAdapter> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(logger);
        InstanceId = instanceId;
        _logger = logger;
    }

    /// <summary>
    /// Snapshot of active sessions, when running. Returns an empty list
    /// when the server is stopped. Implements
    /// <see cref="ISessionTrackingSink.ActiveSessions"/> so the host's
    /// SinkSessionPoller can publish these into the diagnostics surface
    /// without protocol-specific casts.
    /// </summary>
    public IReadOnlyList<SinkSessionSummary> ActiveSessions =>
        _sessionTracker?.Snapshot() ?? Array.Empty<SinkSessionSummary>();

    /// <summary>
    /// Number of tag variable nodes currently in the address space.
    /// </summary>
    public int TagNodeCount => _server?.NodeManager?.TagNodeCount ?? 0;

    /// <inheritdoc/>
    public Task InitializeAsync(SinkConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config is not OpcUaServerConfiguration typedConfig)
        {
            throw new ArgumentException(
                $"Expected OpcUaServerConfiguration; got {config.GetType().FullName}",
                nameof(config));
        }

        if (!string.Equals(typedConfig.InstanceId, InstanceId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Config InstanceId '{typedConfig.InstanceId}' does not match adapter InstanceId '{InstanceId}'.",
                nameof(config));
        }

        // K: the full security surface is honored. Config-coherence
        // checks (e.g. UserName policy enabled but no credentials
        // configured) are surfaced here so operators see the error
        // at gateway start, not at first client connect.
        ValidateSecurityCoherence(typedConfig.Security);

        _config = typedConfig;
        _certManager = new OpcUaCertManager(typedConfig.Security, typedConfig.ApplicationName);
        _userValidator = new OpcUaUserIdentityValidator(typedConfig.Security, _logger);
        TransitionState(AdapterState.Initialized);
        _logger.LogInformation(
            "OPC UA Server sink {InstanceId} initialized: endpoint={EndpointUrl}, namespace={NamespaceUri}",
            InstanceId, _config.EndpointUrl, _config.Namespace.NamespaceUri);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct)
    {
        TransitionState(AdapterState.Starting);

        try
        {
            var appConfig = BuildApplicationConfiguration();
            await appConfig.Validate(ApplicationType.Server).ConfigureAwait(false);

            _appInstance = new ApplicationInstance
            {
                ApplicationName = _config.ApplicationName,
                ApplicationType = ApplicationType.Server,
                ApplicationConfiguration = appConfig,
            };

            // Ensure application-instance certificate exists. Delegated
            // to OpcUaCertManager so K (and future milestones) can
            // evolve cert handling without touching the adapter.
            await _certManager!.EnsureApplicationCertificateAsync(_appInstance, ct).ConfigureAwait(false);

            _server = new EdgeConnectStandardServer(_config.Namespace);
            await _appInstance.Start(_server).ConfigureAwait(false);

            _sessionTracker = new OpcUaSessionTracker();
            _sessionTracker.Attach(_server);

            // K: hook user-identity validation into the session manager.
            // Anonymous / UserName / Certificate tokens are gated against
            // the configured policy + credentials per OPC UA spec.
            _userValidator!.Attach(_server);

            TransitionState(AdapterState.Running);
            _logger.LogInformation(
                "OPC UA Server sink {InstanceId} listening at {EndpointUrl}",
                InstanceId, _config.EndpointUrl);
        }
        catch (Exception ex)
        {
            var error = ClassifyStartFailure(ex, _config.EndpointUrl);
            _lastError = error;
            TransitionState(AdapterState.Failed);
            _logger.LogError(ex,
                "OPC UA Server sink {InstanceId} failed to start: {ErrorCode} — {ErrorMessage}",
                InstanceId, error.Code, error.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct)
    {
        if (State is AdapterState.Stopped or AdapterState.Created)
        {
            return Task.CompletedTask;
        }

        TransitionState(AdapterState.Stopping);

        try
        {
            _userValidator?.Detach();
            _sessionTracker?.Detach();

            if (_appInstance?.Server is { } running)
            {
                running.Stop();
            }

            _appInstance = null;
            _server = null;
            _sessionTracker = null;
            TransitionState(AdapterState.Stopped);
            _logger.LogInformation("OPC UA Server sink {InstanceId} stopped.", InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "OPC UA Server sink {InstanceId} encountered an error during stop; forcing Stopped state.",
                InstanceId);
            TransitionState(AdapterState.Stopped);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
    {
        var level = State switch
        {
            AdapterState.Running => HealthLevel.Healthy,
            AdapterState.Degraded => HealthLevel.Degraded,
            AdapterState.Failed => HealthLevel.Unhealthy,
            AdapterState.Stopped => HealthLevel.Unknown,
            _ => HealthLevel.Unknown,
        };

        var health = new AdapterHealth
        {
            State = State,
            Level = level,
            CheckedAt = DateTime.UtcNow,
            LastSuccessAt = _lastUpdateAtUtc,
            LastError = _lastError,
            Metrics = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["updateCalls"] = Interlocked.Read(ref _updateCalls),
                ["updatedPoints"] = Interlocked.Read(ref _updatedPoints),
                ["droppedPoints"] = Interlocked.Read(ref _droppedPoints),
                ["activeSessions"] = _sessionTracker?.ActiveSessionCount ?? 0,
                ["tagNodes"] = TagNodeCount,
            },
        };

        return Task.FromResult(health);
    }

    /// <inheritdoc/>
    public Task<PublishResult> PublishAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(points);

        var stopwatch = Stopwatch.StartNew();

        if (State != AdapterState.Running || _server?.NodeManager is null)
        {
            stopwatch.Stop();
            var error = MakeError("OPCUA.NOT_RUNNING", ErrorCategory.DeviceState,
                "OPC UA server is not running.", retryable: true);
            _lastError = error;
            return Task.FromResult(PublishResult.Failed(error, stopwatch.Elapsed));
        }

        var (accepted, rejected) = ApplyPoints(points);
        stopwatch.Stop();

        if (rejected > 0 && accepted == 0)
        {
            var error = MakeError("OPCUA.ALL_POINTS_REJECTED", ErrorCategory.Protocol,
                $"All {rejected} points were rejected by the address-space updater.", retryable: false);
            _lastError = error;
            return Task.FromResult(PublishResult.Failed(error, stopwatch.Elapsed));
        }

        return Task.FromResult(new PublishResult
        {
            Success = true,
            AcceptedCount = accepted,
            RejectedCount = rejected,
            Latency = stopwatch.Elapsed,
        });
    }

    /// <inheritdoc/>
    public Task UpdateCurrentValuesAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (State != AdapterState.Running || _server?.NodeManager is null)
        {
            return Task.CompletedTask;
        }

        ApplyPoints(points);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config is not OpcUaServerConfiguration typed)
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_WRONG_TYPE",
                $"Expected OpcUaServerConfiguration; got {config.GetType().FullName}",
                "$"));
        }

        if (string.IsNullOrWhiteSpace(typed.EndpointUrl) || !Uri.TryCreate(typed.EndpointUrl, UriKind.Absolute, out _))
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_INVALID_ENDPOINT",
                $"EndpointUrl is not a valid absolute URI: '{typed.EndpointUrl}'.",
                "$.EndpointUrl"));
        }

        if (string.IsNullOrWhiteSpace(typed.Namespace.NamespaceUri))
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_MISSING_NAMESPACE_URI",
                "Namespace.NamespaceUri is required.",
                "$.Namespace.NamespaceUri"));
        }

        if (typed.MaxSessions <= 0 || typed.MaxMonitoredItemsPerSession <= 0 || typed.MinPublishingIntervalMs <= 0)
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_INVALID_LIMITS",
                "MaxSessions / MaxMonitoredItemsPerSession / MinPublishingIntervalMs must be positive.",
                "$"));
        }

        try
        {
            ValidateSecurityCoherence(typed.Security);
        }
        catch (InvalidOperationException ex)
        {
            // Bubble the OPCUA.* error code embedded in the exception
            // message back to the validation result for operator-facing
            // tooling (Connectivity Studio in Milestone M).
            var code = ex.Message.StartsWith("OPCUA.", StringComparison.Ordinal)
                ? ex.Message.Split(':', 2)[0]
                : "OPCUA.SECURITY_INVALID";
            return Task.FromResult(ValidationResult.Failure(code, ex.Message, "$.Security"));
        }

        return Task.FromResult(ValidationResult.Success());
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (State is AdapterState.Running or AdapterState.Degraded or AdapterState.Starting)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await StopAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Register an authoritative <see cref="TagSemantics"/> entry for a
    /// stable tag id. Host wiring calls this for every configured tag
    /// before <see cref="StartAsync"/> so DisplayName / Description /
    /// EngineeringUnits / EURange / Writable surface on the OPC UA
    /// nodes from the first publish. Optional — adapters work in
    /// dynamic mode without it.
    /// </summary>
    public void RegisterTagSemantics(string stableTagId, TagSemantics semantics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableTagId);
        ArgumentNullException.ThrowIfNull(semantics);
        _tagSemanticsByStableTagId[stableTagId] = semantics;
    }

    private (int Accepted, int Rejected) ApplyPoints(IReadOnlyList<CanonicalDataPoint> points)
    {
        Interlocked.Increment(ref _updateCalls);
        var accepted = 0;
        var rejected = 0;
        var nodeManager = _server?.NodeManager;
        if (nodeManager is null)
        {
            Interlocked.Add(ref _droppedPoints, points.Count);
            return (0, points.Count);
        }

        foreach (var point in points)
        {
            try
            {
                var stableTagId = ResolveStableTagId(point);
                var placeholders = BuildBrowsePathPlaceholders(point, stableTagId);
                _tagSemanticsByStableTagId.TryGetValue(stableTagId, out var semantics);

                nodeManager.UpsertTagValue(point, semantics, stableTagId, placeholders);
                accepted++;
            }
            catch (Exception ex)
            {
                rejected++;
                _logger.LogWarning(ex,
                    "OPC UA Server sink {InstanceId}: failed to upsert tag {TagName} from source {SourceInstanceId}.",
                    InstanceId, point.TagName, point.SourceInstanceId);
            }
        }

        Interlocked.Add(ref _updatedPoints, accepted);
        Interlocked.Add(ref _droppedPoints, rejected);
        if (accepted > 0)
        {
            _lastUpdateAtUtc = DateTime.UtcNow;
        }

        return (accepted, rejected);
    }

    private static string ResolveStableTagId(CanonicalDataPoint point)
    {
        // The pipeline may put an explicit stableTagId in metadata; if not,
        // fall back to the canonical TagName. TagName is operator-renamable
        // but is the best identity-fallback available before the static tag
        // registry lands (H.x). Documented in the namespace-policy contract.
        if (point.Metadata is { } md
            && md.TryGetValue("stableTagId", out var raw)
            && raw is string s
            && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return point.TagName;
    }

    private static Dictionary<string, string?> BuildBrowsePathPlaceholders(CanonicalDataPoint point, string stableTagId)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["sourceId"] = point.SourceInstanceId,
            ["tagName"] = point.TagName,
            ["stableTagId"] = stableTagId,
            ["gatewayId"] = point.GatewayId,
        };

        if (point.Metadata is { } md)
        {
            CopyIfPresent(md, dict, "site");
            CopyIfPresent(md, dict, "area");
            CopyIfPresent(md, dict, "line");
            CopyIfPresent(md, dict, "assetClass");
            CopyIfPresent(md, dict, "assetId");
            CopyIfPresent(md, dict, "deviceClass");
            CopyIfPresent(md, dict, "category");
        }

        return dict;
    }

    private static void CopyIfPresent(IReadOnlyDictionary<string, object> source, Dictionary<string, string?> dest, string key)
    {
        if (source.TryGetValue(key, out var value) && value is string s && !string.IsNullOrWhiteSpace(s))
        {
            dest[key] = s;
        }
    }

    private ApplicationConfiguration BuildApplicationConfiguration()
    {
        var appConfig = new ApplicationConfiguration
        {
            ApplicationName = _config.ApplicationName,
            ApplicationUri = _config.ApplicationUri,
            ApplicationType = ApplicationType.Server,

            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = { _config.EndpointUrl },
                MinRequestThreadCount = 5,
                MaxRequestThreadCount = 100,
                MaxQueuedRequestCount = 200,
                SecurityPolicies = BuildServerSecurityPolicies(_config.Security),
                UserTokenPolicies = BuildUserTokenPolicies(_config.Security),
                MaxSessionCount = _config.MaxSessions,
                MinSessionTimeout = 10_000,
                MaxSessionTimeout = 3_600_000,
                MaxBrowseContinuationPoints = 10,
                MaxQueryContinuationPoints = 10,
                MaxHistoryContinuationPoints = 10,
                MaxRequestAge = 600_000,
                MinPublishingInterval = _config.MinPublishingIntervalMs,
                MaxPublishingInterval = 3_600_000,
                PublishingResolution = _config.MinPublishingIntervalMs,
                MaxSubscriptionLifetime = 3_600_000,
                MaxMessageQueueSize = 100,
                MaxNotificationQueueSize = 100,
                MaxNotificationsPerPublish = 1_000,
                MinSubscriptionLifetime = 10_000,
                MaxPublishRequestCount = 20,
                MaxSubscriptionCount = _config.MaxSessions * 10,
                MaxEventQueueSize = 10_000,
            },

            SecurityConfiguration = _certManager!.BuildSecurityConfiguration(),

            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 120_000,
                MaxStringLength = 1_048_576,
                MaxByteStringLength = 4_194_304,
                MaxArrayLength = 65_535,
                MaxMessageSize = 4_194_304,
                MaxBufferSize = 65_535,
                ChannelLifetime = 300_000,
                SecurityTokenLifetime = 3_600_000,
            },

            ClientConfiguration = new ClientConfiguration(),
            TraceConfiguration = new TraceConfiguration { TraceMasks = 0 },
        };

        return appConfig;
    }

    /// <summary>
    /// Emit one or more <see cref="ServerSecurityPolicy"/> entries
    /// for the endpoint. K offers <c>SecurityPolicies.None</c> for
    /// <see cref="OpcUaSecurityMode.None"/>, <c>Basic256Sha256</c>
    /// with <see cref="MessageSecurityMode.Sign"/> or
    /// <see cref="MessageSecurityMode.SignAndEncrypt"/> for the
    /// hardened modes. Future milestones can add additional policies
    /// (Aes128_Sha256_RsaOaep, Aes256_Sha256_RsaPss) without changing
    /// the adapter contract.
    /// </summary>
    private static ServerSecurityPolicyCollection BuildServerSecurityPolicies(OpcUaSecurityConfig security)
    {
        var policies = new ServerSecurityPolicyCollection();
        switch (security.Mode)
        {
            case OpcUaSecurityMode.None:
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                });
                break;
            case OpcUaSecurityMode.Sign:
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.Sign,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                });
                break;
            case OpcUaSecurityMode.SignAndEncrypt:
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                });
                break;
            default:
                throw new InvalidOperationException(
                    $"OPCUA.UNKNOWN_SECURITY_MODE: unrecognised SecurityMode '{security.Mode}'.");
        }
        return policies;
    }

    /// <summary>
    /// Emit one <see cref="UserTokenPolicy"/> per configured
    /// <see cref="OpcUaUserTokenPolicy"/> entry. Username/Certificate
    /// policies on an unencrypted endpoint expose the credential on
    /// the wire — the adapter accepts the combo for backward-compat
    /// with PLCs / MES products that demand it, but the ops runbook
    /// warns operators to pair UserName/Certificate with
    /// SecurityMode=SignAndEncrypt for production.
    /// </summary>
    private static UserTokenPolicyCollection BuildUserTokenPolicies(OpcUaSecurityConfig security)
    {
        var policies = new UserTokenPolicyCollection();
        foreach (var policy in security.UserTokenPolicies)
        {
            switch (policy)
            {
                case OpcUaUserTokenPolicy.Anonymous:
                    policies.Add(new UserTokenPolicy(UserTokenType.Anonymous));
                    break;
                case OpcUaUserTokenPolicy.UserName:
                    policies.Add(new UserTokenPolicy(UserTokenType.UserName)
                    {
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    });
                    break;
                case OpcUaUserTokenPolicy.Certificate:
                    policies.Add(new UserTokenPolicy(UserTokenType.Certificate)
                    {
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    });
                    break;
                default:
                    throw new InvalidOperationException(
                        $"OPCUA.UNKNOWN_USER_TOKEN_POLICY: '{policy}'.");
            }
        }
        if (policies.Count == 0)
        {
            // Defensive: an endpoint with zero user-token policies is
            // unusable. Coherence check upstream should have caught this,
            // but fall back to Anonymous so the adapter never produces
            // an unconnectable endpoint silently.
            policies.Add(new UserTokenPolicy(UserTokenType.Anonymous));
        }
        return policies;
    }

    /// <summary>
    /// K: config coherence checks. Replace H.1's
    /// <c>EnforceMvpSecurityPolicy</c>, which rejected every mode
    /// other than None+Anonymous. Now we only reject combinations
    /// that are operator errors — e.g. <see cref="OpcUaUserTokenPolicy.UserName"/>
    /// without any credentials configured.
    /// </summary>
    private static void ValidateSecurityCoherence(OpcUaSecurityConfig security)
    {
        if (security.UserTokenPolicies.Count == 0)
        {
            throw new InvalidOperationException(
                "OPCUA.SECURITY_NO_TOKEN_POLICIES: at least one user-token policy must be enabled.");
        }

        var hasUserName = false;
        var hasCertificate = false;
        foreach (var p in security.UserTokenPolicies)
        {
            if (p == OpcUaUserTokenPolicy.UserName) hasUserName = true;
            if (p == OpcUaUserTokenPolicy.Certificate) hasCertificate = true;
        }

        if (hasUserName && security.Credentials.Count == 0)
        {
            throw new InvalidOperationException(
                "OPCUA.USERNAME_POLICY_WITHOUT_CREDENTIALS: UserName user-token policy is enabled " +
                "but Security.Credentials is empty — no username/password pair would match. " +
                "Either remove the UserName policy or populate Credentials.");
        }

        // Certificate policy with no TrustedClientsPath surfaces as a
        // soft warning at K; the library auto-creates the directory on
        // first start and operators populate it by promoting from the
        // rejected/ folder. Explicit path is recommended in
        // multi-instance deployments — that's the ops-runbook concern.
        _ = hasCertificate;
    }

    private void TransitionState(AdapterState target)
    {
        lock (_stateLock)
        {
            State = target;
        }
    }

    /// <summary>
    /// Translate a <see cref="StartAsync"/> failure into an operator-facing
    /// <see cref="AdapterError"/>. Port-bind conflicts (very common on
    /// engineering workstations where Kepware / Matrikon / Prosys / vendor
    /// PLC tooling already owns 4840) get the dedicated
    /// <c>OPCUA.SERVER_PORT_IN_USE</c> code and a remediation hint pointing
    /// at <c>netstat</c>; everything else falls through to
    /// <c>OPCUA.SERVER_START_FAILED</c>.
    /// Operator-feedback fix 2026-05-28.
    /// </summary>
    internal static AdapterError ClassifyStartFailure(Exception ex, string? endpointUrl)
    {
        if (IsPortInUseFailure(ex))
        {
            var portFragment = PortFragment(endpointUrl);
            var message =
                $"OPC UA Server could not bind {portFragment} — another process on this host is already " +
                $"listening on that port. Run 'netstat -ano | findstr :{NumericPort(endpointUrl)}' to identify " +
                "the holder, stop that service, or change the endpoint port and re-apply.";
            return new AdapterError
            {
                Code = "OPCUA.SERVER_PORT_IN_USE",
                Category = ErrorCategory.Configuration,
                Message = message,
                Retryable = false,
            };
        }

        return new AdapterError
        {
            Code = "OPCUA.SERVER_START_FAILED",
            Category = ErrorCategory.Internal,
            Message = $"Failed to start OPC UA server: {ex.Message}",
            Retryable = false,
        };
    }

    /// <summary>
    /// Heuristic match for port-binding failures from the OPC Foundation
    /// stack. The native exception (System.Net.Sockets.SocketException with
    /// SocketError.AddressAlreadyInUse) is wrapped inside
    /// <c>ServiceResultException</c> in StandardServer.Start and surfaces
    /// with the message "Failed to establish tcp listener sockets…", so we
    /// match on either signature.
    /// </summary>
    internal static bool IsPortInUseFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is System.Net.Sockets.SocketException sock
                && sock.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            {
                return true;
            }

            if (current.Message is { Length: > 0 } msg
                && msg.Contains("Failed to establish tcp listener", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string PortFragment(string? endpointUrl)
    {
        var port = NumericPort(endpointUrl);
        return port == "the configured port" ? port : $"port {port}";
    }

    private static string NumericPort(string? endpointUrl)
    {
        if (!string.IsNullOrWhiteSpace(endpointUrl)
            && Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri)
            && uri.Port > 0)
        {
            return uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return "the configured port";
    }

    private static AdapterError MakeError(string code, ErrorCategory category, string message, bool retryable) =>
        new()
        {
            Code = code,
            Category = category,
            Message = message,
            Retryable = retryable,
        };
}
