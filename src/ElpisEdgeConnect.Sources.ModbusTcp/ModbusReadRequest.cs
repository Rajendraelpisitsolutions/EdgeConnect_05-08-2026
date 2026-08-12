// ============================================================================
// File: ModbusReadRequest.cs
// Purpose: Minimal value type describing a single Modbus read transaction.
//          F2's scan-group planner will build ScanPlan objects that carry a
//          list of these; F1 uses them directly for one-shot reads.
// Reference: PHASE3_EXECUTION_PLAN.md §5 (Transaction Executor), §6 (Planner)
// ============================================================================

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// A single Modbus read transaction — unit id, register class, start address,
/// and quantity. Intentionally a <c>readonly record struct</c>: these are
/// allocated on the hot polling path and we want them on the stack.
/// </summary>
/// <remarks>
/// Validation (quantity &gt; 0, quantity within FC limit, unitId &gt; 0) is
/// performed by <see cref="ModbusTransactionExecutor"/>. The record itself
/// carries no validation to keep it cheap.
/// </remarks>
public readonly record struct ModbusReadRequest(
    byte UnitId,
    ModbusRegisterClass RegisterClass,
    ushort StartAddress,
    ushort Quantity);
