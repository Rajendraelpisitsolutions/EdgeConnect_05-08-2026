// ============================================================================
// File: Api/EthernetIpProbeService.cs
// Purpose: Drives the EtherNet/IP source wizard's read-only "Test read" probe:
//          establish a CIP session to host (path / CPU family) and read ONE
//          configured tag once, distinguishing reachable / read-ok / tag-not-
//          found / read-failed / timeout so a broken tag never looks like a
//          connection failure.
//
//          Read-only — never writes to the controller. Mirrors S7ProbeService /
//          ModbusProbeService: license-gated on `source-ethernet-ip`, per-target
//          single-flight, fixed probe budget, correlation ProbeId, and an
//          injectable IEthernetIpClient factory so unit tests pin every outcome
//          against a fake transport.
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §5.2
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.EthernetIp;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Executes the EtherNet/IP source wizard's read-only "Test read" probe.
/// Singleton — owns per-target single-flight leases as instance state.
/// </summary>
public sealed class EthernetIpProbeService
{
    private const string LicenseModuleKey = LicenseModuleKeys.SourceEthernetIp;

    /// <summary>Fixed end-to-end probe budget — matches the Modbus / S7 budgets.</summary>
    internal static readonly TimeSpan DefaultProbeBudget = TimeSpan.FromSeconds(8);

    private readonly Func<IEthernetIpClient> _clientFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<EthernetIpProbeService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _probeBudget;

    /// <summary>
    /// Production constructor — builds a real <see cref="LibPlcTagClient"/> per
    /// probe, gated by <c>source-ethernet-ip</c>. License manager null OR no
    /// license loaded = permissive (matches the other probe services).
    /// </summary>
    public EthernetIpProbeService(ILoggerFactory loggerFactory, ILicenseManager? license = null)
        : this(
            clientFactory: () => new LibPlcTagClient(),
            isModuleEnabled: MqttTestConnectionService.BuildLicenseGate(license),
            loggerFactory: loggerFactory,
            probeBudget: DefaultProbeBudget)
    {
    }

    /// <summary>
    /// Test-only constructor — injects a fake <see cref="IEthernetIpClient"/>,
    /// custom license gate, and budget so unit tests pin every outcome without
    /// a real controller.
    /// </summary>
    internal EthernetIpProbeService(
        Func<IEthernetIpClient> clientFactory,
        Func<string, bool> isModuleEnabled,
        ILoggerFactory loggerFactory,
        TimeSpan probeBudget)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(isModuleEnabled);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _clientFactory = clientFactory;
        _isModuleEnabled = isModuleEnabled;
        _logger = loggerFactory.CreateLogger<EthernetIpProbeService>();
        _probeBudget = probeBudget;
    }

    /// <summary>
    /// Test a single tag read: open a CIP session, read the configured tag
    /// once, and report the outcome. Read-only.
    /// </summary>
    public async Task<EthernetIpProbeOutcome> TestReadAsync(EthernetIpTestReadRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var probeId = GenerateProbeId();
        var sw = Stopwatch.StartNew();

        if (!_isModuleEnabled(LicenseModuleKey))
        {
            return LicenseDisabled(probeId, sw);
        }
        if (string.IsNullOrWhiteSpace(request.Host))
        {
            return Failure(probeId, sw, EthernetIpProbeOutcomes.ConfigInvalid, "ETHERNETIP.PROBE_CONFIG_INVALID", "Host is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Address))
        {
            return Failure(probeId, sw, EthernetIpProbeOutcomes.ConfigInvalid, "ETHERNETIP.PROBE_CONFIG_INVALID", "Tag address is required.");
        }

        EthernetIpElementType elementType;
        try
        {
            elementType = EthernetIpElementTypeExtensions.Parse(request.Datatype);
        }
        catch (ArgumentException ex)
        {
            return Failure(probeId, sw, EthernetIpProbeOutcomes.ConfigInvalid, "ETHERNETIP.PROBE_CONFIG_INVALID",
                $"Tag datatype is invalid: {ex.Message}");
        }

        var cpuFamily = EthernetIpCpuFamilyExtensions.ParseOrNull(request.CpuFamily) ?? EthernetIpCpuFamily.ControlLogix;
        var path = request.Path ?? cpuFamily.DefaultPath();

        var leaseKey = LeaseKey(request.Host, path, request.Address);
        var lease = _leases.GetOrAdd(leaseKey, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            return Busy(probeId, sw, leaseKey);
        }

        IEthernetIpClient? client = null;
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);

            var parameters = new EthernetIpConnectionParameters
            {
                Host = request.Host,
                Path = path,
                CpuFamily = cpuFamily,
                ConnectTimeout = TimeSpan.FromMilliseconds(request.ConnectTimeoutMs <= 0 ? 2000 : request.ConnectTimeoutMs),
                RequestTimeout = TimeSpan.FromMilliseconds(request.ConnectTimeoutMs <= 0 ? 2000 : request.ConnectTimeoutMs),
            };

            client = _clientFactory();

            EthernetIpReadResult read;
            try
            {
                await client.ConnectAsync(parameters, probeCts.Token).ConfigureAwait(false);
                read = await client.ReadTagAsync(request.Address, elementType, probeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (probeCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return Failure(probeId, sw, EthernetIpProbeOutcomes.Timeout, "ETHERNETIP.PROBE_TIMEOUT",
                    $"Read of '{request.Address}' on {request.Host} timed out.");
            }
            catch (EthernetIpFatalException ex)
            {
                return Failure(probeId, sw, EthernetIpProbeOutcomes.ConnectFailed, "ETHERNETIP.PROBE_CONNECT_FAILED",
                    $"Could not reach {request.Host} (path '{path}'): {ex.Message}");
            }

            if (!read.Success)
            {
                var outcome = read.ErrorCode == EthernetIpErrorCodes.TagNotFound
                    ? EthernetIpProbeOutcomes.TagNotFound
                    : EthernetIpProbeOutcomes.ReadFailed;
                return Failure(probeId, sw, outcome, read.ErrorCode ?? "ETHERNETIP.PROBE_READ_FAILED",
                    read.ErrorText ?? $"Read of '{request.Address}' failed.");
            }

            var formatted = Convert.ToString(read.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            _logger.LogInformation("EtherNet/IP probe {ProbeId} read {Address} ok in {Elapsed}ms.", probeId, request.Address, sw.ElapsedMilliseconds);
            return new EthernetIpProbeOutcome(
                EthernetIpProbeStatus.Success,
                Result(probeId, sw, success: true, EthernetIpProbeOutcomes.ReadOk,
                    message: $"Read {request.Address} = {formatted}.", value: formatted));
        }
        finally
        {
            Teardown(client, probeId);
            lease.Release();
        }
    }

    private void Teardown(IEthernetIpClient? client, string probeId)
    {
        if (client is null)
        {
            return;
        }
        try
        {
            client.Disconnect();
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "EtherNet/IP probe {ProbeId} teardown raised an exception (non-fatal).", probeId);
        }
    }

    private static string LeaseKey(string host, string path, string address) => $"{host}|{path}|{address}";

    private EthernetIpProbeOutcome LicenseDisabled(string probeId, Stopwatch sw)
    {
        _logger.LogInformation("EtherNet/IP probe {ProbeId} blocked: license module '{Module}' disabled.", probeId, LicenseModuleKey);
        return new EthernetIpProbeOutcome(
            EthernetIpProbeStatus.LicenseDisabled,
            Result(probeId, sw, success: false, EthernetIpProbeOutcomes.LicenseDisabled,
                errorCode: "ETHERNETIP.PROBE_LICENSE_DISABLED",
                message: "Allen-Bradley EtherNet/IP source module is not licensed for this gateway."));
    }

    private EthernetIpProbeOutcome Busy(string probeId, Stopwatch sw, string leaseKey)
    {
        _logger.LogInformation("EtherNet/IP probe {ProbeId} busy: {Key} has another probe in flight.", probeId, leaseKey);
        return new EthernetIpProbeOutcome(
            EthernetIpProbeStatus.Busy,
            Result(probeId, sw, success: false, EthernetIpProbeOutcomes.Busy,
                errorCode: "ETHERNETIP.PROBE_BUSY",
                message: $"Another test is in flight against {leaseKey}."));
    }

    private EthernetIpProbeOutcome Failure(string probeId, Stopwatch sw, string outcome, string errorCode, string message)
    {
        _logger.LogWarning("EtherNet/IP probe {ProbeId} failed: {Outcome} {ErrorCode} — {Message}", probeId, outcome, errorCode, message);
        return new EthernetIpProbeOutcome(
            EthernetIpProbeStatus.Failure,
            Result(probeId, sw, success: false, outcome, errorCode: errorCode, message: message));
    }

    private static EthernetIpProbeResultDto Result(
        string probeId, Stopwatch sw, bool success, string outcome,
        string? errorCode = null, string? message = null, string? value = null) =>
        new()
        {
            ProbeId = probeId,
            Success = success,
            ElapsedMs = (int)sw.ElapsedMilliseconds,
            Outcome = outcome,
            ErrorCode = errorCode,
            Message = message,
            Value = value,
        };

    private static string GenerateProbeId() =>
        string.Concat("eipprobe-", Guid.NewGuid().ToString("N").AsSpan(0, 8));
}

/// <summary>
/// Stable error-code strings the EtherNet/IP client returns for non-fatal read
/// failures. Duplicated here (not referenced from the internal adapter catalog)
/// so the probe can classify outcomes without reaching into adapter internals.
/// </summary>
internal static class EthernetIpErrorCodes
{
    internal const string TagNotFound = "ETHERNETIP.TAG_NOT_FOUND";
}

/// <summary>Service-level status that drives the HTTP status code in the API layer.</summary>
public enum EthernetIpProbeStatus
{
    /// <summary>Probe succeeded — HTTP 200.</summary>
    Success,

    /// <summary>Probe ran but connect / read failed — HTTP 200 with Success=false.</summary>
    Failure,

    /// <summary>License gate refused the probe — HTTP 403.</summary>
    LicenseDisabled,

    /// <summary>Single-flight lease busy — HTTP 409.</summary>
    Busy,
}

/// <summary>Stable outcome tokens surfaced to the wizard for inline rendering.</summary>
public static class EthernetIpProbeOutcomes
{
    /// <summary>Test read: read succeeded and decoded.</summary>
    public const string ReadOk = "read-ok";
    /// <summary>Test read: the controller does not contain the tag.</summary>
    public const string TagNotFound = "tag-not-found";
    /// <summary>Test read: the read failed for another reason.</summary>
    public const string ReadFailed = "read-failed";
    /// <summary>Test read: could not reach the controller.</summary>
    public const string ConnectFailed = "connect-failed";
    /// <summary>Test read: budget timed out.</summary>
    public const string Timeout = "timeout";
    /// <summary>Config invalid before any network contact.</summary>
    public const string ConfigInvalid = "config-invalid";
    /// <summary>License gate refused.</summary>
    public const string LicenseDisabled = "license-disabled";
    /// <summary>Single-flight busy.</summary>
    public const string Busy = "busy";
}

/// <summary>Single-tag read probe request — connection parameters + one tag.</summary>
public sealed record EthernetIpTestReadRequest
{
    /// <summary>Gateway IP / hostname of the controller.</summary>
    public required string Host { get; init; }

    /// <summary>CIP routing path. When null, derived from the CPU family.</summary>
    public string? Path { get; init; }

    /// <summary>CPU family (ControlLogix / CompactLogix / …). Defaults to ControlLogix.</summary>
    public string CpuFamily { get; init; } = "ControlLogix";

    /// <summary>Controller tag address to read once (e.g. <c>"Program:MainProgram.Speed"</c>).</summary>
    public required string Address { get; init; }

    /// <summary>Element-type hint (BOOL / SINT / INT / DINT / LINT / REAL / LREAL / STRING).</summary>
    public required string Datatype { get; init; }

    /// <summary>Connect timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; init; } = 2000;
}

/// <summary>Result DTO for an EtherNet/IP probe.</summary>
public sealed record EthernetIpProbeResultDto
{
    /// <summary>Correlation id for log lines + UI.</summary>
    public required string ProbeId { get; init; }

    /// <summary>True when the probe reached its success outcome.</summary>
    public required bool Success { get; init; }

    /// <summary>Wall-clock probe duration in milliseconds.</summary>
    public required int ElapsedMs { get; init; }

    /// <summary>Stable outcome token (see <see cref="EthernetIpProbeOutcomes"/>).</summary>
    public required string Outcome { get; init; }

    /// <summary>Machine-readable error code when <see cref="Success"/> is false.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Friendly operator message.</summary>
    public string? Message { get; init; }

    /// <summary>Decoded value for a successful read.</summary>
    public string? Value { get; init; }
}

/// <summary>Service-layer outcome — DTO plus the status enum that drives HTTP mapping.</summary>
public sealed record EthernetIpProbeOutcome(EthernetIpProbeStatus Status, EthernetIpProbeResultDto Result);
