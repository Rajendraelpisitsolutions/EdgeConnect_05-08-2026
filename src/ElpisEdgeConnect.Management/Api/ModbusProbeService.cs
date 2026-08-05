// ============================================================================
// File: Api/ModbusProbeService.cs
// Purpose: Drives the Modbus TCP Test Connection probe behind
//          POST /api/v1/sources/browse/modbus — runs the M.2d.2 v2 §4.3
//          escalation ladder:
//
//            1. TCP connect to host:port (≤2s).
//            2. If wizard already has tags, probe the FIRST configured tag
//               (function code + address from the tag itself).
//            3. Else, fallback to a configurable test address. Defaults:
//               FC03 (Read Holding Registers), address 1, quantity 1.
//               NOT address 0 — many PLCs reject 0.
//
//          Diagnostic fields per v2 §4.4 (support-troubleshooting value):
//             FunctionCodeUsed / AddressTested / UnitIdTested /
//             ProbeStepReached.
//
//          Locked invariants (M.2d.2 v2 §4.7):
//            * No state mutation — probe is read-only, never writes (no
//              FC05/FC06/FC15/FC16). The IModbusProbeTransport.Connect
//              + Read pair is the only protocol surface used.
//            * License-gated by source-modbus-tcp module key.
//            * Single-flight per IP:Port:UnitId target identity.
//            * 8s total probe budget end-to-end.
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §4.3, §4.4
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.ModbusTcp;
using FluentModbus;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Executes a one-shot Modbus TCP Test Connection probe — used by the
/// Modbus source wizard's "Test Connection" button to verify slave
/// reachability before the operator commits a draft.
/// </summary>
/// <remarks>
/// <para>
/// Ladder per v2 §4.3 — TCP connect → first configured tag (if any) →
/// configurable fallback. Fallback defaults are deliberately chosen to
/// avoid vendor footguns: FC03/HR1 instead of HR0 (some PLCs reject
/// address 0), quantity 1.
/// </para>
/// <para>
/// Architecturally this service uses FluentModbus directly rather than
/// the internal <c>IModbusClient</c> contract owned by the adapter —
/// probe is a single read with no scan-group / backoff / retry logic,
/// so reusing the adapter's full transaction-executor would be overkill.
/// FluentModbus is referenced explicitly in the Management csproj
/// (pinned to the same version Sources.ModbusTcp uses).
/// </para>
/// </remarks>
public sealed class ModbusProbeService
{
    private const string LicenseModuleKey = LicenseModuleKeys.SourceModbusTcp;

    /// <summary>Fixed end-to-end probe budget — 8s matches the Focas2 + Brother budgets.</summary>
    internal static readonly TimeSpan DefaultProbeBudget = TimeSpan.FromSeconds(8);

    /// <summary>TCP connect step budget — ≤2s per v2 §4.3 ladder step 1.</summary>
    internal static readonly TimeSpan TcpConnectBudget = TimeSpan.FromSeconds(2);

    private readonly Func<IModbusProbeTransport> _transportFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<ModbusProbeService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _probeBudget;

    /// <summary>
    /// Production constructor — uses the real FluentModbus transport,
    /// license manager gated by <c>source-modbus-tcp</c>. License manager
    /// null OR no license loaded = permissive (matches MQTT + Focas2
    /// dev-mode semantic).
    /// </summary>
    public ModbusProbeService(
        ILoggerFactory loggerFactory,
        ILicenseManager? license = null)
        : this(
            transportFactory: () => new FluentModbusProbeTransport(),
            isModuleEnabled: MqttTestConnectionService.BuildLicenseGate(license),
            loggerFactory: loggerFactory,
            probeBudget: DefaultProbeBudget)
    {
    }

    /// <summary>
    /// Test-only constructor — injects a fake transport, custom license
    /// gate, and budget so unit tests can pin all three ladder steps
    /// deterministically without standing up a real TCP socket.
    /// </summary>
    internal ModbusProbeService(
        Func<IModbusProbeTransport> transportFactory,
        Func<string, bool> isModuleEnabled,
        ILoggerFactory loggerFactory,
        TimeSpan probeBudget)
    {
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(isModuleEnabled);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _transportFactory = transportFactory;
        _isModuleEnabled = isModuleEnabled;
        _logger = loggerFactory.CreateLogger<ModbusProbeService>();
        _probeBudget = probeBudget;
    }

    /// <summary>
    /// Run the probe. Returns a <see cref="ModbusProbeOutcome"/> describing
    /// success or the categorised failure, with diagnostic fields surfaced
    /// for the wizard's support-friendly result panel.
    /// </summary>
    public async Task<ModbusProbeOutcome> ProbeAsync(
        ModbusProbeRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var probeId = GenerateProbeId();
        var sw = Stopwatch.StartNew();

        // ── License gate ──────────────────────────────────────────────
        if (!_isModuleEnabled(LicenseModuleKey))
        {
            _logger.LogInformation("Modbus probe {ProbeId} blocked: license module '{Module}' disabled.",
                probeId, LicenseModuleKey);
            return new ModbusProbeOutcome(
                Status: ModbusProbeStatus.LicenseDisabled,
                Result: new ModbusProbeResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ErrorCode = "MODBUS.PROBE_LICENSE_DISABLED",
                    ErrorMessage = "Modbus TCP source module is not licensed for this gateway.",
                });
        }

        // ── Config validation ─────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.IpAddress))
        {
            return Fail(probeId, sw, ModbusProbeStep.TcpConnect, null, null, null, null,
                "MODBUS.PROBE_CONFIG_INVALID", "ipAddress is required.");
        }
        if (request.Port <= 0 || request.Port > 65535)
        {
            return Fail(probeId, sw, ModbusProbeStep.TcpConnect, null, null, null, null,
                "MODBUS.PROBE_CONFIG_INVALID", $"port {request.Port} is out of range (1-65535).");
        }

        // ── Single-flight per IP:Port:UnitId ──────────────────────────
        var leaseKey = $"{request.IpAddress}:{request.Port}:{request.UnitId}";
        var lease = _leases.GetOrAdd(leaseKey, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("Modbus probe {ProbeId} busy: {Key} has another probe in flight.",
                probeId, leaseKey);
            return new ModbusProbeOutcome(
                Status: ModbusProbeStatus.Busy,
                Result: new ModbusProbeResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ErrorCode = "MODBUS.PROBE_BUSY",
                    ErrorMessage = $"Another Test Connection probe is in flight against {leaseKey}.",
                    UnitIdTested = request.UnitId,
                });
        }

        IModbusProbeTransport? transport = null;
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);

            transport = _transportFactory();

            // ── Step 1: TCP connect ────────────────────────────────────
            _logger.LogInformation(
                "Modbus probe {ProbeId} → step 1 TCP connect to {Host}:{Port} (unit {Unit}, budget {Budget}s).",
                probeId, request.IpAddress, request.Port, request.UnitId, _probeBudget.TotalSeconds);

            try
            {
                await transport.ConnectAsync(request.IpAddress, request.Port, TcpConnectBudget, probeCts.Token)
                    .ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (probeCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return Fail(probeId, sw, ModbusProbeStep.TcpConnect, null, null, null, request.UnitId,
                    "MODBUS.PROBE_TCP_TIMEOUT",
                    $"TCP connect to {request.IpAddress}:{request.Port} timed out within {TcpConnectBudget.TotalSeconds:F0}s.");
            }
            catch (SocketException ex)
            {
                return Fail(probeId, sw, ModbusProbeStep.TcpConnect, null, null, null, request.UnitId,
                    "MODBUS.PROBE_TCP_REFUSED",
                    $"TCP connect to {request.IpAddress}:{request.Port} refused: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Fail(probeId, sw, ModbusProbeStep.TcpConnect, null, null, null, request.UnitId,
                    "MODBUS.PROBE_TCP_FAILED",
                    $"TCP connect to {request.IpAddress}:{request.Port} failed: {ex.Message}");
            }

            // ── Step 2/3: pick the read target ─────────────────────────
            // Step 2 — first configured tag if any; step 3 — operator's
            // overrides (or hardcoded fallback if no overrides).
            var (step, fc, addr, quantity) = ResolveReadTarget(request);

            _logger.LogInformation(
                "Modbus probe {ProbeId} → step {Step} read FC={Fc} addr={Addr} qty={Qty} (unit {Unit}).",
                probeId, step, fc, addr, quantity, request.UnitId);

            try
            {
                await transport.ReadAsync(request.UnitId, fc, addr, quantity, probeCts.Token)
                    .ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (probeCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return Fail(probeId, sw, step, fc, addr, quantity, request.UnitId,
                    "MODBUS.PROBE_READ_TIMEOUT",
                    $"Modbus read on {request.IpAddress}:{request.Port} (unit {request.UnitId}, FC{fc}, addr {addr}) timed out.");
            }
            catch (ModbusProbeSlaveRejectedException ex)
            {
                // Slave returned an exception response (illegal data
                // address, illegal function, gateway-target-failed, etc.).
                // The PLC IS reachable; the address/FC was rejected.
                // Operator can adjust via the wizard's probe-overrides
                // disclosure. The production transport translates
                // FluentModbus.ModbusException into this type; tests
                // throw it directly.
                return Fail(probeId, sw, step, fc, addr, quantity, request.UnitId,
                    "MODBUS.PROBE_SLAVE_REJECTED",
                    $"Slave at unit {request.UnitId} rejected FC{fc} addr {addr}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Fail(probeId, sw, step, fc, addr, quantity, request.UnitId,
                    "MODBUS.PROBE_READ_FAILED",
                    $"Modbus read failed: {ex.Message}");
            }

            _logger.LogInformation(
                "Modbus probe {ProbeId} succeeded in {Elapsed}ms via {Step}.",
                probeId, sw.ElapsedMilliseconds, step);

            return new ModbusProbeOutcome(
                Status: ModbusProbeStatus.Success,
                Result: new ModbusProbeResultDto
                {
                    ProbeId = probeId,
                    Success = true,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    FunctionCodeUsed = fc,
                    AddressTested = addr,
                    UnitIdTested = request.UnitId,
                    ProbeStepReached = step,
                });
        }
        finally
        {
            if (transport is not null)
            {
                try { await transport.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Modbus probe {ProbeId} transport dispose raised an exception (non-fatal).", probeId);
                }
            }
            lease.Release();
        }
    }

    /// <summary>
    /// Resolve which (function-code, address, quantity) the probe should
    /// read. Pure — exposed internal so tests can pin the ladder decision
    /// without standing up the whole service.
    /// </summary>
    internal static (ModbusProbeStep Step, byte FunctionCode, ushort Address, ushort Quantity) ResolveReadTarget(
        ModbusProbeRequest request)
    {
        // Step 2 — first configured tag if any
        if (request.FirstConfiguredTag is { } tag)
        {
            return (ModbusProbeStep.FirstConfiguredTag, tag.FunctionCode, tag.Address, tag.Quantity);
        }

        // Step 3 — operator overrides, or hardcoded fallback default
        var overrides = request.Overrides;
        var fc = overrides?.FunctionCode ?? (byte)0x03; // FC03 Read Holding Registers
        var addr = overrides?.Address ?? (ushort)1;     // Address 1, NOT 0
        var qty = overrides?.Quantity ?? (ushort)1;     // Single register
        return (ModbusProbeStep.FallbackTestAddress, fc, addr, qty);
    }

    private ModbusProbeOutcome Fail(
        string probeId,
        Stopwatch sw,
        ModbusProbeStep step,
        byte? fc,
        ushort? address,
        ushort? quantity,
        byte? unitId,
        string errorCode,
        string errorMessage)
    {
        _logger.LogWarning("Modbus probe {ProbeId} failed at {Step}: {ErrorCode} — {Message}",
            probeId, step, errorCode, errorMessage);
        return new ModbusProbeOutcome(
            Status: errorCode is "MODBUS.PROBE_LICENSE_DISABLED" ? ModbusProbeStatus.LicenseDisabled
                    : errorCode is "MODBUS.PROBE_BUSY" ? ModbusProbeStatus.Busy
                    : ModbusProbeStatus.Failure,
            Result: new ModbusProbeResultDto
            {
                ProbeId = probeId,
                Success = false,
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                FunctionCodeUsed = fc,
                AddressTested = address,
                UnitIdTested = unitId,
                ProbeStepReached = step,
            });
    }

    private static string GenerateProbeId() =>
        string.Concat("probe-", Guid.NewGuid().ToString("N").AsSpan(0, 8));
}

/// <summary>Service-level status that drives the HTTP status code in the API layer.</summary>
public enum ModbusProbeStatus
{
    /// <summary>Probe succeeded — HTTP 200.</summary>
    Success,

    /// <summary>Probe ran but the slave rejected, timed out, or refused — HTTP 200 with Success=false.</summary>
    Failure,

    /// <summary>License gate refused the probe — HTTP 403.</summary>
    LicenseDisabled,

    /// <summary>Single-flight lease busy — HTTP 409.</summary>
    Busy,
}

/// <summary>How far the v2 §4.3 escalation ladder advanced.</summary>
public enum ModbusProbeStep
{
    /// <summary>Step 1: TCP connect to host:port.</summary>
    TcpConnect = 1,

    /// <summary>Step 2: read the FIRST configured tag from the wizard model.</summary>
    FirstConfiguredTag = 2,

    /// <summary>Step 3: read the configurable fallback test address (default FC03 / addr 1).</summary>
    FallbackTestAddress = 3,
}

/// <summary>Service-layer outcome — DTO plus the status enum that drives HTTP status mapping.</summary>
public sealed record ModbusProbeOutcome(
    ModbusProbeStatus Status,
    ModbusProbeResultDto Result);

/// <summary>Input parameters for the Modbus probe.</summary>
public sealed record ModbusProbeRequest
{
    /// <summary>Modbus TCP host IP / DNS name.</summary>
    public required string IpAddress { get; init; }

    /// <summary>Modbus TCP port — defaults to 502 if the wizard doesn't override.</summary>
    public int Port { get; init; } = 502;

    /// <summary>Slave unit id (0-247). The probe targets this exact unit.</summary>
    public byte UnitId { get; init; } = 1;

    /// <summary>
    /// First configured tag from the wizard model — if non-null, the
    /// probe reads this tag (step 2). If null, the probe falls back to
    /// <see cref="Overrides"/> or the hardcoded default (step 3).
    /// </summary>
    public ModbusProbeTagTarget? FirstConfiguredTag { get; init; }

    /// <summary>
    /// Wizard-transient probe overrides (FC / address / quantity).
    /// Only consulted when <see cref="FirstConfiguredTag"/> is null.
    /// NOT persisted into SourceInstanceConfig — these are probe-only
    /// parameters per v2 §4.3.
    /// </summary>
    public ModbusProbeOverrides? Overrides { get; init; }
}

/// <summary>
/// Read target for step 2 of the probe ladder. Sourced from the wizard's
/// first configured tag — function code derived from the tag's
/// register class, address from the tag, quantity from the tag's width.
/// </summary>
public sealed record ModbusProbeTagTarget(byte FunctionCode, ushort Address, ushort Quantity);

/// <summary>
/// Wizard-transient probe overrides per v2 §4.3 step 3. Held wizard-side
/// only — NEVER persisted into <c>SourceInstanceConfig</c>. The defaults
/// when this is omitted are FC03 / address 1 / quantity 1 (deliberately
/// NOT address 0; many vendors reject it).
/// </summary>
public sealed record ModbusProbeOverrides
{
    /// <summary>Function code — FC01/02/03/04. Default FC03 when omitted.</summary>
    public byte? FunctionCode { get; init; }

    /// <summary>Address. Default 1 when omitted (NOT 0).</summary>
    public ushort? Address { get; init; }

    /// <summary>Quantity. Default 1 when omitted.</summary>
    public ushort? Quantity { get; init; }
}

/// <summary>
/// Raised by an <see cref="IModbusProbeTransport"/> when the slave
/// responded with a Modbus exception (illegal data address, illegal
/// function, gateway-target-failed, etc.). The PLC IS reachable; the
/// address/function combination was rejected. The production transport
/// translates FluentModbus's <c>ModbusException</c> into this type so
/// the service can categorise without leaking the third-party type.
/// </summary>
public sealed class ModbusProbeSlaveRejectedException : Exception
{
    /// <summary>Construct with a message.</summary>
    public ModbusProbeSlaveRejectedException(string message) : base(message) { }

    /// <summary>Construct with a message and inner exception.</summary>
    public ModbusProbeSlaveRejectedException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Transport abstraction for the Modbus probe. Production implementation
/// wraps FluentModbus's <c>ModbusTcpClient</c>; the test implementation
/// records calls and returns canned responses so all three ladder steps
/// and every failure mode can be exercised without a real TCP socket.
/// </summary>
public interface IModbusProbeTransport : IAsyncDisposable
{
    /// <summary>TCP connect with the supplied connect timeout.</summary>
    Task ConnectAsync(string host, int port, TimeSpan connectTimeout, CancellationToken ct);

    /// <summary>
    /// Read using the supplied function code. Implementations dispatch
    /// to FC01/FC02/FC03/FC04 internally; the probe only cares about
    /// success vs. exception, never the returned values.
    /// </summary>
    Task ReadAsync(byte unitId, byte functionCode, ushort address, ushort quantity, CancellationToken ct);
}

/// <summary>
/// Production <see cref="IModbusProbeTransport"/> — wraps FluentModbus.
/// Internal: tests use a fake transport, not this wrapper.
/// </summary>
internal sealed class FluentModbusProbeTransport : IModbusProbeTransport
{
    private readonly ModbusTcpClient _client = new();
    private bool _disposed;

    public async Task ConnectAsync(string host, int port, TimeSpan connectTimeout, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await Task.Run(() =>
        {
            _client.ConnectTimeout = (int)connectTimeout.TotalMilliseconds;
            _client.ReadTimeout = (int)connectTimeout.TotalMilliseconds;
            _client.WriteTimeout = (int)connectTimeout.TotalMilliseconds;

            if (!System.Net.IPAddress.TryParse(host, out var ip))
            {
                var entries = System.Net.Dns.GetHostAddresses(host);
                if (entries.Length == 0)
                {
                    throw new SocketException((int)SocketError.HostNotFound);
                }
                ip = entries[0];
            }

            _client.Connect(new System.Net.IPEndPoint(ip, port), ModbusEndianness.BigEndian);
        }, ct).ConfigureAwait(false);
    }

    public async Task ReadAsync(byte unitId, byte functionCode, ushort address, ushort quantity, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await Task.Run(() =>
        {
            try
            {
                switch (functionCode)
                {
                    case 0x01:
                        _client.ReadCoils(unitId, address, quantity);
                        return;
                    case 0x02:
                        _client.ReadDiscreteInputs(unitId, address, quantity);
                        return;
                    case 0x03:
                        _client.ReadHoldingRegisters(unitId, address, quantity);
                        return;
                    case 0x04:
                        _client.ReadInputRegisters(unitId, address, quantity);
                        return;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(functionCode),
                            functionCode,
                            $"Probe does not support function code 0x{functionCode:X2} (probe is read-only — FC01/02/03/04).");
                }
            }
            catch (ModbusException ex)
            {
                // Translate FluentModbus's slave-rejection signal into
                // the probe's exception type so ModbusProbeService can
                // categorise it without leaking the third-party type.
                throw new ModbusProbeSlaveRejectedException(ex.Message, ex);
            }
        }, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        catch
        {
            // Disconnect failures are non-fatal — the connection is
            // being torn down anyway.
        }
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
