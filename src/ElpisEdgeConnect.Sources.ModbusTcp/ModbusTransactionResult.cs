// ============================================================================
// File: ModbusTransactionResult.cs
// Purpose: Result of a single FC01/02/03/04 read transaction. Carries either
//          a typed payload (bits or registers) + metrics, or the error
//          information for diagnostics.
// Reference: PHASE3_EXECUTION_PLAN.md §5 (Transaction Executor)
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Outcome of a single Modbus read transaction. One of <see cref="BitPayload"/>
/// or <see cref="RegisterPayload"/> is non-null on success; both are null on
/// failure with <see cref="Error"/> populated instead.
/// </summary>
public sealed class ModbusTransactionResult
{
    /// <summary>The request that produced this result.</summary>
    public required ModbusReadRequest Request { get; init; }

    /// <summary>True if the transaction succeeded (after any retries).</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Bit values for FC01/FC02 results. <see langword="null"/> for register
    /// reads and for failures.
    /// </summary>
    public bool[]? BitPayload { get; init; }

    /// <summary>
    /// Register values for FC03/FC04 results. <see langword="null"/> for bit
    /// reads and for failures.
    /// </summary>
    public ushort[]? RegisterPayload { get; init; }

    /// <summary>Wall-clock duration from dispatch to response.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>Number of retries attempted before success or final failure.</summary>
    public required int RetryCount { get; init; }

    /// <summary>Error detail. Only populated when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    public AdapterError? Error { get; init; }

    /// <summary>
    /// For slave-exception failures, the raw Modbus exception code
    /// (0x01 Illegal Function, 0x02 Illegal Data Address, …). Zero otherwise.
    /// </summary>
    public byte SlaveExceptionCode { get; init; }
}
