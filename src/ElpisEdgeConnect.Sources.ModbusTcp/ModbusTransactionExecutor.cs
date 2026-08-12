// ============================================================================
// File: ModbusTransactionExecutor.cs
// Purpose: Executes a single FC01/02/03/04 read request with retry, backoff,
//          and wire-lock serialization. Fatal transport errors bubble up as
//          a failed ModbusTransactionResult AND notify the connection
//          manager so it drops and re-opens the socket before the next poll.
// Reference: PHASE3_EXECUTION_PLAN.md §5 (Transaction Executor), §6 (observable outputs)
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Errors;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Executes read transactions (FC01–FC04) against an <see cref="IModbusClient"/>
/// with retry/backoff, request validation, and wire serialization.
/// </summary>
/// <remarks>
/// <para>
/// Retries apply only to transport-fatal errors (socket reset, timeout).
/// Slave-exception responses (Illegal Function / Illegal Data Address) are
/// configuration problems — retrying them just wastes round-trips — so they
/// are surfaced immediately as a failed result.
/// </para>
/// <para>
/// The observable outputs per transaction (elapsed time, retry count, error
/// code) feed <c>AdapterHealth</c> via the caller. F5 extends this with
/// per-block RTT histograms.
/// </para>
/// </remarks>
internal sealed class ModbusTransactionExecutor
{
    private readonly IModbusClient _client;
    private readonly ModbusConnectionManager _connectionManager;
    private readonly ModbusTcpSourceConfiguration _config;
    private readonly string _instanceId;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    public ModbusTransactionExecutor(
        IModbusClient client,
        ModbusConnectionManager connectionManager,
        ModbusTcpSourceConfiguration config,
        string instanceId,
        ILogger logger,
        TimeProvider? time = null)
    {
        _client = client;
        _connectionManager = connectionManager;
        _config = config;
        _instanceId = instanceId;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Execute a single read transaction. Returns a result carrying the
    /// payload on success, or an <see cref="AdapterError"/> on failure.
    /// Only <see cref="OperationCanceledException"/> propagates out.
    /// </summary>
    public async Task<ModbusTransactionResult> ExecuteAsync(ModbusReadRequest request, CancellationToken ct)
    {
        var startedAt = _time.GetTimestamp();

        // Static request validation — cheap, runs before we take the wire lock.
        var configError = ValidateRequest(request);
        if (configError is not null)
        {
            return new ModbusTransactionResult
            {
                Request = request,
                IsSuccess = false,
                Elapsed = _time.GetElapsedTime(startedAt),
                RetryCount = 0,
                Error = configError,
            };
        }

        var maxRetries = Math.Max(0, _config.MaxTransactionRetries);
        ModbusTransactionResult? lastFailure = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (!_client.IsConnected)
            {
                await _connectionManager.EnsureConnectedAsync(ct).ConfigureAwait(false);
            }

            if (!_client.IsConnected)
            {
                lastFailure = Fail(request, startedAt, attempt,
                    MakeError(ModbusErrors.SocketError, ErrorCategory.Network,
                        $"Modbus client for {_config.Host}:{_config.Port} is not connected.",
                        retryable: true));
                if (attempt >= maxRetries)
                {
                    return lastFailure;
                }
                await Task.Delay(ComputeRetryDelay(attempt), ct).ConfigureAwait(false);
                continue;
            }

            ModbusTransactionResult? outcome = null;
            using (await _connectionManager.AcquireWireLockAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    var payload = await DispatchAsync(request, ct).ConfigureAwait(false);
                    outcome = new ModbusTransactionResult
                    {
                        Request = request,
                        IsSuccess = true,
                        BitPayload = payload.Bits,
                        RegisterPayload = payload.Registers,
                        Elapsed = _time.GetElapsedTime(startedAt),
                        RetryCount = attempt,
                    };
                }
                catch (ModbusSlaveException slave)
                {
                    // Slave exceptions are not retryable — configuration fault.
                    _logger.LogDebug(
                        "Modbus source {InstanceId}: slave exception 0x{Code:X2} on FC0{Fc} unit={Unit} addr={Addr} qty={Qty}",
                        _instanceId, slave.ExceptionCode, slave.FunctionCode,
                        request.UnitId, request.StartAddress, request.Quantity);
                    outcome = new ModbusTransactionResult
                    {
                        Request = request,
                        IsSuccess = false,
                        Elapsed = _time.GetElapsedTime(startedAt),
                        RetryCount = attempt,
                        SlaveExceptionCode = slave.ExceptionCode,
                        Error = MapSlaveException(slave),
                    };
                }
                catch (ModbusFatalException fatal)
                {
                    // Fatal transport error — dump the connection and retry
                    // if budget allows. The next loop iteration will call
                    // EnsureConnectedAsync to reopen.
                    _connectionManager.HandleFatalTransportError(fatal);
                    lastFailure = Fail(request, startedAt, attempt,
                        MakeError(fatal.ErrorCode, ErrorCategory.Network, fatal.Message, retryable: true));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Modbus source {InstanceId}: unexpected error on FC0{Fc} unit={Unit} addr={Addr}",
                        _instanceId, (byte)request.RegisterClass + 1,
                        request.UnitId, request.StartAddress);
                    outcome = Fail(request, startedAt, attempt,
                        MakeError(ModbusErrors.TransactionFailed, ErrorCategory.Protocol,
                            $"Unexpected error during Modbus transaction: {ex.Message}",
                            retryable: false));
                }
            }

            if (outcome is not null)
            {
                return outcome;
            }

            // Fatal path: retry if budget remains.
            if (attempt >= maxRetries)
            {
                return lastFailure!;
            }
            await Task.Delay(ComputeRetryDelay(attempt), ct).ConfigureAwait(false);
        }

        return lastFailure ?? Fail(request, startedAt, maxRetries,
            MakeError(ModbusErrors.TransactionFailed, ErrorCategory.Protocol,
                "Modbus transaction exhausted retry budget with no successful result.",
                retryable: true));
    }

    // =========================================================================
    // PRIVATE
    // =========================================================================

    private async Task<ModbusPayload> DispatchAsync(ModbusReadRequest request, CancellationToken ct)
    {
        return request.RegisterClass switch
        {
            ModbusRegisterClass.Coil => new ModbusPayload(
                Bits: await _client.ReadCoilsAsync(request.UnitId, request.StartAddress, request.Quantity, ct).ConfigureAwait(false),
                Registers: null),
            ModbusRegisterClass.DiscreteInput => new ModbusPayload(
                Bits: await _client.ReadDiscreteInputsAsync(request.UnitId, request.StartAddress, request.Quantity, ct).ConfigureAwait(false),
                Registers: null),
            ModbusRegisterClass.HoldingRegister => new ModbusPayload(
                Bits: null,
                Registers: await _client.ReadHoldingRegistersAsync(request.UnitId, request.StartAddress, request.Quantity, ct).ConfigureAwait(false)),
            ModbusRegisterClass.InputRegister => new ModbusPayload(
                Bits: null,
                Registers: await _client.ReadInputRegistersAsync(request.UnitId, request.StartAddress, request.Quantity, ct).ConfigureAwait(false)),
            _ => throw new InvalidOperationException($"Unknown register class {request.RegisterClass}."),
        };
    }

    private AdapterError? ValidateRequest(ModbusReadRequest request)
    {
        if (request.Quantity == 0)
        {
            return MakeError(ModbusErrors.ConfigOutOfRange, ErrorCategory.Configuration,
                "Modbus read quantity must be >= 1.", retryable: false);
        }

        var maxQty = request.RegisterClass.MaxQuantity();
        if (request.Quantity > maxQty)
        {
            return MakeError(ModbusErrors.RequestTooLarge, ErrorCategory.Configuration,
                $"Modbus FC0{request.RegisterClass.ToFunctionCode()} read of {request.Quantity} exceeds max {maxQty}.",
                retryable: false);
        }

        // Modbus address space is 16-bit; start + count must not overflow.
        if (request.StartAddress + request.Quantity > ushort.MaxValue + 1)
        {
            return MakeError(ModbusErrors.ConfigOutOfRange, ErrorCategory.Configuration,
                $"Modbus read range {request.StartAddress}+{request.Quantity} overflows 16-bit address space.",
                retryable: false);
        }

        // unitId 0 is a valid broadcast on some stacks; unitId 255 is the
        // "any slave" sentinel for direct-connect TCP. Only reject obvious
        // out-of-range values (the byte type already caps to 255).
        return null;
    }

    private ModbusTransactionResult Fail(ModbusReadRequest request, long startedAt, int retryCount, AdapterError error)
        => new()
        {
            Request = request,
            IsSuccess = false,
            Elapsed = _time.GetElapsedTime(startedAt),
            RetryCount = retryCount,
            Error = error,
        };

    private TimeSpan ComputeRetryDelay(int attempt)
    {
        // Per-transaction retry uses a bounded linear-plus-jitter delay
        // so paired slaves recovering from a momentary blip get a gap to
        // reinitialize without waiting for the full connect-level backoff.
        var baseMs = Math.Min(100 * (attempt + 1), 1000);
        return TimeSpan.FromMilliseconds(baseMs);
    }

    private static AdapterError MakeError(string code, ErrorCategory category, string message, bool retryable)
        => new()
        {
            Code = code,
            Category = category,
            Message = message,
            Retryable = retryable,
        };

    private static AdapterError MapSlaveException(ModbusSlaveException ex)
    {
        // Map Modbus exception codes onto the taxonomy so the supervisor
        // can decide whether to pause the adapter or mark the tag bad.
        var category = ex.ExceptionCode switch
        {
            0x06 => ErrorCategory.ResourceExhausted, // Slave Device Busy
            0x0A or 0x0B => ErrorCategory.DeviceState, // Gateway unreachable
            _ => ErrorCategory.Protocol,
        };
        var code = ex.ExceptionCode switch
        {
            0x06 => ModbusErrors.SlaveBusy,
            0x0A or 0x0B => ModbusErrors.GatewayUnreachable,
            _ => ModbusErrors.SlaveException,
        };
        return new AdapterError
        {
            Code = code,
            Category = category,
            Message = ex.Message,
            Retryable = category is ErrorCategory.ResourceExhausted or ErrorCategory.DeviceState,
            Context = $"FC={ex.FunctionCode} ExceptionCode=0x{ex.ExceptionCode:X2}",
        };
    }

    private readonly record struct ModbusPayload(bool[]? Bits, ushort[]? Registers);
}
