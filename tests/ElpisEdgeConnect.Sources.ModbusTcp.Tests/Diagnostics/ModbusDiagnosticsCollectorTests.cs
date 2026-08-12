// ============================================================================
// File: Diagnostics/ModbusDiagnosticsCollectorTests.cs
// Purpose: Unit tests for the ModbusDiagnosticsCollector — block routing,
//          per-block isolation, snapshot ordering, global slave-exception
//          aggregation.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sources.ModbusTcp.Diagnostics;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Diagnostics;

public sealed class ModbusDiagnosticsCollectorTests
{
    private static ScanPlan PlanWith(params ModbusTagDefinition[] tags) =>
        ScanPlanner.Build(tags);

    private static ModbusTagDefinition Reg(string name, ushort addr, byte unitId = 1, int scanMs = 1000) => new()
    {
        Name = name,
        UnitId = unitId,
        RegisterClass = ModbusRegisterClass.HoldingRegister,
        Address = addr,
        ScanRateMs = scanMs,
        Datatype = "uint16",
    };

    private static ModbusTransactionResult Success(ScanBlockKey key, double ms) => new()
    {
        Request = new ModbusReadRequest(key.UnitId, key.RegisterClass, key.StartAddress, key.Count),
        IsSuccess = true,
        RegisterPayload = new ushort[key.Count],
        Elapsed = TimeSpan.FromMilliseconds(ms),
        RetryCount = 0,
    };

    private static ModbusTransactionResult TransportFailure(ScanBlockKey key, int retries = 1) => new()
    {
        Request = new ModbusReadRequest(key.UnitId, key.RegisterClass, key.StartAddress, key.Count),
        IsSuccess = false,
        Elapsed = TimeSpan.FromMilliseconds(500),
        RetryCount = retries,
        Error = new AdapterError
        {
            Code = ModbusErrors.Timeout,
            Category = ErrorCategory.Network,
            Message = "timed out",
            Retryable = true,
        },
    };

    private static ModbusTransactionResult SlaveException(ScanBlockKey key, byte exceptionCode) => new()
    {
        Request = new ModbusReadRequest(key.UnitId, key.RegisterClass, key.StartAddress, key.Count),
        IsSuccess = false,
        Elapsed = TimeSpan.FromMilliseconds(12),
        RetryCount = 0,
        SlaveExceptionCode = exceptionCode,
        Error = new AdapterError
        {
            Code = ModbusErrors.SlaveException,
            Category = ErrorCategory.Protocol,
            Message = $"slave exception 0x{exceptionCode:X2}",
            Retryable = false,
        },
    };

    // -------------------------------------------------------------
    // Structural
    // -------------------------------------------------------------

    [Fact]
    public void Constructor_RegistersEveryBlockFromPlan()
    {
        var plan = PlanWith(
            Reg("a", 0),
            Reg("b", 100),
            Reg("c", 0, unitId: 2));

        var collector = new ModbusDiagnosticsCollector(plan);
        var snapshot = collector.Snapshot();

        // 3 distinct (unitId, class, address) ⇒ 3 blocks.
        snapshot.Blocks.Should().HaveCount(3);
        snapshot.Blocks.Should().AllSatisfy(b => b.Transactions.Should().Be(0));
        snapshot.SlaveExceptionsByCode.Should().BeEmpty();
    }

    [Fact]
    public void Snapshot_BlocksOrderedBy_Unit_Class_Address()
    {
        var plan = PlanWith(
            Reg("z", 100, unitId: 2),
            Reg("a", 0,   unitId: 1),
            Reg("b", 50,  unitId: 1));

        var collector = new ModbusDiagnosticsCollector(plan);
        var snapshot = collector.Snapshot();

        snapshot.Blocks.Select(b => (b.Key.UnitId, (int)b.Key.StartAddress)).Should().Equal(
            ((byte)1, 0),
            ((byte)1, 50),
            ((byte)2, 100));
    }

    // -------------------------------------------------------------
    // Per-block isolation
    // -------------------------------------------------------------

    [Fact]
    public void Record_UpdatesOnlyTheTargetedBlock()
    {
        var plan = PlanWith(
            Reg("a", 0),
            Reg("b", 100));
        var collector = new ModbusDiagnosticsCollector(plan);

        var keyA = new ScanBlockKey(1, ModbusRegisterClass.HoldingRegister, 0, 1);
        var keyB = new ScanBlockKey(1, ModbusRegisterClass.HoldingRegister, 100, 1);

        collector.RecordTransaction(keyA, Success(keyA, 10));
        collector.RecordTransaction(keyA, Success(keyA, 12));

        var snap = collector.Snapshot();
        var a = snap.Blocks.Single(b => b.Key.StartAddress == 0);
        var b = snap.Blocks.Single(blk => blk.Key.StartAddress == 100);

        a.Transactions.Should().Be(2);
        b.Transactions.Should().Be(0);
    }

    [Fact]
    public void Record_UnknownKey_DoesNotThrow()
    {
        var plan = PlanWith(Reg("a", 0));
        var collector = new ModbusDiagnosticsCollector(plan);
        var strangerKey = new ScanBlockKey(99, ModbusRegisterClass.InputRegister, 500, 10);

        var act = () => collector.RecordTransaction(strangerKey, Success(strangerKey, 5));
        act.Should().NotThrow("stale keys from old plans must not crash the collector");
    }

    // -------------------------------------------------------------
    // Slave-exception aggregation
    // -------------------------------------------------------------

    [Fact]
    public void RecordSlaveException_AggregatesGlobalMapByFcAndCode()
    {
        var plan = PlanWith(Reg("a", 0));
        var collector = new ModbusDiagnosticsCollector(plan);
        var key = new ScanBlockKey(1, ModbusRegisterClass.HoldingRegister, 0, 1);

        collector.RecordTransaction(key, SlaveException(key, 0x02));
        collector.RecordTransaction(key, SlaveException(key, 0x02));
        collector.RecordTransaction(key, SlaveException(key, 0x06));

        var snap = collector.Snapshot();
        snap.SlaveExceptionsByCode.Should().ContainKey("0x03/0x02")
            .WhoseValue.Should().Be(2);
        snap.SlaveExceptionsByCode.Should().ContainKey("0x03/0x06")
            .WhoseValue.Should().Be(1);

        // Per-block tally also increments.
        var block = snap.Blocks.Single();
        block.SlaveExceptions.Should().Be(3);
        block.TransportErrors.Should().Be(0);
    }

    [Fact]
    public void RecordSlaveException_AcrossBlocks_MapKeyedByFcNotBlock()
    {
        var plan = PlanWith(
            Reg("a", 0),
            Reg("b", 100));
        var collector = new ModbusDiagnosticsCollector(plan);

        var keyA = new ScanBlockKey(1, ModbusRegisterClass.HoldingRegister, 0, 1);
        var keyB = new ScanBlockKey(1, ModbusRegisterClass.HoldingRegister, 100, 1);

        collector.RecordTransaction(keyA, SlaveException(keyA, 0x02));
        collector.RecordTransaction(keyB, SlaveException(keyB, 0x02));

        var snap = collector.Snapshot();
        snap.SlaveExceptionsByCode["0x03/0x02"].Should().Be(2,
            "global map aggregates across blocks for the same (FC, code) pair");
    }

    [Fact]
    public void TransportFailure_DoesNotBumpSlaveExceptionMap()
    {
        var plan = PlanWith(Reg("a", 0));
        var collector = new ModbusDiagnosticsCollector(plan);
        var key = new ScanBlockKey(1, ModbusRegisterClass.HoldingRegister, 0, 1);

        collector.RecordTransaction(key, TransportFailure(key));

        var snap = collector.Snapshot();
        snap.SlaveExceptionsByCode.Should().BeEmpty();
        var block = snap.Blocks.Single();
        block.TransportErrors.Should().Be(1);
        block.SlaveExceptions.Should().Be(0);
    }

    // -------------------------------------------------------------
    // Decode errors
    // -------------------------------------------------------------

    [Fact]
    public void RecordDecodeError_IncrementsPerBlockCounter()
    {
        var plan = PlanWith(Reg("a", 0));
        var collector = new ModbusDiagnosticsCollector(plan);
        var key = new ScanBlockKey(1, ModbusRegisterClass.HoldingRegister, 0, 1);

        collector.RecordDecodeError(key, "MODBUS.DECODE_FAILED", ErrorCategory.Protocol);
        collector.RecordDecodeError(key, "MODBUS.DECODE_FAILED", ErrorCategory.Protocol);

        var block = collector.Snapshot().Blocks.Single();
        block.DecodeErrors.Should().Be(2);
        block.Failures.Should().Be(0, "decode errors don't count as transaction failures");
    }

    // -------------------------------------------------------------
    // Mixed traffic
    // -------------------------------------------------------------

    [Fact]
    public void MixedTraffic_ProducesCoherentSnapshot()
    {
        var plan = PlanWith(Reg("a", 0));
        var collector = new ModbusDiagnosticsCollector(plan);
        var key = new ScanBlockKey(1, ModbusRegisterClass.HoldingRegister, 0, 1);

        // 3 successes, 1 transport fail, 1 slave exception.
        collector.RecordTransaction(key, Success(key, 5));
        collector.RecordTransaction(key, Success(key, 10));
        collector.RecordTransaction(key, TransportFailure(key, retries: 1));
        collector.RecordTransaction(key, Success(key, 15));
        collector.RecordTransaction(key, SlaveException(key, 0x02));

        var block = collector.Snapshot().Blocks.Single();
        block.Transactions.Should().Be(5);
        block.Successes.Should().Be(3);
        block.Failures.Should().Be(2);
        block.TransportErrors.Should().Be(1);
        block.SlaveExceptions.Should().Be(1);
        block.RttMeanMs.Should().BeApproximately(10.0, 1e-9);
        block.Retries.Should().Be(1);
    }
}
