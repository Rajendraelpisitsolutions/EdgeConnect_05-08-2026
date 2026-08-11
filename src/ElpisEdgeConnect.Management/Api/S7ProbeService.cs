// ============================================================================
// File: Api/S7ProbeService.cs
// Purpose: Drives the Siemens S7 source wizard's two optional, read-only
//          probes (M.2b.2 v2 §"Test-connection design"):
//
//            (a) Test connection  — establish an ISO-on-TCP session against
//                host:port for CPU (rack, slot) and report reachability.
//            (b) Test selected tag read — connect, then issue ONE read of a
//                single configured tag's parsed address, distinguishing
//                read-ok / read-failed / DB-access-denied so a broken tag
//                never looks like a connection failure. (S7 reads are raw
//                bytes, so "datatype mismatch" is not read-detectable — it is
//                enforced pre-Save by S7TagCompatibility instead.)
//
//          Both are read-only — the service never writes to the PLC. Mirrors
//          ModbusProbeService / MqttTestConnectionService: license-gated on
//          `source-s7`, per-target single-flight, fixed probe budget,
//          correlation ProbeId, and an injectable IS7Client factory so unit
//          tests pin every outcome against a fake transport.
// Reference: docs/sessions/2026-06-02-s7-source-wizard-plan-v2.md (M2)
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.S7;
using ElpisEdgeConnect.Sources.S7.Decoding;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Executes the S7 source wizard's read-only "Test connection" and "Test
/// selected tag read" probes. Singleton — owns per-target single-flight
/// leases as instance state.
/// </summary>
public sealed class S7ProbeService
{
    private const string LicenseModuleKey = LicenseModuleKeys.SourceS7;

    /// <summary>Fixed end-to-end probe budget — matches the Modbus/Brother budgets.</summary>
    internal static readonly TimeSpan DefaultProbeBudget = TimeSpan.FromSeconds(8);

    private readonly Func<IS7Client> _clientFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<S7ProbeService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _probeBudget;

    /// <summary>
    /// Production constructor — builds a real <see cref="Sharp7Client"/> per
    /// probe, gated by <c>source-s7</c>. License manager null OR no license
    /// loaded = permissive (matches the other probe services' dev-mode
    /// semantic).
    /// </summary>
    public S7ProbeService(ILoggerFactory loggerFactory, ILicenseManager? license = null)
        : this(
            clientFactory: () => new Sharp7Client(),
            isModuleEnabled: MqttTestConnectionService.BuildLicenseGate(license),
            loggerFactory: loggerFactory,
            probeBudget: DefaultProbeBudget)
    {
    }

    /// <summary>
    /// Test-only constructor — injects a fake <see cref="IS7Client"/>, custom
    /// license gate, and budget so unit tests pin every outcome without a
    /// real PLC.
    /// </summary>
    internal S7ProbeService(
        Func<IS7Client> clientFactory,
        Func<string, bool> isModuleEnabled,
        ILoggerFactory loggerFactory,
        TimeSpan probeBudget)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(isModuleEnabled);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _clientFactory = clientFactory;
        _isModuleEnabled = isModuleEnabled;
        _logger = loggerFactory.CreateLogger<S7ProbeService>();
        _probeBudget = probeBudget;
    }

    /// <summary>Test reachability: connect to the CPU and report the outcome.</summary>
    public async Task<S7ProbeOutcome> TestConnectionAsync(S7TestConnectionRequest request, CancellationToken ct)
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
            return Failure(probeId, sw, S7ProbeOutcomes.ConfigInvalid, "S7.PROBE_CONFIG_INVALID", "Host is required.");
        }

        var leaseKey = LeaseKey(request.Host, request.Port, request.Rack, request.Slot);
        var lease = _leases.GetOrAdd(leaseKey, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            return Busy(probeId, sw, leaseKey);
        }

        IS7Client? client = null;
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);

            client = _clientFactory();
            var connect = await ConnectAsync(client, request, probeCts.Token).ConfigureAwait(false);

            if (connect.Cancelled)
            {
                return Failure(probeId, sw, S7ProbeOutcomes.Timeout, "S7.PROBE_TIMEOUT",
                    $"Connection to {request.Host}:{request.Port} timed out.");
            }
            if (!connect.Result.Success)
            {
                return Failure(probeId, sw, S7ProbeOutcomes.Refused, "S7.PROBE_REFUSED",
                    $"Could not connect to {request.Host}:{request.Port} (rack {request.Rack}, slot {request.Slot}): {connect.Result.ErrorText}");
            }

            _logger.LogInformation("S7 probe {ProbeId} reachable in {Elapsed}ms.", probeId, sw.ElapsedMilliseconds);
            return new S7ProbeOutcome(
                S7ProbeStatus.Success,
                Result(probeId, sw, success: true, S7ProbeOutcomes.Reachable,
                    message: $"PLC reachable at {request.Host}:{request.Port}; rack {request.Rack} / slot {request.Slot} accepted."));
        }
        finally
        {
            Teardown(client, probeId);
            lease.Release();
        }
    }

    /// <summary>
    /// Test a single tag read: connect, then issue one read of the parsed
    /// address. Distinguishes read-ok / read-failed / DB-access-denied.
    /// Read-only.
    /// </summary>
    public async Task<S7ProbeOutcome> TestReadAsync(S7TestReadRequest request, CancellationToken ct)
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
            return Failure(probeId, sw, S7ProbeOutcomes.ConfigInvalid, "S7.PROBE_CONFIG_INVALID", "Host is required.");
        }

        // Resolve the address + datatype up front — a malformed selection is
        // a config error, not a connection or read failure.
        S7Address address;
        S7DatatypeSpec spec;
        try
        {
            address = S7AddressParser.Parse(request.Address);
            spec = ResolveSpec(request.Datatype, address);
        }
        catch (ArgumentException ex)
        {
            return Failure(probeId, sw, S7ProbeOutcomes.ConfigInvalid, "S7.PROBE_CONFIG_INVALID",
                $"Tag address/datatype is invalid: {ex.Message}");
        }

        var leaseKey = LeaseKey(request.Host, request.Port, request.Rack, request.Slot);
        var lease = _leases.GetOrAdd(leaseKey, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            return Busy(probeId, sw, leaseKey);
        }

        IS7Client? client = null;
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);

            client = _clientFactory();
            var connect = await ConnectAsync(client, request, probeCts.Token).ConfigureAwait(false);

            if (connect.Cancelled)
            {
                return Failure(probeId, sw, S7ProbeOutcomes.Timeout, "S7.PROBE_TIMEOUT",
                    $"Connection to {request.Host}:{request.Port} timed out.");
            }
            if (!connect.Result.Success)
            {
                return Failure(probeId, sw, S7ProbeOutcomes.ConnectFailed, "S7.PROBE_CONNECT_FAILED",
                    $"Could not connect to {request.Host}:{request.Port}: {connect.Result.ErrorText}");
            }

            var buffer = new byte[Math.Max(1, spec.ByteCount)];
            var dbNumber = address.Area == S7MemoryArea.DataBlock ? address.DbNumber : 0;
            var read = await client.ReadAreaAsync(
                address.Area, dbNumber, address.ByteOffset, spec.ByteCount, buffer, probeCts.Token)
                .ConfigureAwait(false);

            if (!read.Success)
            {
                // A DB read rejected by the CPU is usually optimized-DB /
                // PUT-GET access — surface it distinctly so the operator
                // looks at DB settings, not the wiring.
                var accessDenied = address.Area == S7MemoryArea.DataBlock
                    && read.ErrorText.Contains("access", StringComparison.OrdinalIgnoreCase);
                return accessDenied
                    ? Failure(probeId, sw, S7ProbeOutcomes.DbAccessDenied, "S7.PROBE_DB_ACCESS_DENIED",
                        $"DB {dbNumber} read was refused by the CPU: {read.ErrorText}. " +
                        "Check that the DB is non-optimized or that PUT/GET access is enabled.")
                    : Failure(probeId, sw, S7ProbeOutcomes.ReadFailed, "S7.PROBE_READ_FAILED",
                        $"Read of {address} failed: {read.ErrorText}");
            }

            object value;
            try
            {
                value = S7Decoder.Decode(buffer, byteOffset: 0, address.BitOffset, spec);
            }
            catch (Exception ex)
            {
                // Defensive: the buffer is sized to the datatype, so the
                // decoder does not throw for byte-granular S7 reads — S7
                // values are raw bytes, so a read that succeeds always
                // decodes. Datatype/address compatibility is enforced
                // pre-Save by S7TagCompatibility, not here. This branch only
                // fires if a future decoder learns a throwing type; surface
                // it as a read failure rather than crashing the probe.
                return Failure(probeId, sw, S7ProbeOutcomes.ReadFailed, "S7.PROBE_READ_FAILED",
                    $"Read {address} but could not decode it as {spec.Datatype}: {ex.Message}");
            }

            var formatted = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            _logger.LogInformation("S7 probe {ProbeId} read {Address} ok in {Elapsed}ms.", probeId, address, sw.ElapsedMilliseconds);
            return new S7ProbeOutcome(
                S7ProbeStatus.Success,
                Result(probeId, sw, success: true, S7ProbeOutcomes.ReadOk,
                    message: $"Read {address} = {formatted}.", value: formatted));
        }
        finally
        {
            Teardown(client, probeId);
            lease.Release();
        }
    }

    private async Task<(S7OperationResult Result, bool Cancelled)> ConnectAsync(
        IS7Client client, S7TestConnectionRequest request, CancellationToken probeToken)
    {
        if (!Enum.TryParse<S7ConnectionType>(request.ConnectionType, ignoreCase: true, out var connectionType))
        {
            connectionType = S7ConnectionType.Basic;
        }
        var timeout = TimeSpan.FromMilliseconds(request.ConnectTimeoutMs <= 0 ? 2000 : request.ConnectTimeoutMs);

        try
        {
            var result = await client.ConnectAsync(
                request.Host, request.Port, request.Rack, request.Slot, connectionType, timeout, probeToken)
                .ConfigureAwait(false);
            return (result, false);
        }
        catch (OperationCanceledException) when (probeToken.IsCancellationRequested)
        {
            return (S7OperationResult.Fail(-1, "cancelled"), true);
        }
    }

    private static S7DatatypeSpec ResolveSpec(string? datatype, S7Address address)
    {
        if (!string.IsNullOrWhiteSpace(datatype))
        {
            return S7DatatypeParser.Parse(datatype, defaultSpec: default);
        }
        return address.WidthHint switch
        {
            S7AddressWidthHint.Bit => new S7DatatypeSpec(S7Datatype.Bool),
            S7AddressWidthHint.Byte => new S7DatatypeSpec(S7Datatype.Byte),
            S7AddressWidthHint.Word => new S7DatatypeSpec(S7Datatype.Word),
            S7AddressWidthHint.DWord => new S7DatatypeSpec(S7Datatype.DWord),
            _ => new S7DatatypeSpec(S7Datatype.Word),
        };
    }

    private void Teardown(IS7Client? client, string probeId)
    {
        if (client is null)
        {
            return;
        }
        try
        {
            client.Disconnect();
            client.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "S7 probe {ProbeId} teardown raised an exception (non-fatal).", probeId);
        }
    }

    private static string LeaseKey(string host, int port, int rack, int slot) =>
        $"{host}:{port}:{rack}:{slot}";

    private S7ProbeOutcome LicenseDisabled(string probeId, Stopwatch sw)
    {
        _logger.LogInformation("S7 probe {ProbeId} blocked: license module '{Module}' disabled.", probeId, LicenseModuleKey);
        return new S7ProbeOutcome(
            S7ProbeStatus.LicenseDisabled,
            Result(probeId, sw, success: false, S7ProbeOutcomes.LicenseDisabled,
                errorCode: "S7.PROBE_LICENSE_DISABLED",
                message: "Siemens S7 source module is not licensed for this gateway."));
    }

    private S7ProbeOutcome Busy(string probeId, Stopwatch sw, string leaseKey)
    {
        _logger.LogInformation("S7 probe {ProbeId} busy: {Key} has another probe in flight.", probeId, leaseKey);
        return new S7ProbeOutcome(
            S7ProbeStatus.Busy,
            Result(probeId, sw, success: false, S7ProbeOutcomes.Busy,
                errorCode: "S7.PROBE_BUSY",
                message: $"Another test is in flight against {leaseKey}."));
    }

    private S7ProbeOutcome Failure(string probeId, Stopwatch sw, string outcome, string errorCode, string message)
    {
        _logger.LogWarning("S7 probe {ProbeId} failed: {Outcome} {ErrorCode} — {Message}", probeId, outcome, errorCode, message);
        return new S7ProbeOutcome(
            S7ProbeStatus.Failure,
            Result(probeId, sw, success: false, outcome, errorCode: errorCode, message: message));
    }

    private static S7ProbeResultDto Result(
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
        string.Concat("s7probe-", Guid.NewGuid().ToString("N").AsSpan(0, 8));
}

/// <summary>Service-level status that drives the HTTP status code in the API layer.</summary>
public enum S7ProbeStatus
{
    /// <summary>Probe succeeded — HTTP 200.</summary>
    Success,

    /// <summary>Probe ran but connect/read failed — HTTP 200 with Success=false.</summary>
    Failure,

    /// <summary>License gate refused the probe — HTTP 403.</summary>
    LicenseDisabled,

    /// <summary>Single-flight lease busy — HTTP 409.</summary>
    Busy,
}

/// <summary>Stable outcome tokens surfaced to the wizard for inline rendering.</summary>
public static class S7ProbeOutcomes
{
    /// <summary>Test connection: CPU reachable.</summary>
    public const string Reachable = "reachable";
    /// <summary>Test connection: connection refused / unreachable.</summary>
    public const string Refused = "refused";
    /// <summary>Test connection/read: budget timed out.</summary>
    public const string Timeout = "timeout";
    /// <summary>Test read: connect step failed before the read.</summary>
    public const string ConnectFailed = "connect-failed";
    /// <summary>Test read: read succeeded and decoded.</summary>
    public const string ReadOk = "read-ok";
    /// <summary>Test read: the CPU rejected or failed the read.</summary>
    public const string ReadFailed = "read-failed";
    /// <summary>Test read: DB read refused (optimized DB / PUT-GET).</summary>
    public const string DbAccessDenied = "db-access-denied";
    /// <summary>Config invalid before any network contact.</summary>
    public const string ConfigInvalid = "config-invalid";
    /// <summary>License gate refused.</summary>
    public const string LicenseDisabled = "license-disabled";
    /// <summary>Single-flight busy.</summary>
    public const string Busy = "busy";
}

/// <summary>Reachability probe request — connection parameters only.</summary>
public record S7TestConnectionRequest
{
    /// <summary>PLC IP / hostname.</summary>
    public required string Host { get; init; }

    /// <summary>ISO-on-TCP port (default 102).</summary>
    public int Port { get; init; } = 102;

    /// <summary>Hardware rack number.</summary>
    public int Rack { get; init; }

    /// <summary>CPU slot number.</summary>
    public int Slot { get; init; } = 1;

    /// <summary>Connection type — Basic / OP / PG.</summary>
    public string ConnectionType { get; init; } = "Basic";

    /// <summary>Connect timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; init; } = 2000;
}

/// <summary>Single-tag read probe request — connection parameters + one tag.</summary>
public sealed record S7TestReadRequest : S7TestConnectionRequest
{
    /// <summary>Operator-authored S7 address to read once (e.g. <c>"DB1.DBW0"</c>).</summary>
    public required string Address { get; init; }

    /// <summary>
    /// Datatype hint for decoding — the adapter wire form (e.g. <c>"Int"</c>,
    /// <c>"string[16]"</c>). When omitted, derived from the address width.
    /// </summary>
    public string? Datatype { get; init; }
}

/// <summary>Result DTO for an S7 probe — connection or single-read.</summary>
public sealed record S7ProbeResultDto
{
    /// <summary>Correlation id for log lines + UI.</summary>
    public required string ProbeId { get; init; }

    /// <summary>True when the probe reached its success outcome.</summary>
    public required bool Success { get; init; }

    /// <summary>Wall-clock probe duration in milliseconds.</summary>
    public required int ElapsedMs { get; init; }

    /// <summary>Stable outcome token (see <see cref="S7ProbeOutcomes"/>).</summary>
    public required string Outcome { get; init; }

    /// <summary>Machine-readable error code when <see cref="Success"/> is false.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Friendly operator message.</summary>
    public string? Message { get; init; }

    /// <summary>Decoded value for a successful read.</summary>
    public string? Value { get; init; }
}

/// <summary>Service-layer outcome — DTO plus the status enum that drives HTTP mapping.</summary>
public sealed record S7ProbeOutcome(S7ProbeStatus Status, S7ProbeResultDto Result);
